using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public enum DFMPQuestObjectiveKind
    {
        MarkerBound,
        Dynamic
    }

    public enum DFMPQuestObjectiveLifecycle
    {
        Armed,
        SpawnedAlive,
        DespawnedAlive,
        CreditPending,
        Credited,
        Cancelled
    }

    public enum DFMPQuestObjectiveResult
    {
        Accepted,
        AlreadyRegistered,
        InvalidOwner,
        InvalidIdentity,
        InvalidContext,
        InvalidDescriptor,
        InvalidSequence,
        ObjectiveLimitReached,
        MissingObjective,
        InvalidTransition
    }

    public enum DFMPQuestObjectiveCreditKind
    {
        Kill = 0,
        Injured = 1,
        Clicked = 2
    }

    public enum DFMPQuestFoeCommandKind
    {
        Kill = 1,
        Remove = 2,
        Restrain = 3,
        Unrestrain = 4,
        ChangeTeam = 5,
        ChangeInfighting = 6,
        Click = 7,
        GiveItem = 8,
        CastSpell = 9
    }

    public struct DFMPQuestObjectiveRegistrationMessage : NetworkMessage
    {
        public ulong RequestId;
        public ulong QuestUid;
        public string FoeSymbol;
        public int ActionGeneration;
        public int ObjectiveKind;
        public DFMPWorldContextKey Context;
        public Vector3 ObjectivePosition;
        public float FacingYaw;
        public int MobileType;
        public int Gender;
        public int Reaction;
        public int SpawnCount;
        public string QuestLootJson;
    }

    public struct DFMPQuestObjectiveCancellationMessage : NetworkMessage
    {
        public ulong RequestId;
        public string ObjectiveId;
    }

    public struct DFMPQuestObjectiveResultMessage : NetworkMessage
    {
        public ulong ResultId;
        public string ObjectiveId;
        public int Generation;
        public int KillCount;
        public int CreditKind;
    }

    public struct DFMPQuestFoeCommandMessage : NetworkMessage
    {
        public ulong RequestId;
        public ulong QuestUid;
        public string FoeSymbol;
        public int CommandKind;
        public int IntPayload;
        public string StringPayload;
    }

    public struct DFMPQuestTeleportRequest : NetworkMessage
    {
        public ulong RequestId;
        public ulong QuestUid;
        public string PlaceSymbol;
        public string RegionName;
        public string LocationName;
        public int MarkerIndex;
        public Vector3 MarkerLocalPosition;
    }

    public struct DFMPQuestTeleportResponse : NetworkMessage
    {
        public ulong RequestId;
        public bool Accepted;
        public string Reason;
    }

    public struct DFMPQuestObjectiveResultAckMessage : NetworkMessage
    {
        public ulong ResultId;
        public string ObjectiveId;
    }

    public struct DFMPQuestObjectiveResponseMessage : NetworkMessage
    {
        public ulong RequestId;
        public bool Accepted;
        public string ObjectiveId;
        public string Reason;
    }

    [Serializable]
    public class DFMPQuestObjectiveRecord
    {
        public string ObjectiveId = string.Empty;
        public string OwnerCharacterId = string.Empty;
        public int OwnerConnectionId = -1;
        public string OwnerDisplayName = string.Empty;
        public ulong QuestUid;
        public string FoeSymbol = string.Empty;
        public int ActionGeneration;
        public DFMPQuestObjectiveKind Kind;
        public DFMPWorldContextKey Context;
        public Vector3 ObjectivePosition;
        public float FacingYaw;
        public int MobileType;
        public int Gender;
        public int Reaction;
        public int SpawnCount = 1;
        public int EncounterGeneration;
        public DFMPQuestObjectiveLifecycle Lifecycle = DFMPQuestObjectiveLifecycle.Armed;
        public ulong LastRequestId;
        public ulong PendingResultId;
        public int PendingKillCount;
        public int CreditedKillCount;
        public string QuestLootJson = string.Empty;
        public float OwnerOutsideSince = -1f;
        public int PreservedHealth;
        public int PreservedMaxHealth;
        public bool Hidden;
        public bool Injured;
        public bool Restrained;
        public int Team = -1;
        public bool AttackableByAi = true;
        public string SpellQueueJson = string.Empty;
    }

    public static class DFMPQuestObjectiveIdentity
    {
        public static string Create(string characterId, ulong questUid, string foeSymbol, int actionGeneration)
        {
            string canonical = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}",
                (characterId ?? string.Empty).Trim().ToLowerInvariant(),
                questUid,
                (foeSymbol ?? string.Empty).Trim().ToLowerInvariant(),
                actionGeneration);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(32);
                for (int index = 0; index < 16; index++)
                    builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }

    public sealed class DFMPQuestObjectiveService
    {
        readonly Dictionary<string, DFMPQuestObjectiveRecord> objectives =
            new Dictionary<string, DFMPQuestObjectiveRecord>(StringComparer.Ordinal);
        ulong nextResultId = 1;
        int duplicateRegistrationCount;

        public int Count { get { return objectives.Count; } }
        public int DuplicateRegistrationCount { get { return duplicateRegistrationCount; } }
        public int SpawnedCount { get { return CountWithLifecycle(DFMPQuestObjectiveLifecycle.SpawnedAlive); } }
        public int PendingResultCount { get { return CountWithLifecycle(DFMPQuestObjectiveLifecycle.CreditPending); } }

        public DFMPQuestObjectiveResult Register(
            string ownerCharacterId,
            int ownerConnectionId,
            string ownerDisplayName,
            DFMPWorldContextKey authoritativeContext,
            DFMPQuestObjectiveRegistrationMessage request,
            DFMPServerQuestConfig config,
            out DFMPQuestObjectiveRecord objective)
        {
            objective = null;
            if (string.IsNullOrWhiteSpace(ownerCharacterId) || ownerConnectionId < 0)
                return DFMPQuestObjectiveResult.InvalidOwner;
            if (request.QuestUid == 0 || string.IsNullOrWhiteSpace(request.FoeSymbol) ||
                request.FoeSymbol.Trim().Length > 64 || request.ActionGeneration < 0)
                return DFMPQuestObjectiveResult.InvalidIdentity;
            if (!request.Context.Equals(authoritativeContext))
                return DFMPQuestObjectiveResult.InvalidContext;

            config = config ?? new DFMPServerQuestConfig();
            config.Normalize();
            if (!IsFinite(request.ObjectivePosition) ||
                float.IsNaN(request.FacingYaw) || float.IsInfinity(request.FacingYaw) ||
                !Enum.IsDefined(typeof(DFMPQuestObjectiveKind), request.ObjectiveKind) ||
                request.SpawnCount < 1 || request.SpawnCount > config.MaximumFoesPerObjective ||
                request.MobileType < 0 || request.MobileType > 255 ||
                (request.QuestLootJson != null && request.QuestLootJson.Length > 65536))
                return DFMPQuestObjectiveResult.InvalidDescriptor;

            string objectiveId = DFMPQuestObjectiveIdentity.Create(
                ownerCharacterId,
                request.QuestUid,
                request.FoeSymbol,
                request.ActionGeneration);
            DFMPQuestObjectiveRecord existing;
            if (objectives.TryGetValue(objectiveId, out existing))
            {
                duplicateRegistrationCount++;
                objective = existing;
                if (request.RequestId <= existing.LastRequestId)
                    return request.RequestId == existing.LastRequestId
                        ? DFMPQuestObjectiveResult.AlreadyRegistered
                        : DFMPQuestObjectiveResult.InvalidSequence;
                return DFMPQuestObjectiveResult.AlreadyRegistered;
            }

            int ownerObjectiveCount = 0;
            foreach (DFMPQuestObjectiveRecord value in objectives.Values)
            {
                if (string.Equals(value.OwnerCharacterId, ownerCharacterId, StringComparison.OrdinalIgnoreCase) &&
                    value.Lifecycle != DFMPQuestObjectiveLifecycle.Cancelled &&
                    value.Lifecycle != DFMPQuestObjectiveLifecycle.Credited)
                    ownerObjectiveCount++;
            }
            if (ownerObjectiveCount >= config.MaximumObjectivesPerCharacter)
                return DFMPQuestObjectiveResult.ObjectiveLimitReached;

            objective = new DFMPQuestObjectiveRecord
            {
                ObjectiveId = objectiveId,
                OwnerCharacterId = ownerCharacterId.Trim(),
                OwnerConnectionId = ownerConnectionId,
                OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? "Player" : ownerDisplayName.Trim(),
                QuestUid = request.QuestUid,
                FoeSymbol = request.FoeSymbol.Trim(),
                ActionGeneration = request.ActionGeneration,
                Kind = (DFMPQuestObjectiveKind)request.ObjectiveKind,
                Context = request.Context,
                ObjectivePosition = request.ObjectivePosition,
                FacingYaw = request.FacingYaw,
                MobileType = request.MobileType,
                Gender = request.Gender,
                Reaction = request.Reaction,
                SpawnCount = request.SpawnCount,
                QuestLootJson = request.QuestLootJson ?? string.Empty,
                LastRequestId = request.RequestId
            };
            objectives.Add(objectiveId, objective);
            return DFMPQuestObjectiveResult.Accepted;
        }

        public bool TryGet(string objectiveId, out DFMPQuestObjectiveRecord objective)
        {
            return objectives.TryGetValue(objectiveId ?? string.Empty, out objective);
        }

        public DFMPQuestObjectiveRecord[] GetAll()
        {
            var result = new DFMPQuestObjectiveRecord[objectives.Count];
            objectives.Values.CopyTo(result, 0);
            return result;
        }

        public DFMPQuestObjectiveRecord[] GetForCharacter(string characterId)
        {
            var result = new List<DFMPQuestObjectiveRecord>();
            foreach (DFMPQuestObjectiveRecord objective in objectives.Values)
            {
                if (string.Equals(objective.OwnerCharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                    result.Add(objective);
            }
            return result.ToArray();
        }

        public int RestoreForCharacter(
            string characterId,
            int ownerConnectionId,
            string ownerDisplayName,
            IList<DFMPQuestObjectiveRecord> records,
            int maximumObjectives)
        {
            if (string.IsNullOrWhiteSpace(characterId) || ownerConnectionId < 0 || records == null)
                return 0;

            int restored = 0;
            int limit = Mathf.Clamp(maximumObjectives, 1, 1024);
            for (int index = 0; index < records.Count && restored < limit; index++)
            {
                DFMPQuestObjectiveRecord objective = records[index];
                if (objective == null ||
                    objective.QuestUid == 0 ||
                    string.IsNullOrWhiteSpace(objective.FoeSymbol) ||
                    objective.ActionGeneration < 0)
                    continue;

                string expectedId = DFMPQuestObjectiveIdentity.Create(
                    characterId,
                    objective.QuestUid,
                    objective.FoeSymbol,
                    objective.ActionGeneration);
                if (!string.Equals(expectedId, objective.ObjectiveId, StringComparison.Ordinal))
                    continue;

                objective.OwnerCharacterId = characterId;
                objective.OwnerConnectionId = ownerConnectionId;
                objective.OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? "Player" : ownerDisplayName.Trim();
                objective.OwnerOutsideSince = -1f;
                if (objective.Lifecycle == DFMPQuestObjectiveLifecycle.SpawnedAlive)
                    objective.Lifecycle = DFMPQuestObjectiveLifecycle.DespawnedAlive;
                objectives[objective.ObjectiveId] = objective;
                if (objective.PendingResultId >= nextResultId)
                    nextResultId = objective.PendingResultId + 1;
                restored++;
            }
            return restored;
        }

        public DFMPQuestObjectiveResult Cancel(string objectiveId, int ownerConnectionId, ulong requestId)
        {
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.OwnerConnectionId != ownerConnectionId)
                return DFMPQuestObjectiveResult.InvalidOwner;
            if (requestId <= objective.LastRequestId)
                return DFMPQuestObjectiveResult.InvalidSequence;
            if (objective.Lifecycle == DFMPQuestObjectiveLifecycle.Credited ||
                objective.Lifecycle == DFMPQuestObjectiveLifecycle.Cancelled)
                return DFMPQuestObjectiveResult.InvalidTransition;

            objective.LastRequestId = requestId;
            objective.Lifecycle = DFMPQuestObjectiveLifecycle.Cancelled;
            objective.PendingResultId = 0;
            objective.PendingKillCount = 0;
            return DFMPQuestObjectiveResult.Accepted;
        }

        public bool ShouldSpawn(
            DFMPQuestObjectiveRecord objective,
            DFMPWorldContextKey ownerContext,
            Vector3 ownerPosition,
            DFMPServerQuestConfig config)
        {
            if (objective == null ||
                (objective.Lifecycle != DFMPQuestObjectiveLifecycle.Armed &&
                 objective.Lifecycle != DFMPQuestObjectiveLifecycle.DespawnedAlive) ||
                !objective.Context.Equals(ownerContext))
                return false;

            if (objective.Hidden)
                return false;

            config = config ?? new DFMPServerQuestConfig();
            config.Normalize();
            return HorizontalDistance(ownerPosition, objective.ObjectivePosition, objective.Context.Kind) <= config.EnemyActivationRadius;
        }

        public bool ShouldBeginDespawn(
            DFMPQuestObjectiveRecord objective,
            DFMPWorldContextKey ownerContext,
            Vector3 ownerPosition,
            float now,
            DFMPServerQuestConfig config)
        {
            if (objective == null || objective.Lifecycle != DFMPQuestObjectiveLifecycle.SpawnedAlive)
                return false;

            config = config ?? new DFMPServerQuestConfig();
            config.Normalize();
            bool outside = !objective.Context.Equals(ownerContext) ||
                HorizontalDistance(ownerPosition, objective.ObjectivePosition, objective.Context.Kind) > config.EnemyDespawnRadius;
            if (!outside)
            {
                objective.OwnerOutsideSince = -1f;
                return false;
            }

            if (objective.OwnerOutsideSince < 0f)
                objective.OwnerOutsideSince = now;
            return now - objective.OwnerOutsideSince >= config.EnemyDespawnGraceSeconds;
        }

        public DFMPQuestObjectiveResult MarkSpawned(string objectiveId)
        {
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.Lifecycle != DFMPQuestObjectiveLifecycle.Armed &&
                objective.Lifecycle != DFMPQuestObjectiveLifecycle.DespawnedAlive)
                return DFMPQuestObjectiveResult.InvalidTransition;

            if (objective.Lifecycle == DFMPQuestObjectiveLifecycle.Armed)
                objective.EncounterGeneration++;
            objective.Lifecycle = DFMPQuestObjectiveLifecycle.SpawnedAlive;
            objective.OwnerOutsideSince = -1f;
            return DFMPQuestObjectiveResult.Accepted;
        }

        public DFMPQuestObjectiveResult MarkDespawnedAlive(string objectiveId)
        {
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.Lifecycle != DFMPQuestObjectiveLifecycle.SpawnedAlive)
                return DFMPQuestObjectiveResult.InvalidTransition;

            objective.Lifecycle = DFMPQuestObjectiveLifecycle.DespawnedAlive;
            objective.OwnerOutsideSince = -1f;
            return DFMPQuestObjectiveResult.Accepted;
        }

        public DFMPQuestObjectiveResult MarkKilled(
            string objectiveId,
            int killCount,
            out DFMPQuestObjectiveResultMessage result)
        {
            result = default(DFMPQuestObjectiveResultMessage);
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.Lifecycle != DFMPQuestObjectiveLifecycle.SpawnedAlive)
                return DFMPQuestObjectiveResult.InvalidTransition;

            objective.PendingResultId = nextResultId++;
            objective.PendingKillCount = Mathf.Clamp(killCount, 1, objective.SpawnCount);
            objective.Lifecycle = DFMPQuestObjectiveLifecycle.CreditPending;
            result = new DFMPQuestObjectiveResultMessage
            {
                ResultId = objective.PendingResultId,
                ObjectiveId = objective.ObjectiveId,
                Generation = objective.EncounterGeneration,
                KillCount = objective.PendingKillCount,
                CreditKind = (int)DFMPQuestObjectiveCreditKind.Kill
            };
            return DFMPQuestObjectiveResult.Accepted;
        }

        public DFMPQuestObjectiveResult MarkInjured(string objectiveId, out DFMPQuestObjectiveResultMessage result)
        {
            result = default(DFMPQuestObjectiveResultMessage);
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.Lifecycle != DFMPQuestObjectiveLifecycle.SpawnedAlive)
                return DFMPQuestObjectiveResult.InvalidTransition;

            objective.Injured = true;
            result = new DFMPQuestObjectiveResultMessage
            {
                ResultId = 0,
                ObjectiveId = objective.ObjectiveId,
                Generation = objective.EncounterGeneration,
                KillCount = 0,
                CreditKind = (int)DFMPQuestObjectiveCreditKind.Injured
            };
            return DFMPQuestObjectiveResult.Accepted;
        }

        public DFMPQuestObjectiveResult MarkClicked(string objectiveId, int ownerConnectionId, out DFMPQuestObjectiveResultMessage result)
        {
            result = default(DFMPQuestObjectiveResultMessage);
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.OwnerConnectionId != ownerConnectionId)
                return DFMPQuestObjectiveResult.InvalidOwner;
            if (objective.Lifecycle == DFMPQuestObjectiveLifecycle.Cancelled ||
                objective.Lifecycle == DFMPQuestObjectiveLifecycle.Credited)
                return DFMPQuestObjectiveResult.InvalidTransition;

            result = new DFMPQuestObjectiveResultMessage
            {
                ResultId = 0,
                ObjectiveId = objective.ObjectiveId,
                Generation = objective.EncounterGeneration,
                KillCount = 0,
                CreditKind = (int)DFMPQuestObjectiveCreditKind.Clicked
            };
            return DFMPQuestObjectiveResult.Accepted;
        }

        public void PreservePhysicalState(string objectiveId, int health, int maxHealth)
        {
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return;
            objective.PreservedHealth = Mathf.Max(0, health);
            objective.PreservedMaxHealth = Mathf.Max(0, maxHealth);
        }

        public DFMPQuestObjectiveRecord[] FindForQuestFoe(string characterId, ulong questUid, string foeSymbol)
        {
            var result = new List<DFMPQuestObjectiveRecord>();
            foreach (DFMPQuestObjectiveRecord objective in objectives.Values)
            {
                if (string.Equals(objective.OwnerCharacterId, characterId, StringComparison.OrdinalIgnoreCase) &&
                    objective.QuestUid == questUid &&
                    string.Equals(objective.FoeSymbol, foeSymbol ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    objective.Lifecycle != DFMPQuestObjectiveLifecycle.Cancelled &&
                    objective.Lifecycle != DFMPQuestObjectiveLifecycle.Credited)
                    result.Add(objective);
            }
            return result.ToArray();
        }

        public DFMPQuestObjectiveRecord[] FindForQuest(string characterId, ulong questUid)
        {
            var result = new List<DFMPQuestObjectiveRecord>();
            foreach (DFMPQuestObjectiveRecord objective in objectives.Values)
            {
                if (string.Equals(objective.OwnerCharacterId, characterId, StringComparison.OrdinalIgnoreCase) &&
                    objective.QuestUid == questUid &&
                    objective.Lifecycle != DFMPQuestObjectiveLifecycle.Cancelled &&
                    objective.Lifecycle != DFMPQuestObjectiveLifecycle.Credited)
                    result.Add(objective);
            }
            return result.ToArray();
        }

        public static bool TryValidateTeleport(
            string regionName,
            string locationName,
            int markerIndex,
            Vector3 markerLocalPosition,
            out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(regionName) || string.IsNullOrWhiteSpace(locationName))
            {
                reason = "quest teleport destination is incomplete";
                return false;
            }

            if (markerIndex < -1 || markerIndex > 64)
            {
                reason = "quest teleport marker is out of bounds";
                return false;
            }

            if (!IsFinite(markerLocalPosition))
            {
                reason = "quest teleport marker pose is invalid";
                return false;
            }

            return true;
        }

        public DFMPQuestObjectiveResult AcknowledgeResult(
            string objectiveId,
            int ownerConnectionId,
            ulong resultId)
        {
            DFMPQuestObjectiveRecord objective;
            if (!TryGet(objectiveId, out objective))
                return DFMPQuestObjectiveResult.MissingObjective;
            if (objective.OwnerConnectionId != ownerConnectionId)
                return DFMPQuestObjectiveResult.InvalidOwner;
            if (objective.Lifecycle != DFMPQuestObjectiveLifecycle.CreditPending ||
                objective.PendingResultId != resultId)
                return DFMPQuestObjectiveResult.InvalidTransition;

            objective.PendingResultId = 0;
            objective.CreditedKillCount = Mathf.Min(objective.SpawnCount, objective.CreditedKillCount + objective.PendingKillCount);
            objective.PendingKillCount = 0;
            objective.Lifecycle = objective.CreditedKillCount >= objective.SpawnCount
                ? DFMPQuestObjectiveLifecycle.Credited
                : DFMPQuestObjectiveLifecycle.Armed;
            return DFMPQuestObjectiveResult.Accepted;
        }

        static float HorizontalDistance(Vector3 first, Vector3 second, DFMPWorldContextKind contextKind)
        {
            Vector3 offset = first - second;
            offset.y = 0f;
            float distance = offset.magnitude;
            return contextKind == DFMPWorldContextKind.Exterior
                ? distance / StreamingWorld.SceneMapRatio
                : distance;
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        int CountWithLifecycle(DFMPQuestObjectiveLifecycle lifecycle)
        {
            int count = 0;
            foreach (DFMPQuestObjectiveRecord objective in objectives.Values)
            {
                if (objective.Lifecycle == lifecycle)
                    count++;
            }
            return count;
        }
    }
}

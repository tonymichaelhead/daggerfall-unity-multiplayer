using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPQuestObjectiveServerController : MonoBehaviour
    {
        DFMPQuestObjectiveService objectiveService;
        DFMPDungeonEnemyRosterService enemyService;
        DFMPServerQuestConfig config;
        bool initialized;
        float nextMetricsLogTime;
        readonly HashSet<string> restoredCharacterIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int, Queue<float>> registrationTimesByConnection =
            new Dictionary<int, Queue<float>>();

        public void Initialize(
            DFMPQuestObjectiveService service,
            DFMPDungeonEnemyRosterService rosterService,
            DFMPServerQuestConfig questConfig)
        {
            objectiveService = service;
            enemyService = rosterService;
            config = questConfig ?? new DFMPServerQuestConfig();
            config.Normalize();

            NetworkServer.RegisterHandler<DFMPQuestObjectiveRegistrationMessage>(OnRegister);
            NetworkServer.RegisterHandler<DFMPQuestObjectiveCancellationMessage>(OnCancel);
            NetworkServer.RegisterHandler<DFMPQuestObjectiveResultAckMessage>(OnResultAck);
            NetworkServer.RegisterHandler<DFMPQuestFoeCommandMessage>(OnFoeCommand);
            DFMPEventBus.Instance.EnemyDied += OnEnemyDied;
            initialized = true;
        }

        void OnDestroy()
        {
            if (initialized)
                DFMPEventBus.Instance.EnemyDied -= OnEnemyDied;
            initialized = false;
        }

        void Update()
        {
            if (!initialized || objectiveService == null || enemyService == null)
                return;

            if (Time.unscaledTime >= nextMetricsLogTime)
            {
                nextMetricsLogTime = Time.unscaledTime + 60f;
                Debug.Log(
                    $"[DFMP Quest Metrics] objectives={objectiveService.Count}, " +
                    $"spawnedFoes={objectiveService.SpawnedCount}, pendingAcknowledgements={objectiveService.PendingResultCount}, " +
                    $"duplicateRegistrations={objectiveService.DuplicateRegistrationCount}.");
            }

            DFMPQuestObjectiveRecord[] objectives = objectiveService.GetAll();
            for (int index = 0; index < objectives.Length; index++)
            {
                DFMPQuestObjectiveRecord objective = objectives[index];
                DFMPPlayerSessionState session;
                DFMPWorldContextKey ownerContext;
                if (!DFMPNetworkServer.TryGetPlayerSessionState(objective.OwnerConnectionId, out session) ||
                    session == null ||
                    !DFMPNetworkServer.TryGetSessionWorldContext(objective.OwnerConnectionId, out ownerContext))
                    continue;

                Vector3 ownerPosition = GetAuthoritativePosition(session, ownerContext);
                if (objectiveService.ShouldSpawn(objective, ownerContext, ownerPosition, config))
                {
                    if (objectiveService.MarkSpawned(objective.ObjectiveId) == DFMPQuestObjectiveResult.Accepted)
                    {
                        objectiveService.TryGet(objective.ObjectiveId, out objective);
                        DFMPDynamicEnemyRecord enemyRecord;
                        if (!enemyService.TrySpawnQuestObjective(objective, out enemyRecord))
                            objectiveService.MarkDespawnedAlive(objective.ObjectiveId);
                        else
                        {
                            PublishLifecycle(objective, DFMPQuestObjectiveLifecycle.SpawnedAlive, "owner-proximity");
                            Persist(objective);
                        }
                    }
                }
                else if (objectiveService.ShouldBeginDespawn(
                    objective,
                    ownerContext,
                    ownerPosition,
                    Time.unscaledTime,
                    config))
                {
                    if (enemyService.TryDespawnQuestObjective(objective.ObjectiveId) &&
                        objectiveService.MarkDespawnedAlive(objective.ObjectiveId) == DFMPQuestObjectiveResult.Accepted)
                        PublishLifecycle(objective, DFMPQuestObjectiveLifecycle.DespawnedAlive, "owner-left-proximity");
                    Persist(objective);
                }
            }
        }

        void OnRegister(NetworkConnectionToClient connection, DFMPQuestObjectiveRegistrationMessage request)
        {
            if (connection != null && !TryConsumeRegistrationRate(connection.connectionId, Time.unscaledTime))
            {
                SendResponse(connection, request.RequestId, false, string.Empty, "RateLimited");
                return;
            }

            string characterId;
            DFMPPlayerSessionState session;
            DFMPWorldContextKey context;
            if (connection == null ||
                !DFMPNetworkServer.TryGetActiveCharacterId(connection.connectionId, out characterId) ||
                !DFMPNetworkServer.TryGetPlayerSessionState(connection.connectionId, out session) ||
                session == null ||
                !session.SpawnConfirmed ||
                !DFMPNetworkServer.TryGetSessionWorldContext(connection.connectionId, out context))
            {
                SendResponse(connection, request.RequestId, false, string.Empty, DFMPQuestObjectiveResult.InvalidOwner.ToString());
                return;
            }

            DFMPQuestObjectiveRecord objective;
            EnsureRestoredCharacter(connection.connectionId, characterId, session.DisplayName);
            DFMPQuestObjectiveResult result = objectiveService.Register(
                characterId,
                connection.connectionId,
                session.DisplayName,
                context,
                request,
                config,
                out objective);
            bool accepted = result == DFMPQuestObjectiveResult.Accepted ||
                result == DFMPQuestObjectiveResult.AlreadyRegistered;
            SendResponse(
                connection,
                request.RequestId,
                accepted,
                objective != null ? objective.ObjectiveId : string.Empty,
                accepted ? string.Empty : result.ToString());
            if (accepted && objective != null &&
                objective.Lifecycle == DFMPQuestObjectiveLifecycle.CreditPending &&
                objective.PendingResultId != 0)
            {
                connection.Send(new DFMPQuestObjectiveResultMessage
                {
                    ResultId = objective.PendingResultId,
                    ObjectiveId = objective.ObjectiveId,
                    Generation = objective.EncounterGeneration,
                    KillCount = objective.PendingKillCount,
                    CreditKind = (int)DFMPQuestObjectiveCreditKind.Kill
                });
            }
            if (result == DFMPQuestObjectiveResult.Accepted)
            {
                PublishLifecycle(objective, DFMPQuestObjectiveLifecycle.Armed, "registered");
                Persist(objective);
            }
        }

        void OnCancel(NetworkConnectionToClient connection, DFMPQuestObjectiveCancellationMessage request)
        {
            if (connection == null)
                return;

            DFMPQuestObjectiveResult result = objectiveService.Cancel(
                request.ObjectiveId,
                connection.connectionId,
                request.RequestId);
            if (result == DFMPQuestObjectiveResult.Accepted)
            {
                enemyService.TryDespawnQuestObjective(request.ObjectiveId);
                DFMPQuestObjectiveRecord objective;
                if (objectiveService.TryGet(request.ObjectiveId, out objective))
                {
                    PublishLifecycle(objective, DFMPQuestObjectiveLifecycle.Cancelled, "owner-cancelled");
                    Persist(objective);
                }
            }
            SendResponse(connection, request.RequestId, result == DFMPQuestObjectiveResult.Accepted, request.ObjectiveId, result.ToString());
        }

        void OnResultAck(NetworkConnectionToClient connection, DFMPQuestObjectiveResultAckMessage ack)
        {
            if (connection == null)
                return;

            DFMPQuestObjectiveResult result = objectiveService.AcknowledgeResult(
                ack.ObjectiveId,
                connection.connectionId,
                ack.ResultId);
            if (result != DFMPQuestObjectiveResult.Accepted)
                return;

            DFMPQuestObjectiveRecord objective;
            if (objectiveService.TryGet(ack.ObjectiveId, out objective))
            {
                PublishLifecycle(objective, objective.Lifecycle, "credit-acknowledged");
                DFMPEventBus.Instance.PublishQuestCreditDelivered(new DFMPQuestCreditDeliveredEvent
                {
                    ObjectiveId = objective.ObjectiveId,
                    OwnerConnectionId = objective.OwnerConnectionId,
                    ResultId = ack.ResultId,
                    KillCount = objective.CreditedKillCount
                });
                Persist(objective);
            }
        }

        void OnEnemyDied(DFMPEnemyDiedEvent e)
        {
            if (e == null || e.Encounter.ProviderKind != DFMPDynamicEnemyProviderKind.Quest)
                return;

            DFMPDynamicEnemyRecord enemy;
            if (!enemyService.TryGetRecord(e.EnemyId, out enemy) || string.IsNullOrEmpty(enemy.QuestObjectiveId))
                return;

            DFMPQuestObjectiveResultMessage resultMessage;
            if (objectiveService.MarkKilled(enemy.QuestObjectiveId, 1, out resultMessage) != DFMPQuestObjectiveResult.Accepted)
                return;

            NetworkConnectionToClient owner;
            if (NetworkServer.connections.TryGetValue(enemy.QuestOwnerConnectionId, out owner) && owner != null)
                owner.Send(resultMessage);

            DFMPQuestObjectiveRecord objective;
            if (objectiveService.TryGet(enemy.QuestObjectiveId, out objective))
            {
                PublishLifecycle(objective, DFMPQuestObjectiveLifecycle.CreditPending, "enemy-killed");
                Persist(objective);
            }
        }

        void OnFoeCommand(NetworkConnectionToClient connection, DFMPQuestFoeCommandMessage request)
        {
            string characterId;
            if (connection == null ||
                !DFMPNetworkServer.TryGetActiveCharacterId(connection.connectionId, out characterId))
                return;

            DFMPQuestObjectiveRecord[] matches = objectiveService.FindForQuestFoe(
                characterId,
                request.QuestUid,
                request.FoeSymbol);
            if (matches.Length == 0)
                return;

            DFMPQuestFoeCommandKind kind = (DFMPQuestFoeCommandKind)request.CommandKind;
            for (int index = 0; index < matches.Length; index++)
            {
                DFMPQuestObjectiveRecord objective = matches[index];
                switch (kind)
                {
                    case DFMPQuestFoeCommandKind.Kill:
                        DFMPDynamicEnemyRecord killedRecord;
                        bool killed;
                        if (!enemyService.TryKillQuestObjective(objective.ObjectiveId, out killedRecord, out killed))
                        {
                            DFMPQuestObjectiveResultMessage resultMessage;
                            if (objectiveService.MarkKilled(objective.ObjectiveId, 1, out resultMessage) == DFMPQuestObjectiveResult.Accepted)
                                connection.Send(resultMessage);
                        }
                        break;
                    case DFMPQuestFoeCommandKind.Remove:
                        objective.Hidden = true;
                        enemyService.TryDespawnQuestObjective(objective.ObjectiveId);
                        if (objective.Lifecycle == DFMPQuestObjectiveLifecycle.SpawnedAlive)
                            objectiveService.MarkDespawnedAlive(objective.ObjectiveId);
                        PublishLifecycle(objective, DFMPQuestObjectiveLifecycle.DespawnedAlive, "remove-foe");
                        break;
                    case DFMPQuestFoeCommandKind.Restrain:
                        objective.Restrained = true;
                        break;
                    case DFMPQuestFoeCommandKind.Unrestrain:
                        objective.Restrained = false;
                        break;
                    case DFMPQuestFoeCommandKind.ChangeTeam:
                        objective.Team = request.IntPayload;
                        break;
                    case DFMPQuestFoeCommandKind.ChangeInfighting:
                        objective.AttackableByAi = request.IntPayload != 0;
                        break;
                    case DFMPQuestFoeCommandKind.GiveItem:
                        objective.QuestLootJson = request.StringPayload ?? string.Empty;
                        enemyService.TrySetQuestLoot(objective.ObjectiveId, objective.QuestLootJson);
                        break;
                    case DFMPQuestFoeCommandKind.CastSpell:
                        objective.SpellQueueJson = request.StringPayload ?? string.Empty;
                        break;
                    case DFMPQuestFoeCommandKind.Click:
                        DFMPQuestObjectiveResultMessage clickResult;
                        if (objectiveService.MarkClicked(objective.ObjectiveId, connection.connectionId, out clickResult) ==
                            DFMPQuestObjectiveResult.Accepted)
                            connection.Send(clickResult);
                        break;
                }

                Persist(objective);
            }
        }

        static Vector3 GetAuthoritativePosition(DFMPPlayerSessionState session, DFMPWorldContextKey context)
        {
            return context.Kind == DFMPWorldContextKind.Exterior
                ? new Vector3(session.WorldX, session.WorldY, session.WorldZ)
                : session.DungeonLocalPosition;
        }

        static void SendResponse(
            NetworkConnectionToClient connection,
            ulong requestId,
            bool accepted,
            string objectiveId,
            string reason)
        {
            if (connection == null)
                return;
            connection.Send(new DFMPQuestObjectiveResponseMessage
            {
                RequestId = requestId,
                Accepted = accepted,
                ObjectiveId = objectiveId ?? string.Empty,
                Reason = reason ?? string.Empty
            });
        }

        static void PublishLifecycle(
            DFMPQuestObjectiveRecord objective,
            DFMPQuestObjectiveLifecycle lifecycle,
            string reason)
        {
            if (objective == null)
                return;
            DFMPEventBus.Instance.PublishQuestObjectiveLifecycleChanged(new DFMPQuestObjectiveLifecycleEvent
            {
                ObjectiveId = objective.ObjectiveId,
                OwnerCharacterId = objective.OwnerCharacterId,
                OwnerConnectionId = objective.OwnerConnectionId,
                Lifecycle = lifecycle,
                Reason = reason
            });
        }

        void EnsureRestoredCharacter(int connectionId, string characterId, string displayName)
        {
            if (restoredCharacterIds.Contains(characterId))
                return;

            DFMPCharacterRecord record;
            if (DFMPNetworkServer.TryGetActiveCharacterRecord(connectionId, out record))
            {
                objectiveService.RestoreForCharacter(
                    characterId,
                    connectionId,
                    displayName,
                    record.QuestObjectives,
                    config.MaximumObjectivesPerCharacter);
            }
            restoredCharacterIds.Add(characterId);
        }

        void Persist(DFMPQuestObjectiveRecord objective)
        {
            if (objective == null)
                return;
            DFMPNetworkServer.PersistQuestObjectives(
                objective.OwnerConnectionId,
                objectiveService.GetForCharacter(objective.OwnerCharacterId));
        }

        bool TryConsumeRegistrationRate(int connectionId, float now)
        {
            Queue<float> times;
            if (!registrationTimesByConnection.TryGetValue(connectionId, out times))
            {
                times = new Queue<float>();
                registrationTimesByConnection.Add(connectionId, times);
            }

            while (times.Count > 0 && now - times.Peek() >= 60f)
                times.Dequeue();
            if (times.Count >= config.MaximumRegistrationsPerMinute)
                return false;

            times.Enqueue(now);
            return true;
        }
    }
}

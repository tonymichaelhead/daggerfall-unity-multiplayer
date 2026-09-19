using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Adapts owner-scoped quest objectives to the provider-agnostic dynamic enemy registry.
    /// Objective identity and physical enemy identity intentionally remain separate.
    /// </summary>
    public sealed class DFMPQuestEnemyProvider
    {
        readonly DFMPDynamicEnemyRegistry registry;
        readonly Dictionary<string, string> enemyIdByObjective =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public DFMPQuestEnemyProvider(DFMPDynamicEnemyRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException("registry");
            this.registry = registry;
        }

        public bool TryGetEnemyId(string objectiveId, out string enemyId)
        {
            return enemyIdByObjective.TryGetValue(objectiveId ?? string.Empty, out enemyId);
        }

        public DFMPDynamicEnemyRegistryResult Spawn(
            DFMPQuestObjectiveRecord objective,
            out DFMPDynamicEnemyRecord record)
        {
            record = default(DFMPDynamicEnemyRecord);
            if (objective == null ||
                string.IsNullOrWhiteSpace(objective.ObjectiveId) ||
                objective.Lifecycle != DFMPQuestObjectiveLifecycle.SpawnedAlive)
                return DFMPDynamicEnemyRegistryResult.InvalidRoster;

            string encounterId = string.Format(
                CultureInfo.InvariantCulture,
                "quest-{0}-g{1}",
                objective.ObjectiveId,
                objective.EncounterGeneration);
            string enemyId = encounterId + "-enemy-0";

            DFMPDynamicEnemyRecord existing;
            string mappedId;
            if (enemyIdByObjective.TryGetValue(objective.ObjectiveId, out mappedId) &&
                registry.TryGetRecord(mappedId, out existing))
            {
                if (existing.LifecycleState == DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                {
                    record = existing;
                    return DFMPDynamicEnemyRegistryResult.AlreadyRegistered;
                }

                if (existing.LifecycleState == DFMPDynamicEnemyLifecycleState.DespawnedAlive)
                {
                    DFMPDynamicEnemyRegistryResult resume = registry.TryTransition(
                        true,
                        mappedId,
                        DFMPDynamicEnemyLifecycleState.SpawnedAlive);
                    if (resume == DFMPDynamicEnemyRegistryResult.Accepted &&
                        registry.TryGetRecord(mappedId, out record))
                        return DFMPDynamicEnemyRegistryResult.Accepted;
                }
            }

            int defaultHealth = DFMPDungeonRosterPolicy.GetInitialEnemyHealth(objective.MobileType);
            int maxHealth = objective.PreservedMaxHealth > 0 ? objective.PreservedMaxHealth : defaultHealth;
            int health = objective.PreservedHealth > 0 ? Mathf.Min(objective.PreservedHealth, maxHealth) : maxHealth;
            record = new DFMPDynamicEnemyRecord
            {
                Identity = new DFMPDynamicEnemyIdentity
                {
                    Encounter = new DFMPDynamicEncounterKey
                    {
                        ProviderKind = DFMPDynamicEnemyProviderKind.Quest,
                        Context = objective.Context,
                        EncounterId = encounterId
                    },
                    RosterIndex = 0,
                    EnemyId = enemyId
                },
                LifecycleState = DFMPDynamicEnemyLifecycleState.SpawnedAlive,
                Descriptor = new DFMPDynamicEnemyDescriptor
                {
                    DungeonLocalPosition = objective.ObjectivePosition,
                    FacingYaw = objective.FacingYaw,
                    MobileType = objective.MobileType,
                    Gender = objective.Gender,
                    Reaction = objective.Reaction
                },
                QuestOwnerConnectionId = objective.OwnerConnectionId,
                QuestOwnerDisplayName = objective.OwnerDisplayName,
                QuestObjectiveId = objective.ObjectiveId,
                QuestLootJson = objective.QuestLootJson,
                Health = health,
                MaxHealth = maxHealth,
                TargetConnectionId = -1
            };

            DFMPDynamicEnemyRegistryResult result = registry.RegisterRoster(
                true,
                new[] { record });
            if (result == DFMPDynamicEnemyRegistryResult.Accepted ||
                result == DFMPDynamicEnemyRegistryResult.AlreadyRegistered)
                enemyIdByObjective[objective.ObjectiveId] = enemyId;
            return result;
        }

        public bool TryKill(string objectiveId, out DFMPDynamicEnemyRecord record, out bool killed)
        {
            record = default(DFMPDynamicEnemyRecord);
            killed = false;
            string enemyId;
            if (!TryGetEnemyId(objectiveId, out enemyId))
                return false;
            if (!registry.TryGetRecord(enemyId, out record))
                return false;
            if (record.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                return false;

            int amount = Mathf.Max(1, record.Health);
            return registry.TryApplyDamage(true, enemyId, amount, out record, out _, out killed) ==
                DFMPDynamicEnemyRegistryResult.Accepted;
        }

        public bool TrySetQuestLoot(string objectiveId, string questLootJson, out DFMPDynamicEnemyRecord record)
        {
            record = default(DFMPDynamicEnemyRecord);
            string enemyId;
            if (!TryGetEnemyId(objectiveId, out enemyId))
                return false;
            return registry.TrySetQuestFields(true, enemyId, questLootJson, out record) ==
                DFMPDynamicEnemyRegistryResult.Accepted;
        }

        public DFMPDynamicEnemyRegistryResult DespawnAlive(string objectiveId)
        {
            string enemyId;
            if (!TryGetEnemyId(objectiveId, out enemyId))
                return DFMPDynamicEnemyRegistryResult.MissingEnemy;
            return registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.DespawnedAlive);
        }

        public DFMPDynamicEnemyRegistryResult Retire(string objectiveId)
        {
            string enemyId;
            if (!TryGetEnemyId(objectiveId, out enemyId))
                return DFMPDynamicEnemyRegistryResult.MissingEnemy;

            DFMPDynamicEnemyRecord record;
            if (!registry.TryGetRecord(enemyId, out record))
                return DFMPDynamicEnemyRegistryResult.MissingEnemy;
            if (record.LifecycleState == DFMPDynamicEnemyLifecycleState.Retired)
                return DFMPDynamicEnemyRegistryResult.InvalidTransition;

            DFMPDynamicEnemyRegistryResult result = registry.TryTransition(
                true,
                enemyId,
                DFMPDynamicEnemyLifecycleState.Retired);
            if (result == DFMPDynamicEnemyRegistryResult.Accepted)
                enemyIdByObjective.Remove(objectiveId);
            return result;
        }
    }
}

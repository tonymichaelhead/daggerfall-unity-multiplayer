using System.Collections.Generic;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPDungeonEnemyRosterService : MonoBehaviour
    {
        readonly DFMPDynamicEnemyRegistry registry = new DFMPDynamicEnemyRegistry();
        readonly Dictionary<DFMPWorldContextKey, string[]> enemyIdsByContext = new Dictionary<DFMPWorldContextKey, string[]>();
        ulong serverWorldSeed;
        int dungeonRosterSize;
        bool isSubscribed;

        public int RosterCount
        {
            get { return registry.Count; }
        }

        public void Initialize(DFMPServerEnemyConfig config)
        {
            config = config ?? new DFMPServerEnemyConfig();
            config.Normalize();
            serverWorldSeed = DFMPDungeonRosterPolicy.CreateServerWorldSeed(config.WorldSeed);
            dungeonRosterSize = config.DungeonRosterSize;
            Subscribe();
        }

        void Awake()
        {
            Subscribe();
        }

        void Subscribe()
        {
            if (isSubscribed)
                return;

            DFMPEventBus.Instance.PlayerWorldContextChanged += OnPlayerWorldContextChanged;
            isSubscribed = true;
        }

        void OnDestroy()
        {
            if (!isSubscribed)
                return;

            DFMPEventBus.Instance.PlayerWorldContextChanged -= OnPlayerWorldContextChanged;
            isSubscribed = false;
        }

        public bool TryGetRecord(string enemyId, out DFMPDynamicEnemyRecord record)
        {
            return registry.TryGetRecord(enemyId, out record);
        }

        void OnPlayerWorldContextChanged(DFMPPlayerWorldContextChangedEvent contextChange)
        {
            if (contextChange == null)
                return;

            if (contextChange.HadPreviousContext && IsDungeonBlock(contextChange.PreviousContext) &&
                DFMPNetworkServer.GetConnectionsInWorldContext(contextChange.PreviousContext).Length == 0)
                DespawnContext(contextChange.PreviousContext);

            if (IsDungeonBlock(contextChange.CurrentContext))
                ActivateContext(contextChange.CurrentContext);
        }

        void ActivateContext(DFMPWorldContextKey context)
        {
            string[] enemyIds;
            if (!enemyIdsByContext.TryGetValue(context, out enemyIds))
            {
                DFMPDynamicEnemyRecord[] roster;
                if (!DFMPDungeonRosterPolicy.TryCreateRoster(serverWorldSeed, context, dungeonRosterSize, out roster))
                {
                    Debug.LogWarning($"[DFMP Enemy] Rejected dungeon roster activation: context={context}.");
                    return;
                }

                DFMPDynamicEnemyRegistryResult result = registry.RegisterRoster(true, roster);
                if (result != DFMPDynamicEnemyRegistryResult.Accepted && result != DFMPDynamicEnemyRegistryResult.AlreadyRegistered)
                {
                    Debug.LogWarning($"[DFMP Enemy] Failed to register dungeon roster: context={context}, result={result}.");
                    return;
                }

                enemyIds = new string[roster.Length];
                for (int index = 0; index < roster.Length; index++)
                    enemyIds[index] = roster[index].Identity.EnemyId;

                enemyIdsByContext.Add(context, enemyIds);
                Debug.Log($"[DFMP Enemy] Activated dungeon roster: context={context}, count={enemyIds.Length}.");
            }

            int reactivatedCount = 0;
            for (int index = 0; index < enemyIds.Length; index++)
            {
                DFMPDynamicEnemyRecord record;
                if (registry.TryGetRecord(enemyIds[index], out record) && record.LifecycleState == DFMPDynamicEnemyLifecycleState.DespawnedAlive)
                {
                    if (registry.TryTransition(true, enemyIds[index], DFMPDynamicEnemyLifecycleState.SpawnedAlive) == DFMPDynamicEnemyRegistryResult.Accepted)
                        reactivatedCount++;
                }
            }

            if (reactivatedCount > 0)
                Debug.Log($"[DFMP Enemy] Reactivated dungeon roster: context={context}, count={reactivatedCount}.");
        }

        void DespawnContext(DFMPWorldContextKey context)
        {
            string[] enemyIds;
            if (!enemyIdsByContext.TryGetValue(context, out enemyIds))
                return;

            for (int index = 0; index < enemyIds.Length; index++)
            {
                DFMPDynamicEnemyRecord record;
                if (registry.TryGetRecord(enemyIds[index], out record) && record.LifecycleState == DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                    registry.TryTransition(true, enemyIds[index], DFMPDynamicEnemyLifecycleState.DespawnedAlive);
            }

            Debug.Log($"[DFMP Enemy] Despawned dungeon roster: context={context}, count={enemyIds.Length}.");
        }

        static bool IsDungeonBlock(DFMPWorldContextKey context)
        {
            return context.Kind == DFMPWorldContextKind.Dungeon &&
                context.DungeonBlockIndex > 0 &&
                !string.IsNullOrWhiteSpace(context.LocationId) &&
                !string.IsNullOrWhiteSpace(context.DungeonBlockName);
        }
    }
}

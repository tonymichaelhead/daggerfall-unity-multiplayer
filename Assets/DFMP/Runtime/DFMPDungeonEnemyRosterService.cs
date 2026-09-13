using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPDungeonEnemyRosterService : MonoBehaviour
    {
        readonly DFMPDynamicEnemyRegistry registry = new DFMPDynamicEnemyRegistry();
        readonly Dictionary<DFMPWorldContextKey, string[]> enemyIdsByContext = new Dictionary<DFMPWorldContextKey, string[]>();
        readonly Dictionary<string, GameObject> stateObjectsByEnemyId = new Dictionary<string, GameObject>();
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

        public bool TryApplyDamage(string enemyId, int amount, int killerConnectionId, out DFMPDynamicEnemyRecord updatedRecord, out int appliedAmount, out bool killed)
        {
            DFMPDynamicEnemyRegistryResult result = registry.TryApplyDamage(true, enemyId, amount, out updatedRecord, out appliedAmount, out killed);
            if (result != DFMPDynamicEnemyRegistryResult.Accepted)
                return false;

            if (killed)
            {
                GameObject enemyGo;
                if (stateObjectsByEnemyId.TryGetValue(enemyId, out enemyGo) && enemyGo != null)
                {
                    var state = enemyGo.GetComponent<DFMPDynamicEnemyState>();
                    if (state != null)
                        state.SetLifecycleState(DFMPDynamicEnemyLifecycleState.Dead);
                }

                DFMPEventBus.Instance.PublishEnemyDied(new DFMPEnemyDiedEvent
                {
                    EnemyId = enemyId,
                    Encounter = updatedRecord.Identity.Encounter,
                    KillerConnectionId = killerConnectionId,
                    DamageAmount = appliedAmount
                });

                DaggerfallWorkshop.MobileEnemy enemyDef;
                string lootTableKey = DaggerfallWorkshop.Utility.EnemyBasics.GetEnemy((DaggerfallWorkshop.MobileTypes)updatedRecord.Descriptor.MobileType, out enemyDef)
                    ? enemyDef.LootTableKey
                    : string.Empty;

                DFMPEventBus.Instance.PublishLootGenerated(new DFMPLootGeneratedEvent
                {
                    EnemyId = enemyId,
                    Encounter = updatedRecord.Identity.Encounter,
                    KillerConnectionId = killerConnectionId,
                    LootTableKey = lootTableKey
                });

                Debug.Log($"[DFMP Enemy] Enemy killed: enemyId={enemyId}, killer={killerConnectionId}, amount={appliedAmount}.");
            }

            return true;
        }

        void OnPlayerWorldContextChanged(DFMPPlayerWorldContextChangedEvent contextChange)
        {
            if (contextChange == null)
                return;

            if (contextChange.HadPreviousContext)
                HandleContextVacated(contextChange.PreviousContext);

            if (IsDungeonBlock(contextChange.CurrentContext))
                ActivateContext(contextChange.CurrentContext);
        }

        public void HandleContextVacated(DFMPWorldContextKey context)
        {
            if (IsDungeonBlock(context) && DFMPNetworkServer.GetConnectionsInWorldContext(context).Length == 0)
                DespawnContext(context);
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

            ProjectActiveRecords(context, enemyIds);
        }

        void DespawnContext(DFMPWorldContextKey context)
        {
            string[] enemyIds;
            if (!enemyIdsByContext.TryGetValue(context, out enemyIds))
                return;

            int destroyedStateCount = 0;
            for (int index = 0; index < enemyIds.Length; index++)
            {
                DFMPDynamicEnemyRecord record;
                if (registry.TryGetRecord(enemyIds[index], out record) && record.LifecycleState == DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                    registry.TryTransition(true, enemyIds[index], DFMPDynamicEnemyLifecycleState.DespawnedAlive);

                if (DestroyStateObject(enemyIds[index]))
                    destroyedStateCount++;
            }

            Debug.Log($"[DFMP Enemy] Despawned dungeon roster: context={context}, count={enemyIds.Length}.");
            if (destroyedStateCount > 0)
                Debug.Log($"[DFMP Enemy] Destroyed state projections: context={context}, count={destroyedStateCount}.");
        }

        void ProjectActiveRecords(DFMPWorldContextKey context, string[] enemyIds)
        {
            if (!NetworkServer.active)
                return;

            int spawnedStateCount = 0;
            for (int index = 0; index < enemyIds.Length; index++)
            {
                if (stateObjectsByEnemyId.ContainsKey(enemyIds[index]))
                    continue;

                DFMPDynamicEnemyRecord record;
                if (!registry.TryGetRecord(enemyIds[index], out record) || record.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                    continue;

                GameObject enemyGo = new GameObject("DFMP_DynamicEnemyState_" + record.Identity.EnemyId);
                enemyGo.SetActive(false);
                enemyGo.AddComponent<NetworkIdentity>();
                enemyGo.AddComponent<DFMPWorldContextCarrier>().Initialize(context);
                enemyGo.AddComponent<DFMPDynamicEnemyState>().Initialize(record);
                Object.DontDestroyOnLoad(enemyGo);
                enemyGo.SetActive(true);
                NetworkServer.Spawn(enemyGo, DFMPDynamicEnemyState.AssetId);
                stateObjectsByEnemyId.Add(record.Identity.EnemyId, enemyGo);
                spawnedStateCount++;

                DFMPEventBus.Instance.PublishEnemySpawned(new DFMPEnemySpawnedEvent
                {
                    EnemyId = record.Identity.EnemyId,
                    Encounter = record.Identity.Encounter,
                    RosterIndex = record.Identity.RosterIndex,
                    Descriptor = record.Descriptor
                });
            }

            if (spawnedStateCount > 0)
                Debug.Log($"[DFMP Enemy] Spawned state projections: context={context}, count={spawnedStateCount}.");
        }

        bool DestroyStateObject(string enemyId)
        {
            GameObject enemyGo;
            if (!stateObjectsByEnemyId.TryGetValue(enemyId, out enemyGo))
                return false;

            stateObjectsByEnemyId.Remove(enemyId);
            if (enemyGo == null)
                return false;

            if (NetworkServer.active)
                NetworkServer.Destroy(enemyGo);
            else
                Destroy(enemyGo);
            return true;
        }

        static bool IsDungeonBlock(DFMPWorldContextKey context)
        {
            return context.Kind == DFMPWorldContextKind.Dungeon &&
                context.DungeonBlockIndex >= 0 &&
                !string.IsNullOrWhiteSpace(context.LocationId) &&
                !string.IsNullOrWhiteSpace(context.DungeonBlockName);
        }
    }
}

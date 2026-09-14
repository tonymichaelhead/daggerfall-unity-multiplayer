using System;
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
        readonly Dictionary<string, Vector3> homePositionsByEnemyId = new Dictionary<string, Vector3>();
        readonly Dictionary<string, int> blockedMovementTicksByEnemyId = new Dictionary<string, int>();
        readonly Dictionary<string, float> stuckRecoveryTimesByEnemyId = new Dictionary<string, float>();
        readonly Dictionary<string, int> stuckTargetConnectionIdsByEnemyId = new Dictionary<string, int>();
        readonly Dictionary<DFMPWorldContextKey, float> pendingDespawnTimes = new Dictionary<DFMPWorldContextKey, float>();
        readonly Dictionary<string, float> lastAttackTimesByEnemyId = new Dictionary<string, float>();
        readonly HashSet<DFMPDungeonGeometryScopeKey> missingLineOfSightGeometryWarnings = new HashSet<DFMPDungeonGeometryScopeKey>();
        ulong serverWorldSeed;
        int dungeonRosterSize;
        float despawnDelaySeconds;
        float awarenessRange;
        float attackRange;
        float rangedAttackRange;
        float magicAttackRange;
        float moveSpeed;
        float aiTickIntervalSeconds;
        float aiTickAccumulator;
        float attackCooldownSeconds;
        int attackDamage;
        bool requireLineOfSight;
        float pursuitLeashRange;
        int stuckMovementTickLimit;
        float stuckRecoverySeconds;
        const float EnemyMovementRadius = 0.35f;
        const float EnemyMovementHeight = 1.8f;
        DFMPDungeonGeometryService geometryServiceForTesting;
        Func<string, int, int, bool> serverEnemyDamageApplier = DFMPNetworkServer.TryApplyServerEnemyDamage;
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
            despawnDelaySeconds = config.DespawnDelaySeconds;
            awarenessRange = config.AwarenessRange;
            attackRange = config.AttackRange;
            rangedAttackRange = config.RangedAttackRange;
            magicAttackRange = config.MagicAttackRange;
            moveSpeed = config.MoveSpeed;
            aiTickIntervalSeconds = config.AiTickIntervalSeconds;
            aiTickAccumulator = 0f;
            attackCooldownSeconds = config.AttackCooldownSeconds;
            attackDamage = config.AttackDamage;
            requireLineOfSight = config.RequireLineOfSight;
            pursuitLeashRange = config.PursuitLeashRange;
            stuckMovementTickLimit = config.StuckMovementTickLimit;
            stuckRecoverySeconds = config.StuckRecoverySeconds;
            Subscribe();
        }

        void Awake()
        {
            Subscribe();
        }

        void Update()
        {
            ProcessAiTickCadence(Time.unscaledTime, Time.deltaTime);

            if (pendingDespawnTimes.Count == 0)
                return;

            ProcessPendingDespawns(Time.unscaledTime);
        }

        public void SetServerEnemyDamageApplierForTesting(Func<string, int, int, bool> damageApplier)
        {
            serverEnemyDamageApplier = damageApplier ?? DFMPNetworkServer.TryApplyServerEnemyDamage;
        }

        public void SetDungeonGeometryServiceForTesting(DFMPDungeonGeometryService geometryService)
        {
            geometryServiceForTesting = geometryService;
        }

        public void ProcessAiTickCadence(float currentTime, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            aiTickAccumulator += deltaTime;
            if (aiTickAccumulator < aiTickIntervalSeconds)
                return;

            float tickDeltaTime = aiTickAccumulator;
            aiTickAccumulator = 0f;
            ProcessAiTick(currentTime, tickDeltaTime);
        }

        public void ProcessAiTick(float currentTime, float deltaTime)
        {
            if (enemyIdsByContext.Count == 0 || deltaTime <= 0f)
                return;

            foreach (var kvp in enemyIdsByContext)
            {
                if (DFMPNetworkServer.GetConnectionsInWorldContext(kvp.Key).Length == 0)
                    continue;

                ProcessContextAi(kvp.Key, kvp.Value, currentTime, deltaTime);
            }
        }

        public void ProcessPendingDespawns(float currentTime)
        {
            if (pendingDespawnTimes.Count == 0)
                return;

            List<DFMPWorldContextKey> readyContexts = null;
            foreach (var kvp in pendingDespawnTimes)
            {
                if (currentTime >= kvp.Value)
                {
                    if (readyContexts == null)
                        readyContexts = new List<DFMPWorldContextKey>();

                    readyContexts.Add(kvp.Key);
                }
            }

            if (readyContexts != null)
            {
                for (int index = 0; index < readyContexts.Count; index++)
                {
                    DFMPWorldContextKey context = readyContexts[index];
                    pendingDespawnTimes.Remove(context);
                    if (DFMPNetworkServer.GetConnectionsInWorldContext(context).Length == 0)
                        DespawnContext(context);
                }
            }
        }

        public bool HasPendingDespawn(DFMPWorldContextKey context)
        {
            return pendingDespawnTimes.ContainsKey(context);
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
            pendingDespawnTimes.Clear();
            lastAttackTimesByEnemyId.Clear();
            homePositionsByEnemyId.Clear();
            blockedMovementTicksByEnemyId.Clear();
            stuckRecoveryTimesByEnemyId.Clear();
            stuckTargetConnectionIdsByEnemyId.Clear();
            missingLineOfSightGeometryWarnings.Clear();
            if (!isSubscribed)
                return;

            DFMPEventBus.Instance.PlayerWorldContextChanged -= OnPlayerWorldContextChanged;
            isSubscribed = false;
        }

        public bool TryGetRecord(string enemyId, out DFMPDynamicEnemyRecord record)
        {
            return registry.TryGetRecord(enemyId, out record);
        }

        void ProcessContextAi(DFMPWorldContextKey context, string[] enemyIds, float currentTime, float deltaTime)
        {
            for (int index = 0; index < enemyIds.Length; index++)
            {
                DFMPDynamicEnemyRecord record;
                if (!registry.TryGetRecord(enemyIds[index], out record) || record.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                    continue;

                DFMPDynamicEnemyAttackProfile attackProfile = DFMPDynamicEnemyAttackPolicy.GetProfile(record.Descriptor.MobileType, attackRange, rangedAttackRange, magicAttackRange);

                DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
                {
                    EnemyPosition = record.Descriptor.DungeonLocalPosition,
                    FacingYaw = record.Descriptor.FacingYaw,
                    CurrentTargetConnectionId = record.TargetConnectionId,
                    Targets = CreateSensoryTargets(context, record.Descriptor.DungeonLocalPosition),
                    AwarenessRange = awarenessRange,
                    AttackRange = attackProfile.Range,
                    MoveSpeed = moveSpeed,
                    DeltaTime = deltaTime,
                    RequireLineOfSight = requireLineOfSight,
                    HomePosition = GetHomePosition(record),
                    PursuitLeashRange = pursuitLeashRange,
                    IgnoredTargetConnectionId = GetIgnoredTargetConnectionId(record.Identity.EnemyId, currentTime)
                });

                DFMPDynamicEnemyDescriptor descriptor = record.Descriptor;
                bool movementBlocked;
                Vector3 resolvedPosition;
                bool movementAvailable = TryResolveEnemyMovement(context, record.Descriptor.DungeonLocalPosition, decision.NextDungeonLocalPosition, out resolvedPosition, out movementBlocked);
                UpdateBlockedMovement(record.Identity.EnemyId, decision, movementAvailable, movementBlocked, currentTime);
                descriptor.DungeonLocalPosition = movementAvailable ? resolvedPosition : record.Descriptor.DungeonLocalPosition;
                descriptor.FacingYaw = decision.FacingYaw;
                bool isMoving = decision.IsMoving && movementAvailable && !movementBlocked && descriptor.DungeonLocalPosition != record.Descriptor.DungeonLocalPosition;
                DFMPDynamicEnemyRecord updatedRecord;
                if (registry.TryUpdateAiState(true, record.Identity.EnemyId, descriptor, decision.TargetConnectionId, isMoving, out updatedRecord) == DFMPDynamicEnemyRegistryResult.Accepted)
                    UpdateStateProjection(updatedRecord);

                if (decision.HasTarget && decision.InAttackRange && GetIgnoredTargetConnectionId(record.Identity.EnemyId, currentTime) < 0)
                    TryAttackTarget(record.Identity.EnemyId, decision.TargetConnectionId, attackProfile.Kind, currentTime);
            }
        }

        void UpdateBlockedMovement(string enemyId, DFMPDynamicEnemyAiDecision decision, bool movementAvailable, bool movementBlocked, float currentTime)
        {
            if (!decision.HasTarget || !decision.IsMoving || !movementAvailable || !movementBlocked)
            {
                blockedMovementTicksByEnemyId.Remove(enemyId);
                return;
            }

            int blockedTicks;
            blockedMovementTicksByEnemyId.TryGetValue(enemyId, out blockedTicks);
            blockedTicks++;
            blockedMovementTicksByEnemyId[enemyId] = blockedTicks;
            if (blockedTicks < stuckMovementTickLimit)
                return;

            blockedMovementTicksByEnemyId.Remove(enemyId);
            stuckRecoveryTimesByEnemyId[enemyId] = currentTime + stuckRecoverySeconds;
            stuckTargetConnectionIdsByEnemyId[enemyId] = decision.TargetConnectionId;
            Debug.LogWarning($"[DFMP Enemy] Movement stuck; temporarily releasing target: enemyId={enemyId}, target={decision.TargetConnectionId}, blockedTicks={blockedTicks}.");
        }

        int GetIgnoredTargetConnectionId(string enemyId, float currentTime)
        {
            float recoveryUntil;
            if (!stuckRecoveryTimesByEnemyId.TryGetValue(enemyId, out recoveryUntil))
                return -1;
            if (currentTime >= recoveryUntil)
            {
                stuckRecoveryTimesByEnemyId.Remove(enemyId);
                stuckTargetConnectionIdsByEnemyId.Remove(enemyId);
                return -1;
            }

            int targetConnectionId;
            return stuckTargetConnectionIdsByEnemyId.TryGetValue(enemyId, out targetConnectionId) ? targetConnectionId : -1;
        }

        bool TryResolveEnemyMovement(DFMPWorldContextKey context, Vector3 fromPosition, Vector3 desiredPosition, out Vector3 resolvedPosition, out bool blocked)
        {
            resolvedPosition = fromPosition;
            blocked = false;
            if (fromPosition == desiredPosition && !requireLineOfSight)
            {
                resolvedPosition = desiredPosition;
                return true;
            }

            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            if (geometryService != null && geometryService.TryResolveMovement(
                context,
                fromPosition,
                desiredPosition,
                EnemyMovementRadius,
                EnemyMovementHeight,
                out resolvedPosition,
                out blocked))
                return true;

            if (!requireLineOfSight)
            {
                resolvedPosition = desiredPosition;
                return true;
            }

            return false;
        }

        DFMPDynamicEnemySensoryTarget[] CreateSensoryTargets(DFMPWorldContextKey context, Vector3 enemyPosition)
        {
            int[] connectionIds = DFMPNetworkServer.GetConnectionsInWorldContext(context);
            var targets = new List<DFMPDynamicEnemySensoryTarget>(connectionIds.Length);
            for (int index = 0; index < connectionIds.Length; index++)
            {
                DFMPPlayerSessionState sessionState;
                if (!DFMPNetworkServer.TryGetPlayerSessionState(connectionIds[index], out sessionState) || sessionState == null || !sessionState.HasDungeonLocalPosition)
                    continue;

                targets.Add(new DFMPDynamicEnemySensoryTarget
                {
                    ConnectionId = connectionIds[index],
                    DungeonLocalPosition = sessionState.DungeonLocalPosition,
                    SpawnConfirmed = sessionState.SpawnConfirmed,
                    IsDead = sessionState.IsDead,
                    HasLineOfSight = !requireLineOfSight || HasDungeonLineOfSight(context, enemyPosition, sessionState.DungeonLocalPosition)
                });
            }

            return targets.ToArray();
        }

        bool HasDungeonLineOfSight(DFMPWorldContextKey context, Vector3 enemyPosition, Vector3 targetPosition)
        {
            bool hasLineOfSight;
            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            if (geometryService != null && geometryService.TryHasLineOfSight(context, enemyPosition, targetPosition, out hasLineOfSight))
                return hasLineOfSight;

            DFMPDungeonGeometryScopeKey scope;
            if (DFMPDungeonGeometryService.TryCreateScope(context, out scope) && missingLineOfSightGeometryWarnings.Add(scope))
                Debug.LogWarning($"[DFMP Enemy] Strict line of sight is enabled but dungeon geometry is unavailable: scope={scope}.");

            return false;
        }

        void TryAttackTarget(string enemyId, int targetConnectionId, DFMPDynamicEnemyAttackKind attackKind, float currentTime)
        {
            float lastAttackTime;
            if (lastAttackTimesByEnemyId.TryGetValue(enemyId, out lastAttackTime) && currentTime - lastAttackTime < attackCooldownSeconds)
                return;

            if (serverEnemyDamageApplier != null && serverEnemyDamageApplier(enemyId, targetConnectionId, attackDamage))
            {
                lastAttackTimesByEnemyId[enemyId] = currentTime;
                GameObject enemyGo;
                if (stateObjectsByEnemyId.TryGetValue(enemyId, out enemyGo) && enemyGo != null)
                {
                    var state = enemyGo.GetComponent<DFMPDynamicEnemyState>();
                    if (state != null)
                        state.SetAttackState(attackKind);
                }
            }
        }

        void UpdateStateProjection(DFMPDynamicEnemyRecord record)
        {
            GameObject enemyGo;
            if (!stateObjectsByEnemyId.TryGetValue(record.Identity.EnemyId, out enemyGo) || enemyGo == null)
                return;

            var state = enemyGo.GetComponent<DFMPDynamicEnemyState>();
            if (state != null)
                state.SetAiState(record.Descriptor, record.TargetConnectionId, record.IsMoving);
        }

        public bool TryApplyDamage(string enemyId, int amount, int killerConnectionId, out DFMPDynamicEnemyRecord updatedRecord, out int appliedAmount, out bool killed)
        {
            DFMPDynamicEnemyRegistryResult result = registry.TryApplyDamage(true, enemyId, amount, out updatedRecord, out appliedAmount, out killed);
            if (result != DFMPDynamicEnemyRegistryResult.Accepted)
                return false;

            if (killed)
            {
                ClearTransientAiState(enemyId);
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
            {
                if (despawnDelaySeconds <= 0f)
                {
                    pendingDespawnTimes.Remove(context);
                    DespawnContext(context);
                }
                else
                {
                    pendingDespawnTimes[context] = Time.unscaledTime + despawnDelaySeconds;
                    Debug.Log($"[DFMP Enemy] Scheduled dungeon roster despawn: context={context}, delay={despawnDelaySeconds}s.");
                }
            }
        }

        void ActivateContext(DFMPWorldContextKey context)
        {
            if (pendingDespawnTimes.ContainsKey(context))
            {
                pendingDespawnTimes.Remove(context);
                Debug.Log($"[DFMP Enemy] Cancelled pending dungeon roster despawn on re-entry: context={context}.");
            }

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
                {
                    enemyIds[index] = roster[index].Identity.EnemyId;
                    homePositionsByEnemyId[enemyIds[index]] = roster[index].Descriptor.DungeonLocalPosition;
                }

                enemyIdsByContext.Add(context, enemyIds);
                Debug.Log($"[DFMP Enemy] Activated dungeon roster: context={context}, count={enemyIds.Length}.");
            }

            int reactivatedCount = 0;
            for (int index = 0; index < enemyIds.Length; index++)
            {
                ClearTransientAiState(enemyIds[index]);
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

        Vector3 GetHomePosition(DFMPDynamicEnemyRecord record)
        {
            Vector3 homePosition;
            if (homePositionsByEnemyId.TryGetValue(record.Identity.EnemyId, out homePosition))
                return homePosition;

            homePositionsByEnemyId[record.Identity.EnemyId] = record.Descriptor.DungeonLocalPosition;
            return record.Descriptor.DungeonLocalPosition;
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
                UnityEngine.Object.DontDestroyOnLoad(enemyGo);
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
            ClearTransientAiState(enemyId);
            return true;
        }

        void ClearTransientAiState(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId))
                return;

            lastAttackTimesByEnemyId.Remove(enemyId);
            blockedMovementTicksByEnemyId.Remove(enemyId);
            stuckRecoveryTimesByEnemyId.Remove(enemyId);
            stuckTargetConnectionIdsByEnemyId.Remove(enemyId);
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

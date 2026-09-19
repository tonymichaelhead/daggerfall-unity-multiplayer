using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPDungeonEnemyRosterService : MonoBehaviour
    {
        readonly DFMPDynamicEnemyRegistry registry = new DFMPDynamicEnemyRegistry();
        readonly Dictionary<DFMPWorldContextKey, string[]> enemyIdsByContext = new Dictionary<DFMPWorldContextKey, string[]>();
        readonly Dictionary<string, GameObject> stateObjectsByEnemyId = new Dictionary<string, GameObject>();
        readonly Dictionary<string, DFMPDynamicEnemySensesState> sensesByEnemyId = new Dictionary<string, DFMPDynamicEnemySensesState>();
        readonly Dictionary<string, DFMPDynamicEnemyMotorState> motorByEnemyId = new Dictionary<string, DFMPDynamicEnemyMotorState>();
        readonly Dictionary<string, int> lastLoggedTargetConnectionIdsByEnemyId = new Dictionary<string, int>();
        readonly Dictionary<string, float> lastDetourFailLogTimesByEnemyId = new Dictionary<string, float>();
        readonly Dictionary<string, float> lastDetourLogTimesByEnemyId = new Dictionary<string, float>();
        readonly Dictionary<DFMPWorldContextKey, float> pendingDespawnTimes = new Dictionary<DFMPWorldContextKey, float>();
        readonly Dictionary<string, float> lastAttackTimesByEnemyId = new Dictionary<string, float>();
        readonly Dictionary<string, float> lastStrikeRejectLogTimesByEnemyId = new Dictionary<string, float>();
        readonly HashSet<DFMPDungeonGeometryScopeKey> missingLineOfSightGeometryWarnings = new HashSet<DFMPDungeonGeometryScopeKey>();
        DFMPQuestEnemyProvider questEnemyProvider;
        const float StrikeRejectLogIntervalSeconds = 2f;
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
        string enemyRosterMode;
        float nativeMonsterPower;
        int nativeMonsterVariance;
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
            enemyRosterMode = config.EnemyRosterMode;
            nativeMonsterPower = config.NativeMonsterPower;
            nativeMonsterVariance = config.NativeMonsterVariance;
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
            questEnemyProvider = new DFMPQuestEnemyProvider(registry);
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
            lastStrikeRejectLogTimesByEnemyId.Clear();
            sensesByEnemyId.Clear();
            motorByEnemyId.Clear();
            lastLoggedTargetConnectionIdsByEnemyId.Clear();
            lastDetourFailLogTimesByEnemyId.Clear();
            lastDetourLogTimesByEnemyId.Clear();
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

        public bool TrySpawnQuestObjective(DFMPQuestObjectiveRecord objective, out DFMPDynamicEnemyRecord record)
        {
            record = default(DFMPDynamicEnemyRecord);
            if (objective == null)
                return false;
            if (questEnemyProvider == null)
                questEnemyProvider = new DFMPQuestEnemyProvider(registry);

            DFMPDynamicEnemyRegistryResult result = questEnemyProvider.Spawn(objective, out record);
            if (result != DFMPDynamicEnemyRegistryResult.Accepted &&
                result != DFMPDynamicEnemyRegistryResult.AlreadyRegistered)
                return false;

            string[] existing;
            if (!enemyIdsByContext.TryGetValue(objective.Context, out existing))
            {
                enemyIdsByContext[objective.Context] = new[] { record.Identity.EnemyId };
            }
            else if (Array.IndexOf(existing, record.Identity.EnemyId) < 0)
            {
                var expanded = new string[existing.Length + 1];
                Array.Copy(existing, expanded, existing.Length);
                expanded[existing.Length] = record.Identity.EnemyId;
                enemyIdsByContext[objective.Context] = expanded;
            }

            ProjectActiveRecords(objective.Context, new[] { record.Identity.EnemyId });
            return true;
        }

        public bool TryDespawnQuestObjective(string objectiveId)
        {
            if (questEnemyProvider == null)
                return false;

            string enemyId;
            if (!questEnemyProvider.TryGetEnemyId(objectiveId, out enemyId))
                return false;
            DFMPDynamicEnemyRegistryResult result = questEnemyProvider.DespawnAlive(objectiveId);
            if (result != DFMPDynamicEnemyRegistryResult.Accepted)
                return false;

            DestroyStateObject(enemyId);
            return true;
        }

        void ProcessContextAi(DFMPWorldContextKey context, string[] enemyIds, float currentTime, float deltaTime)
        {
            uint classicGameMinutes = 0;
            if (DaggerfallUnity.Instance != null &&
                DaggerfallUnity.Instance.WorldTime != null &&
                DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime != null)
            {
                classicGameMinutes = DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
            }

            for (int index = 0; index < enemyIds.Length; index++)
            {
                DFMPDynamicEnemyRecord record;
                if (!registry.TryGetRecord(enemyIds[index], out record) || record.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                    continue;

                DFMPDynamicEnemyAttackProfile attackProfile = DFMPDynamicEnemyAttackPolicy.GetProfile(record.Descriptor.MobileType, attackRange, rangedAttackRange, magicAttackRange);
                DFMPDynamicEnemySensesState sensesState = GetOrCreateSensesState(record.Identity.EnemyId);
                float sightModifier;
                float hearingModifier;
                GetMobileSenseModifiers(record.Descriptor.MobileType, out sightModifier, out hearingModifier);

                DFMPDynamicEnemySensesDecision senses = DFMPDynamicEnemySensesPolicy.Evaluate(
                    new DFMPDynamicEnemySensesInput
                    {
                        EnemyPosition = record.Descriptor.DungeonLocalPosition,
                        FacingYaw = record.Descriptor.FacingYaw,
                        ClassicSpawnDistanceType = record.Descriptor.ClassicSpawnDistanceType,
                        SightModifier = sightModifier,
                        HearingModifier = hearingModifier,
                        CurrentTargetConnectionId = record.TargetConnectionId,
                        Targets = CreateSensesTargets(context, record.Descriptor.DungeonLocalPosition),
                        DeltaTime = deltaTime,
                        ClassicGameMinutes = classicGameMinutes,
                        IsPassive = record.Descriptor.Reaction == (int)DFBlock.EnemyReactionTypes.Passive,
                        IgnoredTargetConnectionId = -1,
                        StealthRoll = UnityEngine.Random.Range(0, 100)
                    },
                    sensesState);

                DFMPDynamicEnemyMotorState motorState = GetOrCreateMotorState(record.Identity.EnemyId);
                bool isFlying = DFMPDungeonRosterPolicy.GetNativeFlyingHeightOffset(record.Descriptor.MobileType) > 0f;
                Vector3 predictedTarget = senses.HasLastKnownTargetPosition
                    ? senses.LastKnownTargetPosition
                    : record.Descriptor.DungeonLocalPosition;
                bool hasClearPath = HasClearPathToPredictedTarget(
                    context,
                    record.Descriptor.DungeonLocalPosition,
                    predictedTarget,
                    isFlying);

                Vector3 destination = DFMPDynamicEnemyPursuitPolicy.SelectDestination(
                    new DFMPDynamicEnemyPursuitInput
                    {
                        EnemyPosition = record.Descriptor.DungeonLocalPosition,
                        PredictedTargetPosition = predictedTarget,
                        LastKnownTargetPosition = senses.LastKnownTargetPosition,
                        LastPositionDiff = senses.LastPositionDiff,
                        HasLastKnownTargetPosition = senses.HasLastKnownTargetPosition,
                        HasClearPathToPredictedTarget = hasClearPath,
                        TargetInSight = senses.TargetInSight,
                        CanUseRangedOrMagicPursuit = attackProfile.Kind != DFMPDynamicEnemyAttackKind.Melee,
                        IsFlying = isFlying,
                        // Native shares one stopDistance between the searchMult ramp and the move decision. Using a
                        // different value here lets the ramp stall at a range that still reads as "stop", which pins
                        // a blocked enemy in place forever.
                        StopDistance = attackProfile.Range,
                        DeltaTime = deltaTime,
                        CanAct = senses.CanAct,
                        CurrentTime = currentTime
                    },
                    motorState);

                bool isDetouring = motorState.AvoidObstaclesTimer > 0f;

                TryOpenNearbyDoor(context, record.Identity.EnemyId, record.Descriptor, senses.HasTarget, motorState);

                DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
                {
                    EnemyPosition = record.Descriptor.DungeonLocalPosition,
                    FacingYaw = record.Descriptor.FacingYaw,
                    HasTarget = senses.HasTarget,
                    TargetConnectionId = senses.TargetConnectionId,
                    DestinationPosition = destination,
                    CanAct = senses.CanAct,
                    AttackRange = attackProfile.Range,
                    MoveSpeed = moveSpeed,
                    DeltaTime = deltaTime,
                    IsDetouring = isDetouring
                });

                DFMPDynamicEnemyDescriptor descriptor = record.Descriptor;
                bool skipMoveThisTick = false;
                Vector3 moveDir = Vector3.zero;
                bool hasMoveDir = false;
                if (decision.IsMoving)
                {
                    Vector3 moveOffset = decision.NextDungeonLocalPosition - record.Descriptor.DungeonLocalPosition;
                    moveDir = moveOffset;
                    if (!isFlying)
                        moveDir.y = 0f;
                    if (moveDir.sqrMagnitude > 0.0001f)
                    {
                        moveDir.Normalize();
                        hasMoveDir = true;
                        DFMPObstacleProbeResult hazards;
                        if (TryProbeHazards(context, record.Descriptor.DungeonLocalPosition, moveDir, isFlying, out hazards))
                        {
                            RememberDoorFromProbe(record.Descriptor, motorState, hazards);
                            if (DFMPDynamicEnemyDetourPolicy.ShouldFindDetour(hazards.ObstacleDetected, hazards.FallDetected))
                            {
                                ApplyDetour(
                                    context,
                                    record,
                                    motorState,
                                    destination,
                                    moveDir,
                                    isFlying,
                                    currentTime,
                                    hazards);

                                // Native re-runs this obstacle test inside AttemptMove every rendered frame, so the
                                // frame it spends choosing a detour costs a few milliseconds of travel and nobody
                                // sees it. Our AI tick is 100ms, so skipping the step here means a blocked enemy
                                // near a corner can burn most of its ticks deciding and never actually move, which
                                // is the stall we keep reproducing. The sweep already probed the heading it picked,
                                // so spend this tick's step on it rather than standing still.
                                skipMoveThisTick = true;
                                if (!motorState.LastDetourFailed)
                                {
                                    Vector3 detourOffset = motorState.DetourDestination - record.Descriptor.DungeonLocalPosition;
                                    if (!isFlying)
                                        detourOffset.y = 0f;
                                    float detourDistance = detourOffset.magnitude;
                                    if (detourDistance > 0.0001f)
                                    {
                                        moveDir = detourOffset / detourDistance;
                                        float detourStep = Mathf.Min(moveSpeed * deltaTime, detourDistance);
                                        if (detourStep > 0.0001f)
                                        {
                                            decision.NextDungeonLocalPosition = record.Descriptor.DungeonLocalPosition + moveDir * detourStep;
                                            decision.IsMoving = true;
                                            skipMoveThisTick = false;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                bool movementCollided = false;
                Vector3 resolvedPosition = record.Descriptor.DungeonLocalPosition;
                bool movementAvailable = true;
                if (!skipMoveThisTick)
                {
                    movementAvailable = TryResolveEnemyMovement(
                        context,
                        record.Descriptor.DungeonLocalPosition,
                        decision.NextDungeonLocalPosition,
                        record.Descriptor.MobileType,
                        out resolvedPosition,
                        out movementCollided);
                    resolvedPosition = ResolveEnemyVerticalPosition(
                        context,
                        record.Descriptor.DungeonLocalPosition,
                        resolvedPosition,
                        record.Descriptor.MobileType);

                    // Probe can miss when already flush (CapsuleCast start-overlap) or false-clear a wall as a
                    // slope. If the mover spent the whole step and still did not advance, pick a detour now so
                    // the next tick steers along the surface. Keep this tick's resolved position (usually the
                    // flush contact) — only the heading changes.
                    Vector3 attempted = decision.NextDungeonLocalPosition - record.Descriptor.DungeonLocalPosition;
                    if (!isFlying)
                        attempted.y = 0f;
                    Vector3 achieved = resolvedPosition - record.Descriptor.DungeonLocalPosition;
                    if (!isFlying)
                        achieved.y = 0f;
                    if (decision.IsMoving &&
                        movementAvailable &&
                        movementCollided &&
                        hasMoveDir &&
                        motorState.AvoidObstaclesTimer <= 0f &&
                        attempted.sqrMagnitude > 0.0001f &&
                        achieved.sqrMagnitude <= 0.0001f)
                    {
                        DFMPObstacleProbeResult stuckHazards = new DFMPObstacleProbeResult { ObstacleDetected = true };
                        ApplyDetour(
                            context,
                            record,
                            motorState,
                            destination,
                            moveDir,
                            isFlying,
                            currentTime,
                            stuckHazards);
                    }
                }

                LogTargetTransition(record.Identity.EnemyId, record.TargetConnectionId, decision.TargetConnectionId);
                descriptor.DungeonLocalPosition = !skipMoveThisTick && movementAvailable ? resolvedPosition : record.Descriptor.DungeonLocalPosition;
                descriptor.FacingYaw = decision.FacingYaw;
                // A collision that still slid the enemy along the surface is movement, so displacement is the signal
                // rather than the collision flag; otherwise every wall-hugging detour replicates as a standing enemy.
                bool isMoving = decision.IsMoving &&
                    !skipMoveThisTick &&
                    movementAvailable &&
                    descriptor.DungeonLocalPosition != record.Descriptor.DungeonLocalPosition;
                DFMPDynamicEnemyRecord updatedRecord;
                if (registry.TryUpdateAiState(true, record.Identity.EnemyId, descriptor, decision.TargetConnectionId, isMoving, out updatedRecord) == DFMPDynamicEnemyRegistryResult.Accepted)
                    UpdateStateProjection(updatedRecord);

                if (decision.HasTarget && senses.CanAct)
                {
                    Vector3 liveTargetPosition;
                    bool hasLiveTarget = TryGetLiveTargetPosition(decision.TargetConnectionId, out liveTargetPosition);
                    bool targetInSightForStrike = hasLiveTarget &&
                        DFMPDynamicEnemySensesPolicy.IsWithinSightRange(descriptor.DungeonLocalPosition, liveTargetPosition, sightModifier) &&
                        DFMPDynamicEnemySensesPolicy.IsWithinFieldOfView(descriptor.DungeonLocalPosition, descriptor.FacingYaw, liveTargetPosition) &&
                        (!requireLineOfSight || HasDungeonLineOfSight(context, descriptor.DungeonLocalPosition, liveTargetPosition, false));
                    bool canStrike = hasLiveTarget &&
                        DFMPDynamicEnemyStrikePolicy.CanStrike(
                            decision.HasTarget,
                            senses.CanAct,
                            targetInSightForStrike,
                            descriptor.DungeonLocalPosition,
                            liveTargetPosition,
                            attackProfile.Range);
                    if (canStrike)
                        TryAttackTarget(record.Identity.EnemyId, decision.TargetConnectionId, attackProfile.Kind, currentTime);
                    else if (decision.InAttackRange)
                        LogStrikeRejected(record.Identity.EnemyId, decision.TargetConnectionId, targetInSightForStrike, descriptor.DungeonLocalPosition, liveTargetPosition, destination, currentTime);
                }
            }
        }

        static bool TryGetLiveTargetPosition(int targetConnectionId, out Vector3 liveTargetPosition)
        {
            liveTargetPosition = Vector3.zero;
            DFMPPlayerSessionState sessionState;
            if (!DFMPNetworkServer.TryGetPlayerSessionState(targetConnectionId, out sessionState) || sessionState == null || !sessionState.HasDungeonLocalPosition)
                return false;

            liveTargetPosition = sessionState.DungeonLocalPosition;
            return true;
        }

        void LogStrikeRejected(string enemyId, int targetConnectionId, bool targetInSight, Vector3 enemyPosition, Vector3 liveTargetPosition, Vector3 lastKnownPosition, float currentTime)
        {
            float lastLogTime;
            if (lastStrikeRejectLogTimesByEnemyId.TryGetValue(enemyId, out lastLogTime) && currentTime - lastLogTime < StrikeRejectLogIntervalSeconds)
                return;

            lastStrikeRejectLogTimesByEnemyId[enemyId] = currentTime;
            Vector3 liveOffset = liveTargetPosition - enemyPosition;
            liveOffset.y = 0f;
            Vector3 lastKnownOffset = lastKnownPosition - enemyPosition;
            lastKnownOffset.y = 0f;
            Debug.Log($"[DFMP Enemy] Attack withheld: enemyId={enemyId}, target={targetConnectionId}, targetInSight={targetInSight}, liveDistance={liveOffset.magnitude:0.###}, lastKnownDistance={lastKnownOffset.magnitude:0.###}.");
        }

        DFMPDynamicEnemySensesState GetOrCreateSensesState(string enemyId)
        {
            DFMPDynamicEnemySensesState state;
            if (!sensesByEnemyId.TryGetValue(enemyId, out state) || state == null)
            {
                state = new DFMPDynamicEnemySensesState();
                sensesByEnemyId[enemyId] = state;
            }

            return state;
        }

        DFMPDynamicEnemyMotorState GetOrCreateMotorState(string enemyId)
        {
            DFMPDynamicEnemyMotorState state;
            if (!motorByEnemyId.TryGetValue(enemyId, out state) || state == null)
            {
                state = new DFMPDynamicEnemyMotorState();
                motorByEnemyId[enemyId] = state;
            }

            return state;
        }

        static void GetMobileSenseModifiers(int mobileType, out float sightModifier, out float hearingModifier)
        {
            sightModifier = 0f;
            hearingModifier = 0f;
            MobileEnemy enemy;
            if (EnemyBasics.GetEnemy((MobileTypes)mobileType, out enemy))
            {
                sightModifier = enemy.SightModifier;
                hearingModifier = enemy.HearingModifier;
            }
        }

        static bool CanOpenDoors(int mobileType)
        {
            MobileEnemy enemy;
            return EnemyBasics.GetEnemy((MobileTypes)mobileType, out enemy) && enemy.CanOpenDoors;
        }

        bool HasClearPathToPredictedTarget(
            DFMPWorldContextKey context,
            Vector3 fromPosition,
            Vector3 predictedTarget,
            bool isFlying)
        {
            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            if (geometryService == null)
                return !requireLineOfSight;

            bool hasClearPath;

            // Native sizes this cast from its previous destination, which normally equals the target position and so
            // spans the real gap. Reusing our previous destination is not equivalent: right after a detour it is the
            // two-unit detour waypoint, so the cast stops short of the wall, reports a false clear path, and resets
            // the searchMult ramp that is the only thing driving a blocked enemy around a corner. Span the actual
            // distance to the position under test, which is what ClearPathToPosition means.
            if (geometryService.TryHasClearPathToPosition(
                context,
                fromPosition,
                predictedTarget,
                EnemyMovementRadius,
                EnemyMovementHeight,
                isFlying,
                out hasClearPath))
                return hasClearPath;

            return !requireLineOfSight;
        }

        bool TryProbeHazards(DFMPWorldContextKey context, Vector3 fromPosition, Vector3 direction, bool isFlying, out DFMPObstacleProbeResult result)
        {
            result = new DFMPObstacleProbeResult();
            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            return geometryService != null &&
                geometryService.TryProbeMovementHazards(
                    context,
                    fromPosition,
                    direction,
                    EnemyMovementRadius,
                    EnemyMovementHeight,
                    isFlying,
                    out result);
        }

        DFMPObstacleProbeResult ProbeDetourDirection(DFMPWorldContextKey context, Vector3 fromPosition, Vector3 direction, bool isFlying)
        {
            DFMPObstacleProbeResult result;
            if (!TryProbeHazards(context, fromPosition, direction, isFlying, out result))
                return new DFMPObstacleProbeResult();
            return result;
        }

        static void RememberDoorFromProbe(DFMPDynamicEnemyDescriptor descriptor, DFMPDynamicEnemyMotorState motorState, DFMPObstacleProbeResult hazards)
        {
            if (!DFMPDynamicEnemyDoorPolicy.ShouldRememberDoor(
                hazards.FoundDoor,
                hazards.DoorLoadId,
                DFMPDynamicEnemySensesPolicy.IsWithinYawAngle(
                    descriptor.DungeonLocalPosition,
                    descriptor.FacingYaw,
                    hazards.DoorDungeonLocalPosition,
                    DFMPDynamicEnemyDoorPolicy.DoorYawHalfAngle)))
                return;

            motorState.HasLastKnownDoor = true;
            motorState.LastKnownDoorLoadId = hazards.DoorLoadId;
            motorState.LastKnownDoorPosition = hazards.DoorDungeonLocalPosition;
            motorState.DistanceToDoor = Vector3.Distance(descriptor.DungeonLocalPosition, hazards.DoorDungeonLocalPosition);
        }

        void TryOpenNearbyDoor(
            DFMPWorldContextKey context,
            string enemyId,
            DFMPDynamicEnemyDescriptor descriptor,
            bool hasTarget,
            DFMPDynamicEnemyMotorState motorState)
        {
            if (!hasTarget || !CanOpenDoors(descriptor.MobileType) || !motorState.HasLastKnownDoor)
                return;

            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            if (geometryService == null)
                return;

            bool isOpen;
            bool isLocked;
            Vector3 doorPosition;
            if (!geometryService.TryGetActionDoorState(context, motorState.LastKnownDoorLoadId, out isOpen, out isLocked, out doorPosition))
            {
                motorState.HasLastKnownDoor = false;
                return;
            }

            motorState.LastKnownDoorPosition = doorPosition;
            motorState.DistanceToDoor = Vector3.Distance(descriptor.DungeonLocalPosition, doorPosition);
            if (!DFMPDynamicEnemyDoorPolicy.ShouldOpenDoor(
                true,
                motorState.HasLastKnownDoor,
                isOpen,
                isLocked,
                motorState.DistanceToDoor))
                return;

            if (!geometryService.TryApplyActionDoor(context, motorState.LastKnownDoorLoadId, true))
                return;

            DFMPNetworkServer.BroadcastActionDoorSync(context, motorState.LastKnownDoorLoadId, true);
            Debug.Log($"[DFMP Enemy] Opened action door: enemyId={enemyId}, loadID={motorState.LastKnownDoorLoadId}.");
        }

        void ApplyDetour(
            DFMPWorldContextKey context,
            DFMPDynamicEnemyRecord record,
            DFMPDynamicEnemyMotorState motorState,
            Vector3 destination,
            Vector3 moveDir,
            bool isFlying,
            float currentTime,
            DFMPObstacleProbeResult hazards)
        {
            int firstAngle = UnityEngine.Random.Range(0, 2) == 0 ? 45 : -45;
            int verticalSign = UnityEngine.Random.Range(0, 2) == 0 ? 1 : -1;
            DFMPDynamicEnemyDetourPolicy.FindDetour(
                motorState,
                record.Descriptor.DungeonLocalPosition,
                destination,
                moveDir,
                isFlying,
                currentTime,
                firstAngle,
                verticalSign,
                testDirection => ProbeDetourDirection(context, record.Descriptor.DungeonLocalPosition, testDirection, isFlying));
            if (motorState.LastDetourFailed)
                LogDetourFailed(record.Identity.EnemyId, record.Descriptor.MobileType, currentTime);
            else
                LogDetourStarted(record.Identity.EnemyId, hazards, motorState, record.Descriptor.DungeonLocalPosition, currentTime);
        }

        void LogDetourFailed(string enemyId, int mobileType, float currentTime)
        {
            float lastLogTime;
            if (lastDetourFailLogTimesByEnemyId.TryGetValue(enemyId, out lastLogTime) && currentTime - lastLogTime < StrikeRejectLogIntervalSeconds)
                return;

            lastDetourFailLogTimesByEnemyId[enemyId] = currentTime;
            Debug.LogWarning($"[DFMP Enemy] Detour failed; no clear step: enemyId={enemyId}, mobileType={(MobileTypes)mobileType}.");
        }

        void LogDetourStarted(string enemyId, DFMPObstacleProbeResult hazards, DFMPDynamicEnemyMotorState motorState, Vector3 fromPosition, float currentTime)
        {
            float lastLogTime;
            if (lastDetourLogTimesByEnemyId.TryGetValue(enemyId, out lastLogTime) && currentTime - lastLogTime < StrikeRejectLogIntervalSeconds)
                return;

            lastDetourLogTimesByEnemyId[enemyId] = currentTime;
            Vector3 detourOffset = motorState.DetourDestination - fromPosition;
            Debug.Log($"[DFMP Enemy] Detour started: enemyId={enemyId}, obstacle={hazards.ObstacleDetected}, fall={hazards.FallDetected}, clockwise={motorState.CheckingClockwise}, detourDistance={detourOffset.magnitude:0.###}.");
        }

        void LogTargetTransition(string enemyId, int previousTargetConnectionId, int nextTargetConnectionId)
        {
            int lastLoggedTargetConnectionId;
            if (lastLoggedTargetConnectionIdsByEnemyId.TryGetValue(enemyId, out lastLoggedTargetConnectionId) && lastLoggedTargetConnectionId == nextTargetConnectionId)
                return;

            lastLoggedTargetConnectionIdsByEnemyId[enemyId] = nextTargetConnectionId;
            if (nextTargetConnectionId > 0)
                Debug.Log($"[DFMP Enemy] Target acquired: enemyId={enemyId}, target={nextTargetConnectionId}, previousTarget={previousTargetConnectionId}.");
            else if (previousTargetConnectionId > 0)
                Debug.Log($"[DFMP Enemy] Target lost: enemyId={enemyId}, previousTarget={previousTargetConnectionId}.");
        }

        bool TryResolveEnemyMovement(DFMPWorldContextKey context, Vector3 fromPosition, Vector3 desiredPosition, int mobileType, out Vector3 resolvedPosition, out bool blocked)
        {
            resolvedPosition = fromPosition;
            blocked = false;
            if (fromPosition == desiredPosition && !requireLineOfSight)
            {
                resolvedPosition = desiredPosition;
                return true;
            }

            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            bool isFlying = DFMPDungeonRosterPolicy.GetNativeFlyingHeightOffset(mobileType) > 0f;
            if (geometryService != null && (isFlying
                ? geometryService.TryResolveMovement(
                    context,
                    fromPosition,
                    desiredPosition,
                    EnemyMovementRadius,
                    EnemyMovementHeight,
                    out resolvedPosition,
                    out blocked)
                : geometryService.TryResolveGroundedMovement(
                    context,
                    fromPosition,
                    desiredPosition,
                    EnemyMovementRadius,
                    EnemyMovementHeight,
                    out resolvedPosition,
                    out blocked)))
                return true;

            if (!requireLineOfSight)
            {
                resolvedPosition = desiredPosition;
                return true;
            }

            return false;
        }

        Vector3 ResolveEnemyVerticalPosition(
            DFMPWorldContextKey context,
            Vector3 previousPosition,
            Vector3 position,
            int mobileType)
        {
            if (DFMPDungeonRosterPolicy.GetNativeFlyingHeightOffset(mobileType) > 0f)
                return new Vector3(position.x, previousPosition.y, position.z);

            return position;
        }

        DFMPDynamicEnemySensesTarget[] CreateSensesTargets(DFMPWorldContextKey context, Vector3 enemyPosition)
        {
            int[] connectionIds = DFMPNetworkServer.GetConnectionsInWorldContext(context);
            var targets = new List<DFMPDynamicEnemySensesTarget>(connectionIds.Length);
            for (int index = 0; index < connectionIds.Length; index++)
            {
                DFMPPlayerSessionState sessionState;
                if (!DFMPNetworkServer.TryGetPlayerSessionState(connectionIds[index], out sessionState) || sessionState == null || !sessionState.HasDungeonLocalPosition)
                    continue;

                bool hasSightClearance = !requireLineOfSight || HasDungeonLineOfSight(context, enemyPosition, sessionState.DungeonLocalPosition, false);
                bool hasHearingClearance = HasDungeonLineOfSight(context, enemyPosition, sessionState.DungeonLocalPosition, true);

                int stealthSkill;
                if (!DFMPNetworkServer.TryGetPlayerStealthSkill(connectionIds[index], out stealthSkill))
                    stealthSkill = 0;

                bool movingLessThanHalfSpeed;
                if (!DFMPNetworkServer.TryGetPlayerMovingLessThanHalfSpeed(connectionIds[index], out movingLessThanHalfSpeed))
                    movingLessThanHalfSpeed = !sessionState.IsMoving;

                targets.Add(new DFMPDynamicEnemySensesTarget
                {
                    ConnectionId = connectionIds[index],
                    DungeonLocalPosition = sessionState.DungeonLocalPosition,
                    SpawnConfirmed = sessionState.SpawnConfirmed,
                    IsDead = sessionState.IsDead,
                    HasSightClearance = hasSightClearance,
                    HasHearingClearance = hasHearingClearance,
                    StealthSkill = stealthSkill,
                    MovingLessThanHalfSpeed = movingLessThanHalfSpeed
                });
            }

            return targets.ToArray();
        }

        bool HasDungeonLineOfSight(DFMPWorldContextKey context, Vector3 enemyPosition, Vector3 targetPosition, bool ignoreActionDoors)
        {
            bool hasLineOfSight;
            DFMPDungeonGeometryService geometryService = geometryServiceForTesting ?? DFMPNetworkServer.DungeonGeometryService;
            if (geometryService != null && geometryService.TryHasLineOfSight(context, enemyPosition, targetPosition, ignoreActionDoors, out hasLineOfSight))
                return hasLineOfSight;

            DFMPDungeonGeometryScopeKey scope;
            if (requireLineOfSight && DFMPDungeonGeometryService.TryCreateScope(context, out scope) && missingLineOfSightGeometryWarnings.Add(scope))
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
                Debug.Log($"[DFMP Enemy] Attack accepted: enemyId={enemyId}, target={targetConnectionId}, kind={attackKind}, damage={attackDamage}.");
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

            if (!killed && killerConnectionId > 0)
            {
                if (registry.TryUpdateAiState(true, enemyId, updatedRecord.Descriptor, killerConnectionId, false, out updatedRecord) == DFMPDynamicEnemyRegistryResult.Accepted)
                    UpdateStateProjection(updatedRecord);
            }

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

                Debug.Log($"[DFMP Enemy] Loot generated: enemyId={enemyId}, killer={killerConnectionId}, lootTable={lootTableKey ?? string.Empty}.");
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
            if (!IsDungeonBlock(context))
                return;

            int remainingOccupants = DFMPNetworkServer.GetConnectionsInWorldContext(context).Length;
            Debug.Log($"[DFMP Enemy] Dungeon context vacated: context={context}, remainingOccupants={remainingOccupants}.");
            if (remainingOccupants == 0)
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
                bool nativeParity = string.Equals(enemyRosterMode, DFMPEnemyRosterModes.NativeParity, StringComparison.Ordinal);
                float resolvedMonsterPower = ResolveNativeMonsterPower(context);
                DFMPDungeonRosterPolicy.NativeDungeonGenerationInputs nativeInputs;
                bool rosterCreated;
                if (nativeParity)
                {
                    rosterCreated = DFMPDungeonRosterPolicy.TryCreateNativeRoster(
                        serverWorldSeed,
                        context,
                        resolvedMonsterPower,
                        nativeMonsterVariance,
                        out roster,
                        out nativeInputs);
                }
                else
                {
                    nativeInputs = new DFMPDungeonRosterPolicy.NativeDungeonGenerationInputs();
                    rosterCreated = DFMPDungeonRosterPolicy.TryCreateRoster(serverWorldSeed, context, dungeonRosterSize, out roster);
                }
                if (!rosterCreated)
                {
                    Debug.LogWarning($"[DFMP Enemy] Rejected {(nativeParity ? "native" : "development")} dungeon roster activation: context={context}.");
                    return;
                }

                if (nativeParity)
                    Debug.Log($"[DFMP Enemy] Native roster inputs: context={context}, dungeonType={nativeInputs.DungeonType}, recordId={nativeInputs.DungeonRecordId}, blockSeed={nativeInputs.BlockSeed}, waterLevel={nativeInputs.WaterLevel}, monsterPower={resolvedMonsterPower:0.###}, monsterVariance={nativeMonsterVariance}, count={roster.Length}.");

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

        float ResolveNativeMonsterPower(DFMPWorldContextKey context)
        {
            if (nativeMonsterPower > 0f)
                return nativeMonsterPower;

            int highestPlayerLevel = 1;
            int[] connectionIds = DFMPNetworkServer.GetConnectionsInWorldContext(context);
            for (int index = 0; index < connectionIds.Length; index++)
            {
                int playerLevel;
                if (DFMPNetworkServer.TryGetPlayerLevel(connectionIds[index], out playerLevel))
                    highestPlayerLevel = Math.Max(highestPlayerLevel, playerLevel);
            }

            return DFMPDungeonRosterPolicy.CalculateNativeMonsterPower(highestPlayerLevel);
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
            lastStrikeRejectLogTimesByEnemyId.Remove(enemyId);
            lastDetourFailLogTimesByEnemyId.Remove(enemyId);
            lastDetourLogTimesByEnemyId.Remove(enemyId);
            lastLoggedTargetConnectionIdsByEnemyId.Remove(enemyId);
            DFMPDynamicEnemySensesState sensesState;
            if (sensesByEnemyId.TryGetValue(enemyId, out sensesState) && sensesState != null)
                sensesState.Reset();
            sensesByEnemyId.Remove(enemyId);
            DFMPDynamicEnemyMotorState motorState;
            if (motorByEnemyId.TryGetValue(enemyId, out motorState) && motorState != null)
                motorState.Reset();
            motorByEnemyId.Remove(enemyId);
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

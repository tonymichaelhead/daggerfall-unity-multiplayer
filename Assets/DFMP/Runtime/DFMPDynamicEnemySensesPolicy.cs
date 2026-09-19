using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Classic spawn/despawn radii keyed by ClassicSpawnDistanceType.
    /// Ported from EnemySenses.Start.
    /// </summary>
    public struct DFMPClassicSensesRanges
    {
        public float SpawnXZ;
        public float SpawnYUpper;
        public float SpawnYLower;
        public float DespawnXZ;
        public float DespawnY;

        public static DFMPClassicSensesRanges FromSpawnDistanceType(int classicSpawnDistanceType)
        {
            short[] spawnXZ = { 1024, 384, 640, 768, 768, 768, 768 };
            short[] spawnYUpper = { 128, 128, 128, 384, 768, 128, 256 };
            short[] spawnYLower = { 0, 0, 0, 0, -128, -768, 0 };
            short[] despawnXZ = { 1024, 1024, 1024, 1024, 768, 768, 768 };
            short[] despawnY = { 384, 384, 384, 384, 768, 768, 768 };

            int index = Mathf.Clamp(classicSpawnDistanceType, 0, spawnXZ.Length - 1);
            float scale = MeshReader.GlobalScale;
            return new DFMPClassicSensesRanges
            {
                SpawnXZ = spawnXZ[index] * scale,
                SpawnYUpper = spawnYUpper[index] * scale,
                SpawnYLower = spawnYLower[index] * scale,
                DespawnXZ = despawnXZ[index] * scale,
                DespawnY = despawnY[index] * scale
            };
        }
    }

    /// <summary>
    /// Classic dungeon spawn envelope (wouldBeSpawnedInClassic) with spawn/despawn hysteresis.
    /// Exterior branch is not ported; dungeon contexts are always treated as inside.
    /// </summary>
    public static class DFMPClassicSpawnEnvelope
    {
        public const float MaximumClassicDistance = 1094f;

        public static bool Evaluate(bool previouslyInEnvelope, Vector3 enemyPosition, Vector3 targetPosition, DFMPClassicSensesRanges ranges)
        {
            Vector3 offset = targetPosition - enemyPosition;
            float distance = offset.magnitude;
            float outerGate = MaximumClassicDistance * MeshReader.GlobalScale;
            if (distance >= outerGate)
                return false;

            float upperXZ;
            float upperY;
            float lowerY;
            if (!previouslyInEnvelope)
            {
                upperXZ = ranges.SpawnXZ;
                upperY = ranges.SpawnYUpper;
                lowerY = ranges.SpawnYLower;
            }
            else
            {
                upperXZ = ranges.DespawnXZ;
                upperY = ranges.DespawnY;
                lowerY = 0f;
            }

            float yDiff = enemyPosition.y - targetPosition.y;
            float yDiffAbs = Mathf.Abs(yDiff);
            float planar = Mathf.Sqrt(Mathf.Max(0f, distance * distance - yDiffAbs * yDiffAbs));
            if (planar > upperXZ)
                return false;

            if (lowerY == 0f)
            {
                if (yDiffAbs > upperY)
                    return false;
            }
            else if (yDiff < lowerY || yDiff > upperY)
            {
                return false;
            }

            return true;
        }
    }

    public sealed class DFMPDynamicEnemySensesState
    {
        public int GiveUpTimer;
        public float ClassicUpdateCarry;
        public int TargetConnectionId = -1;
        public Vector3 LastKnownTargetPosition;
        public bool HasLastKnownTargetPosition;
        public Vector3 OldLastKnownTargetPosition;
        public Vector3 LastPositionDiff;
        public bool AwareOfTargetForLastSightSample;
        public bool HasEncounteredTarget;
        public uint LastStealthCheckMinute;
        public bool PreviouslyDetected;
        public float LastHadLosTimer;
        public readonly Dictionary<int, bool> InClassicEnvelopeByConnectionId = new Dictionary<int, bool>();

        public void Reset()
        {
            GiveUpTimer = 0;
            ClassicUpdateCarry = 0f;
            TargetConnectionId = -1;
            LastKnownTargetPosition = Vector3.zero;
            HasLastKnownTargetPosition = false;
            OldLastKnownTargetPosition = Vector3.zero;
            LastPositionDiff = Vector3.zero;
            AwareOfTargetForLastSightSample = false;
            HasEncounteredTarget = false;
            LastStealthCheckMinute = 0;
            PreviouslyDetected = false;
            LastHadLosTimer = 0f;
            InClassicEnvelopeByConnectionId.Clear();
        }
    }

    public struct DFMPDynamicEnemySensesTarget
    {
        public int ConnectionId;
        public Vector3 DungeonLocalPosition;
        public bool SpawnConfirmed;
        public bool IsDead;
        /// <summary>Unobstructed sight ray from enemy to target.</summary>
        public bool HasSightClearance;
        /// <summary>Hearing path not blocked by static geometry.</summary>
        public bool HasHearingClearance;
        public int StealthSkill;
        public bool MovingLessThanHalfSpeed;
    }

    public struct DFMPDynamicEnemySensesInput
    {
        public Vector3 EnemyPosition;
        public float FacingYaw;
        public int ClassicSpawnDistanceType;
        public float SightModifier;
        public float HearingModifier;
        public int CurrentTargetConnectionId;
        public DFMPDynamicEnemySensesTarget[] Targets;
        public float DeltaTime;
        public uint ClassicGameMinutes;
        public bool IsPassive;
        public int IgnoredTargetConnectionId;
        /// <summary>Injected Dice100 roll in [0, 100). Detection when roll &gt;= stealth chance.</summary>
        public int StealthRoll;
    }

    public struct DFMPDynamicEnemySensesDecision
    {
        public bool HasTarget;
        public int TargetConnectionId;
        public bool DetectedTarget;
        public bool CanAct;
        public Vector3 LastKnownTargetPosition;
        public bool HasLastKnownTargetPosition;
        public Vector3 LastPositionDiff;
        public int GiveUpTimer;
        public bool TargetInSight;
    }

    public static class DFMPGiveUpTimerPolicy
    {
        public const int FullTimer = 200;
        public const float ClassicUpdateInterval = 0.0625f;

        public static void Advance(ref int giveUpTimer, int classicTicks, bool detectedTarget)
        {
            if (detectedTarget)
            {
                giveUpTimer = FullTimer;
                return;
            }

            if (classicTicks <= 0 || giveUpTimer <= 0)
                return;

            giveUpTimer = Math.Max(0, giveUpTimer - classicTicks);
        }
    }

    public static class DFMPStealthCheckPolicy
    {
        public static float MaximumStealthDistance
        {
            get { return 1024f * MeshReader.GlobalScale; }
        }

        public static int CalculateStealthChance(float distanceToTarget, int stealthSkill)
        {
            return 2 * ((int)(distanceToTarget / MeshReader.GlobalScale) * Mathf.Max(0, stealthSkill) >> 10);
        }

        /// <summary>
        /// Port of EnemySenses.StealthCheck. Returns true when the enemy detects the target through stealth.
        /// </summary>
        public static bool Evaluate(
            bool inClassicEnvelope,
            float distanceToTarget,
            int stealthSkill,
            uint classicGameMinutes,
            bool movingLessThanHalfSpeed,
            bool hasEncounteredTarget,
            bool previouslyDetected,
            ref uint lastStealthCheckMinute,
            int stealthRoll)
        {
            if (!inClassicEnvelope)
                return false;
            if (distanceToTarget > MaximumStealthDistance)
                return false;

            if (classicGameMinutes == lastStealthCheckMinute)
                return previouslyDetected;

            if (movingLessThanHalfSpeed)
            {
                if ((classicGameMinutes & 1u) == 1u)
                    return previouslyDetected;
            }
            else if (hasEncounteredTarget)
            {
                return true;
            }

            lastStealthCheckMinute = classicGameMinutes;
            int stealthChance = CalculateStealthChance(distanceToTarget, stealthSkill);
            int roll = stealthRoll;
            if (roll < 0)
                roll = 0;
            if (roll > 99)
                roll = 99;
            return roll >= stealthChance;
        }
    }

    public static class DFMPDynamicEnemySensesPolicy
    {
        public static readonly float SightRadius = 4096f * MeshReader.GlobalScale;
        public const float HearingRadius = 25f;
        public const float FieldOfViewDegrees = 180f;

        public static DFMPDynamicEnemySensesDecision Evaluate(DFMPDynamicEnemySensesInput input, DFMPDynamicEnemySensesState state)
        {
            if (state == null)
                throw new ArgumentNullException("state");

            var decision = new DFMPDynamicEnemySensesDecision
            {
                TargetConnectionId = -1,
                LastKnownTargetPosition = state.LastKnownTargetPosition,
                HasLastKnownTargetPosition = state.HasLastKnownTargetPosition,
                LastPositionDiff = state.LastPositionDiff,
                GiveUpTimer = state.GiveUpTimer
            };

            DFMPClassicSensesRanges ranges = DFMPClassicSensesRanges.FromSpawnDistanceType(input.ClassicSpawnDistanceType);
            float classicCarry = state.ClassicUpdateCarry;
            int classicTicks = ConsumeClassicTicks(ref classicCarry, input.DeltaTime);
            state.ClassicUpdateCarry = classicCarry;

            if (classicTicks > 0 && state.LastHadLosTimer > 0f)
                state.LastHadLosTimer = Mathf.Max(0f, state.LastHadLosTimer - classicTicks);

            if (input.IsPassive && input.CurrentTargetConnectionId <= 0 && state.TargetConnectionId <= 0)
            {
                state.TargetConnectionId = -1;
                state.PreviouslyDetected = false;
                DFMPGiveUpTimerPolicy.Advance(ref state.GiveUpTimer, classicTicks, false);
                decision.GiveUpTimer = state.GiveUpTimer;
                return decision;
            }

            UpdateAllEnvelopes(input, ranges, classicTicks, state);

            int preferredTargetId = state.TargetConnectionId > 0 ? state.TargetConnectionId : input.CurrentTargetConnectionId;
            int selectedIndex = -1;

            if (preferredTargetId > 0)
            {
                int preferredIndex = FindTargetIndex(input.Targets, preferredTargetId);
                if (preferredIndex >= 0 && IsBaseValidTarget(input, preferredIndex))
                {
                    bool inEnvelope;
                    state.InClassicEnvelopeByConnectionId.TryGetValue(input.Targets[preferredIndex].ConnectionId, out inEnvelope);
                    bool inSight = ComputeSight(input, preferredIndex);
                    if (inEnvelope || inSight || state.GiveUpTimer > 0)
                        selectedIndex = preferredIndex;
                }
            }

            if (selectedIndex < 0 && !input.IsPassive)
            {
                float bestDistanceSquared = float.MaxValue;
                for (int index = 0; index < (input.Targets != null ? input.Targets.Length : 0); index++)
                {
                    if (!IsBaseValidTarget(input, index))
                        continue;

                    bool inEnvelope;
                    state.InClassicEnvelopeByConnectionId.TryGetValue(input.Targets[index].ConnectionId, out inEnvelope);
                    bool inSight = ComputeSight(input, index);
                    if (!(inEnvelope || inSight))
                        continue;

                    float distanceSquared = GetDistanceSquared(input.EnemyPosition, input.Targets[index].DungeonLocalPosition);
                    if (distanceSquared >= bestDistanceSquared)
                        continue;

                    bestDistanceSquared = distanceSquared;
                    selectedIndex = index;
                }
            }

            if (selectedIndex < 0)
            {
                state.TargetConnectionId = -1;
                state.PreviouslyDetected = false;
                DFMPGiveUpTimerPolicy.Advance(ref state.GiveUpTimer, classicTicks, false);
                decision.GiveUpTimer = state.GiveUpTimer;
                decision.HasLastKnownTargetPosition = state.HasLastKnownTargetPosition;
                decision.LastKnownTargetPosition = state.LastKnownTargetPosition;
                return decision;
            }

            DFMPDynamicEnemySensesTarget selected = input.Targets[selectedIndex];
            state.TargetConnectionId = selected.ConnectionId;

            bool selectedInSight;
            bool selectedDetected;
            ComputeDetection(input, selectedIndex, state, out selectedInSight, out selectedDetected);

            if (selectedDetected)
            {
                bool inEarshot = !selectedInSight && state.PreviouslyDetected && selected.HasHearingClearance && IsWithinHearingRange(input, selected);
                if (selectedInSight || inEarshot)
                {
                    state.LastKnownTargetPosition = selected.DungeonLocalPosition;
                    state.HasLastKnownTargetPosition = true;
                    state.LastHadLosTimer = DFMPGiveUpTimerPolicy.FullTimer;
                }
                else if (state.LastHadLosTimer <= 0f)
                {
                    state.LastKnownTargetPosition = selected.DungeonLocalPosition;
                    state.HasLastKnownTargetPosition = true;
                }

                if (!state.HasEncounteredTarget)
                    state.HasEncounteredTarget = true;
            }

            UpdateLastPositionDiff(state, selectedInSight);

            state.PreviouslyDetected = selectedDetected;
            DFMPGiveUpTimerPolicy.Advance(ref state.GiveUpTimer, classicTicks, selectedDetected);

            decision.HasTarget = true;
            decision.TargetConnectionId = selected.ConnectionId;
            decision.DetectedTarget = selectedDetected;
            decision.TargetInSight = selectedInSight;
            decision.GiveUpTimer = state.GiveUpTimer;
            decision.HasLastKnownTargetPosition = state.HasLastKnownTargetPosition;
            decision.LastKnownTargetPosition = state.LastKnownTargetPosition;
            decision.LastPositionDiff = state.LastPositionDiff;
            decision.CanAct = state.GiveUpTimer > 0 && state.HasLastKnownTargetPosition;
            return decision;
        }

        static void UpdateLastPositionDiff(DFMPDynamicEnemySensesState state, bool targetInSight)
        {
            if (!state.HasLastKnownTargetPosition)
                return;

            if (state.OldLastKnownTargetPosition == Vector3.zero && state.LastKnownTargetPosition != Vector3.zero)
                state.OldLastKnownTargetPosition = state.LastKnownTargetPosition;

            if (targetInSight)
            {
                if (state.AwareOfTargetForLastSightSample)
                    state.LastPositionDiff = state.LastKnownTargetPosition - state.OldLastKnownTargetPosition;

                state.OldLastKnownTargetPosition = state.LastKnownTargetPosition;
                state.AwareOfTargetForLastSightSample = true;
            }
            else
            {
                state.AwareOfTargetForLastSightSample = false;
            }
        }

        public static bool IsWithinFieldOfView(Vector3 enemyPosition, float facingYaw, Vector3 targetPosition)
        {
            return IsWithinYawAngle(enemyPosition, facingYaw, targetPosition, FieldOfViewDegrees * 0.5f);
        }

        public static bool IsWithinYawAngle(Vector3 enemyPosition, float facingYaw, Vector3 targetPosition, float halfAngleDegrees)
        {
            Vector3 toTarget = targetPosition - enemyPosition;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
                return true;

            float yawRadians = facingYaw * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(yawRadians), 0f, Mathf.Cos(yawRadians));
            return Vector3.Angle(toTarget, forward) < halfAngleDegrees;
        }

        public static bool IsWithinSightRange(Vector3 enemyPosition, Vector3 targetPosition, float sightModifier)
        {
            return Vector3.Distance(enemyPosition, targetPosition) < SightRadius + sightModifier;
        }

        public static bool IsWithinHearingRange(DFMPDynamicEnemySensesInput input, DFMPDynamicEnemySensesTarget target)
        {
            return Vector3.Distance(input.EnemyPosition, target.DungeonLocalPosition) < HearingRadius + input.HearingModifier;
        }

        static void UpdateAllEnvelopes(
            DFMPDynamicEnemySensesInput input,
            DFMPClassicSensesRanges ranges,
            int classicTicks,
            DFMPDynamicEnemySensesState state)
        {
            if (input.Targets == null)
                return;

            for (int index = 0; index < input.Targets.Length; index++)
            {
                if (!IsBaseValidTarget(input, index))
                    continue;

                DFMPDynamicEnemySensesTarget target = input.Targets[index];
                bool previousEnvelope;
                state.InClassicEnvelopeByConnectionId.TryGetValue(target.ConnectionId, out previousEnvelope);

                bool inEnvelope = previousEnvelope;
                if (classicTicks > 0)
                {
                    for (int tick = 0; tick < classicTicks; tick++)
                        inEnvelope = DFMPClassicSpawnEnvelope.Evaluate(inEnvelope, input.EnemyPosition, target.DungeonLocalPosition, ranges);
                }
                else if (!state.InClassicEnvelopeByConnectionId.ContainsKey(target.ConnectionId))
                {
                    inEnvelope = DFMPClassicSpawnEnvelope.Evaluate(false, input.EnemyPosition, target.DungeonLocalPosition, ranges);
                }

                state.InClassicEnvelopeByConnectionId[target.ConnectionId] = inEnvelope;
            }
        }

        static bool ComputeSight(DFMPDynamicEnemySensesInput input, int targetIndex)
        {
            DFMPDynamicEnemySensesTarget target = input.Targets[targetIndex];
            return IsWithinSightRange(input.EnemyPosition, target.DungeonLocalPosition, input.SightModifier)
                && IsWithinFieldOfView(input.EnemyPosition, input.FacingYaw, target.DungeonLocalPosition)
                && target.HasSightClearance;
        }

        static void ComputeDetection(
            DFMPDynamicEnemySensesInput input,
            int targetIndex,
            DFMPDynamicEnemySensesState state,
            out bool inSight,
            out bool detected)
        {
            DFMPDynamicEnemySensesTarget target = input.Targets[targetIndex];
            inSight = ComputeSight(input, targetIndex);

            bool inEarshot = false;
            if (state.PreviouslyDetected && !inSight && IsWithinHearingRange(input, target) && target.HasHearingClearance)
                inEarshot = true;

            bool stealthDetected = false;
            if (!inSight && !inEarshot)
            {
                bool inEnvelope;
                state.InClassicEnvelopeByConnectionId.TryGetValue(target.ConnectionId, out inEnvelope);
                float distance = Vector3.Distance(input.EnemyPosition, target.DungeonLocalPosition);
                uint lastStealthMinute = state.LastStealthCheckMinute;
                stealthDetected = DFMPStealthCheckPolicy.Evaluate(
                    inEnvelope,
                    distance,
                    target.StealthSkill,
                    input.ClassicGameMinutes,
                    target.MovingLessThanHalfSpeed,
                    state.HasEncounteredTarget,
                    state.PreviouslyDetected && state.TargetConnectionId == target.ConnectionId,
                    ref lastStealthMinute,
                    input.StealthRoll);
                state.LastStealthCheckMinute = lastStealthMinute;
            }

            detected = inSight || inEarshot || stealthDetected;
        }

        static bool IsBaseValidTarget(DFMPDynamicEnemySensesInput input, int targetIndex)
        {
            if (input.Targets == null || targetIndex < 0 || targetIndex >= input.Targets.Length)
                return false;

            DFMPDynamicEnemySensesTarget target = input.Targets[targetIndex];
            if (target.ConnectionId <= 0 || !target.SpawnConfirmed || target.IsDead)
                return false;
            if (target.ConnectionId == input.IgnoredTargetConnectionId)
                return false;
            return true;
        }

        static int FindTargetIndex(DFMPDynamicEnemySensesTarget[] targets, int connectionId)
        {
            if (targets == null)
                return -1;
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index].ConnectionId == connectionId)
                    return index;
            }

            return -1;
        }

        static int ConsumeClassicTicks(ref float classicUpdateCarry, float deltaTime)
        {
            if (deltaTime <= 0f)
                return 0;

            classicUpdateCarry += deltaTime;
            int ticks = 0;
            while (classicUpdateCarry >= DFMPGiveUpTimerPolicy.ClassicUpdateInterval)
            {
                classicUpdateCarry -= DFMPGiveUpTimerPolicy.ClassicUpdateInterval;
                ticks++;
            }

            return ticks;
        }

        static float GetDistanceSquared(Vector3 first, Vector3 second)
        {
            return (second - first).sqrMagnitude;
        }
    }
}

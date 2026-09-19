using System;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPDynamicEnemyMotorState
    {
        public int SearchMult;
        public float AvoidObstaclesTimer;
        public Vector3 DetourDestination;
        public Vector3 CurrentDestination;
        public bool CheckingClockwise;
        public float CheckingClockwiseTimer;
        public bool DidClockwiseCheck;
        public float LastTimeWasStuck;
        public bool HasLastKnownDoor;
        public ulong LastKnownDoorLoadId;
        public Vector3 LastKnownDoorPosition;
        public float DistanceToDoor;
        public bool LastDetourFailed;

        public void Reset()
        {
            SearchMult = 0;
            AvoidObstaclesTimer = 0f;
            DetourDestination = Vector3.zero;
            CurrentDestination = Vector3.zero;
            CheckingClockwise = false;
            CheckingClockwiseTimer = 0f;
            DidClockwiseCheck = false;
            LastTimeWasStuck = 0f;
            HasLastKnownDoor = false;
            LastKnownDoorLoadId = 0UL;
            LastKnownDoorPosition = Vector3.zero;
            DistanceToDoor = 0f;
            LastDetourFailed = false;
        }
    }

    public struct DFMPObstacleProbeResult
    {
        public bool ObstacleDetected;
        public bool FallDetected;
        public bool FoundUpwardSlope;
        public bool FoundDoor;
        public ulong DoorLoadId;
        public Vector3 DoorDungeonLocalPosition;
    }

    public delegate DFMPObstacleProbeResult DFMPObstacleProbe(Vector3 direction);

    public struct DFMPDynamicEnemyPursuitInput
    {
        public Vector3 EnemyPosition;
        public Vector3 PredictedTargetPosition;
        public Vector3 LastKnownTargetPosition;
        public Vector3 LastPositionDiff;
        public bool HasLastKnownTargetPosition;
        public bool HasClearPathToPredictedTarget;
        public bool TargetInSight;
        public bool CanUseRangedOrMagicPursuit;
        public bool IsFlying;
        public float StopDistance;
        public float DeltaTime;
        public bool CanAct;
        public float CurrentTime;
    }

    public static class DFMPDynamicEnemyPursuitPolicy
    {
        public const float NativeStopDistance = 1.7f;
        public const float DetourArrivalDistance = 0.3f;
        public const int MaximumSearchMult = 10;

        public static Vector3 SelectDestination(DFMPDynamicEnemyPursuitInput input, DFMPDynamicEnemyMotorState motor)
        {
            if (motor == null)
                throw new ArgumentNullException("motor");

            TickTimers(input, motor);

            Vector3 destination;
            if (motor.AvoidObstaclesTimer > 0f)
            {
                destination = motor.DetourDestination;
            }
            else if (!input.HasLastKnownTargetPosition)
            {
                destination = input.EnemyPosition;
                motor.SearchMult = 0;
            }
            else if (input.HasClearPathToPredictedTarget || (input.TargetInSight && input.CanUseRangedOrMagicPursuit))
            {
                destination = input.PredictedTargetPosition;
                motor.SearchMult = 0;
            }
            else
            {
                Vector3 travel = input.LastPositionDiff;
                if (travel.sqrMagnitude <= 0.0001f)
                    travel = Vector3.zero;
                else
                    travel = travel.normalized;

                Vector3 searchPosition = input.LastKnownTargetPosition + travel * motor.SearchMult;
                if (motor.SearchMult <= MaximumSearchMult && (searchPosition - input.EnemyPosition).magnitude <= Mathf.Max(0.01f, input.StopDistance))
                    motor.SearchMult++;

                destination = searchPosition;
            }

            if (motor.AvoidObstaclesTimer <= 0f && !input.IsFlying)
                destination.y = input.EnemyPosition.y;

            motor.CurrentDestination = destination;
            return destination;
        }

        static void TickTimers(DFMPDynamicEnemyPursuitInput input, DFMPDynamicEnemyMotorState motor)
        {
            if (input.DeltaTime > 0f)
            {
                if (motor.AvoidObstaclesTimer > 0f)
                    motor.AvoidObstaclesTimer = Mathf.Max(0f, motor.AvoidObstaclesTimer - input.DeltaTime);
                if (motor.CheckingClockwiseTimer > 0f)
                    motor.CheckingClockwiseTimer = Mathf.Max(0f, motor.CheckingClockwiseTimer - input.DeltaTime);
            }

            if (motor.AvoidObstaclesTimer > 0f && input.CanAct)
            {
                Vector3 detourDestination2D = motor.DetourDestination;
                detourDestination2D.y = input.EnemyPosition.y;
                Vector3 enemy2D = input.EnemyPosition;
                enemy2D.y = input.EnemyPosition.y;
                if ((detourDestination2D - enemy2D).magnitude <= DetourArrivalDistance)
                    motor.AvoidObstaclesTimer = 0f;
            }
        }
    }

    public static class DFMPDynamicEnemyDetourPolicy
    {
        public const float AvoidObstaclesSeconds = 0.75f;
        public const float ClockwiseResetClearSeconds = 2f;
        public const float ClockwiseStickSeconds = 5f;
        public const float DetourDistance = 2f;
        public const float FlyingVerticalMix = 0.3f;
        public const int MaximumSweepCount = 7;

        public static bool ShouldFindDetour(bool obstacleDetected, bool fallDetected)
        {
            return obstacleDetected || fallDetected;
        }

        public static void FindDetour(
            DFMPDynamicEnemyMotorState motor,
            Vector3 enemyPosition,
            Vector3 destination,
            Vector3 direction2d,
            bool fliesOrSwimsOrLevitates,
            float currentTime,
            int firstHorizontalAngleDegrees,
            int verticalSign,
            DFMPObstacleProbe probe)
        {
            if (motor == null)
                throw new ArgumentNullException("motor");
            if (probe == null)
                throw new ArgumentNullException("probe");

            float angle;
            Vector3 testMove = Vector3.zero;
            bool foundUpDown = false;
            motor.LastDetourFailed = false;

            if (fliesOrSwimsOrLevitates)
            {
                float multiplier = verticalSign < 0 ? -FlyingVerticalMix : FlyingVerticalMix;
                Vector3 upOrDown = new Vector3(0f, multiplier, 0f);
                testMove = (direction2d + upOrDown).normalized;
                DFMPObstacleProbeResult result = probe(testMove);
                if (result.ObstacleDetected)
                {
                    upOrDown.y *= -1f;
                    testMove = (direction2d + upOrDown).normalized;
                    result = probe(testMove);
                }

                if (!result.ObstacleDetected)
                    foundUpDown = true;
            }

            if (!foundUpDown && currentTime - motor.LastTimeWasStuck > ClockwiseResetClearSeconds)
            {
                motor.CheckingClockwiseTimer = 0f;
                motor.DidClockwiseCheck = false;
            }

            if (!foundUpDown && motor.CheckingClockwiseTimer <= 0f)
            {
                if (!motor.DidClockwiseCheck)
                {
                    angle = firstHorizontalAngleDegrees >= 0 ? 45f : -45f;
                    testMove = Quaternion.AngleAxis(angle, Vector3.up) * direction2d;
                    DFMPObstacleProbeResult result = CombinedProbe(probe, testMove);
                    if (!result.ObstacleDetected && !result.FallDetected)
                    {
                        motor.CheckingClockwise = angle == 45f;
                    }
                    else
                    {
                        angle *= -1f;
                        testMove = Quaternion.AngleAxis(angle, Vector3.up) * direction2d;
                        result = CombinedProbe(probe, testMove);
                        if (!result.ObstacleDetected && !result.FallDetected)
                        {
                            motor.CheckingClockwise = angle == 45f;
                        }
                        else
                        {
                            Vector3 toTarget = destination - enemyPosition;
                            Vector3 directionToTarget = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : direction2d;
                            float signed = Vector3.SignedAngle(directionToTarget, direction2d, Vector3.up);
                            motor.CheckingClockwise = signed > 0f;
                        }
                    }

                    motor.CheckingClockwiseTimer = ClockwiseStickSeconds;
                    motor.DidClockwiseCheck = true;
                }
                else
                {
                    motor.DidClockwiseCheck = false;
                    motor.CheckingClockwise = !motor.CheckingClockwise;
                    motor.CheckingClockwiseTimer = ClockwiseStickSeconds;
                }
            }

            angle = 0f;
            int count = 0;
            if (!foundUpDown)
            {
                DFMPObstacleProbeResult result;
                do
                {
                    if (motor.CheckingClockwise)
                        angle += 45f;
                    else
                        angle -= 45f;

                    testMove = Quaternion.AngleAxis(angle, Vector3.up) * direction2d;
                    result = CombinedProbe(probe, testMove);
                    count++;
                    if (count > MaximumSweepCount)
                        break;
                }
                while (result.ObstacleDetected || result.FallDetected);

                motor.LastDetourFailed = result.ObstacleDetected || result.FallDetected;
            }

            motor.DetourDestination = enemyPosition + testMove * DetourDistance;
            if (motor.AvoidObstaclesTimer <= 0f)
                motor.AvoidObstaclesTimer = AvoidObstaclesSeconds;
            motor.LastTimeWasStuck = currentTime;
        }

        static DFMPObstacleProbeResult CombinedProbe(DFMPObstacleProbe probe, Vector3 direction)
        {
            return probe(direction);
        }
    }

    public static class DFMPDynamicEnemyDoorPolicy
    {
        public const float OpenDoorDistance = 2f;
        public const float DoorYawHalfAngle = 22.5f;

        public static bool ShouldRememberDoor(bool foundDoor, ulong doorLoadId, bool withinYaw)
        {
            return foundDoor && doorLoadId != 0UL && withinYaw;
        }

        public static bool ShouldOpenDoor(bool canOpenDoors, bool hasLastKnownDoor, bool isOpen, bool isLocked, float distanceToDoor)
        {
            return canOpenDoors &&
                hasLastKnownDoor &&
                !isOpen &&
                !isLocked &&
                distanceToDoor < OpenDoorDistance;
        }
    }
}

using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPDungeonGeometryScopeKey : IEquatable<DFMPDungeonGeometryScopeKey>
    {
        public int MapPixelX;
        public int MapPixelY;
        public int RegionIndex;
        public int LocationIndex;
        public string LocationId;
        public string InstanceId;

        public bool Equals(DFMPDungeonGeometryScopeKey other)
        {
            return MapPixelX == other.MapPixelX &&
                MapPixelY == other.MapPixelY &&
                RegionIndex == other.RegionIndex &&
                LocationIndex == other.LocationIndex &&
                string.Equals(LocationId ?? string.Empty, other.LocationId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(InstanceId ?? string.Empty, other.InstanceId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is DFMPDungeonGeometryScopeKey && Equals((DFMPDungeonGeometryScopeKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MapPixelX;
                hash = (hash * 397) ^ MapPixelY;
                hash = (hash * 397) ^ RegionIndex;
                hash = (hash * 397) ^ LocationIndex;
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(LocationId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(InstanceId ?? string.Empty);
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Format("DungeonGeometry:{0}/{1}:{2}/{3}:{4}:{5}", MapPixelX, MapPixelY, RegionIndex, LocationIndex, LocationId ?? string.Empty, InstanceId ?? string.Empty);
        }
    }

    public sealed class DFMPDungeonGeometryService : MonoBehaviour
    {
        sealed class HostedDungeonGeometry
        {
            public DFMPDungeonGeometryScopeKey Scope;
            public int OccupantCount;
            public GameObject Root;
            public bool GeometryAvailable;
        }

        readonly Dictionary<DFMPDungeonGeometryScopeKey, HostedDungeonGeometry> hostedGeometryByScope = new Dictionary<DFMPDungeonGeometryScopeKey, HostedDungeonGeometry>();
        readonly Dictionary<int, DFMPDungeonGeometryScopeKey> scopeByConnectionId = new Dictionary<int, DFMPDungeonGeometryScopeKey>();
        readonly HashSet<ulong> missingActionDoorWarnings = new HashSet<ulong>();
        bool isSubscribed;
        float nextUngroundedLogTime;

        public int HostedScopeCount
        {
            get { return hostedGeometryByScope.Count; }
        }

        public void Initialize()
        {
            Subscribe();
        }

        void Awake()
        {
            Subscribe();
        }

        void OnDestroy()
        {
            Clear();
            if (!isSubscribed)
                return;

            DFMPEventBus.Instance.PlayerWorldContextChanged -= OnPlayerWorldContextChanged;
            isSubscribed = false;
        }

        void Subscribe()
        {
            if (isSubscribed)
                return;

            DFMPEventBus.Instance.PlayerWorldContextChanged += OnPlayerWorldContextChanged;
            isSubscribed = true;
        }

        public static bool TryCreateScope(DFMPWorldContextKey context, out DFMPDungeonGeometryScopeKey scope)
        {
            scope = new DFMPDungeonGeometryScopeKey();
            if (context.Kind != DFMPWorldContextKind.Dungeon ||
                string.IsNullOrWhiteSpace(context.LocationId) ||
                context.RegionIndex < 0 ||
                context.LocationIndex < 0)
                return false;

            scope = new DFMPDungeonGeometryScopeKey
            {
                MapPixelX = context.MapPixelX,
                MapPixelY = context.MapPixelY,
                RegionIndex = context.RegionIndex,
                LocationIndex = context.LocationIndex,
                LocationId = context.LocationId ?? string.Empty,
                InstanceId = context.InstanceId ?? string.Empty
            };
            return true;
        }

        public bool TryGetRoot(DFMPDungeonGeometryScopeKey scope, out GameObject root)
        {
            HostedDungeonGeometry hostedGeometry;
            if (hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) && hostedGeometry != null && hostedGeometry.Root != null)
            {
                root = hostedGeometry.Root;
                return true;
            }

            root = null;
            return false;
        }

        public int GetOccupantCount(DFMPDungeonGeometryScopeKey scope)
        {
            HostedDungeonGeometry hostedGeometry;
            return hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) && hostedGeometry != null ? hostedGeometry.OccupantCount : 0;
        }

        public bool TryHasLineOfSight(DFMPWorldContextKey context, Vector3 fromDungeonLocalPosition, Vector3 toDungeonLocalPosition, out bool hasLineOfSight)
        {
            return TryHasLineOfSight(context, fromDungeonLocalPosition, toDungeonLocalPosition, false, out hasLineOfSight);
        }

        public bool TryHasLineOfSight(
            DFMPWorldContextKey context,
            Vector3 fromDungeonLocalPosition,
            Vector3 toDungeonLocalPosition,
            bool ignoreActionDoors,
            out bool hasLineOfSight)
        {
            hasLineOfSight = false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null || !hostedGeometry.GeometryAvailable)
                return false;

            Vector3 fromWorldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, fromDungeonLocalPosition + Vector3.up);
            Vector3 toWorldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, toDungeonLocalPosition + Vector3.up);
            hasLineOfSight = !HasHostedGeometryBlocker(hostedGeometry.Root.transform, fromWorldPosition, toWorldPosition, ignoreActionDoors);
            return true;
        }

        public bool TryProbeMovementHazards(
            DFMPWorldContextKey context,
            Vector3 fromDungeonLocalPosition,
            Vector3 direction,
            float radius,
            float height,
            bool isFlying,
            out DFMPObstacleProbeResult result)
        {
            result = new DFMPObstacleProbeResult();
            if (direction.sqrMagnitude <= 0.0001f)
                return false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null || !hostedGeometry.GeometryAvailable)
                return false;

            radius = Mathf.Max(0.01f, radius);
            height = Mathf.Max(radius * 2f, height);
            Transform root = hostedGeometry.Root.transform;
            Vector3 centerWorld = DungeonLocalToCapsuleCenterWorldPosition(root, fromDungeonLocalPosition, height);
            Vector3 dirWorld = root.TransformDirection(direction.normalized);
            ProbeObstacle(root, centerWorld, dirWorld, radius, height, isFlying, ref result);
            ProbeFall(root, centerWorld, dirWorld, height, isFlying, ref result);
            return true;
        }

        /// <summary>
        /// Native <c>EnemyMotor.ObstacleCheck</c> and <c>FallCheck</c> probe from the character capsule center
        /// (its p1 lands at the documented 0.65 climbable step height, and its fall ray reaches 1.5 below the feet).
        /// Hosted enemy descriptors carry the grounded position, so lift the origin by half the capsule height.
        /// </summary>
        static Vector3 DungeonLocalToCapsuleCenterWorldPosition(Transform root, Vector3 groundedDungeonLocalPosition, float height)
        {
            return DungeonLocalToHostedWorldPosition(root, groundedDungeonLocalPosition) + Vector3.up * (height * 0.5f);
        }

        public bool TryHasClearPathToPosition(
            DFMPWorldContextKey context,
            Vector3 fromDungeonLocalPosition,
            Vector3 toDungeonLocalPosition,
            float radius,
            float height,
            bool isFlying,
            out bool hasClearPath)
        {
            hasClearPath = false;
            Vector3 offset = toDungeonLocalPosition - fromDungeonLocalPosition;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
            {
                hasClearPath = true;
                return true;
            }

            Vector3 direction = offset / distance;
            Vector3 direction2d = direction;
            if (!isFlying)
                direction2d.y = 0f;
            if (direction2d.sqrMagnitude > 0.0001f)
                direction2d.Normalize();
            else
                direction2d = direction;

            DFMPObstacleProbeResult hazards;
            if (!TryProbeMovementHazards(context, fromDungeonLocalPosition, direction2d, radius, height, isFlying, out hazards))
                return false;

            if (hazards.ObstacleDetected || hazards.FallDetected)
            {
                hasClearPath = false;
                return true;
            }

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null)
                return false;

            Transform root = hostedGeometry.Root.transform;
            Vector3 centerWorld = DungeonLocalToCapsuleCenterWorldPosition(root, fromDungeonLocalPosition, height);
            Vector3 dirWorld = root.TransformDirection(direction);
            RaycastHit[] hits = Physics.SphereCastAll(centerWorld, Mathf.Max(0.01f, radius / 2f), dirWorld, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (!IsHostedStaticBlocker(root, hitCollider, false))
                    continue;

                hasClearPath = false;
                return true;
            }

            hasClearPath = true;
            return true;
        }

        public bool TryFindOpenableActionDoor(
            DFMPWorldContextKey context,
            Vector3 enemyDungeonLocalPosition,
            float maxDistance,
            out ulong loadId,
            out Vector3 doorDungeonLocalPosition,
            out float distance,
            out bool isOpen,
            out bool isLocked)
        {
            loadId = 0UL;
            doorDungeonLocalPosition = Vector3.zero;
            distance = 0f;
            isOpen = false;
            isLocked = false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null)
                return false;

            DaggerfallActionDoor[] doors = hostedGeometry.Root.GetComponentsInChildren<DaggerfallActionDoor>(true);
            float nearest = float.MaxValue;
            DaggerfallActionDoor nearestDoor = null;
            Vector3 nearestLocal = Vector3.zero;
            for (int index = 0; index < doors.Length; index++)
            {
                DaggerfallActionDoor door = doors[index];
                if (door == null || door.LoadID == 0UL)
                    continue;

                Vector3 doorLocal = hostedGeometry.Root.transform.InverseTransformPoint(door.transform.position);
                float doorDistance = Vector3.Distance(enemyDungeonLocalPosition, doorLocal);
                if (doorDistance >= nearest || doorDistance > maxDistance)
                    continue;

                nearest = doorDistance;
                nearestDoor = door;
                nearestLocal = doorLocal;
            }

            if (nearestDoor == null)
                return false;

            loadId = nearestDoor.LoadID;
            doorDungeonLocalPosition = nearestLocal;
            distance = nearest;
            isOpen = nearestDoor.IsOpen;
            isLocked = nearestDoor.IsLocked;
            return true;
        }

        public bool TryGetActionDoorState(DFMPWorldContextKey context, ulong loadId, out bool isOpen, out bool isLocked, out Vector3 doorDungeonLocalPosition)
        {
            isOpen = false;
            isLocked = false;
            doorDungeonLocalPosition = Vector3.zero;
            if (loadId == 0UL)
                return false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null)
                return false;

            DaggerfallActionDoor[] doors = hostedGeometry.Root.GetComponentsInChildren<DaggerfallActionDoor>(true);
            for (int index = 0; index < doors.Length; index++)
            {
                DaggerfallActionDoor door = doors[index];
                if (door == null || door.LoadID != loadId)
                    continue;

                isOpen = door.IsOpen;
                isLocked = door.IsLocked;
                doorDungeonLocalPosition = hostedGeometry.Root.transform.InverseTransformPoint(door.transform.position);
                return true;
            }

            return false;
        }

        static void ProbeObstacle(
            Transform root,
            Vector3 centerWorld,
            Vector3 dirWorld,
            float radius,
            float height,
            bool isFlying,
            ref DFMPObstacleProbeResult result)
        {
            const float doorCrouchingHeight = 1.65f;
            float checkDistance = radius / Mathf.Sqrt(2f);
            Vector3 p1 = centerWorld + Vector3.up * (-height * 0.1388f);
            Vector3 p2 = p1 + Vector3.up * (Mathf.Min(height, doorCrouchingHeight) / 2f);
            RaycastHit[] hits = Physics.CapsuleCastAll(p1, p2, radius / 2f, dirWorld, checkDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit;
            if (!TryGetNearestHostedHit(root, hits, out hit, false))
                return;

            DaggerfallActionDoor door = hit.transform.GetComponent<DaggerfallActionDoor>();
            DaggerfallLoot loot = hit.transform.GetComponent<DaggerfallLoot>();
            if (door != null)
            {
                result.FoundDoor = true;
                result.DoorLoadId = door.LoadID;
                result.DoorDungeonLocalPosition = root.InverseTransformPoint(door.transform.position);
                return;
            }

            if (loot != null)
                return;

            result.ObstacleDetected = true;
            if (isFlying)
                return;

            Vector3 checkUp = centerWorld + dirWorld;
            checkUp.y += 1f;
            Vector3 slopeDir = (checkUp - centerWorld).normalized;
            Vector3 slopeP1 = centerWorld + Vector3.up * (-height * 0.25f);
            Vector3 slopeP2 = slopeP1 + Vector3.up * (height * 0.75f);
            // Native uses the same checkDistance for this recast, but that distance is only the horizontal
            // component of a 45° climb. The first cast already spent that budget reaching the hit, so the
            // recast is systematically short of a vertical wall and false-clears ObstacleDetected. Match the
            // first cast's total reach along the climb direction instead.
            float slopeCheckDistance = checkDistance * Mathf.Sqrt(2f);
            RaycastHit[] slopeHits = Physics.CapsuleCastAll(slopeP1, slopeP2, radius / 2f, slopeDir, slopeCheckDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit slopeHit;
            if (!TryGetNearestHostedHit(root, slopeHits, out slopeHit, false))
            {
                result.ObstacleDetected = false;
                result.FoundUpwardSlope = true;
            }
        }

        static void ProbeFall(
            Transform root,
            Vector3 centerWorld,
            Vector3 dirWorld,
            float height,
            bool isFlying,
            ref DFMPObstacleProbeResult result)
        {
            if (isFlying || result.ObstacleDetected || result.FoundUpwardSlope || result.FoundDoor)
            {
                result.FallDetected = false;
                return;
            }

            Vector3 rayOrigin = centerWorld + dirWorld.normalized;

            // Native drops this ray a full unit ahead while ObstacleCheck only reaches about a quarter unit, so a
            // wall between the enemy and the ray origin leaves the ray falling outside the dungeon shell and
            // reporting a ledge that is not there. The detour sweep then rejects good headings, which reads as an
            // enemy stuck at a 90 degree corner. A point we cannot reach says nothing about a drop, so require the
            // span to be clear before trusting the verdict; genuine ledges still have open air across that span.
            if (HasHostedGeometryBlocker(root, centerWorld, rayOrigin, false))
            {
                result.FallDetected = false;
                return;
            }

            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, (height * 0.5f) + 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit;
            result.FallDetected = !TryGetNearestHostedHit(root, hits, out hit, false);
        }

        static bool TryGetNearestHostedHit(Transform root, RaycastHit[] hits, out RaycastHit nearest, bool ignoreDoorsAndLoot)
        {
            nearest = default(RaycastHit);
            float nearestDistance = float.MaxValue;
            bool found = false;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (!IsHostedStaticBlocker(root, hitCollider, ignoreDoorsAndLoot))
                    continue;

                if (hits[index].distance >= nearestDistance)
                    continue;

                nearestDistance = hits[index].distance;
                nearest = hits[index];
                found = true;
            }

            return found;
        }

        static bool IsHostedStaticBlocker(Transform root, Collider hitCollider, bool ignoreDoorsAndLoot)
        {
            if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(root))
                return false;

            if (!ignoreDoorsAndLoot)
                return true;

            if (hitCollider.GetComponent<DaggerfallActionDoor>() != null)
                return false;
            if (hitCollider.GetComponent<DaggerfallLoot>() != null)
                return false;
            return true;
        }

        public bool TryApplyActionDoor(DFMPWorldContextKey context, ulong loadID, bool isOpen)
        {
            if (loadID == 0)
                return false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null)
                return false;

            DaggerfallActionDoor[] doors = hostedGeometry.Root.GetComponentsInChildren<DaggerfallActionDoor>(true);
            for (int index = 0; index < doors.Length; index++)
            {
                DaggerfallActionDoor door = doors[index];
                if (door == null || door.LoadID != loadID)
                    continue;

                door.PlaySounds = false;
                door.SetOpen(isOpen, true);
                ApplyHostedActionDoorCollider(door, isOpen);
                return true;
            }

            if (missingActionDoorWarnings.Add(loadID))
                Debug.LogWarning($"[DFMP Dungeon Geometry] Hosted action door not found: loadID={loadID}, scope={scope}.");

            return false;
        }

        static void ApplyHostedActionDoorCollider(DaggerfallActionDoor door, bool isOpen)
        {
            if (door == null)
                return;

            door.CurrentState = isOpen ? ActionState.End : ActionState.Start;
            BoxCollider boxCollider = door.GetComponent<BoxCollider>();
            if (boxCollider != null && boxCollider.enabled)
                boxCollider.isTrigger = isOpen;
        }

        public bool TryResolveMovement(DFMPWorldContextKey context, Vector3 fromDungeonLocalPosition, Vector3 desiredDungeonLocalPosition, float radius, float height, out Vector3 resolvedDungeonLocalPosition, out bool blocked)
        {
            resolvedDungeonLocalPosition = fromDungeonLocalPosition;
            blocked = false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null || !hostedGeometry.GeometryAvailable)
                return false;

            Vector3 offset = desiredDungeonLocalPosition - fromDungeonLocalPosition;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
                return true;

            radius = Mathf.Max(0.01f, radius);
            height = Mathf.Max(radius * 2f, height);
            Transform root = hostedGeometry.Root.transform;
            Vector3 currentWorldPosition = DungeonLocalToHostedWorldPosition(root, fromDungeonLocalPosition);
            Vector3 remaining = root.TransformDirection(offset);

            // Native enemies move through CharacterController.Move, which deflects leftover motion along whatever it
            // hits instead of stopping dead. A detour heading is only probed a quarter-unit ahead, so it usually still
            // points partly into the obstacle; without this slide a detouring enemy converts its whole step into a
            // flush stop and stays pinned to the wall forever.
            const int maximumSlideIterations = 3;
            for (int iteration = 0; iteration < maximumSlideIterations; iteration++)
            {
                float remainingDistance = remaining.magnitude;
                if (remainingDistance <= 0.0001f)
                    break;

                Vector3 stepDirection = remaining / remainingDistance;
                RaycastHit blockerHit;
                if (!TryGetNearestMovementBlocker(root, currentWorldPosition, stepDirection, remainingDistance, radius, height, out blockerHit))
                {
                    currentWorldPosition += remaining;
                    remaining = Vector3.zero;
                    break;
                }

                blocked = true;

                // The skin must exceed Unity's default collider contact offset, or the deflected sweep below starts
                // inside the contact band and reports a zero-distance hit, which cancels the slide.
                const float collisionSkin = 0.05f;
                float safeDistance = Mathf.Max(0f, blockerHit.distance - collisionSkin);
                currentWorldPosition += stepDirection * safeDistance;

                Vector3 slideNormal = blockerHit.normal;
                slideNormal.y = 0f;
                if (slideNormal.sqrMagnitude <= 0.0001f)
                {
                    remaining = Vector3.zero;
                    break;
                }

                remaining = Vector3.ProjectOnPlane(stepDirection * (remainingDistance - safeDistance), slideNormal.normalized);
            }

            resolvedDungeonLocalPosition = blocked
                ? root.InverseTransformPoint(currentWorldPosition)
                : desiredDungeonLocalPosition;
            return true;
        }

        static bool TryGetNearestMovementBlocker(
            Transform root,
            Vector3 fromWorldPosition,
            Vector3 direction,
            float distance,
            float radius,
            float height,
            out RaycastHit nearest)
        {
            nearest = new RaycastHit();
            Vector3 bottom = fromWorldPosition + Vector3.up * radius;
            Vector3 top = fromWorldPosition + Vector3.up * (height - radius);
            RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearestHitDistance = distance;
            bool foundBlocker = false;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(root))
                    continue;

                // Floors and walkable ramps are carried by the grounding pass, not the horizontal sweep.
                if (hits[index].normal.y >= 0.5f)
                    continue;

                if (hits[index].distance < nearestHitDistance)
                {
                    nearestHitDistance = hits[index].distance;
                    nearest = hits[index];
                    foundBlocker = true;
                }
            }

            return foundBlocker;
        }

        public bool TryResolveGroundedMovement(DFMPWorldContextKey context, Vector3 fromDungeonLocalPosition, Vector3 desiredDungeonLocalPosition, float radius, float height, out Vector3 resolvedDungeonLocalPosition, out bool blocked)
        {
            resolvedDungeonLocalPosition = fromDungeonLocalPosition;
            blocked = false;

            // Grounded enemies may only climb a short step, and may only descend as steeply as a ramp; anything
            // steeper is a ledge they refuse to walk off rather than teleporting to the floor far below.
            const float maximumGroundStepUp = 2f;
            Vector3 groundedFrom = fromDungeonLocalPosition;
            Vector3 queriedGroundedFrom;
            bool foundFromGround;
            if (TryResolveGroundedDungeonLocalPosition(context, fromDungeonLocalPosition, out queriedGroundedFrom, out foundFromGround) &&
                foundFromGround &&
                queriedGroundedFrom.y <= fromDungeonLocalPosition.y + maximumGroundStepUp)
                groundedFrom = queriedGroundedFrom;

            Vector3 groundedDestination;
            bool foundDestinationGround;
            if (!TryResolveGroundedDungeonLocalPosition(context, desiredDungeonLocalPosition, out groundedDestination, out foundDestinationGround) || !foundDestinationGround)
            {
                LogUngroundedMovement("destination-ground-missing", fromDungeonLocalPosition, groundedFrom, foundFromGround, desiredDungeonLocalPosition, desiredDungeonLocalPosition, float.NaN);

                // An enemy with no floor underfoot has no ledge to refuse, so fall back to the plain collision sweep.
                if (!foundFromGround)
                    return TryResolveMovement(context, fromDungeonLocalPosition, desiredDungeonLocalPosition, radius, height, out resolvedDungeonLocalPosition, out blocked);

                resolvedDungeonLocalPosition = groundedFrom;
                blocked = true;
                return true;
            }

            if (foundFromGround && groundedDestination.y < groundedFrom.y - GetMaximumGroundDescent(groundedFrom, desiredDungeonLocalPosition))
            {
                LogUngroundedMovement("destination-ground-below-descent-limit", fromDungeonLocalPosition, groundedFrom, foundFromGround, desiredDungeonLocalPosition, groundedFrom, groundedDestination.y);
                resolvedDungeonLocalPosition = groundedFrom;
                blocked = true;
                return true;
            }

            // Sweep at a walkable height so a ledge face cannot block the descent; the re-ground below applies the drop.
            Vector3 sweepDestination = groundedDestination;
            if (sweepDestination.y < groundedFrom.y || sweepDestination.y > groundedFrom.y + maximumGroundStepUp)
                sweepDestination.y = groundedFrom.y;

            const float groundedProbeClearance = 0.1f;
            if (!TryResolveMovement(
                context,
                groundedFrom + Vector3.up * groundedProbeClearance,
                sweepDestination + Vector3.up * groundedProbeClearance,
                radius,
                height,
                out resolvedDungeonLocalPosition,
                out blocked))
                return false;

            // The clearance only lifts the collision capsule off the floor; leaving it in the result makes it accumulate every tick.
            resolvedDungeonLocalPosition.y -= groundedProbeClearance;

            Vector3 regroundedPosition;
            bool foundResolvedGround;
            float maximumDescent = GetMaximumGroundDescent(groundedFrom, resolvedDungeonLocalPosition);
            bool descendedPastLedge = false;
            if (TryResolveGroundedDungeonLocalPosition(context, resolvedDungeonLocalPosition, out regroundedPosition, out foundResolvedGround) &&
                foundResolvedGround &&
                regroundedPosition.y <= groundedFrom.y + maximumGroundStepUp &&
                (!foundFromGround || regroundedPosition.y >= groundedFrom.y - maximumDescent))
            {
                resolvedDungeonLocalPosition = regroundedPosition;
            }
            else
            {
                string reason;
                if (!foundResolvedGround)
                    reason = "resolved-ground-missing";
                else if (regroundedPosition.y > groundedFrom.y + maximumGroundStepUp)
                    reason = "resolved-ground-above-step-limit";
                else
                {
                    reason = "resolved-ground-below-descent-limit";
                    descendedPastLedge = true;
                }

                LogUngroundedMovement(
                    reason,
                    fromDungeonLocalPosition,
                    groundedFrom,
                    foundFromGround,
                    desiredDungeonLocalPosition,
                    descendedPastLedge ? groundedFrom : resolvedDungeonLocalPosition,
                    foundResolvedGround ? regroundedPosition.y : float.NaN);

                if (descendedPastLedge)
                {
                    resolvedDungeonLocalPosition = groundedFrom;
                    blocked = true;
                }
            }

            return true;
        }

        // A step down is a walkable ramp only while it stays within this slope; beyond it the floor below is a ledge.
        static float GetMaximumGroundDescent(Vector3 fromDungeonLocalPosition, Vector3 toDungeonLocalPosition)
        {
            const float stepLipTolerance = 0.5f;
            const float descentSlope = 2f;
            Vector3 travel = toDungeonLocalPosition - fromDungeonLocalPosition;
            travel.y = 0f;
            return stepLipTolerance + travel.magnitude * descentSlope;
        }

        void LogUngroundedMovement(string reason, Vector3 from, Vector3 groundedFrom, bool foundFromGround, Vector3 desired, Vector3 resolved, float regroundedY)
        {
            if (Time.unscaledTime < nextUngroundedLogTime)
                return;

            nextUngroundedLogTime = Time.unscaledTime + 1f;
            Debug.LogWarning($"[DFMP Dungeon Geometry] Grounded movement left enemy ungrounded: reason={reason}, from={from}, groundedFrom={groundedFrom}, foundFromGround={foundFromGround}, desired={desired}, resolved={resolved}, regroundedY={regroundedY}.");
        }

        public bool TryMarkGeometryAvailableForTesting(DFMPDungeonGeometryScopeKey scope)
        {
            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null)
                return false;

            hostedGeometry.GeometryAvailable = true;
            return true;
        }

        public bool TryResolveGroundedDungeonLocalPosition(DFMPWorldContextKey context, Vector3 dungeonLocalPosition, out Vector3 groundedDungeonLocalPosition, out bool foundGround)
        {
            groundedDungeonLocalPosition = dungeonLocalPosition;
            foundGround = false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null || !hostedGeometry.GeometryAvailable)
                return false;

            // A whole dungeon block is one combined mesh collider and RaycastAll reports only its first hit, so a probe
            // starting high above returns the level above and hides the floor underfoot. Probe from just overhead first.
            const float nearProbeRise = 0.5f;
            const float farProbeRise = 8f;
            foundGround =
                TryProbeGroundBelow(hostedGeometry, dungeonLocalPosition, nearProbeRise, out groundedDungeonLocalPosition) ||
                TryProbeGroundBelow(hostedGeometry, dungeonLocalPosition, farProbeRise, out groundedDungeonLocalPosition);

            return true;
        }

        static bool TryProbeGroundBelow(HostedDungeonGeometry hostedGeometry, Vector3 dungeonLocalPosition, float rise, out Vector3 groundedDungeonLocalPosition)
        {
            groundedDungeonLocalPosition = dungeonLocalPosition;
            Vector3 worldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, dungeonLocalPosition + Vector3.up * rise);
            RaycastHit[] hits = Physics.RaycastAll(worldPosition, Vector3.down, rise + 16f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.MaxValue;
            bool found = false;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(hostedGeometry.Root.transform))
                    continue;

                if (hits[index].normal.y < 0.5f)
                    continue;

                if (hits[index].distance < nearestDistance)
                {
                    nearestDistance = hits[index].distance;
                    groundedDungeonLocalPosition = hostedGeometry.Root.transform.InverseTransformPoint(hits[index].point);
                    found = true;
                }
            }

            return found;
        }

        public static Vector3 DungeonLocalToHostedWorldPosition(Transform root, Vector3 dungeonLocalPosition)
        {
            return root != null ? root.TransformPoint(dungeonLocalPosition) : dungeonLocalPosition;
        }

        void OnPlayerWorldContextChanged(DFMPPlayerWorldContextChangedEvent contextChange)
        {
            if (contextChange == null)
                return;

            DFMPDungeonGeometryScopeKey previousScope;
            bool hadPreviousScope = scopeByConnectionId.TryGetValue(contextChange.ConnectionId, out previousScope);
            DFMPDungeonGeometryScopeKey currentScope;
            bool hasCurrentScope = TryCreateScope(contextChange.CurrentContext, out currentScope);

            if (hadPreviousScope && hasCurrentScope && previousScope.Equals(currentScope))
                return;

            if (hadPreviousScope)
                ReleaseScope(contextChange.ConnectionId, previousScope);

            if (hasCurrentScope)
                AcquireScope(contextChange.ConnectionId, currentScope);
        }

        void AcquireScope(int connectionId, DFMPDungeonGeometryScopeKey scope)
        {
            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null)
            {
                hostedGeometry = new HostedDungeonGeometry
                {
                    Scope = scope,
                    Root = CreateGeometryRoot(scope, out bool geometryAvailable),
                    GeometryAvailable = geometryAvailable
                };
                hostedGeometryByScope.Add(scope, hostedGeometry);
                Debug.Log($"[DFMP Dungeon Geometry] Hosted dungeon geometry scope: scope={scope}.");
            }

            hostedGeometry.OccupantCount++;
            scopeByConnectionId[connectionId] = scope;
        }

        void ReleaseScope(int connectionId, DFMPDungeonGeometryScopeKey scope)
        {
            scopeByConnectionId.Remove(connectionId);

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null)
                return;

            hostedGeometry.OccupantCount = Math.Max(0, hostedGeometry.OccupantCount - 1);
            if (hostedGeometry.OccupantCount > 0)
                return;

            hostedGeometryByScope.Remove(scope);
            DestroyRoot(hostedGeometry.Root);
            Debug.Log($"[DFMP Dungeon Geometry] Released dungeon geometry scope: scope={scope}.");
        }

        GameObject CreateGeometryRoot(DFMPDungeonGeometryScopeKey scope, out bool geometryAvailable)
        {
            GameObject root = new GameObject("DFMP_DungeonGeometry_" + SanitizeName(scope.ToString()));
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(root);

            geometryAvailable = TryPopulateNativeGeometry(scope, root);
            return root;
        }

        bool TryPopulateNativeGeometry(DFMPDungeonGeometryScopeKey scope, GameObject root)
        {
            if (!Application.isPlaying || root == null)
                return false;

            DFLocation location;
            if (!TryResolveLocation(scope, out location))
            {
                Debug.LogWarning($"[DFMP Dungeon Geometry] Native dungeon geometry unavailable: scope={scope}, reason=location-unavailable.");
                return false;
            }

            if (!location.HasDungeon)
            {
                Debug.LogWarning($"[DFMP Dungeon Geometry] Native dungeon geometry unavailable: scope={scope}, reason=location-has-no-dungeon.");
                return false;
            }

            try
            {
                DaggerfallDungeon dungeon = root.AddComponent<DaggerfallDungeon>();
                dungeon.DungeonTextureUse = DungeonTextureUse.Disabled;
                dungeon.SetDungeon(location, false);
                DisableAudioComponents(root);
                Debug.Log($"[DFMP Dungeon Geometry] Generated native dungeon geometry: scope={scope}, children={root.transform.childCount}.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DFMP Dungeon Geometry] Native dungeon geometry generation failed: scope={scope}, exception={exception}.");
                return false;
            }
        }

        static bool TryResolveLocation(DFMPDungeonGeometryScopeKey scope, out DFLocation location)
        {
            location = new DFLocation();
            if (DaggerfallUnity.Instance == null || !DaggerfallUnity.Instance.IsReady || DaggerfallUnity.Instance.ContentReader == null)
                return false;

            return DaggerfallUnity.Instance.ContentReader.GetLocation(scope.RegionIndex, scope.LocationIndex, out location);
        }

        static void DisableAudioComponents(GameObject root)
        {
            DaggerfallAudioSource[] daggerfallAudioSources = root.GetComponentsInChildren<DaggerfallAudioSource>(true);
            for (int index = 0; index < daggerfallAudioSources.Length; index++)
            {
                if (daggerfallAudioSources[index] != null)
                    daggerfallAudioSources[index].enabled = false;
            }

            AudioSource[] audioSources = root.GetComponentsInChildren<AudioSource>(true);
            for (int index = 0; index < audioSources.Length; index++)
            {
                if (audioSources[index] != null)
                    audioSources[index].enabled = false;
            }
        }

        static bool HasHostedGeometryBlocker(Transform root, Vector3 fromWorldPosition, Vector3 toWorldPosition, bool ignoreActionDoors)
        {
            Vector3 offset = toWorldPosition - fromWorldPosition;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(fromWorldPosition, offset / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(root))
                    continue;
                if (ignoreActionDoors && hitCollider.GetComponent<DaggerfallActionDoor>() != null)
                    continue;
                return true;
            }

            return false;
        }

        static void DestroyRoot(GameObject root)
        {
            if (root == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(root);
            else
                UnityEngine.Object.DestroyImmediate(root);
        }

        static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";

            char[] chars = value.ToCharArray();
            for (int index = 0; index < chars.Length; index++)
            {
                if (!char.IsLetterOrDigit(chars[index]))
                    chars[index] = '_';
            }

            return new string(chars);
        }

        void Clear()
        {
            foreach (var kvp in hostedGeometryByScope)
            {
                if (kvp.Value != null)
                    DestroyRoot(kvp.Value.Root);
            }

            hostedGeometryByScope.Clear();
            scopeByConnectionId.Clear();
            missingActionDoorWarnings.Clear();
        }
    }
}

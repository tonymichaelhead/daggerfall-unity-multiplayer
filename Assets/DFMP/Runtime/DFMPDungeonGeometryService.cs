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
        bool isSubscribed;

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
            hasLineOfSight = false;

            DFMPDungeonGeometryScopeKey scope;
            if (!TryCreateScope(context, out scope))
                return false;

            HostedDungeonGeometry hostedGeometry;
            if (!hostedGeometryByScope.TryGetValue(scope, out hostedGeometry) || hostedGeometry == null || hostedGeometry.Root == null || !hostedGeometry.GeometryAvailable)
                return false;

            Vector3 fromWorldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, fromDungeonLocalPosition + Vector3.up);
            Vector3 toWorldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, toDungeonLocalPosition + Vector3.up);
            hasLineOfSight = !HasHostedGeometryBlocker(hostedGeometry.Root.transform, fromWorldPosition, toWorldPosition);
            return true;
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
            Vector3 direction = offset / distance;
            Vector3 fromWorldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, fromDungeonLocalPosition);
            Vector3 bottom = fromWorldPosition + Vector3.up * radius;
            Vector3 top = fromWorldPosition + Vector3.up * (height - radius);
            RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, radius, direction, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            float nearestHitDistance = distance;
            bool foundBlocker = false;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(hostedGeometry.Root.transform))
                    continue;

                if (hits[index].distance < nearestHitDistance)
                {
                    nearestHitDistance = hits[index].distance;
                    foundBlocker = true;
                }
            }

            if (!foundBlocker)
            {
                resolvedDungeonLocalPosition = desiredDungeonLocalPosition;
                return true;
            }

            float safeDistance = Mathf.Max(0f, nearestHitDistance - 0.01f);
            Vector3 resolvedWorldPosition = fromWorldPosition + direction * safeDistance;
            resolvedDungeonLocalPosition = hostedGeometry.Root.transform.InverseTransformPoint(resolvedWorldPosition);
            blocked = true;
            return true;
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

            Vector3 worldPosition = DungeonLocalToHostedWorldPosition(hostedGeometry.Root.transform, dungeonLocalPosition + Vector3.up * 8f);
            RaycastHit[] hits = Physics.RaycastAll(worldPosition, Vector3.down, 16f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.MaxValue;
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider == null || hitCollider.transform == null || !hitCollider.transform.IsChildOf(hostedGeometry.Root.transform))
                    continue;

                if (hits[index].distance < nearestDistance)
                {
                    nearestDistance = hits[index].distance;
                    groundedDungeonLocalPosition = hostedGeometry.Root.transform.InverseTransformPoint(hits[index].point);
                    foundGround = true;
                }
            }

            return true;
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

        static bool HasHostedGeometryBlocker(Transform root, Vector3 fromWorldPosition, Vector3 toWorldPosition)
        {
            Vector3 offset = toWorldPosition - fromWorldPosition;
            float distance = offset.magnitude;
            if (distance <= 0.0001f)
                return false;

            RaycastHit[] hits = Physics.RaycastAll(fromWorldPosition, offset / distance, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < hits.Length; index++)
            {
                Collider hitCollider = hits[index].collider;
                if (hitCollider != null && hitCollider.transform != null && hitCollider.transform.IsChildOf(root))
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
        }
    }
}

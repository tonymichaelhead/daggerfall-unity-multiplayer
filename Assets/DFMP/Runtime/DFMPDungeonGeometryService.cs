using System;
using System.Collections.Generic;
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
                    Root = CreateGeometryRoot(scope)
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

        GameObject CreateGeometryRoot(DFMPDungeonGeometryScopeKey scope)
        {
            GameObject root = new GameObject("DFMP_DungeonGeometry_" + SanitizeName(scope.ToString()));
            if (Application.isPlaying)
                UnityEngine.Object.DontDestroyOnLoad(root);
            return root;
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

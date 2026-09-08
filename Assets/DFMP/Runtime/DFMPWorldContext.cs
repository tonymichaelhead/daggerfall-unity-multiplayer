using System;
using System.Collections.Generic;

namespace DFMP.Runtime
{
    public enum DFMPWorldContextKind
    {
        Exterior,
        BuildingInterior,
        Dungeon
    }

    [Serializable]
    public struct DFMPWorldContextKey : IEquatable<DFMPWorldContextKey>
    {
        public DFMPWorldContextKind Kind;
        public int MapPixelX;
        public int MapPixelY;
        public int RegionIndex;
        public int LocationIndex;
        public string LocationId;
        public int BuildingKey;
        public int DungeonBlockIndex;
        public string DungeonBlockName;
        public string InstanceId;

        public bool Equals(DFMPWorldContextKey other)
        {
            return Kind == other.Kind &&
                MapPixelX == other.MapPixelX &&
                MapPixelY == other.MapPixelY &&
                RegionIndex == other.RegionIndex &&
                LocationIndex == other.LocationIndex &&
                BuildingKey == other.BuildingKey &&
                DungeonBlockIndex == other.DungeonBlockIndex &&
                string.Equals(LocationId ?? string.Empty, other.LocationId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(DungeonBlockName ?? string.Empty, other.DungeonBlockName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(InstanceId ?? string.Empty, other.InstanceId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is DFMPWorldContextKey && Equals((DFMPWorldContextKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ MapPixelX;
                hash = (hash * 397) ^ MapPixelY;
                hash = (hash * 397) ^ RegionIndex;
                hash = (hash * 397) ^ LocationIndex;
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(LocationId ?? string.Empty);
                hash = (hash * 397) ^ BuildingKey;
                hash = (hash * 397) ^ DungeonBlockIndex;
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(DungeonBlockName ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(InstanceId ?? string.Empty);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{Kind}:{MapPixelX}/{MapPixelY}:{RegionIndex}/{LocationIndex}:{LocationId ?? string.Empty}:{BuildingKey}:{DungeonBlockIndex}:{DungeonBlockName ?? string.Empty}:{InstanceId ?? string.Empty}";
        }
    }

    [Serializable]
    public class DFMPWorldContextRecord
    {
        public string Kind = DFMPWorldContextKind.Exterior.ToString();
        public int MapPixelX;
        public int MapPixelY;
        public int RegionIndex;
        public int LocationIndex;
        public string LocationId = string.Empty;
        public int BuildingKey;
        public int DungeonBlockIndex;
        public string DungeonBlockName = string.Empty;
        public string InstanceId = string.Empty;

        public void Normalize()
        {
            Kind = ParseKind(Kind).ToString();
            RegionIndex = Math.Max(0, RegionIndex);
            LocationIndex = Math.Max(0, LocationIndex);
            LocationId = string.IsNullOrWhiteSpace(LocationId) ? string.Empty : LocationId.Trim();
            BuildingKey = Math.Max(0, BuildingKey);
            DungeonBlockIndex = Math.Max(0, DungeonBlockIndex);
            DungeonBlockName = string.IsNullOrWhiteSpace(DungeonBlockName) ? string.Empty : DungeonBlockName.Trim();
            InstanceId = string.IsNullOrWhiteSpace(InstanceId) ? string.Empty : InstanceId.Trim();
        }

        public bool IsDefaultExterior()
        {
            return ParseKind(Kind) == DFMPWorldContextKind.Exterior &&
                MapPixelX == 0 &&
                MapPixelY == 0 &&
                RegionIndex <= 0 &&
                LocationIndex <= 0 &&
                string.IsNullOrWhiteSpace(LocationId) &&
                BuildingKey == 0 &&
                DungeonBlockIndex <= 0 &&
                string.IsNullOrWhiteSpace(DungeonBlockName) &&
                string.IsNullOrWhiteSpace(InstanceId);
        }

        public DFMPWorldContextKey ToKey()
        {
            Normalize();
            return new DFMPWorldContextKey
            {
                Kind = ParseKind(Kind),
                MapPixelX = MapPixelX,
                MapPixelY = MapPixelY,
                RegionIndex = RegionIndex,
                LocationIndex = LocationIndex,
                LocationId = LocationId,
                BuildingKey = BuildingKey,
                DungeonBlockIndex = DungeonBlockIndex,
                DungeonBlockName = DungeonBlockName,
                InstanceId = InstanceId
            };
        }

        public static DFMPWorldContextRecord FromKey(DFMPWorldContextKey key)
        {
            var record = new DFMPWorldContextRecord
            {
                Kind = key.Kind.ToString(),
                MapPixelX = key.MapPixelX,
                MapPixelY = key.MapPixelY,
                RegionIndex = key.RegionIndex,
                LocationIndex = key.LocationIndex,
                LocationId = key.LocationId ?? string.Empty,
                BuildingKey = key.BuildingKey,
                DungeonBlockIndex = key.DungeonBlockIndex,
                DungeonBlockName = key.DungeonBlockName ?? string.Empty,
                InstanceId = key.InstanceId ?? string.Empty
            };
            record.Normalize();
            return record;
        }

        public static DFMPWorldContextRecord FromLegacy(string worldContext, int mapPixelX, int mapPixelY)
        {
            var record = new DFMPWorldContextRecord
            {
                Kind = ParseKind(worldContext).ToString(),
                MapPixelX = mapPixelX,
                MapPixelY = mapPixelY
            };
            record.Normalize();
            return record;
        }

        static DFMPWorldContextKind ParseKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DFMPWorldContextKind.Exterior;

            DFMPWorldContextKind kind;
            if (Enum.TryParse(value.Trim(), true, out kind))
                return kind;

            return DFMPWorldContextKind.Exterior;
        }
    }

    public sealed class DFMPWorldOccupancyRegistry
    {
        readonly Dictionary<int, DFMPWorldContextKey> contextByConnection = new Dictionary<int, DFMPWorldContextKey>();
        readonly Dictionary<DFMPWorldContextKey, HashSet<int>> connectionsByContext = new Dictionary<DFMPWorldContextKey, HashSet<int>>();

        public void SetContext(int connectionId, DFMPWorldContextKey context)
        {
            DFMPWorldContextKey previousContext;
            if (contextByConnection.TryGetValue(connectionId, out previousContext) && previousContext.Equals(context))
                return;

            RemoveFromContext(connectionId, previousContext);
            contextByConnection[connectionId] = context;

            HashSet<int> connections;
            if (!connectionsByContext.TryGetValue(context, out connections))
            {
                connections = new HashSet<int>();
                connectionsByContext.Add(context, connections);
            }

            connections.Add(connectionId);
        }

        public bool TryGetContext(int connectionId, out DFMPWorldContextKey context)
        {
            return contextByConnection.TryGetValue(connectionId, out context);
        }

        public int ConnectionCount
        {
            get { return contextByConnection.Count; }
        }

        public int ContextCount
        {
            get { return connectionsByContext.Count; }
        }

        public bool AreCoLocated(int firstConnectionId, int secondConnectionId)
        {
            DFMPWorldContextKey firstContext;
            DFMPWorldContextKey secondContext;
            return TryGetContext(firstConnectionId, out firstContext) &&
                TryGetContext(secondConnectionId, out secondContext) &&
                firstContext.Equals(secondContext);
        }

        public int[] GetConnectionsInContext(DFMPWorldContextKey context)
        {
            HashSet<int> connections;
            if (!connectionsByContext.TryGetValue(context, out connections))
                return new int[0];

            int[] result = new int[connections.Count];
            connections.CopyTo(result);
            return result;
        }

        public void Remove(int connectionId)
        {
            DFMPWorldContextKey context;
            if (!contextByConnection.TryGetValue(connectionId, out context))
                return;

            RemoveFromContext(connectionId, context);
            contextByConnection.Remove(connectionId);
        }

        public void Clear()
        {
            contextByConnection.Clear();
            connectionsByContext.Clear();
        }

        void RemoveFromContext(int connectionId, DFMPWorldContextKey context)
        {
            HashSet<int> connections;
            if (!connectionsByContext.TryGetValue(context, out connections))
                return;

            connections.Remove(connectionId);
            if (connections.Count == 0)
                connectionsByContext.Remove(context);
        }
    }
}

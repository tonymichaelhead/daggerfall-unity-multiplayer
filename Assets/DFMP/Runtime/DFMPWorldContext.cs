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
        public string LocationId;
        public string InstanceId;

        public bool Equals(DFMPWorldContextKey other)
        {
            return Kind == other.Kind &&
                MapPixelX == other.MapPixelX &&
                MapPixelY == other.MapPixelY &&
                string.Equals(LocationId ?? string.Empty, other.LocationId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
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
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(LocationId ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(InstanceId ?? string.Empty);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{Kind}:{MapPixelX}/{MapPixelY}:{LocationId ?? string.Empty}:{InstanceId ?? string.Empty}";
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

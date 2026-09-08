using System;

namespace DFMP.Runtime
{
    public class DFMPPlayerConnectedEvent
    {
        public int ConnectionId;
        public string Address;
        public DFMPPlayerSessionState SessionState;
    }

    public class DFMPPlayerDisconnectedEvent
    {
        public int ConnectionId;
        public string Address;
        public DFMPPlayerSessionState SessionState;
        public string Reason;
    }

    public class DFMPPlayerSpawnedEvent
    {
        public int ConnectionId;
        public DFMPPlayerSessionState SessionState;
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public string DisplayName;
    }

    public class DFMPChatMessageReceivedEvent
    {
        public int ConnectionId;
        public string SenderDisplayName;
        public string MessageText;
        public DFMPChatMessage Message;
    }

    public class DFMPPlayerWorldContextChangedEvent
    {
        public int ConnectionId;
        public DFMPPlayerSessionState SessionState;
        public bool HadPreviousContext;
        public DFMPWorldContextKey PreviousContext;
        public DFMPWorldContextKey CurrentContext;
        public string Reason;
    }

    public class DFMPLocationEnteredEvent
    {
        public int ConnectionId;
        public DFMPPlayerSessionState SessionState;
        public DFMPWorldContextKey Context;
        public string Reason;
    }

    public class DFMPDungeonBlockEnteredEvent
    {
        public int ConnectionId;
        public DFMPPlayerSessionState SessionState;
        public DFMPWorldContextKey Context;
        public string Reason;
    }

    public sealed class DFMPEventBus
    {
        public static DFMPEventBus Instance { get; } = new DFMPEventBus();

        public event Action<DFMPPlayerConnectedEvent> PlayerConnected;
        public event Action<DFMPPlayerDisconnectedEvent> PlayerDisconnected;
        public event Action<DFMPPlayerSpawnedEvent> PlayerSpawned;
        public event Action<DFMPChatMessageReceivedEvent> ChatMessageReceived;
        public event Action<DFMPPlayerWorldContextChangedEvent> PlayerWorldContextChanged;
        public event Action<DFMPLocationEnteredEvent> LocationEntered;
        public event Action<DFMPDungeonBlockEnteredEvent> DungeonBlockEntered;

        public void PublishPlayerConnected(DFMPPlayerConnectedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerConnected;
            if (handler != null)
                handler(e);
        }

        public void PublishPlayerDisconnected(DFMPPlayerDisconnectedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerDisconnected;
            if (handler != null)
                handler(e);
        }

        public void PublishPlayerSpawned(DFMPPlayerSpawnedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerSpawned;
            if (handler != null)
                handler(e);
        }

        public void PublishChatMessageReceived(DFMPChatMessageReceivedEvent e)
        {
            if (e == null)
                return;

            var handler = ChatMessageReceived;
            if (handler != null)
                handler(e);
        }

        public void PublishPlayerWorldContextChanged(DFMPPlayerWorldContextChangedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerWorldContextChanged;
            if (handler != null)
                handler(e);
        }

        public void PublishLocationEntered(DFMPLocationEnteredEvent e)
        {
            if (e == null)
                return;

            var handler = LocationEntered;
            if (handler != null)
                handler(e);
        }

        public void PublishDungeonBlockEntered(DFMPDungeonBlockEnteredEvent e)
        {
            if (e == null)
                return;

            var handler = DungeonBlockEntered;
            if (handler != null)
                handler(e);
        }
    }
}

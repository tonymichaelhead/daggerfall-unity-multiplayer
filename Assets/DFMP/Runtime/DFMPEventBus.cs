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

    public sealed class DFMPEventBus
    {
        public static DFMPEventBus Instance { get; } = new DFMPEventBus();

        public event Action<DFMPPlayerConnectedEvent> PlayerConnected;
        public event Action<DFMPPlayerDisconnectedEvent> PlayerDisconnected;
        public event Action<DFMPPlayerSpawnedEvent> PlayerSpawned;
        public event Action<DFMPChatMessageReceivedEvent> ChatMessageReceived;

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
    }
}

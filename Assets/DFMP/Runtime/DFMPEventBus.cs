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
        public DFMPChatMessageKind Kind;
        public int ConnectionId;
        public string SenderDisplayName;
        public string MessageText;
        public DFMPChatDeliveryMessage Message;
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

    public class DFMPPlayerDamagedEvent
    {
        public int SourceConnectionId;
        public int TargetConnectionId;
        public DFMPDamageSourceKind SourceKind;
        public DFMPVitalKind VitalKind;
        public int RequestedAmount;
        public int AppliedAmount;
        public int CurrentValue;
        public int MaximumValue;
    }

    public class DFMPPlayerDiedEvent
    {
        public int ConnectionId;
        public DFMPDamageSourceKind SourceKind;
        public DFMPVitalKind VitalKind;
        public int AppliedAmount;
        public DFMPPlayerSessionState SessionState;
    }

    public class DFMPPlayerRespawnedEvent
    {
        public int ConnectionId;
        public DFMPPlayerSessionState SessionState;
        public DFMPWorldContextKey Context;
        public string Reason;
    }

    public class DFMPEnemySpawnedEvent
    {
        public string EnemyId;
        public DFMPDynamicEncounterKey Encounter;
        public int RosterIndex;
        public DFMPDynamicEnemyDescriptor Descriptor;
    }

    public class DFMPEnemyDiedEvent
    {
        public string EnemyId;
        public DFMPDynamicEncounterKey Encounter;
        public int KillerConnectionId;
        public int DamageAmount;
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
        public event Action<DFMPPlayerDamagedEvent> PlayerDamaged;
        public event Action<DFMPPlayerDiedEvent> PlayerDied;
        public event Action<DFMPPlayerRespawnedEvent> PlayerRespawned;
        public event Action<DFMPEnemySpawnedEvent> EnemySpawned;
        public event Action<DFMPEnemyDiedEvent> EnemyDied;

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

        public void PublishPlayerDamaged(DFMPPlayerDamagedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerDamaged;
            if (handler != null)
                handler(e);
        }

        public void PublishPlayerDied(DFMPPlayerDiedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerDied;
            if (handler != null)
                handler(e);
        }

        public void PublishPlayerRespawned(DFMPPlayerRespawnedEvent e)
        {
            if (e == null)
                return;

            var handler = PlayerRespawned;
            if (handler != null)
                handler(e);
        }

        public void PublishEnemySpawned(DFMPEnemySpawnedEvent e)
        {
            if (e == null)
                return;

            var handler = EnemySpawned;
            if (handler != null)
                handler(e);
        }

        public void PublishEnemyDied(DFMPEnemyDiedEvent e)
        {
            if (e == null)
                return;

            var handler = EnemyDied;
            if (handler != null)
                handler(e);
        }
    }
}

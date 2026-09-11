using kcp2k;
using Mirror;
using System;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPNetworkClient
    {
        public static DFMPNetworkManager Manager { get; private set; }
        public static KcpTransport Transport { get; private set; }
        public static string Address { get; private set; } = "127.0.0.1";
        public static ushort Port { get; private set; }
        public static string AccountId { get; private set; }

        public static event Action<DFMPAdminRosterResponse> AdminRosterReceived;

        public static bool IsConnected
        {
            get { return NetworkClient.isConnected; }
        }

        public static void Start(string address, ushort port, int tickRate, string accountId = null)
        {
            if (NetworkClient.active)
            {
                Debug.LogWarning("[DFMP Net] Client already active; skipping client startup.");
                return;
            }

            if (NetworkManager.singleton != null && NetworkManager.singleton.isNetworkActive)
            {
                Debug.LogWarning("[DFMP Net] Mirror already has an active NetworkManager; skipping client startup.");
                return;
            }

            Address = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
            Port = port;
            AccountId = string.IsNullOrWhiteSpace(accountId) ? GetDefaultAccountId() : accountId;
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.MarkConnecting();
            if (DFMPChatController.Instance != null)
                DFMPChatController.Instance.ResetForConnection();

            DFMPLogRouter.Initialize(DFMPLogRole.Client);
            DFMPClientBootstrap.ApplyClientRuntimeSettings(tickRate);

            GameObject networkGo = new GameObject("DFMP_NetworkClient");
            networkGo.SetActive(false);

            Transport = networkGo.AddComponent<KcpTransport>();
            Transport.port = port;
            Transport.NoDelay = true;
            Transport.Interval = 10;
            Transport.Timeout = 10000;
            Transport.MaximizeSocketBuffers = true;
            Transport.statisticsLog = false;

            Manager = networkGo.AddComponent<DFMPNetworkManager>();
            Manager.dontDestroyOnLoad = true;
            Manager.runInBackground = true;
            Manager.headlessStartMode = HeadlessStartOptions.DoNothing;
            Manager.transport = Transport;
            Manager.networkAddress = Address;
            Manager.autoCreatePlayer = false;
            Manager.sendRate = tickRate;

            DFMPTimeState.RegisterClientSpawnHandler();
            DFMPPlayerSessionState.RegisterClientSpawnHandler();
            DFMPSpawnAssignmentController.RegisterClientHandler();
            DFMPDeveloperCommandController.RegisterClient();
            NetworkClient.RegisterHandler<DFMPChatDeliveryMessage>(OnChatMessageReceived);
            NetworkClient.RegisterHandler<DFMPAdminRosterResponse>(OnAdminRosterReceived);
            NetworkClient.RegisterHandler<DFMPAdminKickNotice>(OnAdminKickNoticeReceived);
            NetworkClient.RegisterHandler<DFMPJoinResultMessage>(OnJoinResultReceived);
            NetworkClient.RegisterHandler<DFMPCharacterSnapshotMessage>(OnCharacterSnapshotReceived);
            NetworkClient.RegisterHandler<DFMPRestResponse>(OnRestResponseReceived);
            DFMPPositionReporter.EnsureInstance();
            DFMPRemotePlayerPresentationController.EnsureInstance();

            UnityEngine.Object.DontDestroyOnLoad(networkGo);
            networkGo.SetActive(true);

            Mirror.Transport.active = Transport;
            Manager.StartClient();

            Debug.Log($"[DFMP Net] Client connection requested: transport=KCP, address={Address}, port={port}, tickRate={tickRate}.");
        }

        static void OnChatMessageReceived(DFMPChatDeliveryMessage message)
        {
            DFMPEventBus.Instance.PublishChatMessageReceived(new DFMPChatMessageReceivedEvent
            {
            Kind = message.Kind,
                ConnectionId = message.ConnectionId,
                SenderDisplayName = message.SenderDisplayName,
                MessageText = message.Text,
                Message = message
            });

            Debug.Log($"[DFMP Chat] Client received chat: kind={message.Kind}, sender='{message.SenderDisplayName}', text='{message.Text}'.");
        }

        static void OnAdminRosterReceived(DFMPAdminRosterResponse message)
        {
            if (message.Players == null)
                message.Players = new DFMPAdminPlayer[0];

            AdminRosterReceived?.Invoke(message);
        }

        static void OnAdminKickNoticeReceived(DFMPAdminKickNotice message)
        {
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.SetPendingKickNotice(message.Message);

            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPAdminKickAcknowledgement());
        }

        public static void RequestAdminRoster()
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPAdminRosterRequest());
        }

        public static void RequestKickPlayer(int connectionId)
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPAdminKickRequest { TargetConnectionId = connectionId });
        }

        public static void RequestRest(string restModeName)
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPRestRequest { RestModeName = restModeName });
        }

        public static void RequestVampirismTransformation()
        {
            if (NetworkClient.isConnected && NetworkClient.ready)
                NetworkClient.Send(new DFMPVampirismTransformationRequest());
        }

            public static void ReportPlayerDeath()
            {
                if (NetworkClient.isConnected && NetworkClient.ready)
                NetworkClient.Send(new DFMPPlayerDeathReport());
            }

        static void OnRestResponseReceived(DFMPRestResponse message)
        {
            if (message.Accepted)
                Debug.Log("[DFMP Rest] Server accepted rest request; recovery is pending server implementation.");
            else
                Debug.LogWarning($"[DFMP Rest] Server rejected rest request: reason={message.RejectionReason}.");
        }

        static void OnJoinResultReceived(DFMPJoinResultMessage message)
        {
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.ApplyJoinResult(message);

            if (message.Decision == DFMPJoinDecisionKind.Rejected)
            {
                Debug.LogWarning($"[DFMP Join] Server rejected account '{message.AccountId}': {message.Reason}.");
            }
            else
            {
                Debug.Log($"[DFMP Join] Accepted account '{message.AccountId}': decision={message.Decision}, world='{message.ServerWorldId}'.");
                if (NetworkClient.isConnected && !NetworkClient.ready)
                    NetworkClient.Ready();
            }
        }

        static void OnCharacterSnapshotReceived(DFMPCharacterSnapshotMessage message)
        {
            string normalizedAccountId;
            string reason;
            if (!DFMPCharacterSnapshotProtocol.IsValid(message) ||
                !DFMPAccountPolicy.TryNormalize(AccountId, out normalizedAccountId, out reason) ||
                message.AccountId != normalizedAccountId)
            {
                Debug.LogWarning("[DFMP Join] Rejected invalid or mismatched character snapshot from server.");
                return;
            }

            if (DFMPClientJoinFlowController.Instance != null)
            {
                Debug.Log($"[DFMP Join] Received inventory snapshot: bytes={message.InventoryJson.Length}.");
                DFMPClientJoinFlowController.Instance.ApplyCharacterSnapshot(message);
            }
        }

        static string GetDefaultAccountId()
        {
            string userName = Environment.UserName;
            return string.IsNullOrWhiteSpace(userName) ? "local:player" : "local:" + userName;
        }

        public static void Stop()
        {
            DFMPNetworkManager networkManager = Manager;
            if (networkManager != null && networkManager.isNetworkActive)
                networkManager.StopClient();

            if (networkManager != null)
                UnityEngine.Object.Destroy(networkManager.gameObject);

            DFMPSpawnAssignmentController.Reset();
            DFMPPositionReporter.Reset();
            DFMPRemotePlayerPresentationController.Reset();
            Manager = null;
            Transport = null;
            Address = "127.0.0.1";
            Port = 0;
            AccountId = null;
        }
    }
}
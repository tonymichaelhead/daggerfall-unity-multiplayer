using kcp2k;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPNetworkClient
    {
        public static DFMPNetworkManager Manager { get; private set; }
        public static KcpTransport Transport { get; private set; }
        public static string Address { get; private set; } = "127.0.0.1";
        public static ushort Port { get; private set; }

        public static bool IsConnected
        {
            get { return NetworkClient.isConnected; }
        }

        public static void Start(string address, ushort port, int tickRate)
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
            NetworkClient.RegisterHandler<DFMPChatMessage>(OnChatMessageReceived);
            DFMPPositionReporter.EnsureInstance();
            DFMPRemotePlayerPresentationController.EnsureInstance();

            Object.DontDestroyOnLoad(networkGo);
            networkGo.SetActive(true);

            Mirror.Transport.active = Transport;
            Manager.StartClient();

            Debug.Log($"[DFMP Net] Client connection requested: transport=KCP, address={Address}, port={port}, tickRate={tickRate}.");
        }

        static void OnChatMessageReceived(DFMPChatMessage message)
        {
            DFMPEventBus.Instance.PublishChatMessageReceived(new DFMPChatMessageReceivedEvent
            {
                ConnectionId = message.ConnectionId,
                SenderDisplayName = message.SenderDisplayName,
                MessageText = message.Text,
                Message = message
            });

            Debug.Log($"[DFMP Chat] Client received chat: sender='{message.SenderDisplayName}', text='{message.Text}'.");
        }

        public static void Stop()
        {
            if (Manager != null && Manager.isNetworkActive)
                Manager.StopClient();

            DFMPSpawnAssignmentController.Reset();
            DFMPPositionReporter.Reset();
            DFMPRemotePlayerPresentationController.Reset();
            Manager = null;
            Transport = null;
            Address = "127.0.0.1";
            Port = 0;
        }
    }
}
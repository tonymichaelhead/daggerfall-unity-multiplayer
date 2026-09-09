using System;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPClientBootstrap : MonoBehaviour
    {
        public static bool IsClient { get; private set; }
        public static string ServerAddress { get; private set; } = "127.0.0.1";
        public static int ServerPort { get; private set; } = 7777;
        public static int ClientTickRate { get; private set; } = 30;
        public static string AccountId { get; private set; }

        /// <summary>False when launched as a client without an address, so the player boots into Daggerfall and picks a server from the server list.</summary>
        public static bool AutoConnect { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EarlyInitialize()
        {
            var config = ServerCommandLineArgs.Parse(Environment.GetCommandLineArgs(), Application.isBatchMode);

            if (!config.IsClient)
                return;

            IsClient = true;
            ServerAddress = config.Address;
            ServerPort = config.Port;
            ClientTickRate = config.TickRate;
            AccountId = config.AccountId;
            AutoConnect = config.HasExplicitAddress;
            DFMPLogRouter.Initialize(DFMPLogRole.Client);

            ApplyClientRuntimeSettings(ClientTickRate);

            GameObject bootstrapGo = new GameObject("DFMP_ClientBootstrap");
            DontDestroyOnLoad(bootstrapGo);
            bootstrapGo.AddComponent<DFMPClientBootstrap>();

            Debug.Log($"[DFMP Client] Bootstrapped: Address={ServerAddress}, Port={ServerPort}, TickRate={ClientTickRate}, AutoConnect={AutoConnect}, LogFile='{DFMPLogRouter.LogFilePath}'");
        }

        private void Start()
        {
            if (!AutoConnect)
            {
                Debug.Log("[DFMP Client] No address supplied; skipping direct connect. Open the server list to choose a server.");
                return;
            }

            DFMPNetworkClient.Start(ServerAddress, (ushort)ServerPort, ClientTickRate, AccountId);
        }

        public static void ApplyClientRuntimeSettings(int tickRate)
        {
            Application.targetFrameRate = tickRate;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
        }

        private void OnApplicationQuitting()
        {
            DFMPNetworkClient.Stop();
            DFMPLogRouter.Shutdown();
        }
    }
}
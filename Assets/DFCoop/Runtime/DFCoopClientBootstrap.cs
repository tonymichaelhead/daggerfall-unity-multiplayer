using System;
using UnityEngine;

namespace DFCoop.Runtime
{
    public class DFCoopClientBootstrap : MonoBehaviour
    {
        public static bool IsClient { get; private set; }
        public static string ServerAddress { get; private set; } = "127.0.0.1";
        public static int ServerPort { get; private set; } = 7777;
        public static int ClientTickRate { get; private set; } = 30;

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
            DFCoopLogRouter.Initialize(DFCoopLogRole.Client);

            ApplyClientRuntimeSettings(ClientTickRate);

            GameObject bootstrapGo = new GameObject("DFCoop_ClientBootstrap");
            DontDestroyOnLoad(bootstrapGo);
            bootstrapGo.AddComponent<DFCoopClientBootstrap>();

            Debug.Log($"[DFCoop Client] Bootstrapped: Address={ServerAddress}, Port={ServerPort}, TickRate={ClientTickRate}, LogFile='{DFCoopLogRouter.LogFilePath}'");
        }

        private void Start()
        {
            DFCoopNetworkClient.Start(ServerAddress, (ushort)ServerPort, ClientTickRate);
        }

        public static void ApplyClientRuntimeSettings(int tickRate)
        {
            Application.targetFrameRate = tickRate;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
        }

        private void OnApplicationQuitting()
        {
            DFCoopNetworkClient.Stop();
            DFCoopLogRouter.Shutdown();
        }
    }
}
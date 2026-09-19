using System;
using System.Collections;
using Mirror;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DFMP.Runtime
{
    public class DFMPClientBootstrap : MonoBehaviour
    {
        const float CloseFlushSeconds = 0.4f;

        public static DFMPClientBootstrap Instance { get; private set; }
        public static bool IsClient { get; private set; }
        public static string ServerAddress { get; private set; } = "127.0.0.1";
        public static int ServerPort { get; private set; } = 7777;
        public static int ClientTickRate { get; private set; } = 30;
        public static string AccountId { get; private set; }

        /// <summary>False when launched as a client without an address, so the player boots into Daggerfall and picks a server from the server list.</summary>
        public static bool AutoConnect { get; private set; }

        static bool allowQuit;
        static bool closeFlushStarted;

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

            if (DFMPClientSession.TryLoadFromFile(config.SessionFilePath) || DFMPClientSession.TryLoadDevelopmentSession())
            {
                AccountId = DFMPClientSession.AccountId;
                Debug.Log($"[DFMP Client] Loaded session for account '{AccountId}'.");
            }

            ApplyClientRuntimeSettings(ClientTickRate);

            GameObject bootstrapGo = new GameObject("DFMP_ClientBootstrap");
            DontDestroyOnLoad(bootstrapGo);
            bootstrapGo.AddComponent<DFMPClientBootstrap>();

            Debug.Log($"[DFMP Client] Bootstrapped: Address={ServerAddress}, Port={ServerPort}, TickRate={ClientTickRate}, AutoConnect={AutoConnect}, LogFile='{DFMPLogRouter.LogFilePath}'");
        }

        private void Awake()
        {
            Instance = this;
            Application.wantsToQuit += OnWantsToQuit;
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Application.wantsToQuit -= OnWantsToQuit;
            Application.quitting -= OnApplicationQuitting;
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

        public static bool TryHandleMultiplayerExit()
        {
            if (!IsClient || !NetworkClient.isConnected)
                return false;

            DaggerfallWorkshop.DaggerfallUnity.Settings.SaveSettings();
            BeginCloseFlushThenQuit();
            return true;
        }

        static bool OnWantsToQuit()
        {
            if (allowQuit || !IsClient || !NetworkClient.isConnected)
                return true;

            BeginCloseFlushThenQuit();
            return false;
        }

        static void BeginCloseFlushThenQuit()
        {
            if (closeFlushStarted)
                return;

            closeFlushStarted = true;
            DFMPPositionReporter.FlushPersistentState();
            if (Instance != null)
                Instance.StartCoroutine(Instance.CompleteQuitAfterFlush());
            else
                CompleteQuitImmediate();
        }

        IEnumerator CompleteQuitAfterFlush()
        {
            yield return new WaitForSecondsRealtime(CloseFlushSeconds);
            CompleteQuitImmediate();
        }

        static void CompleteQuitImmediate()
        {
            allowQuit = true;
            DFMPNetworkClient.Stop();
            DFMPLogRouter.Shutdown();
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnApplicationQuitting()
        {
            if (!allowQuit)
                DFMPPositionReporter.FlushPersistentState();

            DFMPNetworkClient.Stop();
            DFMPLogRouter.Shutdown();
        }
    }
}

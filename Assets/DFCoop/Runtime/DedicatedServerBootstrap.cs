using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DFCoop.Hooks;

namespace DFCoop.Runtime
{
    /// <summary>
    /// Headless Dedicated Server Bootstrap for Daggerfall Unity (DFCoop).
    /// Initializes early in the boot sequence, parses command-line arguments,
    /// configures headless engine settings, and coordinates non-visual world initialization.
    /// </summary>
    public class DedicatedServerBootstrap : MonoBehaviour
    {
        public static DedicatedServerBootstrap Instance { get; private set; }

        public static bool IsDedicatedServer { get; private set; }
        public static int ServerPort { get; private set; } = 7777;
        public static int ServerTickRate { get; private set; } = 30;
        public static int MaxConnections { get; private set; } = 16;
        public static string Arena2OverridePath { get; private set; } = null;
        public static float HeartbeatInterval { get; private set; } = 5.0f;

        private bool gameSceneInitialized = false;
        private Coroutine heartbeatCoroutine;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EarlyInitialize()
        {
            var config = ServerCommandLineArgs.Parse(Environment.GetCommandLineArgs(), Application.isBatchMode);

            if (!config.IsDedicatedServer)
                return;

            IsDedicatedServer = true;
            ServerPort = config.Port;
            ServerTickRate = config.TickRate;
            MaxConnections = config.MaxConnections;
            Arena2OverridePath = config.Arena2Path;
            HeartbeatInterval = config.HeartbeatInterval;
            DFCoopLogRouter.Initialize(DFCoopLogRole.Server);

            // Configure headless execution settings
            Application.targetFrameRate = ServerTickRate;
            QualitySettings.vSyncCount = 0;
            AudioListener.pause = true;
            AudioListener.volume = 0f;

            // Override Arena2 path if supplied via command line
            if (!string.IsNullOrEmpty(Arena2OverridePath) && Directory.Exists(Arena2OverridePath))
            {
                // If user passed ...\ARENA2 directly, set parent folder since DFU tests for child "arena2"/"ARENA2"
                string resolvedParent = Arena2OverridePath;
                if (string.Equals(Path.GetFileName(Arena2OverridePath), "ARENA2", StringComparison.OrdinalIgnoreCase))
                {
                    string parent = Path.GetDirectoryName(Arena2OverridePath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        resolvedParent = parent;
                }

                DaggerfallUnity.Settings.MyDaggerfallPath = resolvedParent;
            }

            // Always hook OnSetArena2Source to ensure override path is directly supplied if requested
            DaggerfallUnity.OnSetArena2Source += () =>
            {
                if (DaggerfallUnity.Instance != null && !string.IsNullOrEmpty(Arena2OverridePath))
                {
                    if (DaggerfallUnity.ValidateArena2Path(Arena2OverridePath))
                        DaggerfallUnity.Instance.Arena2Path = Arena2OverridePath;
                }
            };

            // Always suppress UI setup wizard on dedicated server
            DaggerfallUnity.Settings.ShowOptionsAtStart = false;

            // Spawn persistent bootstrap coordinator
            GameObject bootstrapGo = new GameObject("DFCoop_DedicatedServerBootstrap");
            DontDestroyOnLoad(bootstrapGo);
            Instance = bootstrapGo.AddComponent<DedicatedServerBootstrap>();

            Debug.Log($"[DFCoop] Dedicated Server Bootstrapped: TickRate={ServerTickRate}, Port={ServerPort}, MaxConnections={MaxConnections}, Arena2Path='{DaggerfallUnity.Settings.MyDaggerfallPath}', LogFile='{DFCoopLogRouter.LogFilePath}'");
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += OnApplicationQuitting;

            try
            {
                Console.CancelKeyPress += OnConsoleCancelKeyPress;
            }
            catch
            {
                // Console control handler not supported on all platforms/subsystems
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[DFCoop] Scene Loaded: {scene.name} (index {scene.buildIndex})");

            // Startup scene (index 0)
            if (scene.buildIndex == SceneControl.StartupSceneIndex)
            {
                // Ensure options wizard remains suppressed
                DaggerfallUnity.Settings.ShowOptionsAtStart = false;
            }
            // Game scene (index 1)
            else if (scene.buildIndex == SceneControl.GameSceneIndex)
            {
                StartCoroutine(InitializeGameScene());
            }
        }

        private IEnumerator InitializeGameScene()
        {
            if (gameSceneInitialized)
                yield break;

            gameSceneInitialized = true;
            Debug.Log("[DFCoop] Initializing Game Scene for Headless Execution...");

            // Wait until StartGameBehaviour is ready in scene
            StartGameBehaviour startGameBehaviour = null;
            while (startGameBehaviour == null)
            {
                startGameBehaviour = FindObjectOfType<StartGameBehaviour>();
                if (startGameBehaviour == null && GameManager.Instance != null)
                    startGameBehaviour = GameManager.Instance.StartGameBehaviour;

                if (startGameBehaviour == null)
                    yield return null;
            }

            // Intercept start method: Force StartMethods.Void to avoid pushing title UI / video player
            startGameBehaviour.PostStartMessage = "dfuiDedicatedServer";
            startGameBehaviour.StartMethod = StartGameBehaviour.StartMethods.Void;

            // Wait one frame for StartGameBehaviour.Update() to invoke StartVoid()
            yield return null;

            // Ensure game is unpaused for server tick execution
            if (GameManager.Instance != null)
            {
                GameManager.Instance.PauseGame(false);
            }

            DFCoopNetworkServer.Start((ushort)ServerPort, ServerTickRate, MaxConnections);

            Debug.Log("[DFCoop] Headless World Initialized (NoWorld=true, Unpaused). Starting heartbeat...");

            if (heartbeatCoroutine != null)
                StopCoroutine(heartbeatCoroutine);

            heartbeatCoroutine = StartCoroutine(HeartbeatRoutine());
        }

        private IEnumerator HeartbeatRoutine()
        {
            int lastFrameCount = Time.frameCount;
            float lastRealtime = Time.realtimeSinceStartup;

            while (true)
            {
                yield return new WaitForSecondsRealtime(HeartbeatInterval);

                float elapsed = Time.realtimeSinceStartup - lastRealtime;
                int frames = Time.frameCount - lastFrameCount;
                float currentFps = elapsed > 0 ? frames / elapsed : 0;

                long managedMemoryMb = GC.GetTotalMemory(false) / (1024 * 1024);
                long processMemoryMb = 0;

                try
                {
                    using (var proc = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        processMemoryMb = proc.WorkingSet64 / (1024 * 1024);
                    }
                }
                catch
                {
                    // Process metrics may fail on restricted platforms
                }

                Debug.Log($"[DFCoop Server Heartbeat] Uptime: {Time.realtimeSinceStartup:F1}s | Ticks: {currentFps:F1} fps | Managed Mem: {managedMemoryMb} MB | WorkingSet: {processMemoryMb} MB | Scene: {SceneManager.GetActiveScene().name}");

                lastFrameCount = Time.frameCount;
                lastRealtime = Time.realtimeSinceStartup;
            }
        }

        private void OnConsoleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Debug.Log("[DFCoop] Shutdown signal received (Ctrl+C). Exiting dedicated server gracefully...");
            e.Cancel = true;
            Application.Quit();
        }

        private void OnApplicationQuitting()
        {
            DFCoopNetworkServer.Stop();
            Debug.Log("[DFCoop] Dedicated Server shutting down.");
            DFCoopLogRouter.Shutdown();
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Utility;

namespace DFMP.Runtime
{
    public enum DFMPClientJoinState
    {
        Disconnected,
        Connecting,
        AwaitingIdentityResult,
        FirstJoinCharacterCreation,
        ReturningPlayerRestore,
        InGame,
        Rejected
    }

    public sealed class DFMPClientJoinFlow
    {
        public DFMPClientJoinState State { get; private set; }
        public DFMPJoinResultMessage LastJoinResult { get; private set; }

        public DFMPClientJoinFlow()
        {
            State = DFMPClientJoinState.Disconnected;
        }

        public void MarkConnecting()
        {
            State = DFMPClientJoinState.AwaitingIdentityResult;
        }

        public void ApplyJoinResult(DFMPJoinResultMessage result)
        {
            LastJoinResult = result;
            State = result.Decision == DFMPJoinDecisionKind.Rejected
                ? DFMPClientJoinState.Rejected
                : result.Decision == DFMPJoinDecisionKind.FirstJoin
                    ? DFMPClientJoinState.FirstJoinCharacterCreation
                    : DFMPClientJoinState.ReturningPlayerRestore;
        }

        public void MarkInGame()
        {
            State = DFMPClientJoinState.InGame;
        }

        public static bool ShouldLoadGameScene(DFMPJoinResultMessage result, int activeSceneIndex)
        {
            return result.Decision != DFMPJoinDecisionKind.Rejected &&
                activeSceneIndex == SceneControl.StartupSceneIndex;
        }
    }

    public sealed class DFMPClientJoinFlowController : MonoBehaviour
    {
        public static DFMPClientJoinFlowController Instance { get; private set; }
        public DFMPClientJoinFlow Flow { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer)
                return;

            if (Instance != null)
                return;

            GameObject flowObject = new GameObject("DFMP_ClientJoinFlow");
            DontDestroyOnLoad(flowObject);
            Instance = flowObject.AddComponent<DFMPClientJoinFlowController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Flow = new DFMPClientJoinFlow();
            SceneManager.sceneLoaded += OnSceneLoaded;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        public void MarkConnecting()
        {
            Flow.MarkConnecting();
        }

        public void ApplyJoinResult(DFMPJoinResultMessage result)
        {
            Flow.ApplyJoinResult(result);

            if (result.Decision == DFMPJoinDecisionKind.Rejected)
                return;

            if (DFMPClientJoinFlow.ShouldLoadGameScene(result, SceneManager.GetActiveScene().buildIndex))
            {
                DaggerfallUnity.Settings.ShowOptionsAtStart = false;
                SceneManager.LoadScene(SceneControl.GameSceneIndex);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex == SceneControl.GameSceneIndex && Flow != null &&
                (Flow.State == DFMPClientJoinState.FirstJoinCharacterCreation ||
                 Flow.State == DFMPClientJoinState.ReturningPlayerRestore))
            {
                Debug.Log($"[DFMP Join] Multiplayer game scene loaded: state={Flow.State}.");
            }
        }
    }
}

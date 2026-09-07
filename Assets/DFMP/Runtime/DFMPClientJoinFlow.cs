using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
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
        public DFMPCharacterSnapshotMessage LastCharacterSnapshot { get; private set; }
        public bool EnableBeginnerTutorial { get; private set; }

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
            EnableBeginnerTutorial = result.EnableBeginnerTutorial;
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

        public void ApplyCharacterSnapshot(DFMPCharacterSnapshotMessage snapshot)
        {
            LastCharacterSnapshot = snapshot;
        }

        public bool ShouldReportLocalIdentity(bool serverIdentityApplied)
        {
            return !serverIdentityApplied && State != DFMPClientJoinState.ReturningPlayerRestore;
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
        public bool ServerIdentityApplied { get; private set; }

        DFMPCharacterSnapshotMessage pendingSnapshot;
        bool hasPendingSnapshot;

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
            ServerIdentityApplied = false;
            SceneManager.sceneLoaded += OnSceneLoaded;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (!hasPendingSnapshot || ServerIdentityApplied || GameManager.Instance == null || GameManager.Instance.PlayerEntity == null)
                return;

            ApplySnapshotToLocalPlayer(pendingSnapshot);
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
            else if (result.Decision == DFMPJoinDecisionKind.ReturningPlayer &&
                SceneManager.GetActiveScene().buildIndex == SceneControl.GameSceneIndex)
            {
                ConfigureReturningPlayerStartup();
            }
        }

        public void ApplyCharacterSnapshot(DFMPCharacterSnapshotMessage snapshot)
        {
            Flow.ApplyCharacterSnapshot(snapshot);
            pendingSnapshot = snapshot;
            hasPendingSnapshot = true;
        }

        public bool ShouldReportLocalIdentity()
        {
            return Flow.ShouldReportLocalIdentity(ServerIdentityApplied);
        }

        void ApplySnapshotToLocalPlayer(DFMPCharacterSnapshotMessage snapshot)
        {
            var playerEntity = GameManager.Instance.PlayerEntity;
            playerEntity.Name = snapshot.CharacterName;
            playerEntity.BirthRaceTemplate = DaggerfallWorkshop.Game.Player.CharacterDocument.GetRaceTemplate((Races)snapshot.Race);
            playerEntity.Gender = (Genders)snapshot.Gender;
            playerEntity.FaceIndex = snapshot.FaceVariant;
            ServerIdentityApplied = true;
            hasPendingSnapshot = false;
            Flow.MarkInGame();
            Debug.Log($"[DFMP Join] Applied server character snapshot: name='{snapshot.CharacterName}', race={snapshot.Race}, gender={snapshot.Gender}, face={snapshot.FaceVariant}.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex == SceneControl.GameSceneIndex && Flow != null &&
                (Flow.State == DFMPClientJoinState.FirstJoinCharacterCreation ||
                 Flow.State == DFMPClientJoinState.ReturningPlayerRestore))
            {
                Debug.Log($"[DFMP Join] Multiplayer game scene loaded: state={Flow.State}.");

                if (Flow.State == DFMPClientJoinState.ReturningPlayerRestore)
                    ConfigureReturningPlayerStartup();

                if (!Flow.EnableBeginnerTutorial)
                    StartCoroutine(SuppressBeginnerTutorialAfterStartup());
            }
        }

        IEnumerator SuppressBeginnerTutorialAfterStartup()
        {
            StartGameBehaviour startGameBehaviour = null;
            while (startGameBehaviour == null || startGameBehaviour.LastStartMethod != StartGameBehaviour.StartMethods.NewCharacter)
            {
                startGameBehaviour = FindObjectOfType<StartGameBehaviour>();
                yield return null;
            }

            if (QuestMachine.Instance != null)
            {
                int removedQuestCount = 0;
                foreach (ulong questUid in QuestMachine.Instance.GetAllActiveQuests())
                {
                    Quest quest = QuestMachine.Instance.GetQuest(questUid);
                    if (quest != null && string.Equals(quest.QuestName, "_TUTOR__", System.StringComparison.OrdinalIgnoreCase))
                    {
                        if (QuestMachine.Instance.RemoveQuest(questUid))
                            removedQuestCount++;
                    }
                }

                Debug.Log($"[DFMP Join] Beginner tutorial disabled by server: removedTutorialQuests={removedQuestCount}.");
            }
        }

        void ConfigureReturningPlayerStartup()
        {
            var startGameBehaviour = FindObjectOfType<StartGameBehaviour>();
            if (startGameBehaviour == null)
                return;

            startGameBehaviour.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;
            Debug.Log("[DFMP Join] Returning player startup configured without local save selection.");
        }
    }
}

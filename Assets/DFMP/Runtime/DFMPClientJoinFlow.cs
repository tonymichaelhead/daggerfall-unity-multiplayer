using UnityEngine;
using UnityEngine.SceneManagement;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;

namespace DFMP.Runtime
{
    public enum DFMPClientJoinState
    {
        Disconnected,
        Connecting,
        AwaitingIdentityResult,
        AwaitingCharacterSelection,
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
                : result.Decision == DFMPJoinDecisionKind.AwaitingCharacterSelection
                    ? DFMPClientJoinState.AwaitingCharacterSelection
                    : result.Decision == DFMPJoinDecisionKind.FirstJoin
                        ? DFMPClientJoinState.FirstJoinCharacterCreation
                        : DFMPClientJoinState.ReturningPlayerRestore;
        }

        public void MarkInGame()
        {
            State = DFMPClientJoinState.InGame;
        }

        public void MarkDisconnected()
        {
            State = DFMPClientJoinState.Disconnected;
        }

        public void ApplyCharacterSnapshot(DFMPCharacterSnapshotMessage snapshot)
        {
            LastCharacterSnapshot = snapshot;
        }

        public bool ShouldReportLocalIdentity()
        {
            // A returning player must stay silent until the server snapshot is applied (InGame),
            // otherwise the first report overwrites the stored character with local startup state.
            return State == DFMPClientJoinState.FirstJoinCharacterCreation || State == DFMPClientJoinState.InGame;
        }

        public static bool ShouldLoadGameScene(DFMPJoinResultMessage result, int activeSceneIndex)
        {
            return (result.Decision == DFMPJoinDecisionKind.FirstJoin ||
                    result.Decision == DFMPJoinDecisionKind.ReturningPlayer) &&
                activeSceneIndex == SceneControl.StartupSceneIndex;
        }
    }

    public sealed class DFMPClientJoinFlowController : MonoBehaviour
    {
        const float ConnectionNoticeDuration = 4f;

        public static DFMPClientJoinFlowController Instance { get; private set; }
        public DFMPClientJoinFlow Flow { get; private set; }
        public bool ServerIdentityApplied { get; private set; }

        DFMPCharacterSnapshotMessage pendingSnapshot;
        bool hasPendingSnapshot;
        string pendingQuestTransferId;
        string pendingQuestPayload;
        bool introQuestSuppressionStarted;
        string pendingConnectionNotice;
        string pendingKickNotice;
        bool shouldShowKickNotice;
        bool joinChatPublished;
        DFMPCharacterSelectWindow characterSelectWindow;
        DFMPDiscordAuthorizeWindow discordAuthorizeWindow;
        DFMPCharacterRosterMessage pendingRoster;
        bool hasPendingRoster;

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
            TryShowKickNotice();
            TryShowConnectionNotice();

            StartGameBehaviour startGameBehaviour = FindObjectOfType<StartGameBehaviour>();
            if (!hasPendingSnapshot || ServerIdentityApplied ||
                !IsQuestPayloadReady(pendingSnapshot, pendingQuestTransferId, pendingQuestPayload) ||
                startGameBehaviour == null ||
                startGameBehaviour.LastStartMethod != StartGameBehaviour.StartMethods.NewCharacter ||
                GameManager.Instance == null || GameManager.Instance.PlayerEntity == null)
                return;

            ApplySnapshotToLocalPlayer(pendingSnapshot);
        }

        void TryShowConnectionNotice()
        {
            if (string.IsNullOrEmpty(pendingConnectionNotice))
                return;

            // The HUD only exists once the world is running, so the notice waits for it rather than being dropped.
            if (DaggerfallUI.Instance == null || DaggerfallUI.Instance.DaggerfallHUD == null ||
                GameManager.Instance == null || !GameManager.Instance.IsPlayingGame())
                return;

            DaggerfallUI.AddHUDText(pendingConnectionNotice, ConnectionNoticeDuration);
            pendingConnectionNotice = null;
        }

        void TryShowKickNotice()
        {
            if (!shouldShowKickNotice || DaggerfallUI.UIManager == null)
                return;

            shouldShowKickNotice = false;
            string message = DFMPAdminProtocol.GetKickNoticeText(pendingKickNotice);
            pendingKickNotice = null;

            var messageBox = new DaggerfallMessageBox(DaggerfallUI.UIManager, DaggerfallUI.UIManager.TopWindow, true);
            messageBox.PauseWhileOpen = true;
            messageBox.SetText(message);
            messageBox.AddButton(DaggerfallMessageBox.MessageBoxButtons.OK, true);
            messageBox.OnButtonClick += (sender, button) =>
            {
                sender.CloseWindow();
                ReturnToStartupScreen();
            };
            DaggerfallUI.UIManager.PushWindow(messageBox);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StartGameBehaviour.OnStartGame -= OnStartGameBehaviourStarted;
            StartGameBehaviour.OnStartMenu -= OnStartMenuOpened;
        }

        public void MarkConnecting()
        {
            StartGameBehaviour.OnStartGame -= OnStartGameBehaviourStarted;
            StartGameBehaviour.OnStartMenu -= OnStartMenuOpened;
            introQuestSuppressionStarted = false;
            pendingConnectionNotice = null;
            pendingKickNotice = null;
            shouldShowKickNotice = false;
            joinChatPublished = false;
            pendingQuestTransferId = null;
            pendingQuestPayload = null;
            hasPendingRoster = false;
            CloseCharacterSelectWindow(true);
            CloseDiscordAuthorizeWindow();
            Flow.MarkConnecting();
        }

        public void SetPendingKickNotice(string message)
        {
            pendingKickNotice = DFMPAdminProtocol.GetKickNoticeText(message);
        }

        public void HandleClientDisconnected()
        {
            Flow.MarkDisconnected();
            pendingConnectionNotice = null;
            joinChatPublished = false;
            hasPendingRoster = false;
            CloseCharacterSelectWindow(true);
            CloseDiscordAuthorizeWindow();

            // An authentication rejection disconnects before any join result arrives, so surface its
            // reason through the same notice the kick flow uses.
            if (string.IsNullOrEmpty(pendingKickNotice) &&
                DFMPNetworkAuthenticator.LastClientResultCode != DFMPAuthResultCode.Accepted &&
                !string.IsNullOrEmpty(DFMPNetworkAuthenticator.LastClientRejectionReason))
            {
                pendingKickNotice = DFMPNetworkAuthenticator.LastClientRejectionReason;
            }

            shouldShowKickNotice = !string.IsNullOrEmpty(pendingKickNotice);
        }

        void ShowCharacterSelectWindow(DFMPCharacterRosterMessage roster)
        {
            var uiManager = DaggerfallUI.UIManager;
            if (uiManager == null)
                return;

            if (characterSelectWindow == null)
            {
                characterSelectWindow = new DFMPCharacterSelectWindow(uiManager, uiManager.TopWindow, roster);
                uiManager.PushWindow(characterSelectWindow);
            }

            characterSelectWindow.ApplyRoster(roster);
        }

        void CloseCharacterSelectWindow(bool keepConnection)
        {
            if (characterSelectWindow == null)
                return;

            characterSelectWindow.CloseForGameplay(keepConnection);
            characterSelectWindow = null;
        }

        public void ShowDiscordAuthorize(string authorizeUri, string status)
        {
            var uiManager = DaggerfallUI.UIManager;
            if (uiManager == null)
                return;

            if (discordAuthorizeWindow == null)
            {
                discordAuthorizeWindow = new DFMPDiscordAuthorizeWindow(uiManager, uiManager.TopWindow);
                uiManager.PushWindow(discordAuthorizeWindow);
            }

            discordAuthorizeWindow.Apply(authorizeUri, status);
        }

        public void UpdateDiscordAuthorizeStatus(string status)
        {
            if (discordAuthorizeWindow != null)
                discordAuthorizeWindow.SetStatus(status);
        }

        public void CloseDiscordAuthorizeWindow()
        {
            if (discordAuthorizeWindow == null)
                return;

            discordAuthorizeWindow.CloseWindow();
            discordAuthorizeWindow = null;
        }

        void ReturnToStartupScreen()
        {
            pendingKickNotice = null;
            shouldShowKickNotice = false;
            DFMPNetworkClient.Stop();
            SceneManager.LoadScene(SceneControl.StartupSceneIndex);
        }

        public void ApplyJoinResult(DFMPJoinResultMessage result)
        {
            Flow.ApplyJoinResult(result);

            if (result.Decision == DFMPJoinDecisionKind.Rejected)
            {
                CloseCharacterSelectWindow(true);
                ShowJoinRejectedMessage(result.Reason);
                return;
            }

            if (result.Decision == DFMPJoinDecisionKind.AwaitingCharacterSelection)
            {
                if (!joinChatPublished)
                {
                    pendingConnectionNotice = BuildConnectionNotice(result.ServerName);
                    PublishJoinChatMessages(result);
                    joinChatPublished = true;
                }

                if (hasPendingRoster)
                    ShowCharacterSelectWindow(pendingRoster);

                return;
            }

            CloseCharacterSelectWindow(true);

            if (!joinChatPublished)
            {
                pendingConnectionNotice = BuildConnectionNotice(result.ServerName);
                PublishJoinChatMessages(result);
                joinChatPublished = true;
            }

            if (DFMPClientJoinFlow.ShouldLoadGameScene(result, SceneManager.GetActiveScene().buildIndex))
            {
                DaggerfallUnity.Settings.ShowOptionsAtStart = false;
                SceneManager.LoadScene(SceneControl.GameSceneIndex);
                return;
            }

            // Joining from the server list means the game scene is already loaded and OnSceneLoaded will never fire.
            BeginMultiplayerStartup();
        }

        public void ApplyCharacterRoster(DFMPCharacterRosterMessage roster)
        {
            pendingRoster = roster;
            hasPendingRoster = true;

            if (Flow == null || Flow.State != DFMPClientJoinState.AwaitingCharacterSelection)
                return;

            ShowCharacterSelectWindow(roster);
        }

        public static string BuildConnectionNotice(string serverName)
        {
            return string.IsNullOrWhiteSpace(serverName)
                ? "Connected to server."
                : string.Format("Connected to {0}.", serverName.Trim());
        }

        public static string BuildWelcomeMessage(string serverName)
        {
            return string.IsNullOrWhiteSpace(serverName)
                ? "Welcome to the server."
                : string.Format("Welcome to {0}.", serverName.Trim());
        }

        static void PublishJoinChatMessages(DFMPJoinResultMessage result)
        {
            PublishLocalChatMessage(DFMPChatMessageKind.Welcome, BuildWelcomeMessage(result.ServerName));

            if (!string.IsNullOrWhiteSpace(result.Motd))
                PublishLocalChatMessage(DFMPChatMessageKind.Motd, result.Motd.Trim());
        }

        static void PublishLocalChatMessage(DFMPChatMessageKind kind, string text)
        {
            var message = new DFMPChatDeliveryMessage
            {
                Kind = kind,
                ConnectionId = -1,
                SenderDisplayName = string.Empty,
                Text = text
            };
            DFMPEventBus.Instance.PublishChatMessageReceived(new DFMPChatMessageReceivedEvent
            {
                Kind = message.Kind,
                ConnectionId = message.ConnectionId,
                SenderDisplayName = message.SenderDisplayName,
                MessageText = message.Text,
                Message = message
            });
        }

        static void ShowJoinRejectedMessage(string reason)
        {
            Debug.LogWarning($"[DFMP Join] Join rejected by server: reason={reason}.");

            var uiManager = DaggerfallUI.UIManager;
            if (uiManager == null)
                return;

            var messageBox = new DaggerfallMessageBox(uiManager, uiManager.TopWindow);
            messageBox.SetText(string.IsNullOrWhiteSpace(reason)
                ? "The server refused the connection."
                : string.Format("The server refused the connection: {0}.", reason));
            messageBox.ClickAnywhereToClose = true;
            uiManager.PushWindow(messageBox);
        }

        public void ApplyCharacterSnapshot(DFMPCharacterSnapshotMessage snapshot)
        {
            Flow.ApplyCharacterSnapshot(snapshot);
            pendingSnapshot = snapshot;
            hasPendingSnapshot = true;
            if (!snapshot.HasQuestState ||
                !string.Equals(pendingQuestTransferId, snapshot.QuestTransferId, System.StringComparison.Ordinal))
            {
                pendingQuestTransferId = null;
                pendingQuestPayload = null;
            }
        }

        public void ApplyQuestStatePayload(string transferId, string payload)
        {
            if (string.IsNullOrEmpty(transferId) || string.IsNullOrEmpty(payload))
                return;

            pendingQuestTransferId = transferId;
            pendingQuestPayload = payload;
        }

        public static bool IsQuestPayloadReady(
            DFMPCharacterSnapshotMessage snapshot,
            string transferId,
            string payload)
        {
            return !snapshot.HasQuestState ||
                (!string.IsNullOrEmpty(payload) &&
                 string.Equals(snapshot.QuestTransferId, transferId, System.StringComparison.Ordinal));
        }

        public bool ShouldReportLocalIdentity()
        {
            return Flow.ShouldReportLocalIdentity();
        }

        public static bool IsMultiplayerIntroQuest(string questName)
        {
            return string.Equals(questName, "_TUTOR__", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(questName, "_BRISIEN", System.StringComparison.OrdinalIgnoreCase);
        }

        void ApplySnapshotToLocalPlayer(DFMPCharacterSnapshotMessage snapshot)
        {
            var playerEntity = GameManager.Instance.PlayerEntity;
            playerEntity.Name = snapshot.CharacterName;
            playerEntity.BirthRaceTemplate = DaggerfallWorkshop.Game.Player.CharacterDocument.GetRaceTemplate((Races)snapshot.Race);
            playerEntity.Gender = (Genders)snapshot.Gender;
            playerEntity.FaceIndex = snapshot.FaceVariant;
            ApplyCareer(playerEntity, snapshot.CareerJson);
            playerEntity.Level = snapshot.Level;
            playerEntity.MaxHealth = snapshot.MaxHealth;
            playerEntity.CurrentHealth = snapshot.Health;
            playerEntity.CurrentMagicka = snapshot.SpellPoints;
            playerEntity.CurrentFatigue = snapshot.Fatigue;
            for (int index = 0; index < snapshot.Attributes.Length; index++)
                playerEntity.Stats.SetPermanentStatValue(index, Mathf.Clamp(snapshot.Attributes[index], 0, 100));
            for (int index = 0; index < snapshot.Skills.Length; index++)
                playerEntity.Skills.SetPermanentSkillValue(index, (short)Mathf.Clamp(snapshot.Skills[index], 0, 100));
            playerEntity.GoldPieces = snapshot.Gold;
            ApplyProgression(playerEntity, snapshot.StartingLevelUpSkillSum);
            bool questStateRestored = !snapshot.HasQuestState;
            DFMPQuestStateEnvelope questState = null;
            if (snapshot.HasQuestState)
            {
                string questReason;
                if (DFMPQuestStateCodec.TryDecode(pendingQuestPayload, out questState, out questReason) &&
                    DFMPQuestStateCodec.TryRestore(questState, out questReason))
                {
                    questStateRestored = true;
                    Debug.Log($"[DFMP Join] Restored quest state before inventory: quests={QuestMachine.Instance.QuestCount}, siteLinks={QuestMachine.Instance.SiteLinkCount}.");
                }
                else
                {
                    Debug.LogWarning($"[DFMP Join] Quest state restore failed; quest-linked inventory will not be restored: reason={questReason}.");
                }
            }

            DFMPCharacterItemRecord[] inventory;
            DFMPCharacterEquipmentRecord[] equipment;
            string inventoryReason = string.Empty;
            if (questStateRestored &&
                !string.IsNullOrWhiteSpace(snapshot.InventoryJson) &&
                DFMPInventorySnapshotCodec.TryDecode(snapshot.InventoryJson, out inventory, out equipment, out inventoryReason))
            {
                // Clear first so starting equipment cannot keep referencing items the deserialize wipes out.
                playerEntity.ItemEquipTable.Clear();
                playerEntity.Items.DeserializeItems(DFMPCharacterPersistence.RestoreItems(inventory));
                playerEntity.ItemEquipTable.DeserializeEquipTable(
                    DFMPCharacterPersistence.RestoreEquipment(equipment),
                    playerEntity.Items);

                int equippedCount = 0;
                ulong[] restoredSlots = playerEntity.ItemEquipTable.SerializeEquipTable();
                for (int slotIndex = 0; slotIndex < restoredSlots.Length; slotIndex++)
                {
                    if (restoredSlots[slotIndex] != 0)
                        equippedCount++;
                }

                Debug.Log($"[DFMP Join] Restored inventory snapshot: requestedItems={inventory.Length}, actualItems={playerEntity.Items.Count}, requestedEquipment={equipment.Length}, equippedItems={equippedCount}.");
                if (DaggerfallUI.Instance != null && DaggerfallUI.Instance.PaperDollRenderer != null)
                    DaggerfallUI.Instance.PaperDollRenderer.Refresh();
            }
            else if (questStateRestored && !string.IsNullOrWhiteSpace(snapshot.InventoryJson))
            {
                Debug.LogWarning($"[DFMP Join] Inventory snapshot rejected during restore: reason={inventoryReason}.");
            }

            if (questStateRestored && questState != null && questState.LightSourceUID != 0)
                playerEntity.LightSource = playerEntity.Items.GetItem(questState.LightSourceUID);
            ServerIdentityApplied = true;
            hasPendingSnapshot = false;
            pendingQuestTransferId = null;
            pendingQuestPayload = null;
            Flow.MarkInGame();
            Debug.Log($"[DFMP Join] Applied server character snapshot: name='{snapshot.CharacterName}', race={snapshot.Race}, gender={snapshot.Gender}, face={snapshot.FaceVariant}, level={snapshot.Level}, health={snapshot.Health}/{snapshot.MaxHealth}, spellPoints={snapshot.SpellPoints}/{snapshot.MaxSpellPoints}, fatigue={snapshot.Fatigue}/{snapshot.MaxFatigue}, startingSkillSum={playerEntity.StartingLevelUpSkillSum}, currentSkillSum={playerEntity.CurrentLevelUpSkillSum}.");
        }

        static void ApplyCareer(DaggerfallWorkshop.Game.Entity.PlayerEntity playerEntity, string careerJson)
        {
            // Without this the client keeps DFU's default Mage career, which drives the wrong
            // level-up skill set, magicka pool, tolerances, and class advantages.
            DaggerfallConnect.DFCareer career;
            string reason;
            if (DFMPCareerCodec.TryDecode(careerJson, out career, out reason))
            {
                playerEntity.Career = career;
                Debug.Log($"[DFMP Join] Restored character class: name='{career.Name}', spellPointMultiplier={career.SpellPointMultiplierValue}, hitPointsPerLevel={career.HitPointsPerLevel}.");
                return;
            }

            Debug.LogWarning($"[DFMP Join] Character class not restored, keeping local default '{playerEntity.Career?.Name}': reason={reason}.");
        }

        static void ApplyProgression(DaggerfallWorkshop.Game.Entity.PlayerEntity playerEntity, int startingLevelUpSkillSum)
        {
            // Zero means a pre-v2 record with no stored sum; DFU's own estimator recovers it from level and skills.
            if (startingLevelUpSkillSum > 0)
                playerEntity.StartingLevelUpSkillSum = startingLevelUpSkillSum;

            playerEntity.SetCurrentLevelUpSkillSum();
            if (startingLevelUpSkillSum <= 0)
                playerEntity.EstimateStartingLevelUpSkillSum();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.buildIndex == SceneControl.GameSceneIndex && Flow != null &&
                (Flow.State == DFMPClientJoinState.FirstJoinCharacterCreation ||
                 Flow.State == DFMPClientJoinState.ReturningPlayerRestore))
            {
                Debug.Log($"[DFMP Join] Multiplayer game scene loaded: state={Flow.State}.");
                BeginMultiplayerStartup();
            }
        }

        void BeginMultiplayerStartup()
        {
            // DFU's Start In Dungeon option would send every join through Privateer's Hold.
            DaggerfallUnity.Settings.StartInDungeon = false;

            if (Flow.State == DFMPClientJoinState.ReturningPlayerRestore)
                ConfigureReturningPlayerStartup();
            else if (Flow.State == DFMPClientJoinState.FirstJoinCharacterCreation)
                ConfigureFirstJoinStartup();

            if (Flow.EnableBeginnerTutorial || introQuestSuppressionStarted)
                return;

            introQuestSuppressionStarted = true;
            // OnStartGame fires at the end of StartNewCharacter, so the intro quests are removed
            // in the same frame they start and before QuestMachine can tick their popups.
            StartGameBehaviour.OnStartGame += OnStartGameBehaviourStarted;
        }

        void OnStartGameBehaviourStarted(object sender, System.EventArgs args)
        {
            StartGameBehaviour.OnStartGame -= OnStartGameBehaviourStarted;
            introQuestSuppressionStarted = false;
            RemoveIntroQuests();
        }

        static void RemoveIntroQuests()
        {
            if (QuestMachine.Instance == null)
                return;

            int removedQuestCount = 0;
            foreach (ulong questUid in QuestMachine.Instance.GetAllActiveQuests())
            {
                Quest quest = QuestMachine.Instance.GetQuest(questUid);
                if (quest != null && IsMultiplayerIntroQuest(quest.QuestName))
                {
                    if (QuestMachine.Instance.RemoveQuest(questUid))
                        removedQuestCount++;
                }
            }

            Debug.Log($"[DFMP Join] Multiplayer intro quests disabled by server: removedQuestCount={removedQuestCount}.");
        }

        void ConfigureReturningPlayerStartup()
        {
            var startGameBehaviour = FindObjectOfType<StartGameBehaviour>();
            if (startGameBehaviour == null)
                return;

            startGameBehaviour.StartMethod = StartGameBehaviour.StartMethods.NewCharacter;
            Debug.Log("[DFMP Join] Returning player startup configured without local save selection.");
        }

        void ConfigureFirstJoinStartup()
        {
            var startGameBehaviour = FindObjectOfType<StartGameBehaviour>();
            if (startGameBehaviour == null)
                return;

            // Redirecting the title menu's post-start message skips the splash video and the
            // single-player load/new-game screen, landing straight in character creation.
            startGameBehaviour.EnableVideos = false;
            startGameBehaviour.StartMethod = StartGameBehaviour.StartMethods.TitleMenu;
            startGameBehaviour.PostStartMessage = DaggerfallUIMessages.dfuiStartNewGameWizard;
            StartGameBehaviour.OnStartMenu += OnStartMenuOpened;
            Debug.Log("[DFMP Join] First join startup configured directly into character creation.");
        }

        void OnStartMenuOpened(object sender, System.EventArgs args)
        {
            StartGameBehaviour.OnStartMenu -= OnStartMenuOpened;

            // StartNewCharacter re-posts PostStartMessage, which would reopen character creation forever.
            var startGameBehaviour = sender as StartGameBehaviour;
            if (startGameBehaviour != null)
                startGameBehaviour.PostStartMessage = string.Empty;
        }
    }
}

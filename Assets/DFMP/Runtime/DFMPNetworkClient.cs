using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
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
        static readonly DFMPQuestPayloadAssembler questPayloadAssembler = new DFMPQuestPayloadAssembler();

        public static event Action<DFMPAdminRosterResponse> AdminRosterReceived;
        public static event Action<DFMPCharacterRosterMessage> CharacterRosterReceived;
        public static event Action<DFMPCharacterActionResultMessage> CharacterActionResultReceived;

        public static bool IsConnected
        {
            get { return NetworkClient.isConnected; }
        }

        public static void Start(string address, ushort port, int tickRate, string accountId = null, string credential = null)
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

            DFMPClientSession.EnsureLoaded();
            if (string.IsNullOrWhiteSpace(accountId))
                accountId = DFMPClientSession.AccountId;

            AccountId = string.IsNullOrWhiteSpace(accountId) ? GetDefaultAccountId() : accountId;
            DFMPNetworkAuthenticator.ClientAccountId = AccountId;
            DFMPNetworkAuthenticator.ClientCredential = credential ?? DFMPClientSession.Credential;

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
            Manager.authenticator = networkGo.AddComponent<DFMPNetworkAuthenticator>();
            Manager.dontDestroyOnLoad = true;
            Manager.runInBackground = true;
            Manager.headlessStartMode = HeadlessStartOptions.DoNothing;
            Manager.transport = Transport;
            Manager.networkAddress = Address;
            Manager.autoCreatePlayer = false;
            Manager.sendRate = tickRate;

            DFMPTimeState.RegisterClientSpawnHandler();
            DFMPWorldSettings.RegisterClientSpawnHandler();
            DFMPPlayerSessionState.RegisterClientSpawnHandler();
            DFMPDynamicEnemyState.RegisterClientSpawnHandler();
            DFMPRestSessionController.EnsureInstance();
            DFMPSpawnAssignmentController.RegisterClientHandler();
            DFMPDeveloperCommandController.RegisterClient();
            NetworkClient.RegisterHandler<DFMPChatDeliveryMessage>(OnChatMessageReceived);
            NetworkClient.RegisterHandler<DFMPAdminRosterResponse>(OnAdminRosterReceived);
            NetworkClient.RegisterHandler<DFMPAdminKickNotice>(OnAdminKickNoticeReceived);
            NetworkClient.RegisterHandler<DFMPJoinResultMessage>(OnJoinResultReceived);
            NetworkClient.RegisterHandler<DFMPCharacterRosterMessage>(OnCharacterRosterReceived);
            NetworkClient.RegisterHandler<DFMPCharacterActionResultMessage>(OnCharacterActionResultReceived);
            NetworkClient.RegisterHandler<DFMPCharacterSnapshotMessage>(OnCharacterSnapshotReceived);
            NetworkClient.RegisterHandler<DFMPQuestPayloadChunkMessage>(OnQuestPayloadChunkReceived);
            NetworkClient.RegisterHandler<DFMPVitalSnapshot>(OnVitalSnapshotReceived);
            NetworkClient.RegisterHandler<DFMPRestResponse>(OnRestResponseReceived);
            NetworkClient.RegisterHandler<DFMPActionDoorSyncMessage>(OnActionDoorSyncReceived);
            DFMPPositionReporter.EnsureInstance();
            DFMPRemotePlayerPresentationController.EnsureInstance();
            DFMPDynamicEnemyPresentationController.EnsureInstance();

            if (DaggerfallUnity.Instance != null)
                DaggerfallUnity.Instance.Option_ImportEnemyPrefabs = false;

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
            RequestRest(restModeName, DFMPRestRequestKind.Start);
        }

        public static void RequestRest(string restModeName, DFMPRestRequestKind kind)
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPRestRequest { RestModeName = restModeName, Kind = kind });
        }

        public static void RequestPlayerDamage(int targetConnectionId, int amount)
        {
            if (NetworkClient.isConnected && NetworkClient.ready)
                NetworkClient.Send(new DFMPDeveloperDamagePlayerRequest
                {
                    TargetConnectionId = targetConnectionId,
                    Amount = amount
                });
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
                Debug.Log($"[DFMP Rest] Server accepted rest request: kind={message.Kind}.");
            else
            {
                Debug.LogWarning($"[DFMP Rest] Server rejected rest request: kind={message.Kind}, reason={message.RejectionReason}.");
                DFMPRestSessionController.NotifyRequestRejected(message.Kind);
            }
        }

        public static void RequestSelectCharacter(string characterId)
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPSelectCharacterMessage { CharacterId = characterId ?? string.Empty });
        }

        public static void RequestCreateCharacter()
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPCreateCharacterMessage());
        }

        public static void RequestDeleteCharacter(string characterId)
        {
            if (NetworkClient.isConnected)
                NetworkClient.Send(new DFMPDeleteCharacterMessage { CharacterId = characterId ?? string.Empty });
        }

        static void OnJoinResultReceived(DFMPJoinResultMessage message)
        {
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.ApplyJoinResult(message);

            if (message.Decision == DFMPJoinDecisionKind.Rejected)
            {
                Debug.LogWarning($"[DFMP Join] Server rejected account '{message.AccountId}': {message.Reason}.");
                return;
            }

            Debug.Log($"[DFMP Join] Accepted account '{message.AccountId}': decision={message.Decision}, world='{message.ServerWorldId}'.");
            if (message.Decision == DFMPJoinDecisionKind.AwaitingCharacterSelection)
                return;

            if (NetworkClient.isConnected && !NetworkClient.ready)
                NetworkClient.Ready();
        }

        static void OnCharacterRosterReceived(DFMPCharacterRosterMessage message)
        {
            if (message.Characters == null)
                message.Characters = new DFMPCharacterSummary[0];

            Debug.Log($"[DFMP Join] Received character roster: count={message.Characters.Length}.");

            CharacterRosterReceived?.Invoke(message);
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.ApplyCharacterRoster(message);
        }

        static void OnCharacterActionResultReceived(DFMPCharacterActionResultMessage message)
        {
            CharacterActionResultReceived?.Invoke(message);
            if (!message.Accepted)
                Debug.LogWarning($"[DFMP Join] Character action rejected: action={message.Action}, reason={message.Reason}.");
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

        static void OnQuestPayloadChunkReceived(DFMPQuestPayloadChunkMessage message)
        {
            DFMPClientJoinFlowController controller = DFMPClientJoinFlowController.Instance;
            if (controller == null || controller.Flow == null ||
                !controller.Flow.LastCharacterSnapshot.HasQuestState ||
                !string.Equals(
                    controller.Flow.LastCharacterSnapshot.QuestTransferId,
                    message.TransferId,
                    StringComparison.Ordinal) ||
                controller.Flow.LastCharacterSnapshot.QuestPayloadBytes != message.TotalBytes ||
                !string.Equals(
                    controller.Flow.LastCharacterSnapshot.QuestPayloadChecksum,
                    message.Checksum,
                    StringComparison.Ordinal))
            {
                Debug.LogWarning("[DFMP Quest] Rejected quest payload chunk not described by the pending character snapshot.");
                questPayloadAssembler.Reset();
                return;
            }

            string payload;
            string reason;
            if (!questPayloadAssembler.TryAdd(message, out payload, out reason))
            {
                Debug.LogWarning($"[DFMP Quest] Rejected quest payload chunk: reason={reason}.");
                return;
            }

            if (!string.IsNullOrEmpty(payload))
            {
                controller.ApplyQuestStatePayload(message.TransferId, payload);
                Debug.Log($"[DFMP Quest] Received complete quest payload: bytes={message.TotalBytes}, chunks={message.ChunkCount}.");
            }
        }

        public static bool SendQuestState(DFMPQuestStateEnvelope envelope, out string checksum)
        {
            checksum = string.Empty;
            string reason;
            if (!NetworkClient.isConnected || envelope == null ||
                !DFMPQuestStateCodec.TryValidate(envelope, out reason))
                return false;

            DFMPQuestPayloadChunkMessage[] chunks;
            if (!DFMPQuestPayloadProtocol.TryCreateChunks(
                DFMPQuestStateCodec.Encode(envelope),
                out chunks,
                out reason))
            {
                Debug.LogWarning($"[DFMP Quest] Quest state was not sent: reason={reason}.");
                return false;
            }

            checksum = chunks[0].Checksum;
            for (int index = 0; index < chunks.Length; index++)
                NetworkClient.Send(chunks[index]);
            return true;
        }

        static void OnVitalSnapshotReceived(DFMPVitalSnapshot message)
        {
            if (!DFMPCombatProtocol.IsValidVitalSnapshot(message) ||
                message.ConnectionId != DFMPSpawnAssignmentController.LocalConnectionId)
            {
                Debug.LogWarning("[DFMP Combat] Rejected invalid or non-owner vital snapshot.");
                return;
            }

            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
            {
                Debug.LogWarning("[DFMP Combat] Cannot apply vital snapshot because the local player is unavailable.");
                return;
            }

            var playerEntity = GameManager.Instance.PlayerEntity;
            playerEntity.MaxHealth = message.MaxHealth;
            playerEntity.CurrentHealth = message.Health;
            playerEntity.CurrentMagicka = message.SpellPoints;
            playerEntity.CurrentFatigue = message.Fatigue;
            DFMPRestSessionController.NotifyVitalSnapshot(message.Health);
            Debug.Log($"[DFMP Combat] Applied authoritative vital snapshot: health={message.Health}/{message.MaxHealth}, fatigue={message.Fatigue}/{message.MaxFatigue}, spellPoints={message.SpellPoints}/{message.MaxSpellPoints}, dead={message.IsDead}.");
        }

        static void OnActionDoorSyncReceived(DFMPActionDoorSyncMessage message)
        {
            if (message.LoadID == 0)
                return;

            foreach (DaggerfallActionDoor door in ActiveGameObjectDatabase.GetActiveActionDoors())
            {
                if (door != null && door.LoadID == message.LoadID)
                {
                    door.SetOpen(message.IsOpen);
                    Debug.Log($"[DFMP World] Applied synced action door state: loadID={message.LoadID}, isOpen={message.IsOpen}.");
                    break;
                }
            }
        }

        static string GetDefaultAccountId()
        {
            string userName = Environment.UserName;
            return string.IsNullOrWhiteSpace(userName) ? "local:player" : "local:" + userName;
        }

        public static void Stop()
        {
            DFMPPositionReporter.FlushPersistentState();
            questPayloadAssembler.Reset();
            DFMPNetworkManager networkManager = Manager;
            if (networkManager != null && networkManager.isNetworkActive)
                networkManager.StopClient();

            if (networkManager != null)
                UnityEngine.Object.Destroy(networkManager.gameObject);

            DFMPSpawnAssignmentController.Reset();
            DFMPPositionReporter.Reset();
            DFMPRemotePlayerPresentationController.Reset();
            DFMPDynamicEnemyPresentationController.Reset();
            DFMPRestSessionController.Reset();

            if (DaggerfallUnity.Instance != null)
                DaggerfallUnity.Instance.Option_ImportEnemyPrefabs = true;

            Manager = null;
            Transport = null;
            Address = "127.0.0.1";
            Port = 0;
            AccountId = null;
        }
    }
}

using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using kcp2k;
using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPNetworkServer
    {
        public static DFMPNetworkManager Manager { get; private set; }
        public static KcpTransport Transport { get; private set; }
        public static DFMPTimeState TimeState { get; private set; }
        public static ushort Port { get; private set; }
        public static string ServerName { get; set; } = "Tony's DFU RP";
        public static int MaxConnections { get; private set; } = 16;
        public static string Motd { get; set; } = "Welcome to Daggerfall Unity Multiplayer";
        public static DFMPServerConfig Config { get; private set; }
        public static IDFMPCharacterStore CharacterStore { get; private set; }
        public static DFMPDungeonGeometryService DungeonGeometryService { get; private set; }
        public static DFMPDungeonEnemyRosterService DungeonEnemyRosterService { get; private set; }

        public static int ConnectedPlayerCount
        {
            get { return playerSessionStates.Count; }
        }

        static DFMPServerDiscoveryListener discoveryListener;

        static readonly Dictionary<int, DFMPPlayerSessionState> playerSessionStates = new Dictionary<int, DFMPPlayerSessionState>();
        static readonly Dictionary<int, DFMPJoinDecision> joinDecisions = new Dictionary<int, DFMPJoinDecision>();
        static readonly DFMPActiveAccountRegistry activeAccounts = new DFMPActiveAccountRegistry();
        static readonly DFMPWorldOccupancyRegistry worldOccupancy = new DFMPWorldOccupancyRegistry();
        static readonly Dictionary<int, string> startMarkerAssignments = new Dictionary<int, string>();
        static readonly Dictionary<int, DFMPTransitionAssignmentState> transitionAssignmentStates = new Dictionary<int, DFMPTransitionAssignmentState>();
        static readonly Dictionary<int, DFMPVitalState> vitalStates = new Dictionary<int, DFMPVitalState>();
        static readonly HashSet<int> initializedIdentityReportConnections = new HashSet<int>();
        static readonly Dictionary<int, uint> lastDamageSequences = new Dictionary<int, uint>();
        static readonly Dictionary<int, ulong> lastDamageRequestIds = new Dictionary<int, ulong>();
        static readonly Dictionary<int, float> lastDamageTimes = new Dictionary<int, float>();
        static readonly Dictionary<int, float> damageWindowStartTimes = new Dictionary<int, float>();
        static readonly Dictionary<int, int> damageWindowCounts = new Dictionary<int, int>();
        static readonly Dictionary<int, float> lastPositionReportTimes = new Dictionary<int, float>();
        static readonly Dictionary<int, float> lastActionReportTimes = new Dictionary<int, float>();
        static readonly HashSet<int> activePositionReportConnections = new HashSet<int>();
        static readonly HashSet<int> rejectedPositionReportConnections = new HashSet<int>();
        static readonly HashSet<int> pendingAdminKickConnections = new HashSet<int>();
        static readonly DFMPChatLifecycleNotifier chatLifecycleNotifier = new DFMPChatLifecycleNotifier();
        static int nextTransitionAssignmentId = 1;

        public static bool IsListening
        {
            get { return NetworkServer.active && Transport != null && Transport.ServerActive(); }
        }

        public static DFMPWorldContextKey CreateExteriorWorldContext(int worldX, int worldZ, string locationId)
        {
            var mapPixel = MapsFile.WorldCoordToMapPixel(worldX, worldZ);
            return new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = mapPixel.X,
                MapPixelY = mapPixel.Y,
                LocationId = locationId
            };
        }

        public static bool TryGetSessionWorldContext(int connectionId, out DFMPWorldContextKey context)
        {
            return worldOccupancy.TryGetContext(connectionId, out context);
        }

        public static int[] GetConnectionsInWorldContext(DFMPWorldContextKey context)
        {
            return worldOccupancy.GetConnectionsInContext(context);
        }

        public static int[] GetConnectionsInInterestScope(DFMPWorldContextKey context)
        {
            return worldOccupancy.GetConnectionsInInterestScope(context);
        }

        public static bool TryGetPlayerSessionState(int connectionId, out DFMPPlayerSessionState sessionState)
        {
            return playerSessionStates.TryGetValue(connectionId, out sessionState);
        }

        public static bool TryGetStartMarkerAssignment(int connectionId, out string markerName)
        {
            return startMarkerAssignments.TryGetValue(connectionId, out markerName);
        }

        public static bool TryApplyWorldContextReport(int connectionId, DFMPPlayerSessionState sessionState, DFMPWorldContextReport report, out DFMPWorldContextRejectionReason rejectionReason)
        {
            rejectionReason = DFMPWorldContextProtocol.GetRejectionReason(sessionState, report);
            if (rejectionReason != DFMPWorldContextRejectionReason.None)
                return false;

            DFMPWorldContextKey context;
            DFMPWorldContextProtocol.TryCreateKey(report, out context);
            SetSessionWorldContext(connectionId, sessionState, context, "client-report");
            return true;
        }

        public static bool SetSessionWorldContext(int connectionId, DFMPPlayerSessionState sessionState, DFMPWorldContextKey context, string reason)
        {
            if (sessionState != null)
                playerSessionStates[connectionId] = sessionState;

            DFMPWorldContextKey previousContext;
            bool hadPreviousContext = worldOccupancy.TryGetContext(connectionId, out previousContext);
            if (hadPreviousContext && previousContext.Equals(context))
                return false;

            worldOccupancy.SetContext(connectionId, context);
            DFMPEventBus.Instance.PublishPlayerWorldContextChanged(new DFMPPlayerWorldContextChangedEvent
            {
                ConnectionId = connectionId,
                SessionState = sessionState,
                HadPreviousContext = hadPreviousContext,
                PreviousContext = previousContext,
                CurrentContext = context,
                Reason = reason ?? string.Empty
            });

            DFMPEventBus.Instance.PublishLocationEntered(new DFMPLocationEnteredEvent
            {
                ConnectionId = connectionId,
                SessionState = sessionState,
                Context = context,
                Reason = reason ?? string.Empty
            });

            if (context.Kind == DFMPWorldContextKind.Dungeon && context.DungeonBlockIndex > 0)
            {
                DFMPEventBus.Instance.PublishDungeonBlockEntered(new DFMPDungeonBlockEnteredEvent
                {
                    ConnectionId = connectionId,
                    SessionState = sessionState,
                    Context = context,
                    Reason = reason ?? string.Empty
                });
            }

            return true;
        }

        static bool IsWorldContextChanged(int connectionId, DFMPWorldContextReport report)
        {
            DFMPWorldContextKey previousContext;
            DFMPWorldContextKey nextContext;
            return !worldOccupancy.TryGetContext(connectionId, out previousContext) ||
                !DFMPWorldContextProtocol.TryCreateKey(report, out nextContext) ||
                !previousContext.Equals(nextContext);
        }

        static void SaveCharacterRecord(int connectionId, DFMPPlayerSessionState sessionState)
        {
            if (CharacterStore == null || sessionState == null)
                return;

            DFMPJoinDecision joinDecision;
            if (!joinDecisions.TryGetValue(connectionId, out joinDecision) || joinDecision.CharacterRecord == null)
                return;

            DFMPWorldContextKey context;
            if (worldOccupancy.TryGetContext(connectionId, out context))
                DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState, context);
            else
                DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState);
            CharacterStore.Save(joinDecision.CharacterRecord);
            Debug.Log($"[DFMP Character] Saved character record: connectionId={connectionId}, account='{joinDecision.AccountId}', world={sessionState.WorldX}/{sessionState.WorldY:F2}/{sessionState.WorldZ}.");
        }

        static void SaveAllCharacterRecords()
        {
            foreach (KeyValuePair<int, DFMPPlayerSessionState> entry in playerSessionStates)
                SaveCharacterRecord(entry.Key, entry.Value);
        }

        static DFMPVitalState CreateVitalState(DFMPCharacterRecord record)
        {
            return new DFMPVitalState
            {
                Health = record.Health,
                MaxHealth = record.MaxHealth,
                Fatigue = record.Fatigue,
                MaxFatigue = record.MaxFatigue,
                SpellPoints = record.SpellPoints,
                MaxSpellPoints = record.MaxSpellPoints
            };
        }

        static DFMPVitalSnapshot CreateVitalSnapshot(DFMPVitalState vitalState, int connectionId, bool isDead)
        {
            return new DFMPVitalSnapshot
            {
                ConnectionId = connectionId,
                Health = vitalState.Health,
                MaxHealth = vitalState.MaxHealth,
                Fatigue = vitalState.Fatigue,
                MaxFatigue = vitalState.MaxFatigue,
                SpellPoints = vitalState.SpellPoints,
                MaxSpellPoints = vitalState.MaxSpellPoints,
                IsDead = isDead || vitalState.Health <= 0
            };
        }

        static void SendVitalSnapshot(NetworkConnectionToClient conn, DFMPVitalState vitalState, bool isDead)
        {
            if (conn != null)
                conn.Send(CreateVitalSnapshot(vitalState, conn.connectionId, isDead));
        }

        static bool TryFinalizePendingDeathRespawnOnDisconnect(int connectionId, DFMPPlayerSessionState sessionState)
        {
            if (sessionState == null || !sessionState.IsDead)
                return false;

            DFMPTransitionAssignmentState assignmentState;
            if (!transitionAssignmentStates.TryGetValue(connectionId, out assignmentState) || assignmentState == null ||
                !assignmentState.HasPendingAssignment || assignmentState.Assignment.Kind != DFMPTransitionKind.DeathRespawn)
                return false;

            DFMPJoinDecision joinDecision;
            if (!joinDecisions.TryGetValue(connectionId, out joinDecision))
                return false;

            DFMPWorldPosition position;
            DFMPWorldContextKey context;
            string startMarkerName;
            if (!TryResolveRespawnPosition(joinDecision, out position, out context, out startMarkerName))
                return false;

            sessionState.SetPosition(position.WorldX, position.WorldY, position.WorldZ);
            sessionState.SetDead(false);
            sessionState.SetMovement(false);
            SetSessionWorldContext(connectionId, sessionState, context, "disconnect-death-respawn");

            if (joinDecision.CharacterRecord != null)
            {
                joinDecision.CharacterRecord.Health = joinDecision.CharacterRecord.MaxHealth;
                joinDecision.CharacterRecord.Fatigue = joinDecision.CharacterRecord.MaxFatigue;
                joinDecision.CharacterRecord.SpellPoints = joinDecision.CharacterRecord.MaxSpellPoints;
                vitalStates[connectionId] = CreateVitalState(joinDecision.CharacterRecord);
            }

            DFMPEventBus.Instance.PublishPlayerRespawned(new DFMPPlayerRespawnedEvent
            {
                ConnectionId = connectionId,
                SessionState = sessionState,
                Context = context,
                Reason = "disconnect-death-respawn"
            });

            Debug.Log($"[DFMP Respawn] Finalized pending death respawn on disconnect: connectionId={connectionId}, anchor={context.LocationId ?? string.Empty}, world={position.WorldX}/{position.WorldZ}.");
            return true;
        }

        public static void Start(ushort port, int tickRate, int maxConnections, string serverName = null, string motd = null, bool enableDiscovery = true, int discoveryPort = 7778, DFMPServerConfig config = null)
        {
            if (IsListening)
            {
                Debug.LogWarning($"[DFMP Net] Server already listening on port {Port}.");
                return;
            }

            if (NetworkManager.singleton != null && NetworkManager.singleton.isNetworkActive)
            {
                Debug.LogWarning("[DFMP Net] Mirror already has an active NetworkManager; skipping dedicated listener startup.");
                return;
            }

            Port = port;
            MaxConnections = maxConnections;
            Config = config ?? new DFMPServerConfig();
            Config.Normalize();
            CharacterStore = new DFMPFileCharacterStore(GetCharacterStoreDirectory());
            if (!string.IsNullOrEmpty(serverName))
                ServerName = serverName;
            if (motd != null)
                Motd = motd;

            GameObject networkGo = new GameObject("DFMP_NetworkServer");
            networkGo.SetActive(false);

            Transport = networkGo.AddComponent<KcpTransport>();
            Transport.port = port;
            Transport.NoDelay = true;
            Transport.Interval = 10;
            Transport.Timeout = 10000;
            Transport.MaximizeSocketBuffers = true;
            Transport.statisticsLog = false;

            networkGo.AddComponent<DFMPWorldInterestManagement>();

            if (DaggerfallUnity.Instance != null)
                DaggerfallUnity.Instance.Option_ImportEnemyPrefabs = false;

            DungeonGeometryService = networkGo.AddComponent<DFMPDungeonGeometryService>();
            DungeonGeometryService.Initialize();
            DungeonEnemyRosterService = networkGo.AddComponent<DFMPDungeonEnemyRosterService>();
            DungeonEnemyRosterService.Initialize(Config.Enemies);

            Manager = networkGo.AddComponent<DFMPNetworkManager>();
            Manager.dontDestroyOnLoad = true;
            Manager.runInBackground = true;
            Manager.headlessStartMode = HeadlessStartOptions.DoNothing;
            Manager.transport = Transport;
            Manager.networkAddress = "localhost";
            Manager.maxConnections = maxConnections;
            Manager.autoCreatePlayer = false;
            Manager.sendRate = tickRate;

            UnityEngine.Object.DontDestroyOnLoad(networkGo);
            networkGo.SetActive(true);

            Mirror.Transport.active = Transport;
            Manager.StartServer();
            NetworkServer.RegisterHandler<DFMPSpawnAcknowledgement>(OnSpawnAcknowledgement);
            NetworkServer.RegisterHandler<DFMPTransitionAcknowledgement>(OnTransitionAcknowledgement);
            NetworkServer.RegisterHandler<DFMPFastTravelRequest>(OnFastTravelRequest);
            NetworkServer.RegisterHandler<DFMPDoorTransitionRequest>(OnDoorTransitionRequest);
            NetworkServer.RegisterHandler<DFMPDungeonTransitionRequest>(OnDungeonTransitionRequest);
            NetworkServer.RegisterHandler<DFMPActionDoorSyncMessage>(OnActionDoorSyncMessage);
            NetworkServer.RegisterHandler<DFMPVampirismTransformationRequest>(OnVampirismTransformationRequest);
            NetworkServer.RegisterHandler<DFMPPlayerDeathReport>(OnPlayerDeathReport);
            NetworkServer.RegisterHandler<DFMPPlayerPositionReport>(OnPlayerPositionReport);
            NetworkServer.RegisterHandler<DFMPPlayerIdentityReport>(OnPlayerIdentityReport);
            NetworkServer.RegisterHandler<DFMPDamageIntent>(OnDamageIntent);
            NetworkServer.RegisterHandler<DFMPPlayerActionReport>(OnPlayerActionReport);
            NetworkServer.RegisterHandler<DFMPRestRequest>(OnRestRequest);
            NetworkServer.RegisterHandler<DFMPWorldContextReport>(OnWorldContextReport);
            NetworkServer.RegisterHandler<DFMPChatSubmitMessage>(OnChatMessage);
            NetworkServer.RegisterHandler<DFMPAdminRosterRequest>(OnAdminRosterRequest);
            NetworkServer.RegisterHandler<DFMPAdminKickRequest>(OnAdminKickRequest);
            NetworkServer.RegisterHandler<DFMPAdminKickAcknowledgement>(OnAdminKickAcknowledgement);
            NetworkServer.RegisterHandler<DFMPDeveloperInfectSelfRequest>(OnDeveloperInfectSelfRequest);
            NetworkServer.RegisterHandler<DFMPDeveloperAdvanceTimeRequest>(OnDeveloperAdvanceTimeRequest);
            NetworkServer.RegisterHandler<DFMPDeveloperDamagePlayerRequest>(OnDeveloperDamagePlayerRequest);
            NetworkServer.RegisterHandler<DFMPDeveloperDamageSelfRequest>(OnDeveloperDamageSelfRequest);
            NetworkServer.RegisterHandler<DFMPAccountIdentityMessage>(OnAccountIdentityMessage);
            SpawnTimeState();

            if (enableDiscovery)
            {
                if (discoveryListener != null)
                {
                    discoveryListener.Stop();
                    discoveryListener = null;
                }

                discoveryListener = new DFMPServerDiscoveryListener(
                    discoveryPort,
                    port,
                    () => ServerName,
                    () => ConnectedPlayerCount,
                    () => MaxConnections,
                    () => Motd
                );
                discoveryListener.Start();
            }

            Debug.Log($"[DFMP Net] Dedicated listener requested: transport=KCP, port={port}, tickRate={tickRate}, maxConnections={maxConnections}, serverName='{ServerName}', discoveryPort={discoveryPort}.");
        }

        public static bool IsJoinAccepted(NetworkConnectionToClient conn)
        {
            return conn != null && joinDecisions.ContainsKey(conn.connectionId) && joinDecisions[conn.connectionId].Accepted;
        }

        static string GetCharacterStoreDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "DFMP", "Characters");
        }

        static void OnAccountIdentityMessage(NetworkConnectionToClient conn, DFMPAccountIdentityMessage message)
        {
            if (conn == null || Config == null || CharacterStore == null)
                return;

            DFMPJoinDecision decision = DFMPJoinPolicy.Resolve(message.AccountId, Config, CharacterStore);

            if (decision.Accepted && !activeAccounts.TryClaim(decision.AccountId, conn.connectionId))
            {
                decision = new DFMPJoinDecision
                {
                    Kind = DFMPJoinDecisionKind.Rejected,
                    AccountId = decision.AccountId,
                    ServerWorldId = decision.ServerWorldId,
                    Reason = "account is already connected"
                };
            }

            if (decision.Accepted && decision.Kind == DFMPJoinDecisionKind.FirstJoin)
            {
                decision.CharacterRecord = DFMPCharacterRecord.CreateNew(
                    decision.AccountId,
                    decision.ServerWorldId,
                    "Player");
                CharacterStore.Save(decision.CharacterRecord);
            }

            Debug.Log($"[DFMP Join] Resolved account identity: connectionId={conn.connectionId}, account='{decision.AccountId}', decision={decision.Kind}, hasCharacterRecord={decision.CharacterRecord != null}, reason='{decision.Reason ?? string.Empty}'.");

            joinDecisions[conn.connectionId] = decision;
            conn.Send(new DFMPJoinResultMessage
            {
                Decision = decision.Kind,
                AccountId = decision.AccountId,
                ServerWorldId = decision.ServerWorldId,
                ServerName = ServerName,
                Motd = Motd,
                Reason = decision.Reason,
                EnableBeginnerTutorial = Config.Gameplay.EnableBeginnerTutorial
            });

            if (decision.Accepted && decision.Kind == DFMPJoinDecisionKind.ReturningPlayer && decision.CharacterRecord != null)
            {
                conn.Send(DFMPCharacterSnapshotProtocol.FromRecord(decision.CharacterRecord));
                SendVitalSnapshot(conn, CreateVitalState(decision.CharacterRecord), false);
                initializedIdentityReportConnections.Add(conn.connectionId);
            }

            if (!decision.Accepted)
            {
                Debug.LogWarning($"[DFMP Join] Rejected account identity: connectionId={conn.connectionId}, reason={decision.Reason}.");
                conn.Disconnect();
                return;
            }

            Debug.Log($"[DFMP Join] Accepted account identity: connectionId={conn.connectionId}, account='{decision.AccountId}', decision={decision.Kind}, world='{decision.ServerWorldId}'.");
        }

        private static void SpawnTimeState()
        {
            if (TimeState != null)
                return;

            GameObject timeGo = new GameObject("DFMP_TimeState");
            timeGo.SetActive(false);

            timeGo.AddComponent<NetworkIdentity>();
            TimeState = timeGo.AddComponent<DFMPTimeState>();
            UnityEngine.Object.DontDestroyOnLoad(timeGo);
            timeGo.SetActive(true);

            NetworkServer.Spawn(timeGo, DFMPTimeState.AssetId);

            Debug.Log("[DFMP Time] Server spawned authoritative time state.");
        }

        public static DFMPPlayerSessionState CreatePlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return null;

            DFMPPlayerSessionState existingState;
            if (playerSessionStates.TryGetValue(conn.connectionId, out existingState))
                return existingState;

            GameObject sessionGo = new GameObject("DFMP_PlayerSessionState");
            sessionGo.SetActive(false);

            sessionGo.AddComponent<NetworkIdentity>();
            var sessionState = sessionGo.AddComponent<DFMPPlayerSessionState>();
            int worldX;
            int worldZ;
            DFMPWorldContextKey initialContext;
            string startMarkerName;
            GetInitialSpawn(out worldX, out worldZ, out initialContext, out startMarkerName);

            DFMPJoinDecision savedJoinDecision;
            if (joinDecisions.TryGetValue(conn.connectionId, out savedJoinDecision) &&
                savedJoinDecision.CharacterRecord != null &&
                (savedJoinDecision.CharacterRecord.WorldX != 0 || savedJoinDecision.CharacterRecord.WorldZ != 0))
            {
                worldX = savedJoinDecision.CharacterRecord.WorldX;
                worldZ = savedJoinDecision.CharacterRecord.WorldZ;
                initialContext = savedJoinDecision.CharacterRecord.Context != null
                    ? savedJoinDecision.CharacterRecord.Context.ToKey()
                    : CreateExteriorWorldContext(worldX, worldZ, "Daggerfall");
                startMarkerName = string.Empty;
            }

            sessionState.Initialize(conn.connectionId, worldX, 0f, worldZ);
            if (savedJoinDecision != null && savedJoinDecision.CharacterRecord != null)
                vitalStates[conn.connectionId] = CreateVitalState(savedJoinDecision.CharacterRecord);
            SetSessionWorldContext(conn.connectionId, sessionState, initialContext, "initial-spawn");
            if (!string.IsNullOrWhiteSpace(startMarkerName))
                startMarkerAssignments[conn.connectionId] = startMarkerName;
            else
                startMarkerAssignments.Remove(conn.connectionId);

            DFMPJoinDecision joinDecision;
            if (joinDecisions.TryGetValue(conn.connectionId, out joinDecision) && joinDecision.CharacterRecord != null)
            {
                DFMPCharacterRecord record = joinDecision.CharacterRecord;
                sessionState.SetDisplayName(DFMPPositionProtocol.SanitizeDisplayName(record.CharacterName));
                sessionState.SetAppearance(
                    DFMPPositionProtocol.GetPlayerRace(record.Race),
                    DFMPPositionProtocol.GetDisplayGender(record.Gender),
                    DFMPPositionProtocol.GetOutfitVariant(record.OutfitVariant),
                    DFMPPositionProtocol.GetFaceVariant(record.FaceVariant));
                sessionState.SetAvatarMobileType(DFMPAvatarProtocol.GetMobileType(record.CareerJson));
            }

            UnityEngine.Object.DontDestroyOnLoad(sessionGo);
            sessionGo.SetActive(true);

            NetworkServer.Spawn(sessionGo, DFMPPlayerSessionState.AssetId);
            playerSessionStates.Add(conn.connectionId, sessionState);
            DFMPVitalState initialVitals;
            if (vitalStates.TryGetValue(conn.connectionId, out initialVitals))
                conn.Send(CreateVitalSnapshot(initialVitals, conn.connectionId, sessionState.IsDead));
            DFMPEventBus.Instance.PublishPlayerConnected(new DFMPPlayerConnectedEvent
            {
                ConnectionId = conn.connectionId,
                Address = conn.address,
                SessionState = sessionState
            });

            Debug.Log($"[DFMP Session] Server created player session state: connectionId={conn.connectionId}, world={worldX}/0/{worldZ}.");
            return sessionState;
        }

        public static bool TrySendReconnectTransitionAssignment(NetworkConnectionToClient conn, DFMPPlayerSessionState sessionState)
        {
            if (conn == null || sessionState == null)
                return false;

            DFMPJoinDecision joinDecision;
            if (!joinDecisions.TryGetValue(conn.connectionId, out joinDecision) || joinDecision.Kind != DFMPJoinDecisionKind.ReturningPlayer)
                return false;

            DFMPWorldContextKey context;
            if (!worldOccupancy.TryGetContext(conn.connectionId, out context) || context.Kind != DFMPWorldContextKind.Exterior)
                return false;

            return TrySendTransitionAssignment(
                conn,
                DFMPTransitionKind.Reconnect,
                new DFMPWorldPosition
                {
                    WorldX = sessionState.WorldX,
                    WorldY = sessionState.WorldY,
                    WorldZ = sessionState.WorldZ
                },
                context);
        }

        public static bool TrySendTransitionAssignment(NetworkConnectionToClient conn, DFMPTransitionKind kind, DFMPWorldPosition position, DFMPWorldContextKey context, string startMarkerName = null, int buildingType = -1)
        {
            if (conn == null || !CanAssignContext(kind, context.Kind))
                return false;

            DFMPPlayerSessionState sessionState;
            if (playerSessionStates.TryGetValue(conn.connectionId, out sessionState) && sessionState != null)
                sessionState.SetResting(false);

            var assignment = DFMPSpawnProtocol.CreateTransitionAssignment(nextTransitionAssignmentId++, conn.connectionId, kind, position, context, startMarkerName, buildingType);
            DFMPTransitionAssignmentState assignmentState;
            if (!transitionAssignmentStates.TryGetValue(conn.connectionId, out assignmentState))
            {
                assignmentState = new DFMPTransitionAssignmentState();
                transitionAssignmentStates.Add(conn.connectionId, assignmentState);
            }

            assignmentState.Receive(assignment);
            assignmentState.TryRequestTeleport();
            conn.Send(assignment);
            Debug.Log($"[DFMP Transition] Server sent transition assignment: connectionId={conn.connectionId}, assignmentId={assignment.AssignmentId}, kind={assignment.Kind}, mapPixel={assignment.MapPixelX}/{assignment.MapPixelY}.");
            return true;
        }

        static bool CanAssignContext(DFMPTransitionKind kind, DFMPWorldContextKind contextKind)
        {
            if (contextKind == DFMPWorldContextKind.Exterior)
                return true;
            if (kind == DFMPTransitionKind.Door && contextKind == DFMPWorldContextKind.BuildingInterior)
                return true;
            return kind == DFMPTransitionKind.DungeonEntry && contextKind == DFMPWorldContextKind.Dungeon;
        }

        public static bool TrySendFastTravelTransitionAssignment(NetworkConnectionToClient conn, int mapPixelX, int mapPixelY)
        {
            if (conn == null || !DFMPSpawnProtocol.IsValidMapPixel(mapPixelX, mapPixelY))
                return false;

            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState) || sessionState == null || !sessionState.SpawnConfirmed)
                return false;

            var context = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = mapPixelX,
                MapPixelY = mapPixelY
            };

            return TrySendTransitionAssignment(conn, DFMPTransitionKind.FastTravel, DFMPSpawnProtocol.GetMapPixelCenter(mapPixelX, mapPixelY), context, "first");
        }

        public static bool TrySendDoorTransitionAssignment(NetworkConnectionToClient conn, DFMPDoorTransitionRequest request, out DFMPDoorTransitionRejectionReason rejectionReason)
        {
            rejectionReason = DFMPDoorTransitionRejectionReason.MissingSession;
            if (conn == null)
                return false;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            DFMPWorldContextKey currentContext;
            bool hasCurrentContext = worldOccupancy.TryGetContext(conn.connectionId, out currentContext);
            rejectionReason = DFMPSpawnProtocol.GetDoorTransitionRejectionReason(sessionState, currentContext, hasCurrentContext, request);
            if (rejectionReason != DFMPDoorTransitionRejectionReason.None)
                return false;

            DFMPWorldContextKey assignedContext = DFMPSpawnProtocol.GetDoorTransitionAssignedContext(request);
            return TrySendTransitionAssignment(
                conn,
                DFMPTransitionKind.Door,
                new DFMPWorldPosition
                {
                    WorldX = sessionState.WorldX,
                    WorldY = sessionState.WorldY,
                    WorldZ = sessionState.WorldZ
                },
                assignedContext,
                null,
                request.EnterInterior ? request.BuildingType : -1);
        }

        public static bool TrySendDungeonTransitionAssignment(NetworkConnectionToClient conn, DFMPDungeonTransitionRequest request, out DFMPDungeonTransitionRejectionReason rejectionReason)
        {
            rejectionReason = DFMPDungeonTransitionRejectionReason.MissingSession;
            if (conn == null)
                return false;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            DFMPWorldContextKey currentContext;
            bool hasCurrentContext = worldOccupancy.TryGetContext(conn.connectionId, out currentContext);
            rejectionReason = DFMPSpawnProtocol.GetDungeonTransitionRejectionReason(sessionState, currentContext, hasCurrentContext, request);
            if (rejectionReason != DFMPDungeonTransitionRejectionReason.None)
                return false;

            DFMPWorldContextKey assignedContext = DFMPSpawnProtocol.GetDungeonTransitionAssignedContext(request);
            return TrySendTransitionAssignment(
                conn,
                request.EnterDungeon ? DFMPTransitionKind.DungeonEntry : DFMPTransitionKind.DungeonExit,
                new DFMPWorldPosition
                {
                    WorldX = sessionState.WorldX,
                    WorldY = sessionState.WorldY,
                    WorldZ = sessionState.WorldZ
                },
                assignedContext);
        }

        public static bool TrySendVampirismTransformationAssignment(
            NetworkConnectionToClient conn,
            out DFMPVampirismTransformationRejectionReason rejectionReason)
        {
            rejectionReason = DFMPVampirismTransformationRejectionReason.MissingSession;
            if (conn == null)
                return false;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            DFMPWorldContextKey currentContext;
            bool hasCurrentContext = worldOccupancy.TryGetContext(conn.connectionId, out currentContext);
            DFMPTransitionAssignmentState assignmentState;
            bool hasPendingTransition = transitionAssignmentStates.TryGetValue(conn.connectionId, out assignmentState) &&
                assignmentState != null && assignmentState.HasPendingAssignment;

            rejectionReason = DFMPVampirismTransformationPolicy.GetRejectionReason(new DFMPVampirismTransformationContext
            {
                HasSession = sessionState != null,
                SpawnConfirmed = sessionState != null && sessionState.SpawnConfirmed,
                HasPendingTransition = hasPendingTransition,
                HasWorldContext = hasCurrentContext,
                RegionIndex = hasCurrentContext ? currentContext.RegionIndex : -1
            });
            if (!DFMPVampirismTransformationPolicy.IsAccepted(rejectionReason))
                return false;

            DFMPVampirismCemeteryCandidate candidate;
            if (!DFMPSpawnProtocol.TryResolveRandomCemetery(currentContext.RegionIndex, out candidate, out rejectionReason))
                return false;

            if (TimeState == null || DaggerfallUnity.Instance == null || DaggerfallUnity.Instance.WorldTime == null)
            {
                rejectionReason = DFMPVampirismTransformationRejectionReason.TimeUnavailable;
                return false;
            }

            uint targetClassicMinutes;
            if (!DFMPVampirismTimePolicy.TryGetTargetClassicMinutes(
                DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime(),
                DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.Hour,
                DaggerfallDateTime.DuskHour,
                out targetClassicMinutes) ||
                !TimeState.TryAdvanceServerTime(targetClassicMinutes))
            {
                rejectionReason = DFMPVampirismTransformationRejectionReason.TimeUnavailable;
                return false;
            }

            return TrySendTransitionAssignment(
                conn,
                DFMPTransitionKind.VampirismTransformation,
                candidate.Position,
                candidate.Context);
        }

        public static void MakeSessionStatesVisibleTo(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            foreach (DFMPPlayerSessionState sessionState in playerSessionStates.Values)
            {
                if (sessionState != null && sessionState.netIdentity != null)
                    NetworkServer.RebuildObservers(sessionState.netIdentity, true);
            }
        }

        private static void GetInitialSpawn(out int worldX, out int worldZ, out DFMPWorldContextKey context, out string startMarkerName)
        {
            worldX = 0;
            worldZ = 0;
            context = CreateExteriorWorldContext(0, 0, "Daggerfall");
            startMarkerName = string.Empty;

            DFMPStartingLocationResolution resolution;
            string reason;
            if (DFMPSpawnProtocol.TryResolveStartingLocation(Config != null ? Config.StartingLocation : null, out resolution, out reason))
            {
                worldX = resolution.Position.WorldX;
                worldZ = resolution.Position.WorldZ;
                context = resolution.Context;
                startMarkerName = resolution.StartMarkerName;
                Debug.Log($"[DFMP Session] Resolved configured spawn: mode={(Config != null && Config.StartingLocation != null ? Config.StartingLocation.Mode : DFMPStartingLocationModes.LocationCenter)}, mapPixel={context.MapPixelX}/{context.MapPixelY}, world={worldX}/{worldZ}.");
                return;
            }

            Debug.LogWarning($"[DFMP Session] Configured spawn was unavailable: {reason}. Falling back to Daggerfall city center.");

            if (DaggerfallUnity.Instance == null || DaggerfallUnity.Instance.ContentReader == null || DaggerfallUnity.Instance.ContentReader.MapFileReader == null)
            {
                Debug.LogWarning("[DFMP Session] Daggerfall City spawn data is unavailable; using world origin.");
                return;
            }

            DFLocation location = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation("Daggerfall", "Daggerfall");
            if (!location.Loaded)
            {
                Debug.LogWarning("[DFMP Session] Daggerfall City was not found in MAPS.BSA; using world origin.");
                return;
            }

            DFPosition mapPixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            Rect locationRect = DaggerfallLocation.GetLocationRect(location);
            DFMPWorldPosition spawnPosition = DFMPSpawnProtocol.GetLocationCenter(locationRect);
            worldX = spawnPosition.WorldX;
            worldZ = spawnPosition.WorldZ;
            context = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = mapPixel.X,
                MapPixelY = mapPixel.Y,
                RegionIndex = location.RegionIndex,
                LocationIndex = location.LocationIndex,
                LocationId = location.Name
            };

            Debug.Log($"[DFMP Session] Resolved Daggerfall City spawn: mapPixel={mapPixel.X}/{mapPixel.Y}, locationRect={locationRect.xMin}/{locationRect.yMin}/{locationRect.width}/{locationRect.height}, world={worldX}/{worldZ}.");
        }

        private static void OnSpawnAcknowledgement(NetworkConnectionToClient conn, DFMPSpawnAcknowledgement acknowledgement)
        {
            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState) || sessionState == null)
            {
                Debug.LogWarning($"[DFMP Session] Ignored spawn acknowledgement without a session: connectionId={conn.connectionId}.");
                return;
            }

            if (!DFMPSpawnProtocol.TryConfirmSpawn(sessionState, acknowledgement))
            {
                Debug.LogWarning($"[DFMP Session] Ignored mismatched spawn acknowledgement: connectionId={conn.connectionId}.");
                return;
            }

            ConfirmSessionArrival(conn.connectionId, sessionState);
            Debug.Log($"[DFMP Session] Server confirmed spawn: connectionId={conn.connectionId}, world={sessionState.WorldX}/{sessionState.WorldY:F2}/{sessionState.WorldZ}.");
        }

        private static void OnTransitionAcknowledgement(NetworkConnectionToClient conn, DFMPTransitionAcknowledgement acknowledgement)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            DFMPTransitionAssignmentState assignmentState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState) || sessionState == null || !transitionAssignmentStates.TryGetValue(conn.connectionId, out assignmentState))
            {
                Debug.LogWarning($"[DFMP Transition] Ignored transition acknowledgement without an assignment: connectionId={conn.connectionId}.");
                return;
            }

            DFMPTransitionAcknowledgeRejectionReason rejectionReason;
            if (!DFMPSpawnProtocol.TryConfirmTransition(sessionState, assignmentState, acknowledgement, false, out rejectionReason))
            {
                Debug.LogWarning($"[DFMP Transition] Ignored transition acknowledgement: connectionId={conn.connectionId}, assignmentId={acknowledgement.AssignmentId}, reason={rejectionReason}.");
                return;
            }

            SetSessionWorldContext(conn.connectionId, sessionState, DFMPSpawnProtocol.GetAssignedContext(assignmentState.Assignment), $"transition-{assignmentState.Assignment.Kind}");
            if (assignmentState.Assignment.Kind == DFMPTransitionKind.Door &&
                assignmentState.Assignment.BuildingType >= 0 &&
                DFMPRespawnAnchorPolicy.IsInnBuildingType(assignmentState.Assignment.BuildingType))
            {
                DFMPJoinDecision joinDecision;
                if (joinDecisions.TryGetValue(conn.connectionId, out joinDecision) && joinDecision.CharacterRecord != null)
                {
                    DFMPCharacterPersistence.ApplyInnRespawnAnchor(
                        joinDecision.CharacterRecord,
                        new DFMPWorldPosition
                        {
                            WorldX = sessionState.WorldX,
                            WorldY = sessionState.WorldY,
                            WorldZ = sessionState.WorldZ
                        },
                        DFMPSpawnProtocol.GetAssignedContext(assignmentState.Assignment));
                    CharacterStore.Save(joinDecision.CharacterRecord);
                    Debug.Log($"[DFMP Respawn] Saved inn anchor: connectionId={conn.connectionId}, location='{assignmentState.Assignment.LocationId}', buildingKey={assignmentState.Assignment.BuildingKey}.");
                }
            }
            if (assignmentState.Assignment.Kind == DFMPTransitionKind.DeathRespawn)
            {
                sessionState.SetDead(false);
                DFMPJoinDecision deathJoinDecision;
                if (joinDecisions.TryGetValue(conn.connectionId, out deathJoinDecision) && deathJoinDecision.CharacterRecord != null)
                {
                    deathJoinDecision.CharacterRecord.Health = deathJoinDecision.CharacterRecord.MaxHealth;
                    deathJoinDecision.CharacterRecord.Fatigue = deathJoinDecision.CharacterRecord.MaxFatigue;
                    deathJoinDecision.CharacterRecord.SpellPoints = deathJoinDecision.CharacterRecord.MaxSpellPoints;
                    vitalStates[conn.connectionId] = CreateVitalState(deathJoinDecision.CharacterRecord);
                    SendVitalSnapshot(conn, vitalStates[conn.connectionId], false);
                    CharacterStore.Save(deathJoinDecision.CharacterRecord);
                }
                DFMPEventBus.Instance.PublishPlayerRespawned(new DFMPPlayerRespawnedEvent
                {
                    ConnectionId = conn.connectionId,
                    SessionState = sessionState,
                    Context = DFMPSpawnProtocol.GetAssignedContext(assignmentState.Assignment),
                    Reason = "death-respawn"
                });
            }
            ConfirmSessionArrival(conn.connectionId, sessionState);
            Debug.Log($"[DFMP Transition] Server confirmed transition: connectionId={conn.connectionId}, assignmentId={acknowledgement.AssignmentId}, kind={assignmentState.Assignment.Kind}, world={sessionState.WorldX}/{sessionState.WorldY:F2}/{sessionState.WorldZ}.");
        }

        private static void OnFastTravelRequest(NetworkConnectionToClient conn, DFMPFastTravelRequest request)
        {
            if (conn == null)
                return;

            if (!TrySendFastTravelTransitionAssignment(conn, request.MapPixelX, request.MapPixelY))
            {
                Debug.LogWarning($"[DFMP Transition] Rejected fast travel request: connectionId={conn.connectionId}, mapPixel={request.MapPixelX}/{request.MapPixelY}.");
                return;
            }

            Debug.Log($"[DFMP Transition] Accepted fast travel request: connectionId={conn.connectionId}, mapPixel={request.MapPixelX}/{request.MapPixelY}.");
        }

        private static void OnDoorTransitionRequest(NetworkConnectionToClient conn, DFMPDoorTransitionRequest request)
        {
            if (conn == null)
                return;

            DFMPDoorTransitionRejectionReason rejectionReason;
            if (!TrySendDoorTransitionAssignment(conn, request, out rejectionReason))
            {
                Debug.LogWarning($"[DFMP Transition] Rejected door transition request: connectionId={conn.connectionId}, enterInterior={request.EnterInterior}, mapPixel={request.MapPixelX}/{request.MapPixelY}, buildingKey={request.BuildingKey}, reason={rejectionReason}.");
                return;
            }

            Debug.Log($"[DFMP Transition] Accepted door transition request: connectionId={conn.connectionId}, enterInterior={request.EnterInterior}, mapPixel={request.MapPixelX}/{request.MapPixelY}, buildingKey={request.BuildingKey}.");
        }

        private static void OnDungeonTransitionRequest(NetworkConnectionToClient conn, DFMPDungeonTransitionRequest request)
        {
            if (conn == null)
                return;

            DFMPDungeonTransitionRejectionReason rejectionReason;
            if (!TrySendDungeonTransitionAssignment(conn, request, out rejectionReason))
            {
                Debug.LogWarning($"[DFMP Transition] Rejected dungeon transition request: connectionId={conn.connectionId}, enterDungeon={request.EnterDungeon}, mapPixel={request.MapPixelX}/{request.MapPixelY}, location='{request.LocationId}', reason={rejectionReason}.");
                return;
            }

            Debug.Log($"[DFMP Transition] Accepted dungeon transition request: connectionId={conn.connectionId}, enterDungeon={request.EnterDungeon}, mapPixel={request.MapPixelX}/{request.MapPixelY}, location='{request.LocationId}'.");
        }

        private static void OnActionDoorSyncMessage(NetworkConnectionToClient conn, DFMPActionDoorSyncMessage message)
        {
            if (conn == null || message.LoadID == 0)
                return;

            DFMPWorldContextKey senderContext;
            if (!worldOccupancy.TryGetContext(conn.connectionId, out senderContext))
                return;

            int[] coLocated = GetConnectionsInInterestScope(senderContext);
            int relayedCount = 0;
            for (int i = 0; i < coLocated.Length; i++)
            {
                int targetConnId = coLocated[i];
                if (targetConnId == conn.connectionId)
                    continue;

                NetworkConnectionToClient targetConn;
                if (NetworkServer.connections.TryGetValue(targetConnId, out targetConn) && targetConn != null)
                {
                    targetConn.Send(message);
                    relayedCount++;
                }
            }

            Debug.Log($"[DFMP World] Relayed action door sync: sender={conn.connectionId}, loadID={message.LoadID}, isOpen={message.IsOpen}, context={senderContext}, recipients={relayedCount}.");
        }

        private static void OnVampirismTransformationRequest(NetworkConnectionToClient conn, DFMPVampirismTransformationRequest request)
        {
            if (conn == null)
                return;

            DFMPVampirismTransformationRejectionReason rejectionReason;
            if (!TrySendVampirismTransformationAssignment(conn, out rejectionReason))
            {
                Debug.LogWarning($"[DFMP Transition] Rejected vampirism transformation request: connectionId={conn.connectionId}, reason={rejectionReason}.");
                return;
            }

            Debug.Log($"[DFMP Transition] Accepted vampirism transformation request: connectionId={conn.connectionId}.");
        }

        private static void OnPlayerDeathReport(NetworkConnectionToClient conn, DFMPPlayerDeathReport report)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState) || sessionState == null || !sessionState.SpawnConfirmed)
            {
                Debug.LogWarning($"[DFMP Respawn] Rejected death report: connectionId={conn.connectionId}, reason=missing-or-unspawned-session.");
                return;
            }

            DFMPJoinDecision joinDecision;
            joinDecisions.TryGetValue(conn.connectionId, out joinDecision);
            TryBeginDeathRespawn(
                conn,
                sessionState,
                joinDecision,
                DFMPDamageSourceKind.Environmental,
                DFMPVitalKind.Health,
                0);
        }

        static bool TryBeginDeathRespawn(
            NetworkConnectionToClient conn,
            DFMPPlayerSessionState sessionState,
            DFMPJoinDecision joinDecision,
            DFMPDamageSourceKind sourceKind,
            DFMPVitalKind vitalKind,
            int appliedAmount)
        {
            if (conn == null || sessionState == null || !sessionState.SpawnConfirmed)
                return false;

            DFMPTransitionAssignmentState assignmentState;
            if (sessionState.IsDead || (transitionAssignmentStates.TryGetValue(conn.connectionId, out assignmentState) && assignmentState != null && assignmentState.HasPendingAssignment))
            {
                Debug.LogWarning($"[DFMP Respawn] Rejected death transition: connectionId={conn.connectionId}, reason=death-or-transition-already-pending.");
                return false;
            }

            DFMPWorldPosition position;
            DFMPWorldContextKey context;
            string startMarkerName;
            if (!TryResolveRespawnPosition(joinDecision, out position, out context, out startMarkerName))
            {
                Debug.LogWarning($"[DFMP Respawn] Rejected death transition: connectionId={conn.connectionId}, reason=missing-respawn-position.");
                return false;
            }

            sessionState.SetDead(true);
            if (!TrySendTransitionAssignment(conn, DFMPTransitionKind.DeathRespawn, position, context, startMarkerName))
            {
                sessionState.SetDead(false);
                Debug.LogWarning($"[DFMP Respawn] Failed to send death respawn assignment: connectionId={conn.connectionId}.");
                return false;
            }

            DFMPEventBus.Instance.PublishPlayerDied(new DFMPPlayerDiedEvent
            {
                ConnectionId = conn.connectionId,
                SourceKind = sourceKind,
                VitalKind = vitalKind,
                AppliedAmount = appliedAmount,
                SessionState = sessionState
            });
            Debug.Log($"[DFMP Respawn] Accepted death transition: connectionId={conn.connectionId}, anchor={context.LocationId ?? string.Empty}, world={position.WorldX}/{position.WorldZ}.");
            return true;
        }

        static bool TryResolveRespawnPosition(DFMPJoinDecision joinDecision, out DFMPWorldPosition position, out DFMPWorldContextKey context, out string startMarkerName)
        {
            position = new DFMPWorldPosition();
            context = new DFMPWorldContextKey();
            startMarkerName = string.Empty;
            if (joinDecision != null && joinDecision.CharacterRecord != null && joinDecision.CharacterRecord.RespawnAnchor != null && joinDecision.CharacterRecord.RespawnAnchor.IsInnAnchor())
            {
                var anchor = joinDecision.CharacterRecord.RespawnAnchor;
                position = new DFMPWorldPosition { WorldX = anchor.WorldX, WorldY = anchor.WorldY, WorldZ = anchor.WorldZ };
                context = anchor.Context.ToKey();
                if (context.Kind == DFMPWorldContextKind.Exterior && DFMPSpawnProtocol.IsValidMapPixel(context.MapPixelX, context.MapPixelY))
                    return true;

                // Interior anchors require a client-side door re-entry assignment; until that exists,
                // fall back to the configured exterior starting location instead of teleporting to an invalid scene position.
                position = new DFMPWorldPosition();
                context = new DFMPWorldContextKey();
            }

            DFMPStartingLocationResolution resolution;
            string reason;
            if (!DFMPSpawnProtocol.TryResolveStartingLocation(Config != null ? Config.StartingLocation : null, out resolution, out reason))
                return false;

            position = resolution.Position;
            context = resolution.Context;
            startMarkerName = resolution.StartMarkerName;
            return DFMPSpawnProtocol.IsValidMapPixel(context.MapPixelX, context.MapPixelY);
        }

        private static void OnDeveloperInfectSelfRequest(NetworkConnectionToClient conn, DFMPDeveloperInfectSelfRequest request)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            DFMPDeveloperCommandRejectionReason rejectionReason = DFMPDeveloperCommandPolicy.GetInfectSelfRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = Config != null && Config.Developer != null && Config.Developer.CommandsEnabled,
                HasSession = sessionState != null,
                SpawnConfirmed = sessionState != null && sessionState.SpawnConfirmed
            });

            if (!DFMPDeveloperCommandPolicy.IsAccepted(rejectionReason))
            {
                conn.Send(new DFMPDeveloperInfectSelfResponse
                {
                    Accepted = false,
                    Reason = rejectionReason.ToString()
                });
                Debug.LogWarning($"[DFMP Developer] Rejected vampirism infection: connectionId={conn.connectionId}, reason={rejectionReason}.");
                return;
            }

            conn.Send(new DFMPDeveloperInfectSelfResponse
            {
                Accepted = true,
                Reason = string.Empty
            });
            Debug.Log($"[DFMP Developer] Authorized vampirism infection: connectionId={conn.connectionId}.");
        }

        private static void OnDeveloperAdvanceTimeRequest(NetworkConnectionToClient conn, DFMPDeveloperAdvanceTimeRequest request)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            const int maximumMinutes = 7 * 24 * 60;
            DFMPDeveloperCommandRejectionReason rejectionReason = DFMPDeveloperCommandPolicy.GetAdvanceTimeRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = Config != null && Config.Developer != null && Config.Developer.CommandsEnabled,
                HasSession = sessionState != null,
                SpawnConfirmed = sessionState != null && sessionState.SpawnConfirmed
            }, request.Minutes, maximumMinutes);

            if (!DFMPDeveloperCommandPolicy.IsAccepted(rejectionReason) || TimeState == null || !TimeState.TryAdvanceServerTimeByMinutes(request.Minutes, out _))
            {
                if (rejectionReason == DFMPDeveloperCommandRejectionReason.None)
                    rejectionReason = DFMPDeveloperCommandRejectionReason.TimeUnavailable;

                conn.Send(new DFMPDeveloperAdvanceTimeResponse
                {
                    Accepted = false,
                    Minutes = request.Minutes,
                    Reason = rejectionReason.ToString()
                });
                Debug.LogWarning($"[DFMP Developer] Rejected time advance: connectionId={conn.connectionId}, minutes={request.Minutes}, reason={rejectionReason}.");
                return;
            }

            conn.Send(new DFMPDeveloperAdvanceTimeResponse
            {
                Accepted = true,
                Minutes = request.Minutes,
                Reason = string.Empty
            });
            Debug.Log($"[DFMP Developer] Authorized time advance: connectionId={conn.connectionId}, minutes={request.Minutes}.");
        }

        static void ConfirmSessionArrival(int connectionId, DFMPPlayerSessionState sessionState)
        {
            lastPositionReportTimes.Remove(connectionId);
            rejectedPositionReportConnections.Remove(connectionId);
            DFMPEventBus.Instance.PublishPlayerSpawned(new DFMPPlayerSpawnedEvent
            {
                ConnectionId = connectionId,
                SessionState = sessionState,
                WorldX = sessionState.WorldX,
                WorldY = sessionState.WorldY,
                WorldZ = sessionState.WorldZ,
                DisplayName = sessionState.DisplayName
            });

            if (chatLifecycleNotifier.TryAnnounceSpawn(connectionId))
            {
                BroadcastSystemMessage(
                    DFMPChatMessageKind.PlayerJoined,
                    string.Format("{0} joined.", sessionState.DisplayName),
                    connectionId);
            }
        }

        private static void OnPlayerPositionReport(NetworkConnectionToClient conn, DFMPPlayerPositionReport report)
        {
            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
            {
                joinDecisions.Remove(conn.connectionId);
                activeAccounts.Release(conn.connectionId);
                return;
            }

            float lastReportTime;
            float elapsedSeconds = lastPositionReportTimes.TryGetValue(conn.connectionId, out lastReportTime)
                ? Time.unscaledTime - lastReportTime
                : DFMPPositionProtocol.MinimumReportInterval;

            DFMPWorldContextKey context;
            TryGetSessionWorldContext(conn.connectionId, out context);
            DFMPPositionRejectionReason rejectionReason = DFMPPositionProtocol.GetRejectionReason(sessionState, report, elapsedSeconds, context);
            if (rejectionReason != DFMPPositionRejectionReason.None)
            {
                // Anchor time is deliberately not advanced so the movement budget keeps growing and a
                // legitimate teleport re-anchors instead of locking the session out forever.
                if (sessionState != null && sessionState.SpawnConfirmed && rejectedPositionReportConnections.Add(conn.connectionId))
                    Debug.LogWarning($"[DFMP Session] Server rejected player position report: connectionId={conn.connectionId}, reason={rejectionReason}, session={sessionState.WorldX}/{sessionState.WorldZ}, report={report.WorldX}/{report.WorldZ}, elapsed={elapsedSeconds:F2}s.");

                return;
            }

            lastPositionReportTimes[conn.connectionId] = Time.unscaledTime;
            rejectedPositionReportConnections.Remove(conn.connectionId);
            bool isMoving = context.Kind == DFMPWorldContextKind.Dungeon
                ? DFMPMovementProtocol.IsMoving(sessionState.DungeonLocalPosition.x, sessionState.DungeonLocalPosition.z, report.DungeonLocalX, report.DungeonLocalZ)
                : DFMPMovementProtocol.IsMoving(sessionState.WorldX, sessionState.WorldZ, report.WorldX, report.WorldZ);
            sessionState.SetPosition(report.WorldX, report.WorldY, report.WorldZ);
            sessionState.SetMovement(isMoving);
            sessionState.SetFacingYaw(DFMPPositionProtocol.NormalizeFacingYaw(report.FacingYaw));
            sessionState.SetSceneHeightOffset(report.SceneHeightOffset);
            sessionState.SetDungeonLocalPosition(report.HasDungeonLocalPosition, DFMPPositionProtocol.GetDungeonLocalPosition(report));
            if (activePositionReportConnections.Add(conn.connectionId))
                Debug.Log($"[DFMP Session] Server accepted player position reports: connectionId={conn.connectionId}.");
        }

        private static void OnDamageIntent(NetworkConnectionToClient conn, DFMPDamageIntent intent)
        {
            TryApplyDamageIntent(conn, intent);
        }

        private static bool TryApplyDamageIntent(NetworkConnectionToClient conn, DFMPDamageIntent intent)
        {
            if (conn == null || !DFMPCombatProtocol.IsValidDamageIntent(intent))
            {
                Debug.LogWarning($"[DFMP Combat] Rejected malformed damage intent: connectionId={(conn != null ? conn.connectionId : -1)}.");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(intent.TargetEnemyId))
                return TryApplyDynamicEnemyDamageIntent(conn, intent);

            return TryApplyPlayerDamageIntent(conn, intent);
        }

        private static bool TryApplyDynamicEnemyDamageIntent(NetworkConnectionToClient conn, DFMPDamageIntent intent)
        {
            DFMPPlayerSessionState sourceSession;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sourceSession) || sourceSession == null)
            {
                Debug.LogWarning($"[DFMP Combat] Rejected enemy damage intent: connectionId={conn.connectionId}, enemyId={intent.TargetEnemyId}, reason=missing-session.");
                return false;
            }

            if (DungeonEnemyRosterService == null)
            {
                Debug.LogWarning($"[DFMP Combat] Rejected enemy damage intent: connectionId={conn.connectionId}, enemyId={intent.TargetEnemyId}, reason=roster-service-unavailable.");
                return false;
            }

            DFMPDynamicEnemyRecord enemyRecord;
            bool enemyExists = DungeonEnemyRosterService.TryGetRecord(intent.TargetEnemyId, out enemyRecord);
            bool enemyIsDead = !enemyExists || enemyRecord.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive || enemyRecord.Health <= 0;

            uint lastSequence;
            if (!lastDamageSequences.TryGetValue(conn.connectionId, out lastSequence))
                lastSequence = 0;

            ulong lastRequestId;
            if (lastDamageRequestIds.TryGetValue(conn.connectionId, out lastRequestId) && lastRequestId == intent.RequestId)
            {
                Debug.LogWarning($"[DFMP Combat] Rejected enemy damage intent: connectionId={conn.connectionId}, requestId={intent.RequestId}, reason=duplicate-request.");
                return false;
            }

            DFMPWorldContextKey sourceContext;
            bool hasSourceContext = worldOccupancy.TryGetContext(conn.connectionId, out sourceContext);
            bool sameWorldContext = hasSourceContext && enemyExists && sourceContext.Equals(enemyRecord.Identity.Encounter.Context);
            bool inRange = sameWorldContext && sourceSession.HasDungeonLocalPosition &&
                IsWithinEnemyRange(sourceSession.DungeonLocalPosition, enemyRecord.Descriptor.DungeonLocalPosition, intent.AttackKind);
            DFMPTransitionAssignmentState sourceAssignmentState;
            bool hasSourcePendingTransition = transitionAssignmentStates.TryGetValue(conn.connectionId, out sourceAssignmentState) &&
                sourceAssignmentState != null && sourceAssignmentState.HasPendingAssignment;

            DFMPDamageValidationRequest validationRequest = new DFMPDamageValidationRequest
            {
                RequestId = intent.RequestId,
                Sequence = intent.Sequence,
                SourceKind = intent.SourceKind,
                VitalKind = intent.VitalKind,
                SourceConnectionId = conn.connectionId,
                TargetConnectionId = 0,
                TargetEnemyId = intent.TargetEnemyId,
                Amount = intent.Amount
            };

            DFMPDamageValidationContext validationContext = new DFMPDamageValidationContext
            {
                HasSession = sourceSession != null,
                SpawnConfirmed = sourceSession.SpawnConfirmed,
                SourceIsDead = sourceSession.IsDead,
                TargetExists = enemyExists,
                TargetIsDead = enemyIsDead,
                HasPendingTransition = hasSourcePendingTransition,
                SameWorldContext = sameWorldContext,
                InRange = inRange,
                PvpEnabled = true,
                CooldownElapsed = IsDamageCooldownElapsed(conn.connectionId) && IsDamageRateAvailable(conn.connectionId),
                AuthoritativeSourceConnectionId = conn.connectionId,
                LastAcceptedSequence = lastSequence,
                MaximumAmount = Config != null && Config.Combat != null ? Config.Combat.MaximumDamagePerHit : 100,
                IsDynamicEnemyTarget = true
            };

            DFMPDamageRejectionReason rejectionReason = DFMPDamagePolicy.GetRejectionReason(validationRequest, validationContext);
            if (!DFMPDamagePolicy.IsAccepted(rejectionReason))
            {
                Debug.LogWarning($"[DFMP Combat] Rejected enemy damage intent: connectionId={conn.connectionId}, enemyId={intent.TargetEnemyId}, requestId={intent.RequestId}, attack={intent.AttackKind}, reason={rejectionReason}.");
                return false;
            }

            DFMPDynamicEnemyRecord updatedRecord;
            int appliedAmount;
            bool killed;
            if (!DungeonEnemyRosterService.TryApplyDamage(intent.TargetEnemyId, intent.Amount, conn.connectionId, out updatedRecord, out appliedAmount, out killed))
            {
                Debug.LogWarning($"[DFMP Combat] Failed to apply damage to dynamic enemy: connectionId={conn.connectionId}, enemyId={intent.TargetEnemyId}.");
                return false;
            }

            lastDamageSequences[conn.connectionId] = intent.Sequence;
            lastDamageRequestIds[conn.connectionId] = intent.RequestId;
            RecordAcceptedDamage(conn.connectionId);

            Debug.Log($"[DFMP Combat] Applied damage to dynamic enemy: source={conn.connectionId}, enemyId={intent.TargetEnemyId}, amount={appliedAmount}, remainingHealth={updatedRecord.Health}, killed={killed}.");
            return true;
        }

        static bool IsWithinEnemyRange(Vector3 sourceDungeonPos, Vector3 enemyDungeonPos, DFMPCombatAttackKind attackKind)
        {
            DFMPServerEnemyConfig enemyConfig = Config != null ? Config.Enemies : null;
            float maxRange = attackKind == DFMPCombatAttackKind.Ranged
                ? (enemyConfig != null ? enemyConfig.PlayerRangedDamageRange : 25f)
                : (enemyConfig != null ? enemyConfig.PlayerMeleeDamageRange : 6f);
            return Vector3.Distance(sourceDungeonPos, enemyDungeonPos) <= maxRange;
        }

        private static bool TryApplyPlayerDamageIntent(NetworkConnectionToClient conn, DFMPDamageIntent intent)
        {
            DFMPPlayerSessionState sourceSession;
            DFMPPlayerSessionState targetSession;
            DFMPVitalState targetVitals;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sourceSession) || sourceSession == null ||
                !playerSessionStates.TryGetValue(intent.TargetConnectionId, out targetSession) || targetSession == null ||
                !vitalStates.TryGetValue(intent.TargetConnectionId, out targetVitals))
            {
                Debug.LogWarning($"[DFMP Combat] Rejected damage intent: connectionId={conn.connectionId}, target={intent.TargetConnectionId}, reason=missing-session.");
                return false;
            }

            uint lastSequence;
            if (!lastDamageSequences.TryGetValue(conn.connectionId, out lastSequence))
                lastSequence = 0;

            ulong lastRequestId;
            if (lastDamageRequestIds.TryGetValue(conn.connectionId, out lastRequestId) && lastRequestId == intent.RequestId)
            {
                Debug.LogWarning($"[DFMP Combat] Rejected damage intent: connectionId={conn.connectionId}, requestId={intent.RequestId}, reason=duplicate-request.");
                return false;
            }

            DFMPWorldContextKey sourceContext;
            DFMPWorldContextKey targetContext;
            bool hasSourceContext = worldOccupancy.TryGetContext(conn.connectionId, out sourceContext);
            bool hasTargetContext = worldOccupancy.TryGetContext(intent.TargetConnectionId, out targetContext);
            DFMPTransitionAssignmentState assignmentState;
            bool hasPendingTransition = transitionAssignmentStates.TryGetValue(intent.TargetConnectionId, out assignmentState) &&
                assignmentState != null && assignmentState.HasPendingAssignment;
            DFMPDamageValidationRequest validationRequest = new DFMPDamageValidationRequest
            {
                RequestId = intent.RequestId,
                Sequence = intent.Sequence,
                SourceKind = intent.SourceKind,
                VitalKind = intent.VitalKind,
                SourceConnectionId = conn.connectionId,
                TargetConnectionId = intent.TargetConnectionId,
                Amount = intent.Amount
            };
            DFMPDamageValidationContext validationContext = new DFMPDamageValidationContext
            {
                HasSession = sourceSession != null,
                SpawnConfirmed = sourceSession.SpawnConfirmed,
                SourceIsDead = sourceSession.IsDead,
                TargetExists = targetSession != null,
                TargetIsDead = targetSession.IsDead || targetVitals.Health <= 0,
                HasPendingTransition = hasPendingTransition,
                SameWorldContext = hasSourceContext && hasTargetContext && sourceContext.Equals(targetContext),
                InRange = hasSourceContext && hasTargetContext && IsWithinPvpRange(sourceSession, targetSession, intent.AttackKind),
                PvpEnabled = Config != null && Config.Combat != null && Config.Combat.PvpEnabled,
                CooldownElapsed = IsDamageCooldownElapsed(conn.connectionId) && IsDamageRateAvailable(conn.connectionId),
                AuthoritativeSourceConnectionId = conn.connectionId,
                LastAcceptedSequence = lastSequence,
                MaximumAmount = Config != null && Config.Combat != null ? Config.Combat.MaximumDamagePerHit : 100
            };

            DFMPDamageRejectionReason rejectionReason = DFMPDamagePolicy.GetRejectionReason(validationRequest, validationContext);
            if (!DFMPDamagePolicy.IsAccepted(rejectionReason))
            {
                Debug.LogWarning($"[DFMP Combat] Rejected damage intent: connectionId={conn.connectionId}, target={intent.TargetConnectionId}, requestId={intent.RequestId}, attack={intent.AttackKind}, reason={rejectionReason}.");
                return false;
            }

            DFMPVitalApplicationResult applicationResult = targetVitals.ApplyDamage(intent.VitalKind, intent.Amount);
            if (!applicationResult.Accepted)
            {
                Debug.LogWarning($"[DFMP Combat] Rejected damage application: connectionId={conn.connectionId}, target={intent.TargetConnectionId}, reason={applicationResult.RejectionReason}.");
                return false;
            }

            vitalStates[intent.TargetConnectionId] = targetVitals;
            lastDamageSequences[conn.connectionId] = intent.Sequence;
            lastDamageRequestIds[conn.connectionId] = intent.RequestId;
            RecordAcceptedDamage(conn.connectionId);

            DFMPJoinDecision joinDecision;
            if (joinDecisions.TryGetValue(intent.TargetConnectionId, out joinDecision) && joinDecision.CharacterRecord != null)
            {
                DFMPCharacterPersistence.ApplyVitalState(joinDecision.CharacterRecord, targetVitals);
                CharacterStore.Save(joinDecision.CharacterRecord);
            }

            DFMPEventBus.Instance.PublishPlayerDamaged(new DFMPPlayerDamagedEvent
            {
                SourceConnectionId = conn.connectionId,
                TargetConnectionId = intent.TargetConnectionId,
                SourceKind = intent.SourceKind,
                VitalKind = intent.VitalKind,
                RequestedAmount = intent.Amount,
                AppliedAmount = applicationResult.AppliedAmount,
                CurrentValue = applicationResult.CurrentValue,
                MaximumValue = applicationResult.MaximumValue
            });

            if (applicationResult.Killed)
            {
                joinDecisions.TryGetValue(intent.TargetConnectionId, out joinDecision);
                NetworkConnectionToClient deathConnection;
                if (NetworkServer.connections.TryGetValue(intent.TargetConnectionId, out deathConnection))
                    TryBeginDeathRespawn(deathConnection, targetSession, joinDecision, intent.SourceKind, intent.VitalKind, applicationResult.AppliedAmount);
            }

            NetworkConnectionToClient targetConnection;
            if (NetworkServer.connections.TryGetValue(intent.TargetConnectionId, out targetConnection))
                SendVitalSnapshot(targetConnection, targetVitals, targetSession.IsDead);

            Debug.Log($"[DFMP Combat] Applied damage: source={conn.connectionId}, target={intent.TargetConnectionId}, sourceKind={intent.SourceKind}, vital={intent.VitalKind}, requested={intent.Amount}, applied={applicationResult.AppliedAmount}, current={applicationResult.CurrentValue}, killed={applicationResult.Killed}.");
            return true;
        }

        public static bool TryApplyServerEnemyDamage(string enemyId, int targetConnectionId, int amount)
        {
            DFMPPlayerSessionState targetSession;
            DFMPVitalState targetVitals;
            if (string.IsNullOrWhiteSpace(enemyId) || !playerSessionStates.TryGetValue(targetConnectionId, out targetSession) || targetSession == null ||
                !vitalStates.TryGetValue(targetConnectionId, out targetVitals))
            {
                Debug.LogWarning($"[DFMP Combat] Rejected server enemy damage: enemyId={enemyId ?? string.Empty}, target={targetConnectionId}, reason=missing-target.");
                return false;
            }

            int maximumAmount = Config != null && Config.Combat != null ? Config.Combat.MaximumDamagePerHit : 100;
            if (amount <= 0 || amount > maximumAmount)
            {
                Debug.LogWarning($"[DFMP Combat] Rejected server enemy damage: enemyId={enemyId}, target={targetConnectionId}, reason=invalid-amount.");
                return false;
            }

            DFMPTransitionAssignmentState assignmentState;
            bool hasPendingTransition = transitionAssignmentStates.TryGetValue(targetConnectionId, out assignmentState) &&
                assignmentState != null && assignmentState.HasPendingAssignment;
            if (!targetSession.SpawnConfirmed || targetSession.IsDead || targetVitals.Health <= 0 || hasPendingTransition)
                return false;

            DFMPVitalApplicationResult applicationResult = targetVitals.ApplyDamage(DFMPVitalKind.Health, amount);
            if (!applicationResult.Accepted)
                return false;

            vitalStates[targetConnectionId] = targetVitals;

            DFMPJoinDecision joinDecision;
            if (CharacterStore != null && joinDecisions.TryGetValue(targetConnectionId, out joinDecision) && joinDecision.CharacterRecord != null)
            {
                DFMPCharacterPersistence.ApplyVitalState(joinDecision.CharacterRecord, targetVitals);
                CharacterStore.Save(joinDecision.CharacterRecord);
            }

            DFMPEventBus.Instance.PublishPlayerDamaged(new DFMPPlayerDamagedEvent
            {
                SourceConnectionId = 0,
                TargetConnectionId = targetConnectionId,
                SourceKind = DFMPDamageSourceKind.NativeEnemy,
                VitalKind = DFMPVitalKind.Health,
                RequestedAmount = amount,
                AppliedAmount = applicationResult.AppliedAmount,
                CurrentValue = applicationResult.CurrentValue,
                MaximumValue = applicationResult.MaximumValue
            });

            if (applicationResult.Killed)
            {
                joinDecisions.TryGetValue(targetConnectionId, out joinDecision);
                NetworkConnectionToClient deathConnection;
                if (NetworkServer.connections.TryGetValue(targetConnectionId, out deathConnection))
                    TryBeginDeathRespawn(deathConnection, targetSession, joinDecision, DFMPDamageSourceKind.NativeEnemy, DFMPVitalKind.Health, applicationResult.AppliedAmount);
            }

            NetworkConnectionToClient targetConnection;
            if (NetworkServer.connections.TryGetValue(targetConnectionId, out targetConnection))
                SendVitalSnapshot(targetConnection, targetVitals, targetSession.IsDead);

            Debug.Log($"[DFMP Combat] Applied server enemy damage: enemyId={enemyId}, target={targetConnectionId}, amount={applicationResult.AppliedAmount}, current={applicationResult.CurrentValue}, killed={applicationResult.Killed}.");
            return true;
        }

        static bool IsWithinPvpRange(DFMPPlayerSessionState sourceSession, DFMPPlayerSessionState targetSession, DFMPCombatAttackKind attackKind)
        {
            long deltaX = (long)sourceSession.WorldX - targetSession.WorldX;
            long deltaZ = (long)sourceSession.WorldZ - targetSession.WorldZ;
            long maximumRange = attackKind == DFMPCombatAttackKind.Ranged && Config != null && Config.Combat != null
                ? Config.Combat.MaximumRangedPvpRange
                : (long)DFMPCombatProtocol.MaximumPvpRange;
            return deltaX * deltaX + deltaZ * deltaZ <= maximumRange * maximumRange;
        }

        static bool IsDamageCooldownElapsed(int connectionId)
        {
            float lastDamageTime;
            float cooldown = Config != null && Config.Combat != null ? Config.Combat.DamageCooldownSeconds : 0.1f;
            return !lastDamageTimes.TryGetValue(connectionId, out lastDamageTime) || Time.unscaledTime - lastDamageTime >= cooldown;
        }

        static bool IsDamageRateAvailable(int connectionId)
        {
            float windowStart;
            int requestCount;
            float window = Config != null && Config.Combat != null ? Config.Combat.DamageRateWindowSeconds : 1.0f;
            int maximumRequests = Config != null && Config.Combat != null ? Config.Combat.MaximumDamageRequestsPerWindow : 10;
            if (!damageWindowStartTimes.TryGetValue(connectionId, out windowStart) || Time.unscaledTime - windowStart >= window)
                return true;

            return !damageWindowCounts.TryGetValue(connectionId, out requestCount) || requestCount < maximumRequests;
        }

        static void RecordAcceptedDamage(int connectionId)
        {
            float windowStart;
            if (!damageWindowStartTimes.TryGetValue(connectionId, out windowStart) || Time.unscaledTime - windowStart >= (Config != null && Config.Combat != null ? Config.Combat.DamageRateWindowSeconds : 1.0f))
            {
                damageWindowStartTimes[connectionId] = Time.unscaledTime;
                damageWindowCounts[connectionId] = 0;
            }

            damageWindowCounts[connectionId]++;
            lastDamageTimes[connectionId] = Time.unscaledTime;
        }

        private static void OnDeveloperDamagePlayerRequest(NetworkConnectionToClient conn, DFMPDeveloperDamagePlayerRequest request)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            int maximumAmount = Config != null && Config.Combat != null ? Config.Combat.MaximumDamagePerHit : 100;
            DFMPDeveloperCommandRejectionReason commandReason = DFMPDeveloperCommandPolicy.GetDamagePlayerRejectionReason(
                new DFMPDeveloperCommandContext
                {
                    CommandsEnabled = Config != null && Config.Developer != null && Config.Developer.CommandsEnabled,
                    HasSession = sessionState != null,
                    SpawnConfirmed = sessionState != null && sessionState.SpawnConfirmed
                },
                request.TargetConnectionId,
                request.Amount,
                maximumAmount);

            if (commandReason != DFMPDeveloperCommandRejectionReason.None)
            {
                conn.Send(new DFMPDeveloperDamagePlayerResponse
                {
                    Accepted = false,
                    TargetConnectionId = request.TargetConnectionId,
                    Amount = request.Amount,
                    Reason = commandReason.ToString()
                });
                return;
            }

            uint sequence;
            if (!lastDamageSequences.TryGetValue(conn.connectionId, out sequence))
                sequence = 0;
            ulong requestId;
            if (!lastDamageRequestIds.TryGetValue(conn.connectionId, out requestId))
                requestId = 0;

            bool accepted = TryApplyDamageIntent(conn, new DFMPDamageIntent
            {
                RequestId = requestId + 1,
                Sequence = sequence + 1,
                SourceKind = DFMPDamageSourceKind.Player,
                VitalKind = DFMPVitalKind.Health,
                TargetConnectionId = request.TargetConnectionId,
                Amount = request.Amount
            });

            conn.Send(new DFMPDeveloperDamagePlayerResponse
            {
                Accepted = true,
                TargetConnectionId = request.TargetConnectionId,
                Amount = request.Amount,
                Reason = accepted ? string.Empty : "damage intent rejected"
            });
        }

        private static void OnDeveloperDamageSelfRequest(NetworkConnectionToClient conn, DFMPDeveloperDamageSelfRequest request)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            int maximumAmount = Config != null && Config.Combat != null ? Config.Combat.MaximumDamagePerHit : 100;
            DFMPDeveloperCommandRejectionReason commandReason = DFMPDeveloperCommandPolicy.GetDamageSelfRejectionReason(
                new DFMPDeveloperCommandContext
                {
                    CommandsEnabled = Config != null && Config.Developer != null && Config.Developer.CommandsEnabled,
                    HasSession = sessionState != null,
                    SpawnConfirmed = sessionState != null && sessionState.SpawnConfirmed
                },
                request.Amount,
                maximumAmount);

            if (commandReason != DFMPDeveloperCommandRejectionReason.None)
            {
                conn.Send(new DFMPDeveloperDamagePlayerResponse
                {
                    Accepted = false,
                    TargetConnectionId = conn.connectionId,
                    Amount = request.Amount,
                    Reason = commandReason.ToString()
                });
                return;
            }

            uint sequence;
            if (!lastDamageSequences.TryGetValue(conn.connectionId, out sequence))
                sequence = 0;
            ulong requestId;
            if (!lastDamageRequestIds.TryGetValue(conn.connectionId, out requestId))
                requestId = 0;

            bool accepted = TryApplyDamageIntent(conn, new DFMPDamageIntent
            {
                RequestId = requestId + 1,
                Sequence = sequence + 1,
                SourceKind = DFMPDamageSourceKind.LocalQuestPve,
                VitalKind = DFMPVitalKind.Health,
                TargetConnectionId = conn.connectionId,
                Amount = request.Amount
            });

            conn.Send(new DFMPDeveloperDamagePlayerResponse
            {
                Accepted = accepted,
                TargetConnectionId = conn.connectionId,
                Amount = request.Amount,
                Reason = accepted ? string.Empty : "damage intent rejected"
            });
        }

        private static void OnPlayerIdentityReport(NetworkConnectionToClient conn, DFMPPlayerIdentityReport report)
        {
            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState) || sessionState == null || !sessionState.SpawnConfirmed)
                return;

            sessionState.SetDisplayName(DFMPPositionProtocol.SanitizeDisplayName(report.DisplayName));
            sessionState.SetAppearance(
                report.Race,
                DFMPPositionProtocol.GetDisplayGender(report.Gender),
                DFMPPositionProtocol.GetOutfitVariant(report.OutfitVariant),
                DFMPPositionProtocol.GetFaceVariant(report.FaceVariant));
            sessionState.SetAvatarMobileType(DFMPAvatarProtocol.GetMobileType(report.CareerJson));

            DFMPJoinDecision joinDecision;
            if (joinDecisions.TryGetValue(conn.connectionId, out joinDecision) &&
                joinDecision.Kind != DFMPJoinDecisionKind.Rejected && joinDecision.CharacterRecord != null)
            {
                bool acceptReportedVitals = joinDecision.Kind == DFMPJoinDecisionKind.FirstJoin &&
                    initializedIdentityReportConnections.Add(conn.connectionId);
                DFMPCharacterPersistence.ApplyIdentityReport(joinDecision.CharacterRecord, report, acceptReportedVitals);
                if (acceptReportedVitals)
                    vitalStates[conn.connectionId] = CreateVitalState(joinDecision.CharacterRecord);
                DFMPWorldContextKey context;
                if (worldOccupancy.TryGetContext(conn.connectionId, out context))
                    DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState, context);
                else
                    DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState);
                CharacterStore.Save(joinDecision.CharacterRecord);
                if (acceptReportedVitals)
                    SendVitalSnapshot(conn, vitalStates[conn.connectionId], sessionState.IsDead);
            }
        }

        private static void OnRestRequest(NetworkConnectionToClient conn, DFMPRestRequest request)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            bool hasSession = playerSessionStates.TryGetValue(conn.connectionId, out sessionState) && sessionState != null;
            DFMPRestRequestContext context = new DFMPRestRequestContext
            {
                HasSession = hasSession,
                SpawnConfirmed = hasSession && sessionState.SpawnConfirmed,
                IsMoving = hasSession && sessionState.IsMoving,
                IsResting = hasSession && sessionState.IsResting,
                HasInterruption = false
            };

            string restPolicy = Config != null && Config.Rest != null
                ? Config.Rest.Policy
                : DFMPRestPolicies.Disabled;
            DFMPRestRequestRejectionReason rejectionReason = DFMPRestProtocol.GetRejectionReason(
                context,
                request.RestModeName,
                restPolicy);
            bool accepted = DFMPRestProtocol.IsAccepted(rejectionReason);
            if (accepted)
                sessionState.SetResting(true);
            else
                Debug.LogWarning($"[DFMP Rest] Rejected rest request: connectionId={conn.connectionId}, mode={request.RestModeName}, reason={rejectionReason}.");

            conn.Send(new DFMPRestResponse
            {
                Accepted = accepted,
                RejectionReason = rejectionReason
            });
        }

        private static void OnPlayerActionReport(NetworkConnectionToClient conn, DFMPPlayerActionReport report)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            float lastReportTime;
            float elapsedSeconds = lastActionReportTimes.TryGetValue(conn.connectionId, out lastReportTime)
                ? Time.unscaledTime - lastReportTime
                : DFMPAvatarProtocol.MinimumActionInterval;
            if (!DFMPAvatarProtocol.IsActionAccepted(sessionState, report.Kind, elapsedSeconds))
                return;

            lastActionReportTimes[conn.connectionId] = Time.unscaledTime;
            sessionState.PlayAction(report.Kind);
        }

        private static void OnWorldContextReport(NetworkConnectionToClient conn, DFMPWorldContextReport report)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            playerSessionStates.TryGetValue(conn.connectionId, out sessionState);
            DFMPWorldContextRejectionReason rejectionReason;
            bool changed = IsWorldContextChanged(conn.connectionId, report);
            if (!TryApplyWorldContextReport(conn.connectionId, sessionState, report, out rejectionReason))
            {
                Debug.LogWarning($"[DFMP Context] Rejected context report: connectionId={conn.connectionId}, reason={rejectionReason}.");
                return;
            }

            if (changed)
            {
                DFMPWorldContextKey context;
                worldOccupancy.TryGetContext(conn.connectionId, out context);
                Debug.Log($"[DFMP Context] Updated session context: connectionId={conn.connectionId}, context={context}.");
            }
        }

        private static void OnChatMessage(NetworkConnectionToClient conn, DFMPChatSubmitMessage message)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
            {
                Debug.LogWarning($"[DFMP Chat] Rejected message without a session: connectionId={conn.connectionId}.");
                return;
            }

            string reason;
            if (!DFMPChatProtocol.TryValidateSender(sessionState, conn.connectionId, conn.connectionId, out reason))
            {
                Debug.LogWarning($"[DFMP Chat] Rejected message: connectionId={conn.connectionId}, reason={reason}.");
                return;
            }

            string sanitizedText;
            if (!DFMPChatProtocol.TrySanitize(message.Text, out sanitizedText, out reason))
            {
                Debug.LogWarning($"[DFMP Chat] Rejected message: connectionId={conn.connectionId}, reason={reason}.");
                return;
            }

            if (!DFMPChatProtocol.RateLimiter.TryConsume(conn.connectionId, Time.unscaledTime, out reason))
            {
                Debug.LogWarning($"[DFMP Chat] Rejected message: connectionId={conn.connectionId}, reason={reason}.");
                return;
            }

            var acceptedMessage = new DFMPChatDeliveryMessage
            {
                Kind = DFMPChatMessageKind.Player,
                ConnectionId = conn.connectionId,
                SenderDisplayName = sessionState.DisplayName,
                Text = sanitizedText
            };

            DFMPEventBus.Instance.PublishChatMessageReceived(new DFMPChatMessageReceivedEvent
            {
                Kind = acceptedMessage.Kind,
                ConnectionId = acceptedMessage.ConnectionId,
                SenderDisplayName = acceptedMessage.SenderDisplayName,
                MessageText = acceptedMessage.Text,
                Message = acceptedMessage
            });
            SendChatDeliveryToSpawned(acceptedMessage);
            Debug.Log($"[DFMP Chat] Accepted message: connectionId={conn.connectionId}, sender='{sessionState.DisplayName}', text='{sanitizedText}'.");
        }

        public static bool BroadcastSystemMessage(DFMPChatMessageKind kind, string text, int excludedConnectionId = -1)
        {
            if (kind == DFMPChatMessageKind.Player || string.IsNullOrWhiteSpace(text) || !NetworkServer.active)
                return false;

            var message = new DFMPChatDeliveryMessage
            {
                Kind = kind,
                ConnectionId = -1,
                SenderDisplayName = string.Empty,
                Text = text.Trim()
            };

            SendChatDeliveryToSpawned(message, excludedConnectionId);

            DFMPEventBus.Instance.PublishChatMessageReceived(new DFMPChatMessageReceivedEvent
            {
                Kind = message.Kind,
                ConnectionId = message.ConnectionId,
                SenderDisplayName = message.SenderDisplayName,
                MessageText = message.Text,
                Message = message
            });
            return true;
        }

        static void SendChatDeliveryToSpawned(DFMPChatDeliveryMessage message, int excludedConnectionId = -1)
        {
            foreach (KeyValuePair<int, DFMPPlayerSessionState> entry in playerSessionStates)
            {
                if (entry.Key == excludedConnectionId || entry.Value == null || !entry.Value.SpawnConfirmed)
                    continue;

                NetworkConnectionToClient recipient;
                if (NetworkServer.connections.TryGetValue(entry.Key, out recipient) && recipient != null && recipient.isReady)
                    recipient.Send(message);
            }
        }

        private static void OnAdminRosterRequest(NetworkConnectionToClient conn, DFMPAdminRosterRequest request)
        {
            DFMPPlayerSessionState requesterSession;
            if (!TryValidateAdminRequester(conn, out requesterSession))
                return;

            conn.Send(CreateAdminRoster(conn.connectionId));
        }

        private static void OnAdminKickRequest(NetworkConnectionToClient conn, DFMPAdminKickRequest request)
        {
            DFMPPlayerSessionState requesterSession;
            if (!TryValidateAdminRequester(conn, out requesterSession))
                return;

            DFMPPlayerSessionState targetSession;
            NetworkConnectionToClient targetConnection;
            if (!playerSessionStates.TryGetValue(request.TargetConnectionId, out targetSession) ||
                targetSession == null ||
                !NetworkServer.connections.TryGetValue(request.TargetConnectionId, out targetConnection) ||
                targetConnection == null)
            {
                Debug.LogWarning($"[DFMP Admin] Rejected kick for unavailable player: requester={conn.connectionId}, target={request.TargetConnectionId}.");
                conn.Send(CreateAdminRoster(conn.connectionId));
                return;
            }

            Debug.Log($"[DFMP Admin] Player kick requested: requester={conn.connectionId}, target={request.TargetConnectionId}, name='{targetSession.DisplayName}'.");
            if (pendingAdminKickConnections.Add(request.TargetConnectionId))
            {
                targetConnection.Send(new DFMPAdminKickNotice { Message = DFMPAdminProtocol.KickNoticeText });
                if (Manager != null)
                    Manager.StartCoroutine(DisconnectAfterAdminKickNotice(request.TargetConnectionId, targetConnection));
                else
                    targetConnection.Disconnect();
            }

            if (request.TargetConnectionId != conn.connectionId && conn.isReady)
                conn.Send(CreateAdminRoster(conn.connectionId, request.TargetConnectionId));
        }

        private static DFMPAdminRosterResponse CreateAdminRoster(int adminConnectionId, int excludedConnectionId = -1)
        {
            var accountIds = new Dictionary<int, string>();
            foreach (KeyValuePair<int, DFMPJoinDecision> entry in joinDecisions)
            {
                if (entry.Value != null && entry.Value.Accepted)
                    accountIds[entry.Key] = entry.Value.AccountId;
            }

            return DFMPAdminProtocol.CreateRoster(playerSessionStates, accountIds, adminConnectionId, excludedConnectionId);
        }

        private static void OnAdminKickAcknowledgement(NetworkConnectionToClient conn, DFMPAdminKickAcknowledgement acknowledgement)
        {
            if (conn != null && pendingAdminKickConnections.Remove(conn.connectionId))
                conn.Disconnect();
        }

        private static IEnumerator DisconnectAfterAdminKickNotice(int connectionId, NetworkConnectionToClient expectedConnection)
        {
            yield return new WaitForSecondsRealtime(1f);

            if (!pendingAdminKickConnections.Remove(connectionId))
                yield break;

            NetworkConnectionToClient currentConnection;
            if (NetworkServer.connections.TryGetValue(connectionId, out currentConnection) && currentConnection == expectedConnection)
                currentConnection.Disconnect();
        }

        private static bool TryValidateAdminRequester(NetworkConnectionToClient conn, out DFMPPlayerSessionState sessionState)
        {
            sessionState = null;
            if (conn == null || !IsJoinAccepted(conn) || !playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
            {
                Debug.LogWarning($"[DFMP Admin] Rejected request without an accepted session: connectionId={(conn != null ? conn.connectionId : -1)}.");
                return false;
            }

            string reason;
            if (!DFMPAdminProtocol.TryValidateRequester(sessionState, conn.connectionId, out reason))
            {
                Debug.LogWarning($"[DFMP Admin] Rejected request: connectionId={conn.connectionId}, reason={reason}.");
                return false;
            }

            return true;
        }

        public static void DestroyPlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            TryFinalizePendingDeathRespawnOnDisconnect(conn.connectionId, sessionState);
            SaveCharacterRecord(conn.connectionId, sessionState);

            DFMPEventBus.Instance.PublishPlayerDisconnected(new DFMPPlayerDisconnectedEvent
            {
                ConnectionId = conn.connectionId,
                Address = conn.address,
                SessionState = sessionState,
                Reason = "disconnect"
            });

            if (chatLifecycleNotifier.TryAnnounceDisconnect(conn.connectionId))
            {
                BroadcastSystemMessage(
                    DFMPChatMessageKind.PlayerLeft,
                    string.Format("{0} left.", sessionState.DisplayName),
                    conn.connectionId);
            }

            playerSessionStates.Remove(conn.connectionId);
            joinDecisions.Remove(conn.connectionId);
            DFMPWorldContextKey previousContext;
            bool hadPreviousContext = worldOccupancy.TryGetContext(conn.connectionId, out previousContext);
            worldOccupancy.Remove(conn.connectionId);
            if (hadPreviousContext && DungeonEnemyRosterService != null)
                DungeonEnemyRosterService.HandleContextVacated(previousContext);
            startMarkerAssignments.Remove(conn.connectionId);
            transitionAssignmentStates.Remove(conn.connectionId);
            vitalStates.Remove(conn.connectionId);
            initializedIdentityReportConnections.Remove(conn.connectionId);
            lastDamageSequences.Remove(conn.connectionId);
            lastDamageRequestIds.Remove(conn.connectionId);
            lastDamageTimes.Remove(conn.connectionId);
            damageWindowStartTimes.Remove(conn.connectionId);
            damageWindowCounts.Remove(conn.connectionId);
            lastPositionReportTimes.Remove(conn.connectionId);
            lastActionReportTimes.Remove(conn.connectionId);
            activePositionReportConnections.Remove(conn.connectionId);
            rejectedPositionReportConnections.Remove(conn.connectionId);
            pendingAdminKickConnections.Remove(conn.connectionId);
            DFMPChatProtocol.RateLimiter.Reset(conn.connectionId);
            activeAccounts.Release(conn.connectionId);
            if (sessionState != null)
                NetworkServer.Destroy(sessionState.gameObject);
        }

        public static void Stop()
        {
            if (discoveryListener != null)
            {
                discoveryListener.Stop();
                discoveryListener = null;
            }

            SaveAllCharacterRecords();

            if (Manager != null && Manager.isNetworkActive)
                Manager.StopServer();

            Manager = null;
            Transport = null;
            TimeState = null;
            DungeonGeometryService = null;
            DungeonEnemyRosterService = null;
            playerSessionStates.Clear();
            joinDecisions.Clear();
            worldOccupancy.Clear();
            startMarkerAssignments.Clear();
            transitionAssignmentStates.Clear();
            vitalStates.Clear();
            initializedIdentityReportConnections.Clear();
            lastDamageSequences.Clear();
            lastDamageRequestIds.Clear();
            lastDamageTimes.Clear();
            damageWindowStartTimes.Clear();
            damageWindowCounts.Clear();
            lastPositionReportTimes.Clear();
            lastActionReportTimes.Clear();
            activePositionReportConnections.Clear();
            rejectedPositionReportConnections.Clear();
            pendingAdminKickConnections.Clear();
            chatLifecycleNotifier.Clear();
            activeAccounts.Clear();
            nextTransitionAssignmentId = 1;
            Port = 0;
            Config = null;
            CharacterStore = null;
        }
    }
}

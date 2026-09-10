using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
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
            NetworkServer.RegisterHandler<DFMPPlayerPositionReport>(OnPlayerPositionReport);
            NetworkServer.RegisterHandler<DFMPPlayerIdentityReport>(OnPlayerIdentityReport);
            NetworkServer.RegisterHandler<DFMPPlayerActionReport>(OnPlayerActionReport);
            NetworkServer.RegisterHandler<DFMPRestRequest>(OnRestRequest);
            NetworkServer.RegisterHandler<DFMPWorldContextReport>(OnWorldContextReport);
            NetworkServer.RegisterHandler<DFMPChatSubmitMessage>(OnChatMessage);
            NetworkServer.RegisterHandler<DFMPAdminRosterRequest>(OnAdminRosterRequest);
            NetworkServer.RegisterHandler<DFMPAdminKickRequest>(OnAdminKickRequest);
            NetworkServer.RegisterHandler<DFMPAdminKickAcknowledgement>(OnAdminKickAcknowledgement);
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
                conn.Send(DFMPCharacterSnapshotProtocol.FromRecord(decision.CharacterRecord));

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

        public static bool TrySendTransitionAssignment(NetworkConnectionToClient conn, DFMPTransitionKind kind, DFMPWorldPosition position, DFMPWorldContextKey context, string startMarkerName = null)
        {
            if (conn == null || !CanAssignContext(kind, context.Kind))
                return false;

            DFMPPlayerSessionState sessionState;
            if (playerSessionStates.TryGetValue(conn.connectionId, out sessionState) && sessionState != null)
                sessionState.SetResting(false);

            var assignment = DFMPSpawnProtocol.CreateTransitionAssignment(nextTransitionAssignmentId++, conn.connectionId, kind, position, context, startMarkerName);
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
                assignedContext);
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

            DFMPPositionRejectionReason rejectionReason = DFMPPositionProtocol.GetRejectionReason(sessionState, report, elapsedSeconds);
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
            bool isMoving = DFMPMovementProtocol.IsMoving(sessionState.WorldX, sessionState.WorldZ, report.WorldX, report.WorldZ);
            sessionState.SetPosition(report.WorldX, report.WorldY, report.WorldZ);
            sessionState.SetMovement(isMoving);
            sessionState.SetFacingYaw(DFMPPositionProtocol.NormalizeFacingYaw(report.FacingYaw));
            sessionState.SetSceneHeightOffset(report.SceneHeightOffset);
            if (activePositionReportConnections.Add(conn.connectionId))
                Debug.Log($"[DFMP Session] Server accepted player position reports: connectionId={conn.connectionId}.");
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
                DFMPCharacterPersistence.ApplyIdentityReport(joinDecision.CharacterRecord, report);
                DFMPWorldContextKey context;
                if (worldOccupancy.TryGetContext(conn.connectionId, out context))
                    DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState, context);
                else
                    DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState);
                CharacterStore.Save(joinDecision.CharacterRecord);
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
            worldOccupancy.Remove(conn.connectionId);
            startMarkerAssignments.Remove(conn.connectionId);
            transitionAssignmentStates.Remove(conn.connectionId);
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
            playerSessionStates.Clear();
            joinDecisions.Clear();
            worldOccupancy.Clear();
            startMarkerAssignments.Clear();
            transitionAssignmentStates.Clear();
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

using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using kcp2k;
using Mirror;
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
        static readonly Dictionary<int, float> lastPositionReportTimes = new Dictionary<int, float>();
        static readonly HashSet<int> activePositionReportConnections = new HashSet<int>();
        static readonly HashSet<int> rejectedPositionReportConnections = new HashSet<int>();

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

        static void SaveCharacterRecord(int connectionId, DFMPPlayerSessionState sessionState)
        {
            if (CharacterStore == null || sessionState == null)
                return;

            DFMPJoinDecision joinDecision;
            if (!joinDecisions.TryGetValue(connectionId, out joinDecision) || joinDecision.CharacterRecord == null)
                return;

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
            NetworkServer.RegisterHandler<DFMPPlayerPositionReport>(OnPlayerPositionReport);
            NetworkServer.RegisterHandler<DFMPPlayerIdentityReport>(OnPlayerIdentityReport);
            NetworkServer.RegisterHandler<DFMPChatMessage>(OnChatMessage);
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
            GetInitialSpawnCoordinates(out worldX, out worldZ);

            DFMPJoinDecision savedJoinDecision;
            if (joinDecisions.TryGetValue(conn.connectionId, out savedJoinDecision) &&
                savedJoinDecision.CharacterRecord != null &&
                (savedJoinDecision.CharacterRecord.WorldX != 0 || savedJoinDecision.CharacterRecord.WorldZ != 0))
            {
                worldX = savedJoinDecision.CharacterRecord.WorldX;
                worldZ = savedJoinDecision.CharacterRecord.WorldZ;
            }

            sessionState.Initialize(conn.connectionId, worldX, 0f, worldZ);
            worldOccupancy.SetContext(conn.connectionId, CreateExteriorWorldContext(worldX, worldZ, "Daggerfall"));

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

        private static void GetInitialSpawnCoordinates(out int worldX, out int worldZ)
        {
            worldX = 0;
            worldZ = 0;

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

            lastPositionReportTimes.Remove(conn.connectionId);
            rejectedPositionReportConnections.Remove(conn.connectionId);
            DFMPEventBus.Instance.PublishPlayerSpawned(new DFMPPlayerSpawnedEvent
            {
                ConnectionId = conn.connectionId,
                SessionState = sessionState,
                WorldX = sessionState.WorldX,
                WorldY = sessionState.WorldY,
                WorldZ = sessionState.WorldZ,
                DisplayName = sessionState.DisplayName
            });
            Debug.Log($"[DFMP Session] Server confirmed spawn: connectionId={conn.connectionId}, world={sessionState.WorldX}/{sessionState.WorldY:F2}/{sessionState.WorldZ}.");
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

            DFMPJoinDecision joinDecision;
            if (joinDecisions.TryGetValue(conn.connectionId, out joinDecision) &&
                joinDecision.Kind != DFMPJoinDecisionKind.Rejected && joinDecision.CharacterRecord != null)
            {
                DFMPCharacterPersistence.ApplyIdentityReport(joinDecision.CharacterRecord, report);
                DFMPCharacterPersistence.ApplySessionState(joinDecision.CharacterRecord, sessionState);
                CharacterStore.Save(joinDecision.CharacterRecord);
            }
        }

        private static void OnChatMessage(NetworkConnectionToClient conn, DFMPChatMessage message)
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

            var acceptedMessage = new DFMPChatMessage
            {
                ConnectionId = conn.connectionId,
                SenderDisplayName = sessionState.DisplayName,
                Text = sanitizedText
            };

            NetworkServer.SendToReady(acceptedMessage);
            Debug.Log($"[DFMP Chat] Accepted message: connectionId={conn.connectionId}, sender='{sessionState.DisplayName}', text='{sanitizedText}'.");
        }

        public static void DestroyPlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            DFMPEventBus.Instance.PublishPlayerDisconnected(new DFMPPlayerDisconnectedEvent
            {
                ConnectionId = conn.connectionId,
                Address = conn.address,
                SessionState = sessionState,
                Reason = "disconnect"
            });

            playerSessionStates.Remove(conn.connectionId);
            joinDecisions.Remove(conn.connectionId);
            worldOccupancy.Remove(conn.connectionId);
            lastPositionReportTimes.Remove(conn.connectionId);
            activePositionReportConnections.Remove(conn.connectionId);
            rejectedPositionReportConnections.Remove(conn.connectionId);
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
            lastPositionReportTimes.Clear();
            activePositionReportConnections.Clear();
            rejectedPositionReportConnections.Clear();
            activeAccounts.Clear();
            Port = 0;
            Config = null;
            CharacterStore = null;
        }
    }
}
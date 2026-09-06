using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using kcp2k;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPNetworkServer
    {
        public static DFMPNetworkManager Manager { get; private set; }
        public static KcpTransport Transport { get; private set; }
        public static DFMPTimeState TimeState { get; private set; }
        public static ushort Port { get; private set; }

        static readonly Dictionary<int, DFMPPlayerSessionState> playerSessionStates = new Dictionary<int, DFMPPlayerSessionState>();
        static readonly Dictionary<int, float> lastPositionReportTimes = new Dictionary<int, float>();
        static readonly HashSet<int> activePositionReportConnections = new HashSet<int>();
        static readonly HashSet<int> rejectedPositionReportConnections = new HashSet<int>();

        public static bool IsListening
        {
            get { return NetworkServer.active && Transport != null && Transport.ServerActive(); }
        }

        public static void Start(ushort port, int tickRate, int maxConnections)
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

            Object.DontDestroyOnLoad(networkGo);
            networkGo.SetActive(true);

            Mirror.Transport.active = Transport;
            Manager.StartServer();
            NetworkServer.RegisterHandler<DFMPSpawnAcknowledgement>(OnSpawnAcknowledgement);
            NetworkServer.RegisterHandler<DFMPPlayerPositionReport>(OnPlayerPositionReport);
            NetworkServer.RegisterHandler<DFMPPlayerIdentityReport>(OnPlayerIdentityReport);
            SpawnTimeState();

            Debug.Log($"[DFMP Net] Dedicated listener requested: transport=KCP, port={port}, tickRate={tickRate}, maxConnections={maxConnections}.");
        }

        private static void SpawnTimeState()
        {
            if (TimeState != null)
                return;

            GameObject timeGo = new GameObject("DFMP_TimeState");
            timeGo.SetActive(false);

            timeGo.AddComponent<NetworkIdentity>();
            TimeState = timeGo.AddComponent<DFMPTimeState>();
            Object.DontDestroyOnLoad(timeGo);
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
            sessionState.Initialize(conn.connectionId, worldX, 0f, worldZ);
            Object.DontDestroyOnLoad(sessionGo);
            sessionGo.SetActive(true);

            NetworkServer.Spawn(sessionGo, DFMPPlayerSessionState.AssetId);
            playerSessionStates.Add(conn.connectionId, sessionState);

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
            Debug.Log($"[DFMP Session] Server confirmed spawn: connectionId={conn.connectionId}, world={sessionState.WorldX}/{sessionState.WorldY:F2}/{sessionState.WorldZ}.");
        }

        private static void OnPlayerPositionReport(NetworkConnectionToClient conn, DFMPPlayerPositionReport report)
        {
            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            float lastReportTime;
            float elapsedSeconds = lastPositionReportTimes.TryGetValue(conn.connectionId, out lastReportTime)
                ? Time.unscaledTime - lastReportTime
                : DFMPPositionProtocol.MinimumReportInterval;

            if (!DFMPPositionProtocol.IsAccepted(sessionState, report, elapsedSeconds))
            {
                // Anchor time is deliberately not advanced so the movement budget keeps growing and a
                // legitimate teleport re-anchors instead of locking the session out forever.
                if (sessionState != null && sessionState.SpawnConfirmed && rejectedPositionReportConnections.Add(conn.connectionId))
                    Debug.LogWarning($"[DFMP Session] Server rejected player position report: connectionId={conn.connectionId}, session={sessionState.WorldX}/{sessionState.WorldZ}, report={report.WorldX}/{report.WorldZ}, elapsed={elapsedSeconds:F2}s.");

                return;
            }

            lastPositionReportTimes[conn.connectionId] = Time.unscaledTime;
            rejectedPositionReportConnections.Remove(conn.connectionId);
            sessionState.SetPosition(report.WorldX, report.WorldY, report.WorldZ);
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
        }

        public static void DestroyPlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            DFMPPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            playerSessionStates.Remove(conn.connectionId);
            lastPositionReportTimes.Remove(conn.connectionId);
            activePositionReportConnections.Remove(conn.connectionId);
            rejectedPositionReportConnections.Remove(conn.connectionId);
            if (sessionState != null)
                NetworkServer.Destroy(sessionState.gameObject);
        }

        public static void Stop()
        {
            if (Manager != null && Manager.isNetworkActive)
                Manager.StopServer();

            Manager = null;
            Transport = null;
            TimeState = null;
            playerSessionStates.Clear();
            lastPositionReportTimes.Clear();
            activePositionReportConnections.Clear();
            rejectedPositionReportConnections.Clear();
            Port = 0;
        }
    }
}
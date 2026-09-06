using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using kcp2k;
using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace DFCoop.Runtime
{
    public static class DFCoopNetworkServer
    {
        public static DFCoopNetworkManager Manager { get; private set; }
        public static KcpTransport Transport { get; private set; }
        public static DFCoopTimeState TimeState { get; private set; }
        public static ushort Port { get; private set; }

        static readonly Dictionary<int, DFCoopPlayerSessionState> playerSessionStates = new Dictionary<int, DFCoopPlayerSessionState>();
        static readonly Dictionary<int, float> lastPositionReportTimes = new Dictionary<int, float>();
        static readonly HashSet<int> activePositionReportConnections = new HashSet<int>();

        public static bool IsListening
        {
            get { return NetworkServer.active && Transport != null && Transport.ServerActive(); }
        }

        public static void Start(ushort port, int tickRate, int maxConnections)
        {
            if (IsListening)
            {
                Debug.LogWarning($"[DFCoop Net] Server already listening on port {Port}.");
                return;
            }

            if (NetworkManager.singleton != null && NetworkManager.singleton.isNetworkActive)
            {
                Debug.LogWarning("[DFCoop Net] Mirror already has an active NetworkManager; skipping dedicated listener startup.");
                return;
            }

            Port = port;

            GameObject networkGo = new GameObject("DFCoop_NetworkServer");
            networkGo.SetActive(false);

            Transport = networkGo.AddComponent<KcpTransport>();
            Transport.port = port;
            Transport.NoDelay = true;
            Transport.Interval = 10;
            Transport.Timeout = 10000;
            Transport.MaximizeSocketBuffers = true;
            Transport.statisticsLog = false;

            Manager = networkGo.AddComponent<DFCoopNetworkManager>();
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
            NetworkServer.RegisterHandler<DFCoopSpawnAcknowledgement>(OnSpawnAcknowledgement);
            NetworkServer.RegisterHandler<DFCoopPlayerPositionReport>(OnPlayerPositionReport);
            SpawnTimeState();

            Debug.Log($"[DFCoop Net] Dedicated listener requested: transport=KCP, port={port}, tickRate={tickRate}, maxConnections={maxConnections}.");
        }

        private static void SpawnTimeState()
        {
            if (TimeState != null)
                return;

            GameObject timeGo = new GameObject("DFCoop_TimeState");
            timeGo.SetActive(false);

            timeGo.AddComponent<NetworkIdentity>();
            TimeState = timeGo.AddComponent<DFCoopTimeState>();
            Object.DontDestroyOnLoad(timeGo);
            timeGo.SetActive(true);

            NetworkServer.Spawn(timeGo, DFCoopTimeState.AssetId);

            Debug.Log("[DFCoop Time] Server spawned authoritative time state.");
        }

        public static DFCoopPlayerSessionState CreatePlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return null;

            DFCoopPlayerSessionState existingState;
            if (playerSessionStates.TryGetValue(conn.connectionId, out existingState))
                return existingState;

            GameObject sessionGo = new GameObject("DFCoop_PlayerSessionState");
            sessionGo.SetActive(false);

            sessionGo.AddComponent<NetworkIdentity>();
            var sessionState = sessionGo.AddComponent<DFCoopPlayerSessionState>();
            int worldX;
            int worldZ;
            GetInitialSpawnCoordinates(out worldX, out worldZ);
            sessionState.Initialize(conn.connectionId, worldX, 0f, worldZ);
            Object.DontDestroyOnLoad(sessionGo);
            sessionGo.SetActive(true);

            NetworkServer.Spawn(sessionGo, DFCoopPlayerSessionState.AssetId);
            playerSessionStates.Add(conn.connectionId, sessionState);

            Debug.Log($"[DFCoop Session] Server created player session state: connectionId={conn.connectionId}, world={worldX}/0/{worldZ}.");
            return sessionState;
        }

        public static void MakeSessionStatesVisibleTo(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            foreach (DFCoopPlayerSessionState sessionState in playerSessionStates.Values)
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
                Debug.LogWarning("[DFCoop Session] Daggerfall City spawn data is unavailable; using world origin.");
                return;
            }

            DFLocation location = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation("Daggerfall", "Daggerfall");
            if (!location.Loaded)
            {
                Debug.LogWarning("[DFCoop Session] Daggerfall City was not found in MAPS.BSA; using world origin.");
                return;
            }

            DFPosition mapPixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            Rect locationRect = DaggerfallLocation.GetLocationRect(location);
            DFCoopWorldPosition spawnPosition = DFCoopSpawnProtocol.GetLocationCenter(locationRect);
            worldX = spawnPosition.WorldX;
            worldZ = spawnPosition.WorldZ;

            Debug.Log($"[DFCoop Session] Resolved Daggerfall City spawn: mapPixel={mapPixel.X}/{mapPixel.Y}, locationRect={locationRect.xMin}/{locationRect.yMin}/{locationRect.width}/{locationRect.height}, world={worldX}/{worldZ}.");
        }

        private static void OnSpawnAcknowledgement(NetworkConnectionToClient conn, DFCoopSpawnAcknowledgement acknowledgement)
        {
            DFCoopPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState) || sessionState == null)
            {
                Debug.LogWarning($"[DFCoop Session] Ignored spawn acknowledgement without a session: connectionId={conn.connectionId}.");
                return;
            }

            if (!DFCoopSpawnProtocol.TryConfirmSpawn(sessionState, acknowledgement))
            {
                Debug.LogWarning($"[DFCoop Session] Ignored mismatched spawn acknowledgement: connectionId={conn.connectionId}.");
                return;
            }

            Debug.Log($"[DFCoop Session] Server confirmed spawn: connectionId={conn.connectionId}, world={sessionState.WorldX}/{sessionState.WorldY:F2}/{sessionState.WorldZ}.");
        }

        private static void OnPlayerPositionReport(NetworkConnectionToClient conn, DFCoopPlayerPositionReport report)
        {
            DFCoopPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            float lastReportTime;
            if (!lastPositionReportTimes.TryGetValue(conn.connectionId, out lastReportTime))
                lastReportTime = Time.unscaledTime - DFCoopPositionProtocol.MinimumReportInterval;

            float elapsedSeconds = Time.unscaledTime - lastReportTime;
            lastPositionReportTimes[conn.connectionId] = Time.unscaledTime;
            if (!DFCoopPositionProtocol.IsAccepted(sessionState, report, elapsedSeconds))
                return;

            sessionState.SetPosition(report.WorldX, report.WorldY, report.WorldZ);
            if (activePositionReportConnections.Add(conn.connectionId))
                Debug.Log($"[DFCoop Session] Server accepted player position reports: connectionId={conn.connectionId}.");
        }

        public static void DestroyPlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            DFCoopPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            playerSessionStates.Remove(conn.connectionId);
            lastPositionReportTimes.Remove(conn.connectionId);
            activePositionReportConnections.Remove(conn.connectionId);
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
            Port = 0;
        }
    }
}
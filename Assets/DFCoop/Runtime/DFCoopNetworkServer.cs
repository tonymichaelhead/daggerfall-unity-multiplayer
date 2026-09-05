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
            DFPosition worldPosition = MapsFile.MapPixelToWorldCoord(mapPixel.X, mapPixel.Y);
            worldX = worldPosition.X;
            worldZ = worldPosition.Y;

            Debug.Log($"[DFCoop Session] Resolved Daggerfall City spawn: mapPixel={mapPixel.X}/{mapPixel.Y}, world={worldX}/{worldZ}.");
        }

        public static void DestroyPlayerSessionState(NetworkConnectionToClient conn)
        {
            if (conn == null)
                return;

            DFCoopPlayerSessionState sessionState;
            if (!playerSessionStates.TryGetValue(conn.connectionId, out sessionState))
                return;

            playerSessionStates.Remove(conn.connectionId);
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
            Port = 0;
        }
    }
}
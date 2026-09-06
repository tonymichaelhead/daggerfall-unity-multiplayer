using Mirror;
using UnityEngine;

namespace DFCoop.Runtime
{
    public class DFCoopNetworkManager : NetworkManager
    {
        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log($"[DFCoop Net] Server started on {transport.GetType().Name} port {DFCoopNetworkServer.Port}.");
        }

        public override void OnStopServer()
        {
            Debug.Log("[DFCoop Net] Server stopped.");
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[DFCoop Net] Client starting: address={DFCoopNetworkClient.Address}, port={DFCoopNetworkClient.Port}.");
        }

        public override void OnStopClient()
        {
            Debug.Log("[DFCoop Net] Client stopped.");
            base.OnStopClient();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            Debug.Log($"[DFCoop Net] Client connected: connectionId={conn.connectionId}, address={conn.address}.");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            DFCoopNetworkServer.DestroyPlayerSessionState(conn);
            Debug.Log($"[DFCoop Net] Client disconnected: connectionId={conn.connectionId}, address={conn.address}.");
            base.OnServerDisconnect(conn);
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn);

            conn.Send(new ObjectSpawnStartedMessage());

            if (DFCoopNetworkServer.TimeState != null && DFCoopNetworkServer.TimeState.netIdentity != null)
            {
                NetworkServer.RebuildObservers(DFCoopNetworkServer.TimeState.netIdentity, true);

                int observerCount = DFCoopNetworkServer.TimeState.netIdentity.observers.Count;
                Debug.Log($"[DFCoop Time] Server marked time state visible to ready client: connectionId={conn.connectionId}, observers={observerCount}.");
            }

            DFCoopNetworkServer.MakeSessionStatesVisibleTo(conn);
            conn.Send(new ObjectSpawnFinishedMessage());

            DFCoopPlayerSessionState sessionState = DFCoopNetworkServer.CreatePlayerSessionState(conn);
            if (sessionState != null)
            {
                var mapPixel = DaggerfallConnect.Arena2.MapsFile.WorldCoordToMapPixel(sessionState.WorldX, sessionState.WorldZ);
                conn.Send(new DFCoopSpawnAssignment
                {
                    ConnectionId = conn.connectionId,
                    MapPixelX = mapPixel.X,
                    MapPixelY = mapPixel.Y
                });

                Debug.Log($"[DFCoop Session] Server sent spawn assignment: connectionId={conn.connectionId}, mapPixel={mapPixel.X}/{mapPixel.Y}.");
            }
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            Debug.Log($"[DFCoop Net] Connected to server: address={networkAddress}, port={DFCoopNetworkClient.Port}.");
        }

        public override void OnClientDisconnect()
        {
            Debug.Log($"[DFCoop Net] Disconnected from server: address={networkAddress}, port={DFCoopNetworkClient.Port}.");
            base.OnClientDisconnect();
        }
    }
}
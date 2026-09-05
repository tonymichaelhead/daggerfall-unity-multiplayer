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

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            Debug.Log($"[DFCoop Net] Client connected: connectionId={conn.connectionId}, address={conn.address}.");
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[DFCoop Net] Client disconnected: connectionId={conn.connectionId}, address={conn.address}.");
            base.OnServerDisconnect(conn);
        }
    }
}
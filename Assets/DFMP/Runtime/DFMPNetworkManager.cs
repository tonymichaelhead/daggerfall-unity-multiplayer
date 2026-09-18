using System.Collections;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPNetworkManager : NetworkManager
    {
        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log($"[DFMP Net] Server started on {transport.GetType().Name} port {DFMPNetworkServer.Port}.");
        }

        public override void OnStopServer()
        {
            Debug.Log("[DFMP Net] Server stopped.");
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[DFMP Net] Client starting: address={DFMPNetworkClient.Address}, port={DFMPNetworkClient.Port}.");
        }

        public override void OnStopClient()
        {
            Debug.Log("[DFMP Net] Client stopped.");
            base.OnStopClient();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            Debug.Log($"[DFMP Net] Client connected: connectionId={conn.connectionId}, address={conn.address}.");
            DFMPNetworkServer.BeginJoin(conn);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            DFMPNetworkServer.DestroyPlayerSessionState(conn);

            var auth = authenticator as DFMPNetworkAuthenticator;
            if (auth != null)
                auth.HandleServerDisconnect(conn.connectionId);

            Debug.Log($"[DFMP Net] Client disconnected: connectionId={conn.connectionId}, address={conn.address}.");
            base.OnServerDisconnect(conn);
        }

        /// <summary>
        /// Lets the join-result rejection message flush before dropping the transport connection.
        /// Matches the authenticator's delayed-reject pattern.
        /// </summary>
        public void DisconnectAfterJoinRejection(NetworkConnectionToClient conn)
        {
            StartCoroutine(DisconnectAfterJoinRejectionCoroutine(conn));
        }

        IEnumerator DisconnectAfterJoinRejectionCoroutine(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(1f);
            if (conn != null)
                conn.Disconnect();
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            if (!DFMPNetworkServer.IsJoinAccepted(conn) || !DFMPNetworkServer.IsCharacterBound(conn))
            {
                Debug.LogWarning($"[DFMP Join] Client became ready before character binding: connectionId={conn.connectionId}.");
                conn.Disconnect();
                return;
            }

            base.OnServerReady(conn);

            conn.Send(new ObjectSpawnStartedMessage());

            if (DFMPNetworkServer.TimeState != null && DFMPNetworkServer.TimeState.netIdentity != null)
            {
                NetworkServer.RebuildObservers(DFMPNetworkServer.TimeState.netIdentity, true);

                int observerCount = DFMPNetworkServer.TimeState.netIdentity.observers.Count;
                Debug.Log($"[DFMP Time] Server marked time state visible to ready client: connectionId={conn.connectionId}, observers={observerCount}.");
            }

            DFMPNetworkServer.MakeSessionStatesVisibleTo(conn);
            conn.Send(new ObjectSpawnFinishedMessage());

            DFMPPlayerSessionState sessionState = DFMPNetworkServer.CreatePlayerSessionState(conn);
            if (sessionState != null)
            {
                if (DFMPNetworkServer.TrySendReconnectTransitionAssignment(conn, sessionState))
                    return;

                var mapPixel = DaggerfallConnect.Arena2.MapsFile.WorldCoordToMapPixel(sessionState.WorldX, sessionState.WorldZ);
                string startMarkerName;
                bool hasStartMarker = DFMPNetworkServer.TryGetStartMarkerAssignment(conn.connectionId, out startMarkerName);
                conn.Send(new DFMPSpawnAssignment
                {
                    ConnectionId = conn.connectionId,
                    MapPixelX = mapPixel.X,
                    MapPixelY = mapPixel.Y,
                    WorldX = hasStartMarker ? 0 : sessionState.WorldX,
                    WorldY = hasStartMarker ? 0f : sessionState.WorldY,
                    WorldZ = hasStartMarker ? 0 : sessionState.WorldZ,
                    StartMarkerName = hasStartMarker ? startMarkerName : string.Empty
                });

                Debug.Log($"[DFMP Session] Server sent spawn assignment: connectionId={conn.connectionId}, mapPixel={mapPixel.X}/{mapPixel.Y}.");
            }
        }

        public override void OnClientConnect()
        {
            Debug.Log($"[DFMP Net] Connected to server: address={networkAddress}, port={DFMPNetworkClient.Port}.");
        }

        public override void OnClientDisconnect()
        {
            Debug.Log($"[DFMP Net] Disconnected from server: address={networkAddress}, port={DFMPNetworkClient.Port}.");
            DFMPDynamicEnemyPresentationController.Reset();
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.HandleClientDisconnected();
            base.OnClientDisconnect();
        }
    }
}

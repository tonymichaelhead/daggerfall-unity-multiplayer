using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPWorldInterestManagement : InterestManagement
    {
        void Awake()
        {
            DFMPEventBus.Instance.PlayerWorldContextChanged += OnPlayerWorldContextChanged;
        }

        void OnDestroy()
        {
            DFMPEventBus.Instance.PlayerWorldContextChanged -= OnPlayerWorldContextChanged;
        }

        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient newObserver)
        {
            return ShouldObserve(identity, newObserver);
        }

        public override void OnRebuildObservers(NetworkIdentity identity, HashSet<NetworkConnectionToClient> newObservers)
        {
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (ShouldObserve(identity, connection))
                    newObservers.Add(connection);
            }
        }

        public bool ShouldObserve(NetworkIdentity identity, NetworkConnectionToClient observer)
        {
            if (identity == null || observer == null || !observer.isReady)
                return false;

            if (identity.GetComponent<DFMPTimeState>() != null)
                return true;

            DFMPWorldContextKey identityContext;
            if (!TryGetIdentityContext(identity, out identityContext))
                return false;

            DFMPWorldContextKey observerContext;
            return DFMPNetworkServer.TryGetSessionWorldContext(observer.connectionId, out observerContext) &&
                identityContext.Equals(observerContext);
        }

        public static bool TryGetIdentityContext(NetworkIdentity identity, out DFMPWorldContextKey context)
        {
            context = new DFMPWorldContextKey();
            if (identity == null)
                return false;

            DFMPPlayerSessionState sessionState = identity.GetComponent<DFMPPlayerSessionState>();
            return sessionState != null && DFMPNetworkServer.TryGetSessionWorldContext(sessionState.ConnectionId, out context);
        }

        void OnPlayerWorldContextChanged(DFMPPlayerWorldContextChangedEvent e)
        {
            if (!NetworkServer.active)
                return;

            RebuildAll();
        }
    }
}
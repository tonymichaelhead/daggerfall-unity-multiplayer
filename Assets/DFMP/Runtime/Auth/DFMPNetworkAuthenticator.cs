using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Owns the connect handshake. Nothing else may run before this accepts a connection, which is
    /// what keeps the gameplay, admin, and developer message surface unreachable to an unauthenticated peer.
    /// </summary>
    public sealed class DFMPNetworkAuthenticator : NetworkAuthenticator
    {
        const int NonceBytes = 16;

        readonly Dictionary<int, string> pendingNonces = new Dictionary<int, string>();
        readonly DFMPAuthThrottle addressThrottle = new DFMPAuthThrottle(null);
        readonly DFMPAuthThrottle accountThrottle = new DFMPAuthThrottle(null);

        public static string ClientAccountId { get; set; } = string.Empty;
        public static string ClientCredential { get; set; } = string.Empty;
        public static DFMPAuthResultCode LastClientResultCode { get; private set; } = DFMPAuthResultCode.Accepted;
        public static string LastClientRejectionReason { get; private set; } = string.Empty;

        public override void OnStartServer()
        {
            pendingNonces.Clear();
            addressThrottle.Clear();
            accountThrottle.Clear();
            NetworkServer.RegisterHandler<DFMPAuthRequestMessage>(OnAuthRequest, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<DFMPAuthRequestMessage>();
            pendingNonces.Clear();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            string nonce = CreateNonce();
            pendingNonces[conn.connectionId] = nonce;

            conn.Send(new DFMPAuthChallengeMessage
            {
                ProtocolVersion = DFMPProtocol.Version,
                ServerBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                Mode = DFMPAuthModes.Normalize(DFMPNetworkServer.Config != null ? DFMPNetworkServer.Config.Identity.Mode : null),
                Nonce = nonce
            });
        }

        void OnAuthRequest(NetworkConnectionToClient conn, DFMPAuthRequestMessage message)
        {
            string address = conn.address ?? string.Empty;

            // A second request on the same connection means the first nonce is gone; never reuse one.
            string expectedNonce;
            if (!pendingNonces.TryGetValue(conn.connectionId, out expectedNonce))
            {
                RejectConnection(conn, DFMPAuthResultCode.InvalidRequest, "unexpected authentication request", address, string.Empty);
                return;
            }

            pendingNonces.Remove(conn.connectionId);

            if (addressThrottle.IsLockedOut(address))
            {
                RejectConnection(conn, DFMPAuthResultCode.TooManyAttempts, "too many attempts; try again later", address, string.Empty);
                return;
            }

            string throttledAccountId;
            string normalizeReason;
            DFMPAccountPolicy.TryNormalize(message.AccountId, out throttledAccountId, out normalizeReason);
            if (accountThrottle.IsLockedOut(throttledAccountId))
            {
                RejectConnection(conn, DFMPAuthResultCode.TooManyAttempts, "too many attempts; try again later", address, string.Empty);
                return;
            }

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(
                message,
                expectedNonce,
                DFMPNetworkServer.Config,
                DFMPNetworkServer.LocalAccountStore);

            if (!decision.Accepted)
            {
                RejectConnection(conn, decision.Code, decision.Reason, address, throttledAccountId);
                return;
            }

            addressThrottle.RecordSuccess(address);
            accountThrottle.RecordSuccess(throttledAccountId);

            DFMPAccountRole role = DFMPAccountRoles.Resolve(decision.AccountId, DFMPNetworkServer.Config);
            DFMPAuthenticatedConnections.Set(conn.connectionId, decision.AccountId, role);

            conn.Send(new DFMPAuthResponseMessage
            {
                Code = DFMPAuthResultCode.Accepted,
                Reason = string.Empty,
                ServerProtocolVersion = DFMPProtocol.Version,
                ServerBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId)
            });

            Debug.Log($"[DFMP Auth] Authenticated connection: connectionId={conn.connectionId}, account='{decision.AccountId}', role={role}, registered={decision.Registered}, buildId='{DFMPProtocol.SanitizeBuildId(message.ClientBuildId)}'.");
            ServerAccept(conn);
        }

        void RejectConnection(NetworkConnectionToClient conn, DFMPAuthResultCode code, string reason, string address, string accountId)
        {
            // Credential failures feed the throttle; protocol and policy failures do not, so a
            // mismatched build or an unlisted tester does not lock out the whole address.
            if (code == DFMPAuthResultCode.InvalidCredentials || code == DFMPAuthResultCode.InvalidRequest)
            {
                addressThrottle.RecordFailure(address);
                accountThrottle.RecordFailure(accountId);
            }

            Debug.LogWarning($"[DFMP Auth] Rejected connection: connectionId={conn.connectionId}, address={address}, code={code}, reason='{reason}'.");

            conn.Send(new DFMPAuthResponseMessage
            {
                Code = code,
                Reason = reason ?? string.Empty,
                ServerProtocolVersion = DFMPProtocol.Version,
                ServerBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId)
            });

            // Disconnecting immediately can drop the queued rejection, leaving the player with no reason.
            conn.isAuthenticated = false;
            StartCoroutine(DisconnectAfterDelay(conn));
        }

        IEnumerator DisconnectAfterDelay(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(1f);
            ServerReject(conn);
        }

        public override void OnStartClient()
        {
            LastClientResultCode = DFMPAuthResultCode.Accepted;
            LastClientRejectionReason = string.Empty;
            NetworkClient.RegisterHandler<DFMPAuthChallengeMessage>(OnAuthChallenge, false);
            NetworkClient.RegisterHandler<DFMPAuthResponseMessage>(OnAuthResponse, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<DFMPAuthChallengeMessage>();
            NetworkClient.UnregisterHandler<DFMPAuthResponseMessage>();
        }

        public override void OnClientAuthenticate()
        {
            // The server speaks first so it can bind a nonce to this connection.
        }

        void OnAuthChallenge(DFMPAuthChallengeMessage message)
        {
            if (message.ProtocolVersion != DFMPProtocol.Version)
            {
                LastClientResultCode = DFMPAuthResultCode.VersionMismatch;
                LastClientRejectionReason = $"This client speaks protocol {DFMPProtocol.Version} but the server expects {message.ProtocolVersion}. Update your DFMP client.";
                Debug.LogWarning($"[DFMP Auth] {LastClientRejectionReason}");
                ClientReject();
                return;
            }

            NetworkClient.Send(new DFMPAuthRequestMessage
            {
                ProtocolVersion = DFMPProtocol.Version,
                ClientBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                AccountId = ClientAccountId ?? string.Empty,
                Credential = ClientCredential ?? string.Empty,
                Nonce = message.Nonce ?? string.Empty
            });
        }

        void OnAuthResponse(DFMPAuthResponseMessage message)
        {
            LastClientResultCode = message.Code;

            if (message.Code == DFMPAuthResultCode.Accepted)
            {
                LastClientRejectionReason = string.Empty;
                Debug.Log("[DFMP Auth] Server accepted authentication.");
                ClientAccept();
                return;
            }

            LastClientRejectionReason = string.IsNullOrEmpty(message.Reason) ? "authentication failed" : message.Reason;
            Debug.LogWarning($"[DFMP Auth] Server rejected authentication: code={message.Code}, reason='{LastClientRejectionReason}'.");
            ClientReject();
        }

        static string CreateNonce()
        {
            byte[] bytes = new byte[NonceBytes];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);

            return DFMPCredential.ToHex(bytes);
        }
    }

    public static class DFMPAuthenticatedConnections
    {
        struct Entry
        {
            public string AccountId;
            public DFMPAccountRole Role;
        }

        static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

        public static void Set(int connectionId, string accountId, DFMPAccountRole role)
        {
            entries[connectionId] = new Entry { AccountId = accountId, Role = role };
        }

        public static void Remove(int connectionId)
        {
            entries.Remove(connectionId);
        }

        public static void Clear()
        {
            entries.Clear();
        }

        public static bool TryGetAccountId(int connectionId, out string accountId)
        {
            accountId = string.Empty;
            Entry entry;
            if (!entries.TryGetValue(connectionId, out entry))
                return false;

            accountId = entry.AccountId;
            return true;
        }

        public static DFMPAccountRole GetRole(int connectionId)
        {
            Entry entry;
            return entries.TryGetValue(connectionId, out entry) ? entry.Role : DFMPAccountRole.Player;
        }

        public static bool HasRole(int connectionId, DFMPAccountRole required)
        {
            Entry entry;
            return entries.TryGetValue(connectionId, out entry) && DFMPAccountRoles.IsAtLeast(entry.Role, required);
        }
    }
}

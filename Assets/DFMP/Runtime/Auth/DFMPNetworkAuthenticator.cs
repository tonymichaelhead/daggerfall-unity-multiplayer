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

        // How long a player has to finish the Discord consent page in their browser.
        const float BrowserConsentTimeoutSeconds = 180f;

        class PendingChallenge
        {
            public string Nonce;
        }

        readonly Dictionary<int, PendingChallenge> pendingChallenges = new Dictionary<int, PendingChallenge>();
        readonly DFMPAuthThrottle addressThrottle = new DFMPAuthThrottle(null);
        readonly DFMPAuthThrottle accountThrottle = new DFMPAuthThrottle(null);

        IDFMPDiscordAuthClient discordClient;

        public static string ClientAccountId { get; set; } = string.Empty;
        public static string ClientCredential { get; set; } = string.Empty;
        public static DFMPAuthResultCode LastClientResultCode { get; private set; } = DFMPAuthResultCode.Accepted;
        public static string LastClientRejectionReason { get; private set; } = string.Empty;
        public static DFMPAuthStatusStage LastClientStatusStage { get; private set; }
        public static string LastClientStatusDetail { get; private set; } = string.Empty;

        public override void OnStartServer()
        {
            pendingChallenges.Clear();
            addressThrottle.Clear();
            accountThrottle.Clear();
            discordClient = CreateDiscordClient();
            LogAuthModeOnStart();
            NetworkServer.RegisterHandler<DFMPAuthRequestMessage>(OnAuthRequest, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<DFMPAuthRequestMessage>();
            pendingChallenges.Clear();
            StopAllCoroutines();
        }

        /// <summary>
        /// Clears challenge state for a connection that left before finishing authentication.
        /// </summary>
        public void HandleServerDisconnect(int connectionId)
        {
            pendingChallenges.Remove(connectionId);
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            string nonce = CreateNonce();
            string mode = CurrentMode();

            if (mode == DFMPAuthModes.Discord && discordClient == null)
            {
                RejectConnection(conn, DFMPAuthResultCode.ServerUnavailable, "discord authentication is not configured", conn.address ?? string.Empty, string.Empty);
                return;
            }

            pendingChallenges[conn.connectionId] = new PendingChallenge { Nonce = nonce };

            DFMPServerConfig config = DFMPNetworkServer.Config;
            bool discord = mode == DFMPAuthModes.Discord;

            // The server names the redirect target rather than letting the client pick one, because
            // it has to match this application's portal registration exactly.
            conn.Send(new DFMPAuthChallengeMessage
            {
                ProtocolVersion = DFMPProtocol.Version,
                ServerBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                Mode = mode,
                Nonce = nonce,
                DiscordClientId = discord ? DiscordClientId() : string.Empty,
                DiscordRedirectUri = discord && config != null ? config.Identity.DiscordRedirectUri : string.Empty,
                ExpiresIn = discord ? Mathf.RoundToInt(BrowserConsentTimeoutSeconds) : 0
            });
        }

        bool PendingConnectionStillOpen(int connectionId)
        {
            return NetworkServer.connections != null && NetworkServer.connections.ContainsKey(connectionId);
        }

        void OnAuthRequest(NetworkConnectionToClient conn, DFMPAuthRequestMessage message)
        {
            StartCoroutine(HandleAuthRequest(conn, message));
        }

        IEnumerator HandleAuthRequest(NetworkConnectionToClient conn, DFMPAuthRequestMessage message)
        {
            string address = conn.address ?? string.Empty;

            PendingChallenge pending;
            if (!pendingChallenges.TryGetValue(conn.connectionId, out pending))
            {
                RejectConnection(conn, DFMPAuthResultCode.InvalidRequest, "unexpected authentication request", address, string.Empty);
                yield break;
            }

            pendingChallenges.Remove(conn.connectionId);

            if (addressThrottle.IsLockedOut(address))
            {
                RejectConnection(conn, DFMPAuthResultCode.TooManyAttempts, "too many attempts; try again later", address, string.Empty);
                yield break;
            }

            string mode = CurrentMode();
            if (mode == DFMPAuthModes.Discord)
            {
                yield return CompleteDiscordAuth(conn, message, pending, address);
                yield break;
            }

            string throttledAccountId;
            string normalizeReason;
            DFMPAccountPolicy.TryNormalize(message.AccountId, out throttledAccountId, out normalizeReason);
            if (accountThrottle.IsLockedOut(throttledAccountId))
            {
                RejectConnection(conn, DFMPAuthResultCode.TooManyAttempts, "too many attempts; try again later", address, string.Empty);
                yield break;
            }

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(
                message,
                pending.Nonce,
                DFMPNetworkServer.Config,
                DFMPNetworkServer.LocalAccountStore);

            FinishDecision(conn, decision, address, throttledAccountId, message.ClientBuildId);
        }

        IEnumerator CompleteDiscordAuth(
            NetworkConnectionToClient conn,
            DFMPAuthRequestMessage message,
            PendingChallenge pending,
            string address)
        {
            if (message.ProtocolVersion != DFMPProtocol.Version)
            {
                RejectConnection(
                    conn,
                    DFMPAuthResultCode.VersionMismatch,
                    $"client protocol {message.ProtocolVersion} does not match server protocol {DFMPProtocol.Version}",
                    address,
                    string.Empty);
                yield break;
            }

            if (string.IsNullOrEmpty(pending.Nonce) || !DFMPCredential.FixedTimeEquals(message.Nonce, pending.Nonce))
            {
                RejectConnection(conn, DFMPAuthResultCode.InvalidRequest, "challenge mismatch", address, string.Empty);
                yield break;
            }

            if (discordClient == null)
            {
                RejectConnection(conn, DFMPAuthResultCode.ServerUnavailable, "discord authentication is not configured", address, string.Empty);
                yield break;
            }

            DFMPAuthResultCode authorizeCode;
            string authorizeReason;
            if (DFMPDiscordConnectPolicy.TryMapAuthorizeError(message.AuthorizationError, out authorizeCode, out authorizeReason))
            {
                RejectConnection(conn, authorizeCode, authorizeReason, address, string.Empty);
                yield break;
            }

            if (string.IsNullOrEmpty(message.AuthorizationCode) || string.IsNullOrEmpty(message.CodeVerifier))
            {
                RejectConnection(conn, DFMPAuthResultCode.InvalidRequest, "discord authorization code is missing", address, string.Empty);
                yield break;
            }

            SendAuthStatus(conn, DFMPAuthStatusStage.CheckingAccess, "Checking server access.");

            DFMPServerConfig redirectConfig = DFMPNetworkServer.Config;
            string redirectUri = redirectConfig != null ? redirectConfig.Identity.DiscordRedirectUri : string.Empty;

            string accessToken = string.Empty;
            string exchangeError = string.Empty;
            bool exchanged = false;
            yield return discordClient.ExchangeCode(
                message.AuthorizationCode,
                message.CodeVerifier,
                redirectUri,
                (token, error) =>
                {
                    accessToken = token;
                    exchangeError = error;
                    exchanged = true;
                });

            while (!exchanged)
                yield return null;

            if (!PendingConnectionStillOpen(conn.connectionId))
                yield break;

            if (string.IsNullOrEmpty(accessToken))
            {
                RejectConnection(
                    conn,
                    DFMPAuthResultCode.ServerUnavailable,
                    string.IsNullOrEmpty(exchangeError) ? "discord code exchange failed" : exchangeError,
                    address,
                    string.Empty);
                yield break;
            }

            string userId = string.Empty;
            string userError = string.Empty;
            bool gotUser = false;
            yield return discordClient.GetUserId(accessToken, (id, error) =>
            {
                userId = id;
                userError = error;
                gotUser = true;
            });

            while (!gotUser)
                yield return null;

            if (string.IsNullOrEmpty(userId))
            {
                RejectConnection(conn, DFMPAuthResultCode.ServerUnavailable, string.IsNullOrEmpty(userError) ? "discord user lookup failed" : userError, address, string.Empty);
                yield break;
            }

            DFMPServerConfig config = DFMPNetworkServer.Config;
            bool roleCheckRequired = config != null && config.Identity.HasDiscordRoleWhitelist;
            string botToken = System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.BotTokenVariable);
            DFMPDiscordGuildMember member = null;

            if (roleCheckRequired)
            {
                bool gotMember = false;
                string memberError = string.Empty;
                yield return discordClient.GetGuildMember(userId, config.Identity.DiscordGuildId, (result, error) =>
                {
                    member = result;
                    memberError = error;
                    gotMember = true;
                });

                while (!gotMember)
                    yield return null;

                if (member == null && !string.IsNullOrEmpty(memberError))
                {
                    RejectConnection(conn, DFMPAuthResultCode.ServerUnavailable, memberError, address, string.Empty);
                    yield break;
                }
            }

            DFMPAuthDecision decision = DFMPDiscordConnectPolicy.Resolve(
                userId,
                config,
                member,
                roleCheckRequired,
                !string.IsNullOrWhiteSpace(botToken));

            FinishDecision(conn, decision, address, decision.AccountId, message.ClientBuildId);
        }

        void FinishDecision(
            NetworkConnectionToClient conn,
            DFMPAuthDecision decision,
            string address,
            string throttleKey,
            string clientBuildId)
        {
            if (!decision.Accepted)
            {
                RejectConnection(conn, decision.Code, decision.Reason, address, throttleKey);
                return;
            }

            addressThrottle.RecordSuccess(address);
            accountThrottle.RecordSuccess(throttleKey);

            DFMPAccountRole role = DFMPAccountRoles.Resolve(decision.AccountId, DFMPNetworkServer.Config);
            DFMPAuthenticatedConnections.Set(conn.connectionId, decision.AccountId, role);

            conn.Send(new DFMPAuthResponseMessage
            {
                Code = DFMPAuthResultCode.Accepted,
                Reason = string.Empty,
                ServerProtocolVersion = DFMPProtocol.Version,
                ServerBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                AccountId = decision.AccountId
            });

            Debug.Log($"[DFMP Auth] Authenticated connection: connectionId={conn.connectionId}, account='{decision.AccountId}', role={role}, registered={decision.Registered}, buildId='{DFMPProtocol.SanitizeBuildId(clientBuildId)}'.");
            ServerAccept(conn);
        }

        static void SendAuthStatus(NetworkConnectionToClient conn, DFMPAuthStatusStage stage, string detail)
        {
            conn.Send(new DFMPAuthStatusMessage
            {
                Stage = stage,
                Detail = detail ?? string.Empty
            });
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
                ServerBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                AccountId = string.Empty
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
            LastClientStatusDetail = string.Empty;
            NetworkClient.RegisterHandler<DFMPAuthChallengeMessage>(OnAuthChallenge, false);
            NetworkClient.RegisterHandler<DFMPAuthStatusMessage>(OnAuthStatus, false);
            NetworkClient.RegisterHandler<DFMPAuthResponseMessage>(OnAuthResponse, false);
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<DFMPAuthChallengeMessage>();
            NetworkClient.UnregisterHandler<DFMPAuthStatusMessage>();
            NetworkClient.UnregisterHandler<DFMPAuthResponseMessage>();
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.CloseDiscordAuthorizeWindow();
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

            string mode = DFMPAuthModes.Normalize(message.Mode);
            if (mode == DFMPAuthModes.Discord)
            {
                StartCoroutine(RunDiscordLoopbackAuthorize(message));
                return;
            }

            NetworkClient.Send(new DFMPAuthRequestMessage
            {
                ProtocolVersion = DFMPProtocol.Version,
                ClientBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                AccountId = ClientAccountId ?? string.Empty,
                Credential = ClientCredential ?? string.Empty,
                Nonce = message.Nonce ?? string.Empty,
                AuthorizationCode = string.Empty,
                CodeVerifier = string.Empty,
                AuthorizationError = string.Empty
            });
        }

        /// <summary>
        /// Sends the player to Discord in their own browser and waits for the redirect back to
        /// loopback. Only the resulting authorization code reaches the game server; the player's
        /// Discord session and the resulting token never do.
        /// </summary>
        IEnumerator RunDiscordLoopbackAuthorize(DFMPAuthChallengeMessage challenge)
        {
            int port;
            string redirectReason;
            if (!DFMPDiscordRedirect.TryParseLoopback(challenge.DiscordRedirectUri, out port, out redirectReason))
            {
                FailDiscordAuthorize(challenge, "server_misconfigured", $"This server's Discord redirect is unusable: {redirectReason}.");
                yield break;
            }

            using (var listener = new DFMPDiscordLoopbackListener())
            {
                string listenError;
                if (!listener.Start(port, out listenError))
                {
                    FailDiscordAuthorize(challenge, "loopback_unavailable", $"Could not start the local sign-in listener: {listenError}");
                    yield break;
                }

                string verifier = DFMPDiscordPkce.CreateVerifier();
                string authorizeUrl = DFMPDiscordRedirect.BuildAuthorizeUrl(
                    challenge.DiscordClientId,
                    challenge.DiscordRedirectUri,
                    challenge.Nonce,
                    DFMPDiscordPkce.CreateChallenge(verifier));

                LastClientStatusStage = DFMPAuthStatusStage.WaitingForDiscord;
                LastClientStatusDetail = "Approve this server in your browser.";
                if (DFMPClientJoinFlowController.Instance != null)
                    DFMPClientJoinFlowController.Instance.ShowDiscordAuthorize(authorizeUrl, LastClientStatusDetail);

                TryOpenBrowser(authorizeUrl);

                float deadline = Time.unscaledTime +
                    (challenge.ExpiresIn > 0 ? challenge.ExpiresIn : BrowserConsentTimeoutSeconds);

                while (!listener.HasResult)
                {
                    if (!NetworkClient.active)
                        yield break;

                    if (Time.unscaledTime >= deadline)
                    {
                        listener.Fail("timeout");
                        break;
                    }

                    listener.Poll();
                    yield return null;
                }

                // A mismatched state means the redirect we caught belongs to some other flow.
                if (listener.Error.Length == 0 &&
                    !DFMPCredential.FixedTimeEquals(listener.State, challenge.Nonce ?? string.Empty))
                {
                    FailDiscordAuthorize(challenge, "state_mismatch", "The Discord response did not match this connection.");
                    yield break;
                }

                NetworkClient.Send(new DFMPAuthRequestMessage
                {
                    ProtocolVersion = DFMPProtocol.Version,
                    ClientBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                    AccountId = string.Empty,
                    Credential = string.Empty,
                    Nonce = challenge.Nonce ?? string.Empty,
                    AuthorizationCode = listener.Code ?? string.Empty,
                    CodeVerifier = verifier,
                    AuthorizationError = listener.Error ?? string.Empty
                });
            }
        }

        void FailDiscordAuthorize(DFMPAuthChallengeMessage challenge, string errorCode, string detail)
        {
            Debug.LogWarning($"[DFMP Auth] {detail}");
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.UpdateDiscordAuthorizeStatus(detail);

            NetworkClient.Send(new DFMPAuthRequestMessage
            {
                ProtocolVersion = DFMPProtocol.Version,
                ClientBuildId = DFMPProtocol.SanitizeBuildId(DFMPProtocol.BuildId),
                AccountId = string.Empty,
                Credential = string.Empty,
                Nonce = challenge.Nonce ?? string.Empty,
                AuthorizationCode = string.Empty,
                CodeVerifier = string.Empty,
                AuthorizationError = errorCode
            });
        }

        void OnAuthStatus(DFMPAuthStatusMessage message)
        {
            LastClientStatusStage = message.Stage;
            LastClientStatusDetail = message.Detail ?? string.Empty;
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.UpdateDiscordAuthorizeStatus(LastClientStatusDetail);
        }

        void OnAuthResponse(DFMPAuthResponseMessage message)
        {
            LastClientResultCode = message.Code;

            if (message.Code == DFMPAuthResultCode.Accepted)
            {
                LastClientRejectionReason = string.Empty;
                if (!string.IsNullOrEmpty(message.AccountId))
                    DFMPNetworkClient.BindAuthenticatedAccount(message.AccountId);

                if (DFMPClientJoinFlowController.Instance != null)
                    DFMPClientJoinFlowController.Instance.CloseDiscordAuthorizeWindow();

                Debug.Log("[DFMP Auth] Server accepted authentication.");
                ClientAccept();
                return;
            }

            LastClientRejectionReason = string.IsNullOrEmpty(message.Reason) ? "authentication failed" : message.Reason;
            Debug.LogWarning($"[DFMP Auth] Server rejected authentication: code={message.Code}, reason='{LastClientRejectionReason}'.");
            if (DFMPClientJoinFlowController.Instance != null)
                DFMPClientJoinFlowController.Instance.CloseDiscordAuthorizeWindow();
            ClientReject();
        }

        static void TryOpenBrowser(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            try
            {
                Application.OpenURL(uri);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DFMP Auth] Could not open the Discord authorization URL: {ex.Message}");
            }
        }

        static string CurrentMode()
        {
            return DFMPAuthModes.Normalize(DFMPNetworkServer.Config != null ? DFMPNetworkServer.Config.Identity.Mode : null);
        }

        // The client id is public, and only the secret's length is reported, so an operator can
        // confirm the environment reached this process without the log leaking the secret.
        static void LogAuthModeOnStart()
        {
            string mode = CurrentMode();
            if (mode != DFMPAuthModes.Discord)
            {
                Debug.Log($"[DFMP Auth] Authentication mode '{mode}'.");
                return;
            }

            string clientId = System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.ClientIdVariable) ?? string.Empty;
            string clientSecret = System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.ClientSecretVariable) ?? string.Empty;

            Debug.Log(
                $"[DFMP Auth] Authentication mode 'discord': clientId='{clientId}', " +
                $"secret={(clientSecret.Length > 0 ? clientSecret.Length + " chars" : "MISSING")}.");
        }

        static string DiscordClientId()
        {
            return DFMPDiscordSecrets.Clean(
                System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.ClientIdVariable));
        }

        static IDFMPDiscordAuthClient CreateDiscordClient()
        {
            if (CurrentMode() != DFMPAuthModes.Discord)
                return null;

            return new DFMPDiscordHttpClient(
                System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.ClientIdVariable),
                System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.ClientSecretVariable),
                System.Environment.GetEnvironmentVariable(DFMPDiscordSecrets.BotTokenVariable));
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

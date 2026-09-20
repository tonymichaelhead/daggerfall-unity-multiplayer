using System;

namespace DFMP.Runtime
{
    /// <summary>
    /// Pure connect-time authentication decision. Answers "who are you", never "may you play here" —
    /// that stays with <see cref="DFMPJoinPolicy"/>, which runs after this succeeds.
    /// Discord identity is resolved by the authenticator before <see cref="DFMPDiscordConnectPolicy"/>.
    /// </summary>
    public static class DFMPAuthPolicy
    {
        public static DFMPAuthDecision Resolve(
            DFMPAuthRequestMessage request,
            string expectedNonce,
            DFMPServerConfig config,
            IDFMPLocalAccountStore accountStore)
        {
            if (config == null)
                return Reject(DFMPAuthResultCode.ServerUnavailable, "server configuration unavailable");

            config.Normalize();

            if (request.ProtocolVersion != DFMPProtocol.Version)
            {
                return Reject(
                    DFMPAuthResultCode.VersionMismatch,
                    $"client protocol {request.ProtocolVersion} does not match server protocol {DFMPProtocol.Version}");
            }

            if (string.IsNullOrEmpty(expectedNonce) || !DFMPCredential.FixedTimeEquals(request.Nonce, expectedNonce))
                return Reject(DFMPAuthResultCode.InvalidRequest, "challenge mismatch");

            string mode = DFMPAuthModes.Normalize(config.Identity.Mode);
            switch (mode)
            {
                case DFMPAuthModes.Open:
                    return ResolveOpen(request, config);

                case DFMPAuthModes.ServerLocal:
                    return ResolveServerLocal(request, config, accountStore);

                case DFMPAuthModes.Discord:
                    return Reject(DFMPAuthResultCode.InvalidRequest, "discord identity is assigned by the server");

                default:
                    return Reject(DFMPAuthResultCode.ModeUnsupported, "server authentication mode is not available in this build");
            }
        }

        static DFMPAuthDecision ResolveOpen(DFMPAuthRequestMessage request, DFMPServerConfig config)
        {
            string accountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(request.AccountId, out accountId, out reason))
                return Reject(DFMPAuthResultCode.InvalidRequest, reason);

            if (!DFMPAccountPolicy.IsAllowed(accountId, config))
                return Reject(DFMPAuthResultCode.NotWhitelisted, "account is not whitelisted");

            return Accept(accountId, false);
        }

        static DFMPAuthDecision ResolveServerLocal(
            DFMPAuthRequestMessage request,
            DFMPServerConfig config,
            IDFMPLocalAccountStore accountStore)
        {
            string accountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(request.AccountId, out accountId, out reason))
                return Reject(DFMPAuthResultCode.InvalidRequest, reason);

            if (!DFMPAccountPolicy.IsAllowed(accountId, config))
                return Reject(DFMPAuthResultCode.NotWhitelisted, "account is not whitelisted");

            // A launcher-run client carries no credential at all, and server_local has no player-facing
            // login yet, so say that rather than reporting a malformed value.
            if (string.IsNullOrEmpty(request.Credential))
            {
                return Reject(
                    DFMPAuthResultCode.InvalidRequest,
                    "this server uses username and password login, which this client cannot provide yet");
            }

            if (!DFMPCredential.IsValidCredentialFormat(request.Credential))
                return Reject(DFMPAuthResultCode.InvalidRequest, "malformed credential");

            if (accountStore == null)
                return Reject(DFMPAuthResultCode.ServerUnavailable, "account store unavailable");

            DFMPLocalAccountRecord record;
            if (accountStore.TryLoad(accountId, out record))
            {
                if (!record.Matches(request.Credential))
                    return Reject(DFMPAuthResultCode.InvalidCredentials, "invalid username or password");

                record.LastSeenUtc = DateTime.UtcNow.ToString("o");
                accountStore.Save(record);
                return Accept(accountId, false);
            }

            if (!config.Identity.AllowSelfRegistration)
                return Reject(DFMPAuthResultCode.RegistrationClosed, "this server does not accept new accounts");

            accountStore.Save(DFMPLocalAccountRecord.Create(accountId, request.Credential));
            return Accept(accountId, true);
        }

        static DFMPAuthDecision Accept(string accountId, bool registered)
        {
            return new DFMPAuthDecision
            {
                Code = DFMPAuthResultCode.Accepted,
                AccountId = accountId,
                Registered = registered
            };
        }

        static DFMPAuthDecision Reject(DFMPAuthResultCode code, string reason)
        {
            return new DFMPAuthDecision
            {
                Code = code,
                Reason = reason ?? string.Empty
            };
        }
    }
}

using System;

namespace DFMP.Runtime
{
    /// <summary>
    /// Pure connect-time authentication decision. Answers "who are you", never "may you play here" —
    /// that stays with <see cref="DFMPJoinPolicy"/>, which runs after this succeeds.
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

            string accountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(request.AccountId, out accountId, out reason))
                return Reject(DFMPAuthResultCode.InvalidRequest, reason);

            if (!DFMPAccountPolicy.IsAllowed(accountId, config))
                return Reject(DFMPAuthResultCode.NotWhitelisted, "account is not whitelisted");

            string mode = DFMPAuthModes.Normalize(config.Identity.Mode);
            switch (mode)
            {
                case DFMPAuthModes.Open:
                    return Accept(accountId, false);

                case DFMPAuthModes.ServerLocal:
                    return ResolveServerLocal(request, expectedNonce, accountId, config, accountStore);

                default:
                    return Reject(DFMPAuthResultCode.ModeUnsupported, "server authentication mode is not available in this build");
            }
        }

        static DFMPAuthDecision ResolveServerLocal(
            DFMPAuthRequestMessage request,
            string expectedNonce,
            string accountId,
            DFMPServerConfig config,
            IDFMPLocalAccountStore accountStore)
        {
            if (string.IsNullOrEmpty(expectedNonce) || !DFMPCredential.FixedTimeEquals(request.Nonce, expectedNonce))
                return Reject(DFMPAuthResultCode.InvalidRequest, "challenge mismatch");

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

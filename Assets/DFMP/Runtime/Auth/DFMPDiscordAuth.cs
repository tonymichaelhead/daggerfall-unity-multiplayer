using System;
using System.Security.Cryptography;
using System.Text;

namespace DFMP.Runtime
{
    /// <summary>
    /// Proof Key for Code Exchange (RFC 7636). The player's browser carries the authorization code
    /// back over plain loopback HTTP, so the code alone must not be enough to redeem.
    /// </summary>
    public static class DFMPDiscordPkce
    {
        const int VerifierBytes = 48;

        public static string CreateVerifier()
        {
            byte[] bytes = new byte[VerifierBytes];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);

            return ToBase64Url(bytes);
        }

        public static string CreateChallenge(string verifier)
        {
            if (string.IsNullOrEmpty(verifier))
                return string.Empty;

            using (var sha = SHA256.Create())
                return ToBase64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }

    /// <summary>
    /// The redirect target the player's browser is sent back to. It must be a loopback address so no
    /// listener is ever exposed off the machine, and it must match the portal registration exactly.
    /// </summary>
    public static class DFMPDiscordRedirect
    {
        public const string Default = "http://127.0.0.1:53682/dfmp-auth";
        public const string AuthorizeEndpoint = "https://discord.com/oauth2/authorize";
        public const string Scope = "identify";

        public static bool TryParseLoopback(string redirectUri, out int port, out string reason)
        {
            port = 0;
            reason = string.Empty;

            Uri parsed;
            if (string.IsNullOrWhiteSpace(redirectUri) ||
                !Uri.TryCreate(redirectUri.Trim(), UriKind.Absolute, out parsed))
            {
                reason = "discord redirect uri is not a valid absolute url";
                return false;
            }

            if (parsed.Scheme != Uri.UriSchemeHttp)
            {
                reason = "discord redirect uri must use http on loopback";
                return false;
            }

            if (parsed.Host != "127.0.0.1" && parsed.Host != "localhost")
            {
                reason = "discord redirect uri must point at 127.0.0.1 or localhost";
                return false;
            }

            if (parsed.IsDefaultPort || parsed.Port <= 1024 || parsed.Port > 65535)
            {
                reason = "discord redirect uri must name an explicit port above 1024";
                return false;
            }

            port = parsed.Port;
            return true;
        }

        public static string BuildAuthorizeUrl(string clientId, string redirectUri, string state, string codeChallenge)
        {
            return AuthorizeEndpoint +
                "?response_type=code" +
                "&client_id=" + Uri.EscapeDataString(clientId ?? string.Empty) +
                "&redirect_uri=" + Uri.EscapeDataString(redirectUri ?? string.Empty) +
                "&scope=" + Uri.EscapeDataString(Scope) +
                "&state=" + Uri.EscapeDataString(state ?? string.Empty) +
                "&code_challenge=" + Uri.EscapeDataString(codeChallenge ?? string.Empty) +
                "&code_challenge_method=S256" +
                "&prompt=consent";
        }
    }

    public sealed class DFMPDiscordGuildMember
    {
        public bool IsMember;
        public string[] RoleIds = new string[0];
    }

    public static class DFMPDiscordIdentity
    {
        public const string AccountPrefix = "discord:";

        public static bool TryFormatAccountId(string snowflake, out string accountId, out string reason)
        {
            accountId = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(snowflake))
            {
                reason = "discord user id is empty";
                return false;
            }

            string trimmed = snowflake.Trim();
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!char.IsDigit(trimmed[i]))
                {
                    reason = "discord user id is not a snowflake";
                    return false;
                }
            }

            return DFMPAccountPolicy.TryNormalize(AccountPrefix + trimmed, out accountId, out reason);
        }
    }

    public static class DFMPDiscordSecrets
    {
        public const string ClientIdVariable = "DFMP_DISCORD_CLIENT_ID";
        public const string ClientSecretVariable = "DFMP_DISCORD_CLIENT_SECRET";
        public const string BotTokenVariable = "DFMP_DISCORD_BOT_TOKEN";

        /// <summary>
        /// Strips whitespace and wrapping quotes that shells and env files commonly leave behind.
        /// </summary>
        public static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string trimmed = value.Trim();
            if (trimmed.Length >= 2 &&
                ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                 (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();

            return trimmed;
        }

        public static bool TryValidate(DFMPServerConfig config, out string error)
        {
            return TryValidate(
                config,
                Environment.GetEnvironmentVariable(ClientIdVariable),
                Environment.GetEnvironmentVariable(ClientSecretVariable),
                Environment.GetEnvironmentVariable(BotTokenVariable),
                out error);
        }

        public static bool TryValidate(
            DFMPServerConfig config,
            string clientId,
            string clientSecret,
            string botToken,
            out string error)
        {
            error = string.Empty;
            if (config == null)
            {
                error = "server configuration unavailable";
                return false;
            }

            config.Normalize();
            if (DFMPAuthModes.Normalize(config.Identity.Mode) != DFMPAuthModes.Discord)
                return true;

            string id = Clean(clientId);
            string secret = Clean(clientSecret);

            if (id.Length == 0 || secret.Length == 0)
            {
                error = "Identity.Mode is discord, but " + ClientIdVariable + " or " + ClientSecretVariable +
                    " is missing. Set both in the server process environment.";
                return false;
            }

            // Discord answers a non-snowflake client id with an opaque "invalid client id" only once a
            // player tries to connect, so reject it while the operator is still looking at the console.
            for (int i = 0; i < id.Length; i++)
            {
                if (!char.IsDigit(id[i]))
                {
                    error = ClientIdVariable + " must be the application id, which is digits only. Got '" + id +
                        "'. Copy the Application ID from the Discord developer portal, not the secret or a bot token.";
                    return false;
                }
            }

            int port;
            string redirectReason;
            if (!DFMPDiscordRedirect.TryParseLoopback(config.Identity.DiscordRedirectUri, out port, out redirectReason))
            {
                error = "Identity.DiscordRedirectUri is invalid: " + redirectReason +
                    ". Use something like " + DFMPDiscordRedirect.Default +
                    " and register that exact value under OAuth2 > Redirects in the Discord developer portal.";
                return false;
            }

            if (!config.Identity.HasDiscordRoleWhitelist)
                return true;

            if (string.IsNullOrWhiteSpace(config.Identity.DiscordGuildId))
            {
                error = "DiscordAllowedRoleIds is set, but DiscordGuildId is empty.";
                return false;
            }

            if (Clean(botToken).Length == 0)
            {
                error = "Discord role whitelist is configured, but " + BotTokenVariable +
                    " is missing. Role checks fail closed.";
                return false;
            }

            return true;
        }
    }

    public static class DFMPDiscordConnectPolicy
    {
        public static DFMPAuthDecision Resolve(
            string discordUserId,
            DFMPServerConfig config,
            DFMPDiscordGuildMember member,
            bool roleCheckRequired,
            bool botTokenPresent)
        {
            if (config == null)
                return Reject(DFMPAuthResultCode.ServerUnavailable, "server configuration unavailable");

            config.Normalize();

            string accountId;
            string reason;
            if (!DFMPDiscordIdentity.TryFormatAccountId(discordUserId, out accountId, out reason))
                return Reject(DFMPAuthResultCode.InvalidRequest, reason);

            if (!DFMPAccountPolicy.IsAllowed(accountId, config))
                return Reject(DFMPAuthResultCode.NotWhitelisted, "account is not whitelisted");

            if (!roleCheckRequired)
                return Accept(accountId);

            if (!botTokenPresent)
                return Reject(DFMPAuthResultCode.ServerUnavailable, "discord role whitelist is configured but the bot token is unavailable");

            if (member == null || !member.IsMember)
                return Reject(DFMPAuthResultCode.NotWhitelisted, "discord member is not in the required guild");

            if (!HasAllowedRole(member.RoleIds, config.Identity.DiscordAllowedRoleIds))
                return Reject(DFMPAuthResultCode.NotWhitelisted, "discord member does not have an allowed role");

            return Accept(accountId);
        }

        /// <summary>
        /// Maps the OAuth2 error the browser handed back on the loopback redirect.
        /// </summary>
        public static bool TryMapAuthorizeError(string authorizeError, out DFMPAuthResultCode code, out string reason)
        {
            code = DFMPAuthResultCode.Accepted;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(authorizeError))
                return false;

            switch (authorizeError.Trim().ToLowerInvariant())
            {
                case "access_denied":
                    code = DFMPAuthResultCode.DiscordDenied;
                    reason = "discord authorization was declined";
                    return true;
                case "timeout":
                    code = DFMPAuthResultCode.DiscordTimeout;
                    reason = "discord authorization timed out";
                    return true;
                default:
                    code = DFMPAuthResultCode.ServerUnavailable;
                    reason = "discord authorization failed: " + authorizeError.Trim();
                    return true;
            }
        }

        static bool HasAllowedRole(string[] memberRoles, string[] allowedRoles)
        {
            if (memberRoles == null || allowedRoles == null)
                return false;

            for (int i = 0; i < allowedRoles.Length; i++)
            {
                string allowed = allowedRoles[i] == null ? string.Empty : allowedRoles[i].Trim();
                if (allowed.Length == 0)
                    continue;

                for (int j = 0; j < memberRoles.Length; j++)
                {
                    string memberRole = memberRoles[j] == null ? string.Empty : memberRoles[j].Trim();
                    if (string.Equals(memberRole, allowed, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        static DFMPAuthDecision Accept(string accountId)
        {
            return new DFMPAuthDecision
            {
                Code = DFMPAuthResultCode.Accepted,
                AccountId = accountId
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

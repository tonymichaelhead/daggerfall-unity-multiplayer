using Mirror;

namespace DFMP.Runtime
{
    public static class DFMPAuthModes
    {
        public const string Open = "open";
        public const string ServerLocal = "server_local";
        public const string Discord = "discord";
        public const string Dfmp = "dfmp";

        public static string Normalize(string mode)
        {
            // Unset means "use the documented default", which is open so a fresh server runs on LAN
            // without a Discord application. Unrecognized values still fall back to server_local,
            // which no client can satisfy, so a typo fails closed rather than disabling auth.
            if (string.IsNullOrWhiteSpace(mode))
                return Open;

            switch (mode.Trim().ToLowerInvariant())
            {
                case Open:
                    return Open;
                case Discord:
                    return Discord;
                case Dfmp:
                    return Dfmp;
                case ServerLocal:
                    return ServerLocal;
                default:
                    return ServerLocal;
            }
        }
    }

    public enum DFMPAuthResultCode
    {
        Accepted = 0,
        VersionMismatch = 1,
        InvalidRequest = 2,
        InvalidCredentials = 3,
        NotWhitelisted = 4,
        RegistrationClosed = 5,
        TooManyAttempts = 6,
        ServerUnavailable = 7,
        ModeUnsupported = 8,
        DiscordDenied = 9,
        DiscordTimeout = 10
    }

    public enum DFMPAuthStatusStage
    {
        WaitingForDiscord = 0,
        CheckingAccess = 1
    }

    public struct DFMPAuthChallengeMessage : NetworkMessage
    {
        public int ProtocolVersion;
        public string ServerBuildId;
        public string Mode;
        public string Nonce;
        public string DiscordClientId;
        public string DiscordRedirectUri;
        public int ExpiresIn;
    }

    public struct DFMPAuthRequestMessage : NetworkMessage
    {
        public int ProtocolVersion;
        public string ClientBuildId;
        public string AccountId;
        public string Credential;
        public string Nonce;

        // Discord loopback flow. The code is useless without the server's client secret, which is
        // why it is safe to carry over the unencrypted game transport.
        public string AuthorizationCode;
        public string CodeVerifier;
        public string AuthorizationError;
    }

    public struct DFMPAuthStatusMessage : NetworkMessage
    {
        public DFMPAuthStatusStage Stage;
        public string Detail;
    }

    public struct DFMPAuthResponseMessage : NetworkMessage
    {
        public DFMPAuthResultCode Code;
        public string Reason;
        public int ServerProtocolVersion;
        public string ServerBuildId;
        public string AccountId;
    }

    public sealed class DFMPAuthDecision
    {
        public DFMPAuthResultCode Code;
        public string AccountId = string.Empty;
        public string Reason = string.Empty;
        public bool Registered;

        public bool Accepted
        {
            get { return Code == DFMPAuthResultCode.Accepted; }
        }
    }
}

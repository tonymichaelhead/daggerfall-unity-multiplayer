using Mirror;

namespace DFMP.Runtime
{
    public static class DFMPAuthModes
    {
        public const string Open = "open";
        public const string ServerLocal = "server_local";
        public const string Dfmp = "dfmp";

        public static string Normalize(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return ServerLocal;

            switch (mode.Trim().ToLowerInvariant())
            {
                case Open:
                    return Open;
                case Dfmp:
                    return Dfmp;
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
        ModeUnsupported = 8
    }

    public struct DFMPAuthChallengeMessage : NetworkMessage
    {
        public int ProtocolVersion;
        public string ServerBuildId;
        public string Mode;
        public string Nonce;
    }

    public struct DFMPAuthRequestMessage : NetworkMessage
    {
        public int ProtocolVersion;
        public string ClientBuildId;
        public string AccountId;
        public string Credential;
        public string Nonce;
    }

    public struct DFMPAuthResponseMessage : NetworkMessage
    {
        public DFMPAuthResultCode Code;
        public string Reason;
        public int ServerProtocolVersion;
        public string ServerBuildId;
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

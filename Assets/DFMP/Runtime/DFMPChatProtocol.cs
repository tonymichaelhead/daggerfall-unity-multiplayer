using System.Collections.Generic;
using System.Text;
using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPChatMessage : NetworkMessage
    {
        public int ConnectionId;
        public string SenderDisplayName;
        public string Text;
    }

    public class DFMPChatRateLimiter
    {
        readonly Dictionary<int, float> lastMessageTimeByConnection = new Dictionary<int, float>();
        readonly float minimumIntervalSeconds;

        public DFMPChatRateLimiter(float minimumIntervalSeconds)
        {
            this.minimumIntervalSeconds = minimumIntervalSeconds > 0f ? minimumIntervalSeconds : 1f;
        }

        public bool TryConsume(int connectionId, float currentTimeSeconds, out string reason)
        {
            reason = string.Empty;

            float lastMessageTime;
            if (lastMessageTimeByConnection.TryGetValue(connectionId, out lastMessageTime) && currentTimeSeconds - lastMessageTime < minimumIntervalSeconds)
            {
                reason = "chat rate limit exceeded";
                return false;
            }

            lastMessageTimeByConnection[connectionId] = currentTimeSeconds;
            return true;
        }

        public void Reset(int connectionId)
        {
            if (lastMessageTimeByConnection.ContainsKey(connectionId))
                lastMessageTimeByConnection.Remove(connectionId);
        }
    }

    public static class DFMPChatProtocol
    {
        public const int MaxMessageLength = 256;
        public const float DefaultRateLimitSeconds = 1.0f;

        static int maximumMessageLength = MaxMessageLength;
        static DFMPChatRateLimiter rateLimiter = new DFMPChatRateLimiter(DefaultRateLimitSeconds);

        public static int MaximumMessageLength
        {
            get { return maximumMessageLength; }
        }

        public static DFMPChatRateLimiter RateLimiter
        {
            get { return rateLimiter; }
        }

        public static void ConfigureFromConfig(DFMPServerConfig config)
        {
            if (config == null)
                return;

            if (config.Chat != null)
            {
                maximumMessageLength = config.Chat.MaxMessageLength > 0 ? config.Chat.MaxMessageLength : MaxMessageLength;
                rateLimiter = new DFMPChatRateLimiter(config.Chat.MinIntervalSeconds > 0f ? config.Chat.MinIntervalSeconds : DefaultRateLimitSeconds);
            }
            else
            {
                maximumMessageLength = MaxMessageLength;
                rateLimiter = new DFMPChatRateLimiter(DefaultRateLimitSeconds);
            }
        }

        public static void ConfigureRateLimit(float minimumIntervalSeconds)
        {
            rateLimiter = new DFMPChatRateLimiter(minimumIntervalSeconds > 0f ? minimumIntervalSeconds : DefaultRateLimitSeconds);
        }

        public static bool TrySanitize(string input, out string sanitizedText, out string reason)
        {
            sanitizedText = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                reason = "empty chat message";
                return false;
            }

            string trimmed = input.Trim();
            if (trimmed.Length == 0)
            {
                reason = "empty chat message";
                return false;
            }

            if (trimmed.Length > maximumMessageLength)
            {
                reason = "chat message exceeds maximum length";
                return false;
            }

            StringBuilder builder = new StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                if (char.IsControl(c) || c == '<' || c == '>' || c == '\r' || c == '\n' || c == '\t')
                {
                    reason = "chat message contains unsupported characters";
                    return false;
                }

                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || ",.!?;:'\"-_/()[]{}@#$%^&+=~`".IndexOf(c) >= 0)
                    builder.Append(c);
                else
                {
                    reason = "chat message contains unsupported characters";
                    return false;
                }
            }

            sanitizedText = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(sanitizedText))
            {
                reason = "empty chat message";
                return false;
            }

            return true;
        }

        public static bool TryValidateSender(DFMPPlayerSessionState sessionState, int connectionId, int expectedConnectionId, out string reason)
        {
            reason = string.Empty;

            if (sessionState == null)
            {
                reason = "session missing";
                return false;
            }

            if (connectionId != expectedConnectionId)
            {
                reason = "session mismatch";
                return false;
            }

            if (sessionState.ConnectionId != expectedConnectionId)
            {
                reason = "session mismatch";
                return false;
            }

            if (!sessionState.SpawnConfirmed)
            {
                reason = "session is not ready or spawn confirmed";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sessionState.DisplayName))
            {
                reason = "session display name unavailable";
                return false;
            }

            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPAdminRosterRequest : NetworkMessage
    {
    }

    public struct DFMPAdminPlayer
    {
        public int ConnectionId;
        public string DisplayName;
        public string AccountId;
        public bool IsAdmin;
    }

    public struct DFMPAdminRosterResponse : NetworkMessage
    {
        public DFMPAdminPlayer[] Players;
    }

    public struct DFMPAdminKickRequest : NetworkMessage
    {
        public int TargetConnectionId;
    }

    public struct DFMPAdminKickNotice : NetworkMessage
    {
        public string Message;
    }

    public struct DFMPAdminKickAcknowledgement : NetworkMessage
    {
    }

    public static class DFMPAdminProtocol
    {
        public const string KickNoticeText = "You have been removed from the server by an administrator.";

        public static string GetKickNoticeText(string message)
        {
            return string.IsNullOrWhiteSpace(message) ? KickNoticeText : message.Trim();
        }

        public static DFMPAdminRosterResponse CreateRoster(
            IEnumerable<KeyValuePair<int, DFMPPlayerSessionState>> sessions,
            IDictionary<int, string> accountIds,
            int adminConnectionId,
            int excludedConnectionId = -1)
        {
            var players = new List<DFMPAdminPlayer>();
            if (sessions != null)
            {
                foreach (KeyValuePair<int, DFMPPlayerSessionState> entry in sessions)
                {
                    if (entry.Key == excludedConnectionId || entry.Value == null)
                        continue;

                    players.Add(new DFMPAdminPlayer
                    {
                        ConnectionId = entry.Key,
                        DisplayName = string.IsNullOrWhiteSpace(entry.Value.DisplayName)
                            ? "Player"
                            : entry.Value.DisplayName,
                        AccountId = GetAccountId(accountIds, entry.Key),
                        IsAdmin = entry.Key == adminConnectionId
                    });
                }
            }

            players.Sort(ComparePlayers);
            return new DFMPAdminRosterResponse { Players = players.ToArray() };
        }

        static string GetAccountId(IDictionary<int, string> accountIds, int connectionId)
        {
            string accountId;
            return accountIds != null && accountIds.TryGetValue(connectionId, out accountId) && !string.IsNullOrWhiteSpace(accountId)
                ? accountId
                : "unknown";
        }

        public static bool TryValidateRequester(
            DFMPPlayerSessionState sessionState,
            int requesterConnectionId,
            out string reason)
        {
            reason = string.Empty;
            if (sessionState == null)
            {
                reason = "session missing";
                return false;
            }

            if (sessionState.ConnectionId != requesterConnectionId)
            {
                reason = "session mismatch";
                return false;
            }

            return true;
        }

        static int ComparePlayers(DFMPAdminPlayer left, DFMPAdminPlayer right)
        {
            int nameComparison = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
            return nameComparison != 0 ? nameComparison : left.ConnectionId.CompareTo(right.ConnectionId);
        }
    }
}
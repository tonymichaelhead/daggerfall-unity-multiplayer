namespace DFMP.Runtime
{
    public enum DFMPAccountRole
    {
        Player = 0,
        Moderator = 1,
        Admin = 2
    }

    public static class DFMPAccountRoles
    {
        public static DFMPAccountRole Resolve(string accountId, DFMPServerConfig config)
        {
            if (config == null)
                return DFMPAccountRole.Player;

            config.Normalize();

            string normalizedAccountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(accountId, out normalizedAccountId, out reason))
                return DFMPAccountRole.Player;

            if (Contains(config.Identity.AdminAccountIds, normalizedAccountId))
                return DFMPAccountRole.Admin;

            if (Contains(config.Identity.ModeratorAccountIds, normalizedAccountId))
                return DFMPAccountRole.Moderator;

            return DFMPAccountRole.Player;
        }

        public static bool IsAtLeast(DFMPAccountRole role, DFMPAccountRole required)
        {
            return (int)role >= (int)required;
        }

        static bool Contains(string[] accountIds, string normalizedAccountId)
        {
            if (accountIds == null)
                return false;

            foreach (string candidate in accountIds)
            {
                string normalizedCandidate;
                string reason;
                if (DFMPAccountPolicy.TryNormalize(candidate, out normalizedCandidate, out reason) &&
                    normalizedCandidate == normalizedAccountId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

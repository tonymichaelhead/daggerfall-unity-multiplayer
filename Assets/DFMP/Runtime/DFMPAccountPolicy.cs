using System;

namespace DFMP.Runtime
{
    public static class DFMPAccountPolicy
    {
        public const int MaximumAccountIdLength = 128;

        public static bool TryNormalize(string input, out string accountId, out string reason)
        {
            accountId = string.Empty;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                reason = "account identity is empty";
                return false;
            }

            string normalized = input.Trim().ToLowerInvariant();
            if (normalized.Length > MaximumAccountIdLength)
            {
                reason = "account identity exceeds maximum length";
                return false;
            }

            foreach (char value in normalized)
            {
                if (char.IsLetterOrDigit(value) || ":._-".IndexOf(value) >= 0)
                    continue;

                reason = "account identity contains unsupported characters";
                return false;
            }

            accountId = normalized;
            return true;
        }

        public static bool IsAllowed(string accountId, DFMPServerConfig config)
        {
            if (config == null)
                return false;

            config.Normalize();
            if (!config.Identity.WhitelistEnabled)
                return true;

            string normalizedAccountId;
            string reason;
            if (!TryNormalize(accountId, out normalizedAccountId, out reason))
                return false;

            foreach (string allowedAccountId in config.Identity.AllowedAccountIds)
            {
                string normalizedAllowedId;
                if (TryNormalize(allowedAccountId, out normalizedAllowedId, out reason) && normalizedAllowedId == normalizedAccountId)
                    return true;
            }

            return false;
        }
    }
}
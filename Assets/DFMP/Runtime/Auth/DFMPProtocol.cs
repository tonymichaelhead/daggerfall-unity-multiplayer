namespace DFMP.Runtime
{
    public static class DFMPProtocol
    {
        // Bump whenever a replicated message changes shape. Mismatched builds are rejected at connect.
        public const int Version = 1;

        // Informational only; Version is what gates compatibility. Set per release build.
        public const string BuildId = "dev";

        public const int MaximumBuildIdLength = 64;

        public static string SanitizeBuildId(string buildId)
        {
            if (string.IsNullOrWhiteSpace(buildId))
                return "unknown";

            string trimmed = buildId.Trim();
            return trimmed.Length > MaximumBuildIdLength ? trimmed.Substring(0, MaximumBuildIdLength) : trimmed;
        }
    }
}

namespace DFMP.Runtime
{
    public enum DFMPRestAdvanceMode
    {
        TimedRest,
        FullRest,
        Loiter
    }

    public static class DFMPRestAdvancePolicy
    {
        public static bool TryParseMode(string restModeName, out DFMPRestAdvanceMode mode)
        {
            if (restModeName == "TimedRest")
            {
                mode = DFMPRestAdvanceMode.TimedRest;
                return true;
            }

            if (restModeName == "FullRest")
            {
                mode = DFMPRestAdvanceMode.FullRest;
                return true;
            }

            if (restModeName == "Loiter")
            {
                mode = DFMPRestAdvanceMode.Loiter;
                return true;
            }

            mode = DFMPRestAdvanceMode.TimedRest;
            return false;
        }

        public static bool ShouldConsumeRestAdvance(bool isMultiplayerClientConnected, DFMPRestAdvanceMode mode)
        {
            return ShouldConsumeRestAdvance(isMultiplayerClientConnected, mode, DFMPWorldSettings.CurrentRestPolicy);
        }

        public static bool ShouldConsumeRestAdvance(bool isMultiplayerClientConnected, DFMPRestAdvanceMode mode, string restPolicy)
        {
            if (!isMultiplayerClientConnected)
                return false;

            if (mode == DFMPRestAdvanceMode.Loiter)
                return true;

            if (mode == DFMPRestAdvanceMode.TimedRest || mode == DFMPRestAdvanceMode.FullRest)
                return !DFMPServerRestConfig.IsServerManaged(restPolicy);

            return false;
        }

        public static bool ShouldSkipRestWorldTime(bool isMultiplayerClientConnected)
        {
            return isMultiplayerClientConnected;
        }

        public static string GetBlockedRestMessage(DFMPRestAdvanceMode mode)
        {
            return mode == DFMPRestAdvanceMode.Loiter
                ? "Loitering is disabled in multiplayer."
                : "Resting is disabled on this server.";
        }
    }
}

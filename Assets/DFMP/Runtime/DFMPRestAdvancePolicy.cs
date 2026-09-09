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
            if (!isMultiplayerClientConnected)
                return false;

            return mode == DFMPRestAdvanceMode.TimedRest ||
                   mode == DFMPRestAdvanceMode.FullRest ||
                   mode == DFMPRestAdvanceMode.Loiter;
        }
    }
}

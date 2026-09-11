namespace DFMP.Runtime
{
    public static class DFMPVampirismTimePolicy
    {
        const int SecondsPerWeek = 604800;
        const int SecondsPerHour = 3600;

        public static bool TryGetTargetClassicMinutes(
            uint currentClassicMinutes,
            int currentHour,
            int duskHour,
            out uint targetClassicMinutes)
        {
            targetClassicMinutes = currentClassicMinutes;
            if (currentHour < 0 || currentHour > 23 || duskHour < 0 || duskHour > 23)
                return false;

            ulong raiseTimeSeconds = (ulong)(2 * SecondsPerWeek + (duskHour + 1 - currentHour) * SecondsPerHour);
            ulong raiseTimeMinutes = raiseTimeSeconds / 60;
            ulong target = currentClassicMinutes + raiseTimeMinutes;
            if (target > uint.MaxValue)
                return false;

            targetClassicMinutes = (uint)target;
            return true;
        }
    }
}
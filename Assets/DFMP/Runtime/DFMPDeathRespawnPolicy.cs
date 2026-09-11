namespace DFMP.Runtime
{
    public struct DFMPDeathRespawnContext
    {
        public bool HasSession;
        public bool SpawnConfirmed;
        public bool IsDead;
        public bool HasPendingTransition;
        public bool HasRespawnPosition;
    }

    public enum DFMPDeathRespawnRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        PlayerNotDead,
        TransitionAlreadyPending,
        MissingRespawnPosition
    }

    public static class DFMPDeathRespawnPolicy
    {
        public static bool ShouldInterceptClientDeath(bool isConnected, bool isReady, bool hasPendingSpawnAssignment, bool hasPendingTransition)
        {
            return isConnected && isReady && !hasPendingSpawnAssignment && !hasPendingTransition;
        }

        public static DFMPDeathRespawnRejectionReason GetRejectionReason(DFMPDeathRespawnContext context)
        {
            if (!context.HasSession)
                return DFMPDeathRespawnRejectionReason.MissingSession;

            if (!context.SpawnConfirmed)
                return DFMPDeathRespawnRejectionReason.SpawnNotConfirmed;

            if (!context.IsDead)
                return DFMPDeathRespawnRejectionReason.PlayerNotDead;

            if (context.HasPendingTransition)
                return DFMPDeathRespawnRejectionReason.TransitionAlreadyPending;

            if (!context.HasRespawnPosition)
                return DFMPDeathRespawnRejectionReason.MissingRespawnPosition;

            return DFMPDeathRespawnRejectionReason.None;
        }

        public static bool IsAccepted(DFMPDeathRespawnRejectionReason reason)
        {
            return reason == DFMPDeathRespawnRejectionReason.None;
        }
    }
}
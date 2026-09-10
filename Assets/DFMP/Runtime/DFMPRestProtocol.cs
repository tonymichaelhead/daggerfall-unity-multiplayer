using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPRestRequest : NetworkMessage
    {
        public string RestModeName;
    }

    public struct DFMPRestResponse : NetworkMessage
    {
        public bool Accepted;
        public DFMPRestRequestRejectionReason RejectionReason;
    }

    public struct DFMPRestRequestContext
    {
        public bool HasSession;
        public bool SpawnConfirmed;
        public bool IsMoving;
        public bool IsResting;
        public bool HasInterruption;
    }

    public enum DFMPRestRequestRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        Moving,
        AlreadyResting,
        Interrupted,
        UnsupportedMode,
        DisabledByServer,
        ServerManagedUnavailable
    }

    public static class DFMPRestProtocol
    {
        public static DFMPRestRequestRejectionReason GetRejectionReason(
            DFMPRestRequestContext context,
            string restModeName)
        {
            DFMPRestAdvanceMode mode;
            if (!DFMPRestAdvancePolicy.TryParseMode(restModeName, out mode))
                return DFMPRestRequestRejectionReason.UnsupportedMode;

            return GetContextRejectionReason(context);
        }

        public static DFMPRestRequestRejectionReason GetRejectionReason(
            DFMPRestRequestContext context,
            string restModeName,
            string restPolicy)
        {
            DFMPRestAdvanceMode mode;
            if (!DFMPRestAdvancePolicy.TryParseMode(restModeName, out mode))
                return DFMPRestRequestRejectionReason.UnsupportedMode;

            if (string.Equals(restPolicy, DFMPRestPolicies.ServerManaged, System.StringComparison.OrdinalIgnoreCase))
                 return DFMPRestRequestRejectionReason.ServerManagedUnavailable;

            if (string.Equals(restPolicy, DFMPRestPolicies.Disabled, System.StringComparison.OrdinalIgnoreCase))
                return DFMPRestRequestRejectionReason.DisabledByServer;

            return DFMPRestRequestRejectionReason.DisabledByServer;
        }

        static DFMPRestRequestRejectionReason GetContextRejectionReason(DFMPRestRequestContext context)
        {
            if (!context.HasSession)
                return DFMPRestRequestRejectionReason.MissingSession;

            if (!context.SpawnConfirmed)
                return DFMPRestRequestRejectionReason.SpawnNotConfirmed;

            if (context.IsMoving)
                return DFMPRestRequestRejectionReason.Moving;

            if (context.IsResting)
                return DFMPRestRequestRejectionReason.AlreadyResting;

            if (context.HasInterruption)
                return DFMPRestRequestRejectionReason.Interrupted;

            return DFMPRestRequestRejectionReason.None;
        }

        public static bool IsAccepted(DFMPRestRequestRejectionReason reason)
        {
            return reason == DFMPRestRequestRejectionReason.None;
        }

        public static bool ShouldInterruptOnMovement(bool isResting, bool isMoving)
        {
            return isResting && isMoving;
        }
    }
}

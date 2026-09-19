using Mirror;

namespace DFMP.Runtime
{
    public enum DFMPRestRequestKind
    {
        Start = 0,
        HourElapsed = 1,
        Stop = 2
    }

    public struct DFMPRestRequest : NetworkMessage
    {
        public string RestModeName;
        public DFMPRestRequestKind Kind;
    }

    public struct DFMPRestResponse : NetworkMessage
    {
        public bool Accepted;
        public DFMPRestRequestKind Kind;
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
        LoiterDisabled,
        NotResting,
        RateLimited
    }

    public static class DFMPRestProtocol
    {
        public const float MinimumRestHourIntervalSeconds = 0.5f;

        public static DFMPRestRequestRejectionReason GetRejectionReason(
            DFMPRestRequestContext context,
            string restModeName)
        {
            return GetRejectionReason(context, restModeName, DFMPRestPolicies.ServerManaged, DFMPRestRequestKind.Start);
        }

        public static DFMPRestRequestRejectionReason GetRejectionReason(
            DFMPRestRequestContext context,
            string restModeName,
            string restPolicy)
        {
            return GetRejectionReason(context, restModeName, restPolicy, DFMPRestRequestKind.Start);
        }

        public static DFMPRestRequestRejectionReason GetRejectionReason(
            DFMPRestRequestContext context,
            string restModeName,
            string restPolicy,
            DFMPRestRequestKind kind)
        {
            DFMPRestAdvanceMode mode;
            if (!DFMPRestAdvancePolicy.TryParseMode(restModeName, out mode))
                return DFMPRestRequestRejectionReason.UnsupportedMode;

            if (kind == DFMPRestRequestKind.Stop)
            {
                if (!context.HasSession)
                    return DFMPRestRequestRejectionReason.MissingSession;

                return DFMPRestRequestRejectionReason.None;
            }

            if (mode == DFMPRestAdvanceMode.Loiter)
                return DFMPRestRequestRejectionReason.LoiterDisabled;

            if (!DFMPServerRestConfig.IsServerManaged(restPolicy))
                return DFMPRestRequestRejectionReason.DisabledByServer;

            if (kind == DFMPRestRequestKind.HourElapsed)
                return GetHourElapsedRejectionReason(context);

            return GetStartRejectionReason(context);
        }

        static DFMPRestRequestRejectionReason GetStartRejectionReason(DFMPRestRequestContext context)
        {
            DFMPRestRequestRejectionReason sessionReason = GetSessionRejectionReason(context);
            if (sessionReason != DFMPRestRequestRejectionReason.None)
                return sessionReason;

            if (context.IsMoving)
                return DFMPRestRequestRejectionReason.Moving;

            if (context.IsResting)
                return DFMPRestRequestRejectionReason.AlreadyResting;

            if (context.HasInterruption)
                return DFMPRestRequestRejectionReason.Interrupted;

            return DFMPRestRequestRejectionReason.None;
        }

        static DFMPRestRequestRejectionReason GetHourElapsedRejectionReason(DFMPRestRequestContext context)
        {
            DFMPRestRequestRejectionReason sessionReason = GetSessionRejectionReason(context);
            if (sessionReason != DFMPRestRequestRejectionReason.None)
                return sessionReason;

            if (!context.IsResting)
                return DFMPRestRequestRejectionReason.NotResting;

            if (context.IsMoving)
                return DFMPRestRequestRejectionReason.Moving;

            if (context.HasInterruption)
                return DFMPRestRequestRejectionReason.Interrupted;

            return DFMPRestRequestRejectionReason.None;
        }

        static DFMPRestRequestRejectionReason GetSessionRejectionReason(DFMPRestRequestContext context)
        {
            if (!context.HasSession)
                return DFMPRestRequestRejectionReason.MissingSession;

            if (!context.SpawnConfirmed)
                return DFMPRestRequestRejectionReason.SpawnNotConfirmed;

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

        public static bool IsHourTickTooSoon(float elapsedSecondsSinceLastHour)
        {
            return elapsedSecondsSinceLastHour >= 0f && elapsedSecondsSinceLastHour < MinimumRestHourIntervalSeconds;
        }
    }
}

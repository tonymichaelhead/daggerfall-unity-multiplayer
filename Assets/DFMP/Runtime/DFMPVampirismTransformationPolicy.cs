namespace DFMP.Runtime
{
    public struct DFMPVampirismTransformationContext
    {
        public bool HasSession;
        public bool SpawnConfirmed;
        public bool HasPendingTransition;
        public bool HasWorldContext;
        public int RegionIndex;
    }

    public enum DFMPVampirismTransformationRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        TransitionAlreadyPending,
        MissingWorldContext,
        InvalidRegion,
        CemeteryUnavailable,
        TimeUnavailable
    }

    public enum DFMPVampirismTransformationAction
    {
        AllowVanilla,
        DeferToServerTransition
    }

    public static class DFMPVampirismTransformationPolicy
    {
        public static DFMPVampirismTransformationAction GetAction(bool isMultiplayerClientConnected)
        {
            return isMultiplayerClientConnected
                ? DFMPVampirismTransformationAction.DeferToServerTransition
                : DFMPVampirismTransformationAction.AllowVanilla;
        }

        public static DFMPVampirismTransformationRejectionReason GetRejectionReason(DFMPVampirismTransformationContext context)
        {
            if (!context.HasSession)
                return DFMPVampirismTransformationRejectionReason.MissingSession;
            if (!context.SpawnConfirmed)
                return DFMPVampirismTransformationRejectionReason.SpawnNotConfirmed;
            if (context.HasPendingTransition)
                return DFMPVampirismTransformationRejectionReason.TransitionAlreadyPending;
            if (!context.HasWorldContext)
                return DFMPVampirismTransformationRejectionReason.MissingWorldContext;
            if (context.RegionIndex < 0)
                return DFMPVampirismTransformationRejectionReason.InvalidRegion;

            return DFMPVampirismTransformationRejectionReason.None;
        }

        public static bool IsAccepted(DFMPVampirismTransformationRejectionReason reason)
        {
            return reason == DFMPVampirismTransformationRejectionReason.None;
        }
    }
}
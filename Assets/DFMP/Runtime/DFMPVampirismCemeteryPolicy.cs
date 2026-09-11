using System.Collections.Generic;

namespace DFMP.Runtime
{
    public struct DFMPVampirismCemeteryCandidate
    {
        public string LocationId;
        public DFMPWorldPosition Position;
        public DFMPWorldContextKey Context;
    }

    public enum DFMPVampirismCemeteryRejectionReason
    {
        None,
        NoCandidates,
        InvalidSelection,
        InvalidDestination
    }

    public static class DFMPVampirismCemeteryPolicy
    {
        public static bool TrySelectCandidate(
            IList<DFMPVampirismCemeteryCandidate> candidates,
            int selectionIndex,
            out DFMPVampirismCemeteryCandidate candidate,
            out DFMPVampirismCemeteryRejectionReason rejectionReason)
        {
            candidate = default(DFMPVampirismCemeteryCandidate);
            rejectionReason = DFMPVampirismCemeteryRejectionReason.None;

            if (candidates == null || candidates.Count == 0)
            {
                rejectionReason = DFMPVampirismCemeteryRejectionReason.NoCandidates;
                return false;
            }

            if (selectionIndex < 0 || selectionIndex >= candidates.Count)
            {
                rejectionReason = DFMPVampirismCemeteryRejectionReason.InvalidSelection;
                return false;
            }

            candidate = candidates[selectionIndex];
            if (string.IsNullOrWhiteSpace(candidate.LocationId) ||
                candidate.Context.Kind != DFMPWorldContextKind.Exterior ||
                !DFMPSpawnProtocol.IsValidMapPixel(candidate.Context.MapPixelX, candidate.Context.MapPixelY) ||
                candidate.Context.RegionIndex < 0 ||
                candidate.Context.LocationIndex < 0)
            {
                candidate = default(DFMPVampirismCemeteryCandidate);
                rejectionReason = DFMPVampirismCemeteryRejectionReason.InvalidDestination;
                return false;
            }

            return true;
        }
    }
}
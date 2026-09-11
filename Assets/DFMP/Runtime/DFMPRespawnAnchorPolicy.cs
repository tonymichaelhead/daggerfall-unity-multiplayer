using DaggerfallConnect;

namespace DFMP.Runtime
{
    public enum DFMPRespawnAnchorKind
    {
        None,
        Inn,
        StartingLocation
    }

    public struct DFMPRespawnAnchorContext
    {
        public bool HasInnAnchor;
        public bool HasStartingLocation;
    }

    public static class DFMPRespawnAnchorPolicy
    {
        public static bool IsInnBuildingType(int buildingType)
        {
            return buildingType == (int)DFLocation.BuildingTypes.Tavern;
        }

        public static DFMPRespawnAnchorKind ResolveDefaultAnchor(DFMPRespawnAnchorContext context)
        {
            if (context.HasInnAnchor)
                return DFMPRespawnAnchorKind.Inn;
            if (context.HasStartingLocation)
                return DFMPRespawnAnchorKind.StartingLocation;

            return DFMPRespawnAnchorKind.None;
        }
    }
}
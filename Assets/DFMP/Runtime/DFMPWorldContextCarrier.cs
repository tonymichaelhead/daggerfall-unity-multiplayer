using Mirror;

namespace DFMP.Runtime
{
    public sealed class DFMPWorldContextCarrier : NetworkBehaviour
    {
        [SyncVar]
        int contextKind;

        [SyncVar]
        int mapPixelX;

        [SyncVar]
        int mapPixelY;

        [SyncVar]
        int regionIndex;

        [SyncVar]
        int locationIndex;

        [SyncVar]
        string locationId;

        [SyncVar]
        int buildingKey;

        [SyncVar]
        int dungeonBlockIndex;

        [SyncVar]
        string dungeonBlockName;

        [SyncVar]
        string instanceId;

        public DFMPWorldContextKey Context
        {
            get
            {
                return new DFMPWorldContextKey
                {
                    Kind = (DFMPWorldContextKind)contextKind,
                    MapPixelX = mapPixelX,
                    MapPixelY = mapPixelY,
                    RegionIndex = regionIndex,
                    LocationIndex = locationIndex,
                    LocationId = locationId,
                    BuildingKey = buildingKey,
                    DungeonBlockIndex = dungeonBlockIndex,
                    DungeonBlockName = dungeonBlockName,
                    InstanceId = instanceId
                };
            }
        }

        public void Initialize(DFMPWorldContextKey context)
        {
            contextKind = (int)context.Kind;
            mapPixelX = context.MapPixelX;
            mapPixelY = context.MapPixelY;
            regionIndex = context.RegionIndex;
            locationIndex = context.LocationIndex;
            locationId = context.LocationId ?? string.Empty;
            buildingKey = context.BuildingKey;
            dungeonBlockIndex = context.DungeonBlockIndex;
            dungeonBlockName = context.DungeonBlockName ?? string.Empty;
            instanceId = context.InstanceId ?? string.Empty;
        }
    }
}

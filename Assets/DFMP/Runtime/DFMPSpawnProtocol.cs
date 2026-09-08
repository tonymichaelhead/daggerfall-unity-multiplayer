using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPWorldPosition
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
    }

    public struct DFMPStartingLocationResolution
    {
        public DFMPWorldPosition Position;
        public DFMPWorldContextKey Context;
    }

    public class DFMPSpawnAssignmentState
    {
        public DFMPSpawnAssignment Assignment { get; private set; }
        public bool HasAssignment { get; private set; }
        public bool HasPendingAssignment { get; private set; }
        public bool TeleportRequested { get; private set; }

        public void Receive(DFMPSpawnAssignment assignment)
        {
            Assignment = assignment;
            HasAssignment = true;
            HasPendingAssignment = true;
            TeleportRequested = false;
        }

        public void Requeue()
        {
            if (!HasAssignment)
                return;

            HasPendingAssignment = true;
            TeleportRequested = false;
        }

        public bool TryRequestTeleport()
        {
            if (!HasPendingAssignment || TeleportRequested)
                return false;

            TeleportRequested = true;
            return true;
        }

        public bool TryAcknowledge(bool repositioning, int mapPixelX, int mapPixelY)
        {
            if (!HasPendingAssignment || !TeleportRequested || repositioning || mapPixelX != Assignment.MapPixelX || mapPixelY != Assignment.MapPixelY)
                return false;

            HasPendingAssignment = false;
            TeleportRequested = false;
            return true;
        }

        public void Reset()
        {
            Assignment = default(DFMPSpawnAssignment);
            HasAssignment = false;
            HasPendingAssignment = false;
            TeleportRequested = false;
        }
    }

    public static class DFMPSpawnProtocol
    {
        public const int WorldMapPixelDimension = 32768;
        public const int WorldMapHeightInPixels = 500;

        public static DFMPWorldPosition GetLocationCenter(Rect locationRect)
        {
            return new DFMPWorldPosition
            {
                WorldX = Mathf.RoundToInt(locationRect.center.x),
                WorldY = 0f,
                WorldZ = Mathf.RoundToInt(locationRect.center.y)
            };
        }

        public static bool IsWithinMapPixel(int worldX, int worldZ, int mapPixelX, int mapPixelY)
        {
            return worldX / WorldMapPixelDimension == mapPixelX &&
                WorldMapHeightInPixels - 1 - (worldZ / WorldMapPixelDimension) == mapPixelY;
        }

        public static bool TryConfirmSpawn(DFMPPlayerSessionState sessionState, DFMPSpawnAcknowledgement acknowledgement)
        {
            if (sessionState == null)
                return false;

            int expectedMapPixelX = sessionState.WorldX / WorldMapPixelDimension;
            int expectedMapPixelY = WorldMapHeightInPixels - 1 - (sessionState.WorldZ / WorldMapPixelDimension);
            if (!IsWithinMapPixel(acknowledgement.WorldX, acknowledgement.WorldZ, expectedMapPixelX, expectedMapPixelY))
                return false;

            sessionState.SetPosition(acknowledgement.WorldX, acknowledgement.WorldY, acknowledgement.WorldZ);
            sessionState.ConfirmSpawn();
            sessionState.SetMovement(false);
            return true;
        }

        public static bool TryResolveStartingLocation(DFMPServerStartingLocationConfig config, out DFMPStartingLocationResolution resolution, out string reason)
        {
            resolution = new DFMPStartingLocationResolution();
            reason = string.Empty;

            if (config == null)
                config = new DFMPServerStartingLocationConfig();

            config.Normalize();
            if (config.Mode == DFMPStartingLocationModes.ExplicitWorldCoordinates)
                return TryResolveExplicitWorldCoordinates(config, out resolution, out reason);

            if (config.Mode == DFMPStartingLocationModes.NamedStartMarker)
            {
                reason = "named start markers require the transition assignment protocol to carry marker identity";
                return false;
            }

            if (config.Mode == DFMPStartingLocationModes.Scripted)
            {
                reason = "scripted starting locations are reserved for a later scripting milestone";
                return false;
            }

            return TryResolveLocationCenter(config.RegionName, config.LocationName, out resolution, out reason);
        }

        public static bool TryResolveExplicitWorldCoordinates(DFMPServerStartingLocationConfig config, out DFMPStartingLocationResolution resolution, out string reason)
        {
            resolution = new DFMPStartingLocationResolution();
            reason = string.Empty;

            if (config == null)
            {
                reason = "starting-location config is unavailable";
                return false;
            }

            if (config.WorldX == 0 && config.WorldZ == 0)
            {
                reason = "explicit world coordinates must not be the world origin";
                return false;
            }

            DFPosition mapPixel = MapsFile.WorldCoordToMapPixel(config.WorldX, config.WorldZ);
            resolution = new DFMPStartingLocationResolution
            {
                Position = new DFMPWorldPosition
                {
                    WorldX = config.WorldX,
                    WorldY = config.WorldY,
                    WorldZ = config.WorldZ
                },
                Context = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = mapPixel.X,
                    MapPixelY = mapPixel.Y,
                    LocationId = config.LocationName
                }
            };
            return true;
        }

        public static bool TryResolveLocationCenter(string regionName, string locationName, out DFMPStartingLocationResolution resolution, out string reason)
        {
            resolution = new DFMPStartingLocationResolution();
            reason = string.Empty;

            if (DaggerfallUnity.Instance == null || DaggerfallUnity.Instance.ContentReader == null || DaggerfallUnity.Instance.ContentReader.MapFileReader == null)
            {
                reason = "Daggerfall location data is unavailable";
                return false;
            }

            DFLocation location = DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation(regionName, locationName);
            if (!location.Loaded)
            {
                reason = $"location '{regionName}/{locationName}' was not found in MAPS.BSA";
                return false;
            }

            DFPosition mapPixel = MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude, location.MapTableData.Latitude);
            Rect locationRect = DaggerfallLocation.GetLocationRect(location);
            DFMPWorldPosition spawnPosition = GetLocationCenter(locationRect);
            resolution = new DFMPStartingLocationResolution
            {
                Position = spawnPosition,
                Context = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = mapPixel.X,
                    MapPixelY = mapPixel.Y,
                    RegionIndex = location.RegionIndex,
                    LocationIndex = location.LocationIndex,
                    LocationId = location.Name
                }
            };
            return true;
        }
    }
}
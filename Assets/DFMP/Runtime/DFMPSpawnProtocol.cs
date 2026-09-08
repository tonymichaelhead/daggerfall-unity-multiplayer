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
        public string StartMarkerName;
    }

    public enum DFMPTransitionKind
    {
        InitialSpawn,
        FastTravel,
        Door,
        DungeonEntry,
        DungeonExit,
        DeathRespawn,
        Reconnect,
        SaveLoad
    }

    public struct DFMPTransitionAssignment : Mirror.NetworkMessage
    {
        public int AssignmentId;
        public int ConnectionId;
        public DFMPTransitionKind Kind;
        public int MapPixelX;
        public int MapPixelY;
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public string StartMarkerName;
        public DFMPWorldContextKind ContextKind;
        public int RegionIndex;
        public int LocationIndex;
        public string LocationId;
        public int BuildingKey;
        public int DungeonBlockIndex;
        public string DungeonBlockName;
        public string InstanceId;
    }

    public struct DFMPTransitionAcknowledgement : Mirror.NetworkMessage
    {
        public int AssignmentId;
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public DFMPWorldContextReport Context;
    }

    public struct DFMPFastTravelRequest : Mirror.NetworkMessage
    {
        public int MapPixelX;
        public int MapPixelY;
    }

    public struct DFMPDoorTransitionRequest : Mirror.NetworkMessage
    {
        public bool EnterInterior;
        public int MapPixelX;
        public int MapPixelY;
        public int RegionIndex;
        public int LocationIndex;
        public string LocationId;
        public int BuildingKey;
    }

    public enum DFMPDoorTransitionRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        InvalidMapPixel,
        MissingLocation,
        MissingBuildingKey,
        ContextMismatch
    }

    public enum DFMPTransitionAcknowledgeRejectionReason
    {
        None,
        MissingAssignment,
        AssignmentMismatch,
        TeleportNotRequested,
        Repositioning,
        OutsideAssignedMapPixel,
        ContextMismatch
    }

    public class DFMPTransitionAssignmentState
    {
        public DFMPTransitionAssignment Assignment { get; private set; }
        public bool HasAssignment { get; private set; }
        public bool HasPendingAssignment { get; private set; }
        public bool TeleportRequested { get; private set; }

        public void Receive(DFMPTransitionAssignment assignment)
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

        public DFMPTransitionAcknowledgeRejectionReason GetAcknowledgeRejectionReason(DFMPTransitionAcknowledgement acknowledgement, bool repositioning)
        {
            if (!HasPendingAssignment)
                return DFMPTransitionAcknowledgeRejectionReason.MissingAssignment;
            if (acknowledgement.AssignmentId != Assignment.AssignmentId)
                return DFMPTransitionAcknowledgeRejectionReason.AssignmentMismatch;
            if (!TeleportRequested)
                return DFMPTransitionAcknowledgeRejectionReason.TeleportNotRequested;
            if (repositioning)
                return DFMPTransitionAcknowledgeRejectionReason.Repositioning;
            if (!DFMPSpawnProtocol.IsWithinMapPixel(acknowledgement.WorldX, acknowledgement.WorldZ, Assignment.MapPixelX, Assignment.MapPixelY))
                return DFMPTransitionAcknowledgeRejectionReason.OutsideAssignedMapPixel;

            DFMPWorldContextKey expectedContext = DFMPSpawnProtocol.GetAssignedContext(Assignment);
            DFMPWorldContextKey acknowledgedContext;
            DFMPWorldContextProtocol.TryCreateKey(acknowledgement.Context, out acknowledgedContext);
            if (!expectedContext.Equals(acknowledgedContext))
                return DFMPTransitionAcknowledgeRejectionReason.ContextMismatch;

            return DFMPTransitionAcknowledgeRejectionReason.None;
        }

        public bool TryAcknowledge(DFMPTransitionAcknowledgement acknowledgement, bool repositioning)
        {
            if (GetAcknowledgeRejectionReason(acknowledgement, repositioning) != DFMPTransitionAcknowledgeRejectionReason.None)
                return false;

            HasPendingAssignment = false;
            TeleportRequested = false;
            return true;
        }

        public void Reset()
        {
            Assignment = default(DFMPTransitionAssignment);
            HasAssignment = false;
            HasPendingAssignment = false;
            TeleportRequested = false;
        }
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

        public static bool IsValidMapPixel(int mapPixelX, int mapPixelY)
        {
            return mapPixelX >= DFMPPositionProtocol.MinimumMapPixelX && mapPixelX <= DFMPPositionProtocol.MaximumMapPixelX &&
                mapPixelY >= DFMPPositionProtocol.MinimumMapPixelY && mapPixelY <= DFMPPositionProtocol.MaximumMapPixelY;
        }

        public static DFMPWorldPosition GetMapPixelCenter(int mapPixelX, int mapPixelY)
        {
            return new DFMPWorldPosition
            {
                WorldX = mapPixelX * WorldMapPixelDimension + WorldMapPixelDimension / 2,
                WorldY = 0f,
                WorldZ = (WorldMapHeightInPixels - 1 - mapPixelY) * WorldMapPixelDimension + WorldMapPixelDimension / 2
            };
        }

        public static DFMPDoorTransitionRejectionReason GetDoorTransitionRejectionReason(DFMPPlayerSessionState sessionState, DFMPWorldContextKey currentContext, bool hasCurrentContext, DFMPDoorTransitionRequest request)
        {
            if (sessionState == null)
                return DFMPDoorTransitionRejectionReason.MissingSession;
            if (!sessionState.SpawnConfirmed)
                return DFMPDoorTransitionRejectionReason.SpawnNotConfirmed;
            if (!IsValidMapPixel(request.MapPixelX, request.MapPixelY))
                return DFMPDoorTransitionRejectionReason.InvalidMapPixel;
            if (string.IsNullOrWhiteSpace(request.LocationId))
                return DFMPDoorTransitionRejectionReason.MissingLocation;
            if (request.BuildingKey <= 0)
                return DFMPDoorTransitionRejectionReason.MissingBuildingKey;
            if (!hasCurrentContext)
                return DFMPDoorTransitionRejectionReason.ContextMismatch;

            if (request.EnterInterior)
            {
                if (currentContext.Kind != DFMPWorldContextKind.Exterior || currentContext.MapPixelX != request.MapPixelX || currentContext.MapPixelY != request.MapPixelY)
                    return DFMPDoorTransitionRejectionReason.ContextMismatch;
            }
            else
            {
                if (currentContext.Kind != DFMPWorldContextKind.BuildingInterior || currentContext.BuildingKey != request.BuildingKey || currentContext.MapPixelX != request.MapPixelX || currentContext.MapPixelY != request.MapPixelY)
                    return DFMPDoorTransitionRejectionReason.ContextMismatch;
            }

            return DFMPDoorTransitionRejectionReason.None;
        }

        public static DFMPWorldContextKey GetDoorTransitionAssignedContext(DFMPDoorTransitionRequest request)
        {
            return new DFMPWorldContextKey
            {
                Kind = request.EnterInterior ? DFMPWorldContextKind.BuildingInterior : DFMPWorldContextKind.Exterior,
                MapPixelX = request.MapPixelX,
                MapPixelY = request.MapPixelY,
                RegionIndex = request.RegionIndex,
                LocationIndex = request.LocationIndex,
                LocationId = request.LocationId ?? string.Empty,
                BuildingKey = request.EnterInterior ? request.BuildingKey : 0
            };
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

        public static bool TryConfirmTransition(DFMPPlayerSessionState sessionState, DFMPTransitionAssignmentState assignmentState, DFMPTransitionAcknowledgement acknowledgement, bool repositioning, out DFMPTransitionAcknowledgeRejectionReason rejectionReason)
        {
            rejectionReason = DFMPTransitionAcknowledgeRejectionReason.MissingAssignment;
            if (sessionState == null || assignmentState == null)
                return false;

            rejectionReason = assignmentState.GetAcknowledgeRejectionReason(acknowledgement, repositioning);
            if (rejectionReason != DFMPTransitionAcknowledgeRejectionReason.None)
                return false;

            if (!assignmentState.TryAcknowledge(acknowledgement, repositioning))
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
                if (string.IsNullOrWhiteSpace(config.MarkerName))
                {
                    reason = "named start marker mode requires a marker name or selector";
                    return false;
                }

                if (!TryResolveLocationCenter(config.RegionName, config.LocationName, out resolution, out reason))
                    return false;

                resolution.StartMarkerName = config.MarkerName;
                return true;
            }

            if (config.Mode == DFMPStartingLocationModes.Scripted)
            {
                reason = "scripted starting locations are reserved for a later scripting milestone";
                return false;
            }

            return TryResolveLocationCenter(config.RegionName, config.LocationName, out resolution, out reason);
        }

        public static DFMPTransitionAssignment CreateTransitionAssignment(int assignmentId, DFMPTransitionKind kind, DFMPWorldPosition position, DFMPWorldContextKey context, string startMarkerName = null)
        {
            return CreateTransitionAssignment(assignmentId, -1, kind, position, context, startMarkerName);
        }

        public static DFMPTransitionAssignment CreateTransitionAssignment(int assignmentId, int connectionId, DFMPTransitionKind kind, DFMPWorldPosition position, DFMPWorldContextKey context, string startMarkerName = null)
        {
            return new DFMPTransitionAssignment
            {
                AssignmentId = assignmentId,
                ConnectionId = connectionId,
                Kind = kind,
                MapPixelX = context.MapPixelX,
                MapPixelY = context.MapPixelY,
                WorldX = position.WorldX,
                WorldY = position.WorldY,
                WorldZ = position.WorldZ,
                StartMarkerName = startMarkerName ?? string.Empty,
                ContextKind = context.Kind,
                RegionIndex = context.RegionIndex,
                LocationIndex = context.LocationIndex,
                LocationId = context.LocationId ?? string.Empty,
                BuildingKey = context.BuildingKey,
                DungeonBlockIndex = context.DungeonBlockIndex,
                DungeonBlockName = context.DungeonBlockName ?? string.Empty,
                InstanceId = context.InstanceId ?? string.Empty
            };
        }

        public static DFMPWorldContextKey GetAssignedContext(DFMPTransitionAssignment assignment)
        {
            var record = new DFMPWorldContextRecord
            {
                Kind = assignment.ContextKind.ToString(),
                MapPixelX = assignment.MapPixelX,
                MapPixelY = assignment.MapPixelY,
                RegionIndex = assignment.RegionIndex,
                LocationIndex = assignment.LocationIndex,
                LocationId = assignment.LocationId ?? string.Empty,
                BuildingKey = assignment.BuildingKey,
                DungeonBlockIndex = assignment.DungeonBlockIndex,
                DungeonBlockName = assignment.DungeonBlockName ?? string.Empty,
                InstanceId = assignment.InstanceId ?? string.Empty
            };

            return record.ToKey();
        }

        public static DFMPWorldContextReport GetAssignedContextReport(DFMPTransitionAssignment assignment)
        {
            return new DFMPWorldContextReport
            {
                Kind = assignment.ContextKind,
                MapPixelX = assignment.MapPixelX,
                MapPixelY = assignment.MapPixelY,
                RegionIndex = assignment.RegionIndex,
                LocationIndex = assignment.LocationIndex,
                LocationId = assignment.LocationId ?? string.Empty,
                BuildingKey = assignment.BuildingKey,
                DungeonBlockIndex = assignment.DungeonBlockIndex,
                DungeonBlockName = assignment.DungeonBlockName ?? string.Empty,
                InstanceId = assignment.InstanceId ?? string.Empty
            };
        }

        public static bool TrySelectStartMarker(Vector3[] markerPositions, string markerName, out int markerIndex)
        {
            markerIndex = -1;
            if (markerPositions == null || markerPositions.Length == 0)
                return false;

            string selector = string.IsNullOrWhiteSpace(markerName) ? "first" : markerName.Trim().ToLowerInvariant();
            bool preferMaxX = selector == "southeast" || selector == "northeast" || selector == "east";
            bool preferMaxZ = selector == "northwest" || selector == "northeast" || selector == "north";
            bool preferMinX = selector == "first" || selector == "southwest" || selector == "northwest" || selector == "west";
            bool preferMinZ = selector == "first" || selector == "southwest" || selector == "southeast" || selector == "south";

            if (!preferMaxX && !preferMaxZ && !preferMinX && !preferMinZ)
                return false;

            for (int index = 0; index < markerPositions.Length; index++)
            {
                if (markerIndex < 0 || IsPreferredMarker(markerPositions[index], markerPositions[markerIndex], preferMaxX, preferMaxZ, preferMinX, preferMinZ))
                    markerIndex = index;
            }

            return markerIndex >= 0;
        }

        static bool IsPreferredMarker(Vector3 candidate, Vector3 current, bool preferMaxX, bool preferMaxZ, bool preferMinX, bool preferMinZ)
        {
            if (preferMaxX && !Mathf.Approximately(candidate.x, current.x))
                return candidate.x > current.x;
            if (preferMinX && !Mathf.Approximately(candidate.x, current.x))
                return candidate.x < current.x;
            if (preferMaxZ && !Mathf.Approximately(candidate.z, current.z))
                return candidate.z > current.z;
            if (preferMinZ && !Mathf.Approximately(candidate.z, current.z))
                return candidate.z < current.z;

            return false;
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
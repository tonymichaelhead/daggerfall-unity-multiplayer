using UnityEngine;

namespace DFCoop.Runtime
{
    public struct DFCoopWorldPosition
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
    }

    public class DFCoopSpawnAssignmentState
    {
        public DFCoopSpawnAssignment Assignment { get; private set; }
        public bool HasAssignment { get; private set; }
        public bool HasPendingAssignment { get; private set; }
        public bool TeleportRequested { get; private set; }

        public void Receive(DFCoopSpawnAssignment assignment)
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
            Assignment = default(DFCoopSpawnAssignment);
            HasAssignment = false;
            HasPendingAssignment = false;
            TeleportRequested = false;
        }
    }

    public static class DFCoopSpawnProtocol
    {
        public const int WorldMapPixelDimension = 32768;
        public const int WorldMapHeightInPixels = 500;

        public static DFCoopWorldPosition GetLocationCenter(Rect locationRect)
        {
            return new DFCoopWorldPosition
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

        public static bool TryConfirmSpawn(DFCoopPlayerSessionState sessionState, DFCoopSpawnAcknowledgement acknowledgement)
        {
            if (sessionState == null)
                return false;

            int expectedMapPixelX = sessionState.WorldX / WorldMapPixelDimension;
            int expectedMapPixelY = WorldMapHeightInPixels - 1 - (sessionState.WorldZ / WorldMapPixelDimension);
            if (!IsWithinMapPixel(acknowledgement.WorldX, acknowledgement.WorldZ, expectedMapPixelX, expectedMapPixelY))
                return false;

            sessionState.SetPosition(acknowledgement.WorldX, acknowledgement.WorldY, acknowledgement.WorldZ);
            sessionState.ConfirmSpawn();
            return true;
        }
    }
}
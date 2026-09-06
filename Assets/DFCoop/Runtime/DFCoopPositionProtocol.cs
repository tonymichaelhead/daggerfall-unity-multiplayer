using UnityEngine;

namespace DFCoop.Runtime
{
    public struct DFCoopPlayerPositionReport : Mirror.NetworkMessage
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
    }

    public static class DFCoopPositionProtocol
    {
        public const float MinimumReportInterval = 0.1f;
        public const float MaximumSpeed = 4096f;
        public const int MinimumMapPixelX = 3;
        public const int MaximumMapPixelX = 998;
        public const int MinimumMapPixelY = 3;
        public const int MaximumMapPixelY = 498;

        public static bool IsValidWorldPosition(DFCoopPlayerPositionReport report)
        {
            if (float.IsNaN(report.WorldY) || float.IsInfinity(report.WorldY))
                return false;

            int mapPixelX = report.WorldX / DFCoopSpawnProtocol.WorldMapPixelDimension;
            int mapPixelY = DFCoopSpawnProtocol.WorldMapHeightInPixels - 1 - (report.WorldZ / DFCoopSpawnProtocol.WorldMapPixelDimension);
            return mapPixelX >= MinimumMapPixelX && mapPixelX <= MaximumMapPixelX &&
                mapPixelY >= MinimumMapPixelY && mapPixelY <= MaximumMapPixelY;
        }

        public static bool IsAccepted(DFCoopPlayerSessionState sessionState, DFCoopPlayerPositionReport report, float elapsedSeconds)
        {
            if (sessionState == null || !sessionState.SpawnConfirmed || elapsedSeconds < MinimumReportInterval || !IsValidWorldPosition(report))
                return false;

            float maxDistance = MaximumSpeed * elapsedSeconds;
            float deltaX = report.WorldX - sessionState.WorldX;
            float deltaZ = report.WorldZ - sessionState.WorldZ;
            return deltaX * deltaX + deltaZ * deltaZ <= maxDistance * maxDistance;
        }
    }
}
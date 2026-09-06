using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPPlayerPositionReport : Mirror.NetworkMessage
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public float FacingYaw;
    }

    public struct DFMPPlayerIdentityReport : Mirror.NetworkMessage
    {
        public string DisplayName;
        public int Race;
        public int Gender;
        public int OutfitVariant;
        public int FaceVariant;
    }

    public static class DFMPPositionProtocol
    {
        public const float MinimumReportInterval = 0.1f;
        // Reports routinely arrive slightly early because of send/receive jitter, so only clear flooding is rejected.
        public const float MinimumAcceptedReportInterval = MinimumReportInterval * 0.5f;
        public const float MaximumSpeed = 4096f;
        public const int MinimumMapPixelX = 3;
        public const int MaximumMapPixelX = 998;
        public const int MinimumMapPixelY = 3;
        public const int MaximumMapPixelY = 498;
        public const int MaximumDisplayNameLength = 24;
        public const int OutfitVariantCount = 4;
        public const int FaceVariantCount = 24;

        public static int GetDisplayRace(int race)
        {
            return race == 1 || race == 2 || race == 3 ? race : 1;
        }

        public static int GetDisplayGender(int gender)
        {
            return gender == 1 ? 1 : 0;
        }

        public static int GetOutfitVariant(int outfitVariant)
        {
            return Mathf.Clamp(outfitVariant, 0, OutfitVariantCount - 1);
        }

        public static int GetFaceVariant(int faceVariant)
        {
            return Mathf.Clamp(faceVariant, 0, FaceVariantCount - 1);
        }

        public static int GetInitialOutfitVariant(int faceVariant)
        {
            return Mathf.Abs(faceVariant) % OutfitVariantCount;
        }

        public static string SanitizeDisplayName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return "Player";

            var sanitized = new System.Text.StringBuilder(MaximumDisplayNameLength);
            foreach (char character in displayName.Trim())
            {
                if ((char.IsLetterOrDigit(character) || character == ' ' || character == '-' || character == '_') && sanitized.Length < MaximumDisplayNameLength)
                    sanitized.Append(character);
            }

            string result = sanitized.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "Player" : result;
        }

        public static bool IsValidFacingYaw(float facingYaw)
        {
            return !float.IsNaN(facingYaw) && !float.IsInfinity(facingYaw);
        }

        public static float NormalizeFacingYaw(float facingYaw)
        {
            float normalizedYaw = facingYaw % 360f;
            return normalizedYaw < 0f ? normalizedYaw + 360f : normalizedYaw;
        }

        public static bool IsValidWorldPosition(DFMPPlayerPositionReport report)
        {
            if (float.IsNaN(report.WorldY) || float.IsInfinity(report.WorldY) || !IsValidFacingYaw(report.FacingYaw))
                return false;

            int mapPixelX = report.WorldX / DFMPSpawnProtocol.WorldMapPixelDimension;
            int mapPixelY = DFMPSpawnProtocol.WorldMapHeightInPixels - 1 - (report.WorldZ / DFMPSpawnProtocol.WorldMapPixelDimension);
            return mapPixelX >= MinimumMapPixelX && mapPixelX <= MaximumMapPixelX &&
                mapPixelY >= MinimumMapPixelY && mapPixelY <= MaximumMapPixelY;
        }

        public static bool IsAccepted(DFMPPlayerSessionState sessionState, DFMPPlayerPositionReport report, float elapsedSeconds)
        {
            if (sessionState == null || !sessionState.SpawnConfirmed || elapsedSeconds < MinimumAcceptedReportInterval || !IsValidWorldPosition(report))
                return false;

            float maxDistance = MaximumSpeed * elapsedSeconds;
            float deltaX = report.WorldX - sessionState.WorldX;
            float deltaZ = report.WorldZ - sessionState.WorldZ;
            return deltaX * deltaX + deltaZ * deltaZ <= maxDistance * maxDistance;
        }
    }
}
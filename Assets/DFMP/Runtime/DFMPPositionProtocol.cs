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
        public int Level;
        public int Health;
        public int MaxHealth;
        public int SpellPoints;
        public int MaxSpellPoints;
        public int Fatigue;
        public int MaxFatigue;
        public int Gold;
        public int StartingLevelUpSkillSum;
        public string CareerJson;
        public int[] Attributes;
        public int[] Skills;
        public string InventoryJson;
        public string EquipmentJson;
    }

    public struct DFMPWorldContextReport : Mirror.NetworkMessage
    {
        public DFMPWorldContextKind Kind;
        public int MapPixelX;
        public int MapPixelY;
        public int RegionIndex;
        public int LocationIndex;
        public string LocationId;
        public int BuildingKey;
        public int DungeonBlockIndex;
        public string DungeonBlockName;
        public string InstanceId;
    }

    public enum DFMPPositionRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        TooSoon,
        InvalidWorldPosition,
        ExcessiveDisplacement
    }

    public enum DFMPWorldContextRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        InvalidKind,
        InvalidMapPixel,
        MissingLocation,
        MissingBuildingKey
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

        public static int GetPlayerRace(int race)
        {
            return Mathf.Clamp(race, 1, 8);
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
            return GetRejectionReason(sessionState, report, elapsedSeconds) == DFMPPositionRejectionReason.None;
        }

        public static DFMPPositionRejectionReason GetRejectionReason(DFMPPlayerSessionState sessionState, DFMPPlayerPositionReport report, float elapsedSeconds)
        {
            if (sessionState == null)
                return DFMPPositionRejectionReason.MissingSession;
            if (!sessionState.SpawnConfirmed)
                return DFMPPositionRejectionReason.SpawnNotConfirmed;
            if (elapsedSeconds < MinimumAcceptedReportInterval)
                return DFMPPositionRejectionReason.TooSoon;
            if (!IsValidWorldPosition(report))
                return DFMPPositionRejectionReason.InvalidWorldPosition;

            float maxDistance = MaximumSpeed * elapsedSeconds;
            float deltaX = report.WorldX - sessionState.WorldX;
            float deltaZ = report.WorldZ - sessionState.WorldZ;
            return deltaX * deltaX + deltaZ * deltaZ <= maxDistance * maxDistance
                ? DFMPPositionRejectionReason.None
                : DFMPPositionRejectionReason.ExcessiveDisplacement;
        }
    }

    public static class DFMPMovementProtocol
    {
        public const int MovementThreshold = 0;

        public static bool IsMoving(int previousWorldX, int previousWorldZ, int currentWorldX, int currentWorldZ)
        {
            long deltaX = (long)currentWorldX - previousWorldX;
            long deltaZ = (long)currentWorldZ - previousWorldZ;
            long threshold = MovementThreshold;
            return deltaX * deltaX + deltaZ * deltaZ > threshold * threshold;
        }
    }

    public static class DFMPWorldContextProtocol
    {
        public static DFMPWorldContextRejectionReason GetRejectionReason(DFMPPlayerSessionState sessionState, DFMPWorldContextReport report)
        {
            if (sessionState == null)
                return DFMPWorldContextRejectionReason.MissingSession;
            if (!sessionState.SpawnConfirmed)
                return DFMPWorldContextRejectionReason.SpawnNotConfirmed;
            if (report.Kind != DFMPWorldContextKind.Exterior && report.Kind != DFMPWorldContextKind.BuildingInterior && report.Kind != DFMPWorldContextKind.Dungeon)
                return DFMPWorldContextRejectionReason.InvalidKind;
            if (report.MapPixelX < DFMPPositionProtocol.MinimumMapPixelX || report.MapPixelX > DFMPPositionProtocol.MaximumMapPixelX ||
                report.MapPixelY < DFMPPositionProtocol.MinimumMapPixelY || report.MapPixelY > DFMPPositionProtocol.MaximumMapPixelY)
                return DFMPWorldContextRejectionReason.InvalidMapPixel;
            if ((report.Kind == DFMPWorldContextKind.BuildingInterior || report.Kind == DFMPWorldContextKind.Dungeon) && string.IsNullOrWhiteSpace(report.LocationId))
                return DFMPWorldContextRejectionReason.MissingLocation;
            if (report.Kind == DFMPWorldContextKind.BuildingInterior && report.BuildingKey <= 0)
                return DFMPWorldContextRejectionReason.MissingBuildingKey;

            return DFMPWorldContextRejectionReason.None;
        }

        public static bool TryCreateKey(DFMPWorldContextReport report, out DFMPWorldContextKey key)
        {
            key = new DFMPWorldContextKey();
            var record = new DFMPWorldContextRecord
            {
                Kind = report.Kind.ToString(),
                MapPixelX = report.MapPixelX,
                MapPixelY = report.MapPixelY,
                RegionIndex = report.RegionIndex,
                LocationIndex = report.LocationIndex,
                LocationId = report.LocationId ?? string.Empty,
                BuildingKey = report.BuildingKey,
                DungeonBlockIndex = report.DungeonBlockIndex,
                DungeonBlockName = report.DungeonBlockName ?? string.Empty,
                InstanceId = report.InstanceId ?? string.Empty
            };
            key = record.ToKey();
            return true;
        }

        public static int GetSignature(DFMPWorldContextReport report)
        {
            unchecked
            {
                int signature = (int)report.Kind;
                signature = signature * 397 ^ report.MapPixelX;
                signature = signature * 397 ^ report.MapPixelY;
                signature = signature * 397 ^ report.RegionIndex;
                signature = signature * 397 ^ report.LocationIndex;
                signature = signature * 397 ^ (report.LocationId ?? string.Empty).ToLowerInvariant().GetHashCode();
                signature = signature * 397 ^ report.BuildingKey;
                signature = signature * 397 ^ report.DungeonBlockIndex;
                signature = signature * 397 ^ (report.DungeonBlockName ?? string.Empty).ToLowerInvariant().GetHashCode();
                signature = signature * 397 ^ (report.InstanceId ?? string.Empty).ToLowerInvariant().GetHashCode();
                return signature;
            }
        }
    }
}
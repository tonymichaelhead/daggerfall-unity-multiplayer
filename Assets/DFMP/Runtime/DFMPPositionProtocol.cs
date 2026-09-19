using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPPlayerPositionReport : Mirror.NetworkMessage
    {
        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public float FacingYaw;
        public float SceneHeightOffset;
        public bool HasDungeonLocalPosition;
        public float DungeonLocalX;
        public float DungeonLocalY;
        public float DungeonLocalZ;
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

    public struct DFMPPlayerActionReport : Mirror.NetworkMessage
    {
        public DFMPPlayerActionKind Kind;
    }

    public enum DFMPPlayerActionKind
    {
        None,
        PrimaryAttack,
        RangedAttack,
        Spell,
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
        public bool HasExteriorDoor;
        public DFMPNetworkStaticDoor ExteriorDoor;
    }

    public enum DFMPPositionRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        TooSoon,
        InvalidWorldPosition,
        InvalidDungeonLocalPosition,
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
        MissingBuildingKey,
        PendingTransition
    }

    public static class DFMPPositionProtocol
    {
        public const float MinimumReportInterval = 0.1f;
        // Reports routinely arrive slightly early because of send/receive jitter, so only clear flooding is rejected.
        public const float MinimumAcceptedReportInterval = MinimumReportInterval * 0.5f;
        public const float MaximumSpeed = 4096f;
        public const float MaximumSceneHeightOffset = 20f;
        public const float MaximumDungeonLocalCoordinate = 8192f;
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

        public static float GetSceneHeightOffset(float sceneY, float groundY, float controllerCenterY, float controllerHeight, float controllerSkinWidth)
        {
            float feetY = GetControllerFeetY(sceneY, controllerCenterY, controllerHeight, controllerSkinWidth);
            return Mathf.Clamp(feetY - groundY, -MaximumSceneHeightOffset, MaximumSceneHeightOffset);
        }

        public static float GetControllerFeetY(float sceneY, float controllerCenterY, float controllerHeight, float controllerSkinWidth)
        {
            return sceneY + controllerCenterY - controllerHeight * 0.5f - Mathf.Max(0f, controllerSkinWidth);
        }

        public static float GetControllerCentreY(float feetY, float controllerCenterY, float controllerHeight, float controllerSkinWidth)
        {
            return feetY - controllerCenterY + controllerHeight * 0.5f + Mathf.Max(0f, controllerSkinWidth);
        }

        public static bool IsValidWorldPosition(DFMPPlayerPositionReport report)
        {
            if (float.IsNaN(report.WorldY) || float.IsInfinity(report.WorldY) ||
                float.IsNaN(report.SceneHeightOffset) || float.IsInfinity(report.SceneHeightOffset) ||
                Mathf.Abs(report.SceneHeightOffset) > MaximumSceneHeightOffset || !IsValidFacingYaw(report.FacingYaw))
                return false;

            int mapPixelX = report.WorldX / DFMPSpawnProtocol.WorldMapPixelDimension;
            int mapPixelY = DFMPSpawnProtocol.WorldMapHeightInPixels - 1 - (report.WorldZ / DFMPSpawnProtocol.WorldMapPixelDimension);
            return mapPixelX >= MinimumMapPixelX && mapPixelX <= MaximumMapPixelX &&
                mapPixelY >= MinimumMapPixelY && mapPixelY <= MaximumMapPixelY;
        }

            public static bool IsValidDungeonLocalPosition(Vector3 position)
            {
                return !float.IsNaN(position.x) && !float.IsInfinity(position.x) && Mathf.Abs(position.x) <= MaximumDungeonLocalCoordinate &&
                !float.IsNaN(position.y) && !float.IsInfinity(position.y) && Mathf.Abs(position.y) <= MaximumDungeonLocalCoordinate &&
                !float.IsNaN(position.z) && !float.IsInfinity(position.z) && Mathf.Abs(position.z) <= MaximumDungeonLocalCoordinate;
            }

            public static Vector3 GetDungeonLocalPosition(DFMPPlayerPositionReport report)
            {
                return new Vector3(report.DungeonLocalX, report.DungeonLocalY, report.DungeonLocalZ);
            }

        public static bool IsAccepted(DFMPPlayerSessionState sessionState, DFMPPlayerPositionReport report, float elapsedSeconds)
        {
            return GetRejectionReason(sessionState, report, elapsedSeconds) == DFMPPositionRejectionReason.None;
        }

        public static DFMPPositionRejectionReason GetRejectionReason(DFMPPlayerSessionState sessionState, DFMPPlayerPositionReport report, float elapsedSeconds)
        {
            return GetRejectionReason(sessionState, report, elapsedSeconds, new DFMPWorldContextKey());
        }

        public static DFMPPositionRejectionReason GetRejectionReason(DFMPPlayerSessionState sessionState, DFMPPlayerPositionReport report, float elapsedSeconds, DFMPWorldContextKey authoritativeContext)
        {
            if (sessionState == null)
                return DFMPPositionRejectionReason.MissingSession;
            if (!sessionState.SpawnConfirmed)
                return DFMPPositionRejectionReason.SpawnNotConfirmed;
            if (elapsedSeconds < MinimumAcceptedReportInterval)
                return DFMPPositionRejectionReason.TooSoon;
            if (!IsValidWorldPosition(report))
                return DFMPPositionRejectionReason.InvalidWorldPosition;

            if (authoritativeContext.Kind == DFMPWorldContextKind.Dungeon &&
                (!report.HasDungeonLocalPosition || !IsValidDungeonLocalPosition(GetDungeonLocalPosition(report))))
                return DFMPPositionRejectionReason.InvalidDungeonLocalPosition;

            if (authoritativeContext.Kind == DFMPWorldContextKind.Dungeon && !sessionState.HasDungeonLocalPosition)
                return DFMPPositionRejectionReason.None;

            float maxDistance = MaximumSpeed * elapsedSeconds;
            float deltaX = authoritativeContext.Kind == DFMPWorldContextKind.Dungeon
                ? report.DungeonLocalX - sessionState.DungeonLocalPosition.x
                : report.WorldX - sessionState.WorldX;
            float deltaZ = authoritativeContext.Kind == DFMPWorldContextKind.Dungeon
                ? report.DungeonLocalZ - sessionState.DungeonLocalPosition.z
                : report.WorldZ - sessionState.WorldZ;
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
            return IsMoving((float)previousWorldX, (float)previousWorldZ, currentWorldX, currentWorldZ);
        }

        public static bool IsMoving(float previousX, float previousZ, float currentX, float currentZ)
        {
            float deltaX = currentX - previousX;
            float deltaZ = currentZ - previousZ;
            float threshold = MovementThreshold;
            return deltaX * deltaX + deltaZ * deltaZ > threshold * threshold;
        }
    }

    public static class DFMPAvatarProtocol
    {
        public const int DefaultMobileType = (int)MobileTypes.Warrior;
        public const float MinimumActionInterval = 0.1f;

        public static int GetMobileType(string careerJson)
        {
            DaggerfallConnect.DFCareer career;
            string reason;
            return DFMPCareerCodec.TryDecode(careerJson, out career, out reason)
                ? GetMobileTypeFromCareerName(career.Name)
                : DefaultMobileType;
        }

        public static int GetMobileTypeFromCareerName(string careerName)
        {
            string normalizedName = NormalizeCareerName(careerName);
            ClassCareers career;
            if (Enum.TryParse(normalizedName, true, out career) && career >= ClassCareers.Mage && career <= ClassCareers.Knight)
                return (int)MobileTypes.Mage + (int)career;

            return DefaultMobileType;
        }

        public static bool IsValidAction(DFMPPlayerActionKind kind)
        {
            return kind == DFMPPlayerActionKind.PrimaryAttack ||
                kind == DFMPPlayerActionKind.RangedAttack ||
                kind == DFMPPlayerActionKind.Spell;
        }

        public static bool IsActionAccepted(DFMPPlayerSessionState sessionState, DFMPPlayerActionKind kind, float elapsedSeconds)
        {
            return sessionState != null && sessionState.SpawnConfirmed && IsValidAction(kind) && elapsedSeconds >= MinimumActionInterval;
        }

        static string NormalizeCareerName(string careerName)
        {
            if (string.IsNullOrWhiteSpace(careerName))
                return string.Empty;

            return careerName.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
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

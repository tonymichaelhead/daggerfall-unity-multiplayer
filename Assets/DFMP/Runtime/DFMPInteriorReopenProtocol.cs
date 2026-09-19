using DaggerfallWorkshop;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Validates and packs the reopen payload needed to restore building/dungeon interiors on reconnect.
    /// </summary>
    public static class DFMPInteriorReopenProtocol
    {
        public static bool TryGetPersistedReopenContext(DFMPCharacterRecord record, out DFMPWorldContextKey context, out bool canReopenInterior)
        {
            context = default(DFMPWorldContextKey);
            canReopenInterior = false;
            if (record == null)
                return false;

            context = record.Context != null
                ? record.Context.ToKey()
                : default(DFMPWorldContextKey);

            if (context.Kind == DFMPWorldContextKind.BuildingInterior)
            {
                canReopenInterior = CanReopenBuilding(record, context);
                return true;
            }

            if (context.Kind == DFMPWorldContextKind.Dungeon)
            {
                canReopenInterior = CanReopenDungeon(context);
                return true;
            }

            return context.Kind == DFMPWorldContextKind.Exterior;
        }

        public static bool CanReopenBuilding(DFMPCharacterRecord record, DFMPWorldContextKey context)
        {
            if (record == null || context.Kind != DFMPWorldContextKind.BuildingInterior)
                return false;
            if (context.BuildingKey <= 0)
                return false;
            if (!DFMPSpawnProtocol.IsValidMapPixel(context.MapPixelX, context.MapPixelY))
                return false;
            return TryGetPrimaryExteriorDoor(record.ExteriorDoors, out _);
        }

        public static bool CanReopenDungeon(DFMPWorldContextKey context)
        {
            if (context.Kind != DFMPWorldContextKind.Dungeon)
                return false;
            if (!DFMPSpawnProtocol.IsValidMapPixel(context.MapPixelX, context.MapPixelY))
                return false;
            return !string.IsNullOrWhiteSpace(context.LocationId) ||
                (context.RegionIndex >= 0 && context.LocationIndex >= 0 &&
                 (context.RegionIndex > 0 || context.LocationIndex > 0));
        }

        public static bool TryGetPrimaryExteriorDoor(DFMPStaticDoorRecord[] doors, out StaticDoor door)
        {
            door = default(StaticDoor);
            if (doors == null || doors.Length == 0)
                return false;

            for (int index = 0; index < doors.Length; index++)
            {
                if (doors[index] == null || !doors[index].IsValidForBuildingReopen())
                    continue;

                door = doors[index].ToStaticDoor();
                return true;
            }

            return false;
        }

        public static bool TryGetPrimaryExteriorDoor(DFMPPlayerSessionState sessionState, out StaticDoor door)
        {
            door = default(StaticDoor);
            if (sessionState == null)
                return false;
            return TryGetPrimaryExteriorDoor(sessionState.ExteriorDoors, out door);
        }

        public static Vector3 GetInteriorLocalPosition(DFMPCharacterRecord record)
        {
            if (record == null || !record.HasInteriorLocalPosition)
                return Vector3.zero;
            return new Vector3(record.InteriorLocalX, record.InteriorLocalY, record.InteriorLocalZ);
        }

        public static Vector3 GetInteriorLocalPosition(DFMPPlayerSessionState sessionState)
        {
            if (sessionState == null || !sessionState.HasDungeonLocalPosition)
                return Vector3.zero;
            return sessionState.DungeonLocalPosition;
        }

        public static bool IsValidInteriorLocalPosition(Vector3 position)
        {
            return DFMPPositionProtocol.IsValidDungeonLocalPosition(position);
        }
    }
}

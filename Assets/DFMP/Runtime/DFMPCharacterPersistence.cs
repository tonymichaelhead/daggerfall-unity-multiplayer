using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPCharacterPersistence
    {
        const int EquipmentSlotCount = 27;
        public static DFMPCharacterItemRecord[] CaptureItems(ItemData_v1[] itemData)
        {
            if (itemData == null || itemData.Length == 0)
                return new DFMPCharacterItemRecord[0];

            var records = new DFMPCharacterItemRecord[itemData.Length];
            for (int index = 0; index < itemData.Length; index++)
                records[index] = DFMPCharacterItemRecord.FromItemData(itemData[index]);

            return records;
        }

        public static ItemData_v1[] RestoreItems(DFMPCharacterItemRecord[] records)
        {
            if (records == null || records.Length == 0)
                return new ItemData_v1[0];

            var itemData = new ItemData_v1[records.Length];
            for (int index = 0; index < records.Length; index++)
                itemData[index] = records[index] == null ? null : records[index].ToItemData();

            return itemData;
        }

        public static DFMPCharacterEquipmentRecord[] CaptureEquipment(ulong[] equipmentItemIds)
        {
            if (equipmentItemIds == null || equipmentItemIds.Length == 0)
                return new DFMPCharacterEquipmentRecord[0];

            var equipment = new DFMPCharacterEquipmentRecord[equipmentItemIds.Length];
            for (int index = 0; index < equipmentItemIds.Length; index++)
            {
                equipment[index] = new DFMPCharacterEquipmentRecord
                {
                    EquipSlot = index,
                    ItemId = equipmentItemIds[index]
                };
            }

            return equipment;
        }

        public static ulong[] RestoreEquipment(DFMPCharacterEquipmentRecord[] equipment)
        {
            if (equipment == null || equipment.Length == 0)
                return new ulong[0];

            var itemIds = new ulong[EquipmentSlotCount];
            for (int index = 0; index < equipment.Length; index++)
            {
                if (equipment[index] != null && equipment[index].EquipSlot >= 0 && equipment[index].EquipSlot < EquipmentSlotCount)
                    itemIds[equipment[index].EquipSlot] = equipment[index].ItemId;
            }

            return itemIds;
        }

        public static void ApplyIdentityReport(DFMPCharacterRecord record, DFMPPlayerIdentityReport report)
        {
            if (record == null || report.Attributes == null || report.Skills == null || report.Attributes.Length != 8 || report.Skills.Length != 35)
                return;

            record.CharacterName = DFMPPositionProtocol.SanitizeDisplayName(report.DisplayName);
            record.Race = DFMPPositionProtocol.GetPlayerRace(report.Race);
            record.Gender = DFMPPositionProtocol.GetDisplayGender(report.Gender);
            record.OutfitVariant = DFMPPositionProtocol.GetOutfitVariant(report.OutfitVariant);
            record.FaceVariant = Mathf.Clamp(report.FaceVariant, 0, 9);
            record.Level = Mathf.Max(1, report.Level);
            record.MaxHealth = Mathf.Max(1, report.MaxHealth);
            record.Health = Mathf.Clamp(report.Health, 0, record.MaxHealth);
            record.MaxSpellPoints = Mathf.Max(0, report.MaxSpellPoints);
            record.SpellPoints = Mathf.Clamp(report.SpellPoints, 0, record.MaxSpellPoints);
            record.MaxFatigue = Mathf.Max(1, report.MaxFatigue);
            record.Fatigue = Mathf.Clamp(report.Fatigue, 0, record.MaxFatigue);
            record.Gold = Mathf.Max(0, report.Gold);
            for (int index = 0; index < 8; index++)
                record.Attributes[index] = Mathf.Clamp(report.Attributes[index], 0, 100);
            for (int index = 0; index < 35; index++)
                record.Skills[index] = Mathf.Clamp(report.Skills[index], 0, 100);

            if (!string.IsNullOrWhiteSpace(report.InventoryJson))
            {
                DFMPCharacterItemRecord[] inventory;
                DFMPCharacterEquipmentRecord[] equipment;
                string reason;
                if (DFMPInventorySnapshotCodec.TryDecode(report.InventoryJson, out inventory, out equipment, out reason))
                {
                    record.Inventory = inventory;
                    record.Equipment = equipment;
                }
            }
        }

        public static void ApplySessionState(DFMPCharacterRecord record, DFMPPlayerSessionState sessionState)
        {
            if (record == null || sessionState == null)
                return;

            record.CharacterName = DFMPPositionProtocol.SanitizeDisplayName(sessionState.DisplayName);
            record.WorldX = sessionState.WorldX;
            record.WorldY = sessionState.WorldY;
            record.WorldZ = sessionState.WorldZ;
            record.WorldContext = "Exterior";

            var mapPixel = MapsFile.WorldCoordToMapPixel(sessionState.WorldX, sessionState.WorldZ);
            record.MapPixelX = mapPixel.X;
            record.MapPixelY = mapPixel.Y;
            record.Race = DFMPPositionProtocol.GetPlayerRace(sessionState.Race);
            record.Gender = DFMPPositionProtocol.GetDisplayGender(sessionState.Gender);
            record.OutfitVariant = DFMPPositionProtocol.GetOutfitVariant(sessionState.OutfitVariant);
            record.FaceVariant = DFMPPositionProtocol.GetFaceVariant(sessionState.FaceVariant);
        }
    }
}

using System;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    class DFMPInventorySnapshotDocument
    {
        public DFMPCharacterItemRecord[] Inventory = new DFMPCharacterItemRecord[0];
        public DFMPCharacterEquipmentRecord[] Equipment = new DFMPCharacterEquipmentRecord[0];
    }

    public static class DFMPInventorySnapshotCodec
    {
        public const int MaximumItemCount = 512;
        public const int MaximumJsonLength = 1024 * 1024;

        public static string Encode(DFMPCharacterItemRecord[] inventory, DFMPCharacterEquipmentRecord[] equipment)
        {
            var document = new DFMPInventorySnapshotDocument
            {
                Inventory = inventory ?? new DFMPCharacterItemRecord[0],
                Equipment = equipment ?? new DFMPCharacterEquipmentRecord[0]
            };
            return JsonUtility.ToJson(document, false);
        }

        public static bool TryDecode(string json, out DFMPCharacterItemRecord[] inventory, out DFMPCharacterEquipmentRecord[] equipment, out string reason)
        {
            inventory = new DFMPCharacterItemRecord[0];
            equipment = new DFMPCharacterEquipmentRecord[0];
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "inventory snapshot is empty";
                return false;
            }

            if (json.Length > MaximumJsonLength)
            {
                reason = "inventory snapshot exceeds maximum size";
                return false;
            }

            DFMPInventorySnapshotDocument document;
            try
            {
                document = JsonUtility.FromJson<DFMPInventorySnapshotDocument>(json);
            }
            catch (Exception ex)
            {
                reason = "inventory snapshot is malformed: " + ex.Message;
                return false;
            }

            if (document == null)
            {
                reason = "inventory snapshot is malformed";
                return false;
            }

            inventory = document.Inventory ?? new DFMPCharacterItemRecord[0];
            equipment = document.Equipment ?? new DFMPCharacterEquipmentRecord[0];
            if (inventory.Length > MaximumItemCount || equipment.Length > MaximumItemCount)
            {
                reason = "inventory snapshot contains too many entries";
                return false;
            }

            for (int index = 0; index < inventory.Length; index++)
            {
                if (inventory[index] == null)
                {
                    reason = "inventory snapshot contains a null item";
                    return false;
                }

                inventory[index].Normalize();
            }

            for (int index = 0; index < equipment.Length; index++)
            {
                if (equipment[index] == null || equipment[index].EquipSlot < 0)
                {
                    reason = "inventory snapshot contains an invalid equipment slot";
                    return false;
                }
            }

            return true;
        }
    }
}

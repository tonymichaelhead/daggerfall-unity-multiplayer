using System;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPCharacterRecord
    {
        public const int CurrentSchemaVersion = 5;

        public int SchemaVersion = CurrentSchemaVersion;
        public string AccountId = string.Empty;
        public string ServerWorldId = string.Empty;
        public string CharacterName = "Player";

        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public int MapPixelX;
        public int MapPixelY;
        public string WorldContext = "Exterior";
        public DFMPWorldContextRecord Context = new DFMPWorldContextRecord();
        public DFMPRespawnAnchorRecord RespawnAnchor = new DFMPRespawnAnchorRecord();

        public int Health = 1;
        public int MaxHealth = 1;
        public int SpellPoints;
        public int MaxSpellPoints;
        public int Fatigue = 1;
        public int MaxFatigue = 1;

        public int Race;
        public int Gender;
        // Serialized DFCareer. Drives level-up skills, magicka, tolerances, and class advantages.
        public string CareerJson = string.Empty;
        public int OutfitVariant;
        public int FaceVariant;
        public int Level = 1;
        // Daggerfall has no experience points; level derives from this plus the current skill sum.
        // Zero means unknown (schema v1 record) and the client re-estimates it from DFU.
        public int StartingLevelUpSkillSum;
        public int Gold;
        public int[] Attributes = new int[8];
        public int[] Skills = new int[35];
        public DFMPCharacterItemRecord[] Inventory = new DFMPCharacterItemRecord[0];
        public DFMPCharacterEquipmentRecord[] Equipment = new DFMPCharacterEquipmentRecord[0];

        public void Normalize(string accountId, string serverWorldId)
        {
            SchemaVersion = CurrentSchemaVersion;
            AccountId = accountId ?? string.Empty;
            ServerWorldId = serverWorldId ?? string.Empty;
            CharacterName = string.IsNullOrWhiteSpace(CharacterName) ? "Player" : CharacterName.Trim();
            WorldContext = string.IsNullOrWhiteSpace(WorldContext) ? "Exterior" : WorldContext.Trim();
            if (Context == null || Context.IsDefaultExterior() && (MapPixelX != 0 || MapPixelY != 0 || !string.Equals(WorldContext, "Exterior", StringComparison.OrdinalIgnoreCase)))
                Context = DFMPWorldContextRecord.FromLegacy(WorldContext, MapPixelX, MapPixelY);

            Context.Normalize();
            WorldContext = Context.Kind;
            MapPixelX = Context.MapPixelX;
            MapPixelY = Context.MapPixelY;
            if (RespawnAnchor == null)
                RespawnAnchor = new DFMPRespawnAnchorRecord();
            RespawnAnchor.Normalize();
            Level = Mathf.Max(1, Level);
            MaxHealth = Mathf.Max(1, MaxHealth);
            Health = Mathf.Clamp(Health, 0, MaxHealth);
            MaxSpellPoints = Mathf.Max(0, MaxSpellPoints);
            SpellPoints = Mathf.Clamp(SpellPoints, 0, MaxSpellPoints);
            MaxFatigue = Mathf.Max(1, MaxFatigue);
            Fatigue = Mathf.Clamp(Fatigue, 0, MaxFatigue);
            StartingLevelUpSkillSum = Mathf.Max(0, StartingLevelUpSkillSum);
            CareerJson = CareerJson ?? string.Empty;
            Gold = Mathf.Max(0, Gold);
            Attributes = Attributes ?? new int[8];
            Skills = Skills ?? new int[35];
            Inventory = Inventory ?? new DFMPCharacterItemRecord[0];
            Equipment = Equipment ?? new DFMPCharacterEquipmentRecord[0];
            for (int index = 0; index < Inventory.Length; index++)
            {
                if (Inventory[index] != null)
                    Inventory[index].Normalize();
            }

            for (int index = 0; index < Equipment.Length; index++)
            {
                if (Equipment[index] != null)
                    Equipment[index].EquipSlot = Mathf.Max(0, Equipment[index].EquipSlot);
            }
        }

        public static DFMPCharacterRecord CreateNew(string accountId, string serverWorldId, string characterName)
        {
            var record = new DFMPCharacterRecord
            {
                AccountId = accountId,
                ServerWorldId = serverWorldId,
                CharacterName = characterName
            };
            record.Normalize(accountId, serverWorldId);
            return record;
        }

        public static DFMPCharacterRecord FromJson(string json, string accountId, string serverWorldId)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var record = JsonUtility.FromJson<DFMPCharacterRecord>(json);
            if (record == null)
                return null;

            record.Normalize(accountId, serverWorldId);
            return record;
        }

        public string ToJson()
        {
            Normalize(AccountId, ServerWorldId);
            return JsonUtility.ToJson(this, true);
        }
    }
}

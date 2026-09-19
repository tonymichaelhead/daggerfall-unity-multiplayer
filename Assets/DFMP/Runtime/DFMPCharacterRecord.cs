using System;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPCharacterRecord
    {
        public const int CurrentSchemaVersion = 8;

        public int SchemaVersion = CurrentSchemaVersion;
        public string AccountId = string.Empty;
        public string ServerWorldId = string.Empty;
        public string CharacterId = string.Empty;
        public string CharacterName = "Player";
        public string LastPlayedUtc = string.Empty;

        public int WorldX;
        public float WorldY;
        public int WorldZ;
        public int MapPixelX;
        public int MapPixelY;
        public string WorldContext = "Exterior";
        public DFMPWorldContextRecord Context = new DFMPWorldContextRecord();
        public DFMPRespawnAnchorRecord RespawnAnchor = new DFMPRespawnAnchorRecord();
        public bool HasInteriorLocalPosition;
        public float InteriorLocalX;
        public float InteriorLocalY;
        public float InteriorLocalZ;
        public DFMPStaticDoorRecord[] ExteriorDoors = new DFMPStaticDoorRecord[0];

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
        public DFMPQuestStateEnvelope QuestState;
        public DFMPQuestObjectiveRecord[] QuestObjectives = new DFMPQuestObjectiveRecord[0];
        public DFMPCharacterItemRecord[] Inventory = new DFMPCharacterItemRecord[0];
        public DFMPCharacterEquipmentRecord[] Equipment = new DFMPCharacterEquipmentRecord[0];

        public void Normalize(string accountId, string serverWorldId)
        {
            SchemaVersion = CurrentSchemaVersion;
            AccountId = accountId ?? string.Empty;
            ServerWorldId = serverWorldId ?? string.Empty;
            CharacterId = CharacterId ?? string.Empty;
            LastPlayedUtc = LastPlayedUtc ?? string.Empty;
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
            ExteriorDoors = ExteriorDoors ?? new DFMPStaticDoorRecord[0];
            for (int index = 0; index < ExteriorDoors.Length; index++)
            {
                if (ExteriorDoors[index] != null)
                    ExteriorDoors[index].Normalize();
            }

            if (!HasInteriorLocalPosition)
            {
                InteriorLocalX = 0f;
                InteriorLocalY = 0f;
                InteriorLocalZ = 0f;
            }

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
            if (QuestState != null)
            {
                string questStateReason;
                if (!DFMPQuestStateCodec.TryValidate(QuestState, out questStateReason))
                    QuestState = null;
            }
            QuestObjectives = QuestObjectives ?? new DFMPQuestObjectiveRecord[0];
            if (QuestObjectives.Length > DFMPServerQuestConfig.DefaultMaximumObjectivesPerCharacter)
                Array.Resize(ref QuestObjectives, DFMPServerQuestConfig.DefaultMaximumObjectivesPerCharacter);
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
            record.EnsureCharacterId();
            record.Normalize(accountId, serverWorldId);
            return record;
        }

        public void EnsureCharacterId()
        {
            Guid parsed;
            if (!string.IsNullOrWhiteSpace(CharacterId) && Guid.TryParse(CharacterId.Trim(), out parsed))
            {
                CharacterId = parsed.ToString("N");
                return;
            }

            CharacterId = Guid.NewGuid().ToString("N");
        }

        public void MarkPlayed()
        {
            LastPlayedUtc = DateTime.UtcNow.ToString("o");
        }

        public DateTime GetLastPlayedTimeUtc()
        {
            DateTime parsed;
            if (DateTime.TryParse(LastPlayedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsed))
                return parsed.ToUniversalTime();

            return DateTime.MinValue;
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

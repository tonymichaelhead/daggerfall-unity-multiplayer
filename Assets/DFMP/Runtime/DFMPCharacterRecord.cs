using System;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPCharacterRecord
    {
        public const int CurrentSchemaVersion = 1;

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

        public int Health = 1;
        public int MaxHealth = 1;
        public int SpellPoints;
        public int MaxSpellPoints;
        public int Fatigue = 1;
        public int MaxFatigue = 1;

        public int Race;
        public int Gender;
        public int OutfitVariant;
        public int FaceVariant;
        public int Level = 1;
        public int Experience;
        public int Gold;
        public int[] Attributes = new int[8];
        public int[] Skills = new int[35];
        public string[] Inventory = new string[0];
        public string[] Equipment = new string[0];

        public void Normalize(string accountId, string serverWorldId)
        {
            SchemaVersion = CurrentSchemaVersion;
            AccountId = accountId ?? string.Empty;
            ServerWorldId = serverWorldId ?? string.Empty;
            CharacterName = string.IsNullOrWhiteSpace(CharacterName) ? "Player" : CharacterName.Trim();
            WorldContext = string.IsNullOrWhiteSpace(WorldContext) ? "Exterior" : WorldContext.Trim();
            Level = Mathf.Max(1, Level);
            MaxHealth = Mathf.Max(1, MaxHealth);
            Health = Mathf.Clamp(Health, 0, MaxHealth);
            MaxSpellPoints = Mathf.Max(0, MaxSpellPoints);
            SpellPoints = Mathf.Clamp(SpellPoints, 0, MaxSpellPoints);
            MaxFatigue = Mathf.Max(1, MaxFatigue);
            Fatigue = Mathf.Clamp(Fatigue, 0, MaxFatigue);
            Experience = Mathf.Max(0, Experience);
            Gold = Mathf.Max(0, Gold);
            Attributes = Attributes ?? new int[8];
            Skills = Skills ?? new int[35];
            Inventory = Inventory ?? new string[0];
            Equipment = Equipment ?? new string[0];
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

using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPAccountIdentityMessage : NetworkMessage
    {
        public string AccountId;
    }

    public struct DFMPJoinResultMessage : NetworkMessage
    {
        public DFMPJoinDecisionKind Decision;
        public string AccountId;
        public string ServerWorldId;
        public string ServerName;
        public string Motd;
        public string Reason;
        public bool EnableBeginnerTutorial;
    }

    public struct DFMPCharacterSnapshotMessage : NetworkMessage
    {
        public string AccountId;
        public string CharacterName;
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
        public int[] Attributes;
        public int[] Skills;
        public int Gold;
        public int StartingLevelUpSkillSum;
        public string CareerJson;
        public string InventoryJson;
        public string EquipmentJson;
    }

    public static class DFMPCharacterSnapshotProtocol
    {
        public static DFMPCharacterSnapshotMessage FromRecord(DFMPCharacterRecord record)
        {
            return new DFMPCharacterSnapshotMessage
            {
                AccountId = record.AccountId,
                CharacterName = DFMPPositionProtocol.SanitizeDisplayName(record.CharacterName),
                Race = DFMPPositionProtocol.GetPlayerRace(record.Race),
                Gender = DFMPPositionProtocol.GetDisplayGender(record.Gender),
                OutfitVariant = DFMPPositionProtocol.GetOutfitVariant(record.OutfitVariant),
                FaceVariant = Mathf.Clamp(record.FaceVariant, 0, 9),
                Level = record.Level,
                Health = record.Health,
                MaxHealth = record.MaxHealth,
                SpellPoints = record.SpellPoints,
                MaxSpellPoints = record.MaxSpellPoints,
                Fatigue = record.Fatigue,
                MaxFatigue = record.MaxFatigue,
                Attributes = record.Attributes ?? new int[0],
                Skills = record.Skills ?? new int[0],
                Gold = record.Gold,
                StartingLevelUpSkillSum = record.StartingLevelUpSkillSum,
                CareerJson = record.CareerJson ?? string.Empty,
                InventoryJson = DFMPInventorySnapshotCodec.Encode(record.Inventory, record.Equipment)
            };
        }

        public static bool IsValid(DFMPCharacterSnapshotMessage snapshot)
        {
            return !string.IsNullOrWhiteSpace(snapshot.AccountId) &&
                DFMPPositionProtocol.SanitizeDisplayName(snapshot.CharacterName) == snapshot.CharacterName &&
                snapshot.Race == DFMPPositionProtocol.GetPlayerRace(snapshot.Race) &&
                snapshot.Gender == DFMPPositionProtocol.GetDisplayGender(snapshot.Gender) &&
                snapshot.OutfitVariant == DFMPPositionProtocol.GetOutfitVariant(snapshot.OutfitVariant) &&
                snapshot.FaceVariant >= 0 && snapshot.FaceVariant <= 9 &&
                snapshot.Level >= 1 &&
                snapshot.MaxHealth >= 1 && snapshot.Health >= 0 && snapshot.Health <= snapshot.MaxHealth &&
                snapshot.MaxSpellPoints >= 0 && snapshot.SpellPoints >= 0 && snapshot.SpellPoints <= snapshot.MaxSpellPoints &&
                snapshot.MaxFatigue >= 1 && snapshot.Fatigue >= 0 && snapshot.Fatigue <= snapshot.MaxFatigue &&
                snapshot.Attributes != null && snapshot.Attributes.Length == 8 &&
                snapshot.Skills != null && snapshot.Skills.Length == 35 &&
                snapshot.Gold >= 0 && snapshot.StartingLevelUpSkillSum >= 0;
        }
    }

    public enum DFMPJoinDecisionKind
    {
        Rejected,
        FirstJoin,
        ReturningPlayer
    }

    public sealed class DFMPJoinDecision
    {
        public DFMPJoinDecisionKind Kind;
        public string AccountId;
        public string ServerWorldId;
        public string Reason;
        public DFMPCharacterRecord CharacterRecord;

        public bool Accepted
        {
            get { return Kind != DFMPJoinDecisionKind.Rejected; }
        }
    }

    public static class DFMPJoinPolicy
    {
        public static DFMPJoinDecision Resolve(string accountId, DFMPServerConfig config, IDFMPCharacterStore characterStore)
        {
            string normalizedAccountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(accountId, out normalizedAccountId, out reason))
                return Reject(reason);

            if (config == null)
                return Reject("server configuration unavailable");

            config.Normalize();
            if (!DFMPAccountPolicy.IsAllowed(normalizedAccountId, config))
                return Reject("account is not whitelisted");

            if (characterStore == null)
                return Reject("character store unavailable");

            DFMPCharacterRecord record;
            if (characterStore.TryLoad(normalizedAccountId, config.Identity.ServerWorldId, out record))
            {
                return new DFMPJoinDecision
                {
                    Kind = DFMPJoinDecisionKind.ReturningPlayer,
                    AccountId = normalizedAccountId,
                    ServerWorldId = config.Identity.ServerWorldId,
                    CharacterRecord = record
                };
            }

            return new DFMPJoinDecision
            {
                Kind = DFMPJoinDecisionKind.FirstJoin,
                AccountId = normalizedAccountId,
                ServerWorldId = config.Identity.ServerWorldId
            };
        }

        static DFMPJoinDecision Reject(string reason)
        {
            return new DFMPJoinDecision
            {
                Kind = DFMPJoinDecisionKind.Rejected,
                Reason = reason
            };
        }
    }
}

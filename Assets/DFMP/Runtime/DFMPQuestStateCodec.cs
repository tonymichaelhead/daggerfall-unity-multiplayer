using System;
using System.Collections.Generic;
using System.Text;
using DaggerfallConnect.FallExe;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility.AssetInjection;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Serializable mirror of <see cref="GlobalVar"/>, which is not marked [Serializable]
    /// upstream and is therefore dropped by JsonUtility.
    /// </summary>
    [Serializable]
    public struct DFMPQuestGlobalVar
    {
        public int Index;
        public string Name;
        public bool Value;

        public static DFMPQuestGlobalVar[] FromGlobalVars(GlobalVar[] globalVars)
        {
            if (globalVars == null || globalVars.Length == 0)
                return new DFMPQuestGlobalVar[0];

            var converted = new DFMPQuestGlobalVar[globalVars.Length];
            for (int index = 0; index < globalVars.Length; index++)
            {
                converted[index] = new DFMPQuestGlobalVar
                {
                    Index = globalVars[index].index,
                    Name = globalVars[index].name ?? string.Empty,
                    Value = globalVars[index].value
                };
            }

            return converted;
        }

        public static GlobalVar[] ToGlobalVars(DFMPQuestGlobalVar[] globalVars)
        {
            if (globalVars == null || globalVars.Length == 0)
                return new GlobalVar[0];

            var converted = new GlobalVar[globalVars.Length];
            for (int index = 0; index < globalVars.Length; index++)
            {
                converted[index] = new GlobalVar
                {
                    index = globalVars[index].Index,
                    name = globalVars[index].Name ?? string.Empty,
                    value = globalVars[index].Value
                };
            }

            return converted;
        }
    }

    [Serializable]
    public class DFMPGuildMembershipRecord
    {
        public int GuildGroup;
        public int Rank;
        public int LastRankChange;
        public int Variant;
        public int Flags;
    }

    [Serializable]
    public class DFMPQuestStateEnvelope
    {
        public const int MinimumSupportedVersion = 1;
        public const int FirstClassGuildVersion = 2;
        public const int CurrentVersion = 2;

        public int Version = CurrentVersion;
        public string QuestMachineJson = string.Empty;
        public string FactionJson = string.Empty;
        public string ConversationJson = string.Empty;
        public string NotebookJson = string.Empty;
        public string DiscoveryJson = string.Empty;
        public string WorldVariationJson = string.Empty;
        public string QuestAdjacentPlayerJson = string.Empty;
        public string BankJson = string.Empty;
        public string BankDeedJson = string.Empty;
        public string EscortingFacesJson = string.Empty;
        public DFMPQuestGlobalVar[] GlobalVars = new DFMPQuestGlobalVar[0];
        public short[] SocialGroupReputations = new short[0];
        public string[] OneTimeQuestsAccepted = new string[0];
        public DFMPGuildMembershipRecord[] GuildMemberships = new DFMPGuildMembershipRecord[0];
        public DFMPGuildMembershipRecord[] VampireMemberships = new DFMPGuildMembershipRecord[0];
        public ulong LightSourceUID;

        public void Normalize()
        {
            QuestMachineJson = QuestMachineJson ?? string.Empty;
            FactionJson = FactionJson ?? string.Empty;
            ConversationJson = ConversationJson ?? string.Empty;
            NotebookJson = NotebookJson ?? string.Empty;
            DiscoveryJson = DiscoveryJson ?? string.Empty;
            WorldVariationJson = WorldVariationJson ?? string.Empty;
            QuestAdjacentPlayerJson = QuestAdjacentPlayerJson ?? string.Empty;
            BankJson = BankJson ?? string.Empty;
            BankDeedJson = BankDeedJson ?? string.Empty;
            EscortingFacesJson = EscortingFacesJson ?? string.Empty;
            GlobalVars = GlobalVars ?? new DFMPQuestGlobalVar[0];
            SocialGroupReputations = SocialGroupReputations ?? new short[0];
            OneTimeQuestsAccepted = OneTimeQuestsAccepted ?? new string[0];
            GuildMemberships = GuildMemberships ?? new DFMPGuildMembershipRecord[0];
            VampireMemberships = VampireMemberships ?? new DFMPGuildMembershipRecord[0];
        }
    }

    public static class DFMPQuestStateCodec
    {
        public const int MaximumEnvelopeBytes = 4 * 1024 * 1024;
        public const int MaximumSectionBytes = 2 * 1024 * 1024;
        public const int MaximumGlobalVarCount = 1024;
        public const int MaximumOneTimeQuestCount = 4096;
        public const int MaximumGuildMembershipCount = 32;

        public static bool IsSupportedVersion(int version)
        {
            return version >= DFMPQuestStateEnvelope.MinimumSupportedVersion &&
                version <= DFMPQuestStateEnvelope.CurrentVersion;
        }

        public static DFMPGuildMembershipRecord[] FromMembershipData(Dictionary<int, GuildMembership_v1> data)
        {
            if (data == null || data.Count == 0)
                return new DFMPGuildMembershipRecord[0];

            var records = new DFMPGuildMembershipRecord[data.Count];
            int index = 0;
            foreach (KeyValuePair<int, GuildMembership_v1> pair in data)
            {
                GuildMembership_v1 membership = pair.Value ?? new GuildMembership_v1();
                records[index++] = new DFMPGuildMembershipRecord
                {
                    GuildGroup = pair.Key,
                    Rank = membership.rank,
                    LastRankChange = membership.lastRankChange,
                    Variant = membership.variant,
                    Flags = membership.flags
                };
            }

            return records;
        }

        public static Dictionary<int, GuildMembership_v1> ToMembershipData(DFMPGuildMembershipRecord[] records)
        {
            var data = new Dictionary<int, GuildMembership_v1>();
            if (records == null)
                return data;

            for (int index = 0; index < records.Length; index++)
            {
                DFMPGuildMembershipRecord record = records[index];
                if (record == null || data.ContainsKey(record.GuildGroup))
                    continue;

                data[record.GuildGroup] = new GuildMembership_v1
                {
                    rank = record.Rank,
                    lastRankChange = record.LastRankChange,
                    variant = record.Variant,
                    flags = record.Flags
                };
            }

            return data;
        }

        public static bool TryCapture(out DFMPQuestStateEnvelope envelope, out string reason)
        {
            envelope = null;
            reason = string.Empty;
            if (!GameManager.HasInstance || !SaveLoadManager.HasInstance ||
                GameManager.Instance.PlayerEntity == null ||
                !QuestMachine.HasInstance || GameManager.Instance.TalkManager == null)
            {
                reason = "quest runtime is unavailable";
                return false;
            }

            SerializableStateManager stateManager = SaveLoadManager.StateManager;
            if (stateManager == null)
            {
                reason = "serializable state manager is unavailable";
                return false;
            }

            try
            {
                envelope = new DFMPQuestStateEnvelope
                {
                    Version = DFMPQuestStateEnvelope.CurrentVersion,
                    QuestMachineJson = SaveLoadManager.Serialize(
                        typeof(QuestMachine.QuestMachineData_v1),
                        QuestMachine.Instance.GetSaveData(),
                        false),
                    FactionJson = SaveLoadManager.Serialize(
                        typeof(FactionData_v2),
                        stateManager.GetPlayerFactionData(),
                        false),
                    ConversationJson = SaveLoadManager.Serialize(
                        typeof(TalkManager.SaveDataConversation),
                        GameManager.Instance.TalkManager.GetConversationSaveData(),
                        false),
                    NotebookJson = SaveLoadManager.Serialize(
                        typeof(PlayerNotebook.NotebookData_v1),
                        GameManager.Instance.PlayerEntity.Notebook.GetNotebookSaveData(),
                        false),
                    GlobalVars = DFMPQuestGlobalVar.FromGlobalVars(
                        GameManager.Instance.PlayerEntity.GlobalVars.SerializeGlobalVars()),
                    SocialGroupReputations =
                        (short[])GameManager.Instance.PlayerEntity.SGroupReputations.Clone(),
                    OneTimeQuestsAccepted = GameManager.Instance.QuestListsManager != null &&
                        GameManager.Instance.QuestListsManager.oneTimeQuestsAccepted != null
                            ? GameManager.Instance.QuestListsManager.oneTimeQuestsAccepted.ToArray()
                            : new string[0],
                    DiscoveryJson = SaveLoadManager.Serialize(
                        typeof(Dictionary<int, PlayerGPS.DiscoveredLocation>),
                        GameManager.Instance.PlayerGPS.GetDiscoverySaveData(),
                        false),
                    WorldVariationJson = SaveLoadManager.Serialize(
                        typeof(WorldDataVariants.WorldVariationData_v1),
                        WorldDataVariants.GetWorldVariationSaveData(),
                        false),
                    BankJson = CaptureBankAccountsJson(),
                    BankDeedJson = CaptureBankDeedsJson(),
                    EscortingFacesJson = CaptureEscortingFacesJson(),
                    LightSourceUID = GameManager.Instance.PlayerEntity.LightSource != null
                        ? GameManager.Instance.PlayerEntity.LightSource.UID
                        : 0UL
                };

                if (GameManager.Instance.GuildManager != null)
                {
                    envelope.GuildMemberships = FromMembershipData(
                        GameManager.Instance.GuildManager.GetMembershipData());
                    envelope.VampireMemberships = FromMembershipData(
                        GameManager.Instance.GuildManager.GetMembershipData(true));
                }

                SerializablePlayer serializablePlayer =
                    GameManager.Instance.PlayerObject != null
                        ? GameManager.Instance.PlayerObject.GetComponent<SerializablePlayer>()
                        : null;
                if (serializablePlayer != null)
                {
                    envelope.QuestAdjacentPlayerJson = SaveLoadManager.Serialize(
                        typeof(PlayerData_v1),
                        serializablePlayer.GetSaveData(),
                        false);
                }
            }
            catch (Exception ex)
            {
                envelope = null;
                reason = "quest state capture failed: " + ex.Message;
                return false;
            }

            return TryValidate(envelope, out reason);
        }

        public static string Encode(DFMPQuestStateEnvelope envelope)
        {
            if (envelope == null)
                return string.Empty;

            envelope.Normalize();
            return JsonUtility.ToJson(envelope, false);
        }

        public static bool TryDecode(string payload, out DFMPQuestStateEnvelope envelope, out string reason)
        {
            envelope = null;
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                reason = "quest state payload is empty";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(payload) > MaximumEnvelopeBytes)
            {
                reason = "quest state payload exceeds maximum size";
                return false;
            }

            try
            {
                envelope = JsonUtility.FromJson<DFMPQuestStateEnvelope>(payload);
            }
            catch (Exception ex)
            {
                reason = "quest state payload is malformed: " + ex.Message;
                return false;
            }

            return TryValidate(envelope, out reason);
        }

        public static bool TryValidate(DFMPQuestStateEnvelope envelope, out string reason)
        {
            reason = string.Empty;
            if (envelope == null)
            {
                reason = "quest state envelope is missing";
                return false;
            }

            if (!IsSupportedVersion(envelope.Version))
            {
                reason = "unsupported quest state version";
                return false;
            }

            envelope.Normalize();
            if (string.IsNullOrWhiteSpace(envelope.QuestMachineJson))
            {
                reason = "quest machine state is missing";
                return false;
            }

            if (SectionIsOversized(envelope.QuestMachineJson) ||
                SectionIsOversized(envelope.FactionJson) ||
                SectionIsOversized(envelope.ConversationJson) ||
                SectionIsOversized(envelope.NotebookJson) ||
                SectionIsOversized(envelope.DiscoveryJson) ||
                SectionIsOversized(envelope.WorldVariationJson) ||
                SectionIsOversized(envelope.QuestAdjacentPlayerJson) ||
                SectionIsOversized(envelope.BankJson) ||
                SectionIsOversized(envelope.BankDeedJson) ||
                SectionIsOversized(envelope.EscortingFacesJson))
            {
                reason = "quest state section exceeds maximum size";
                return false;
            }

            if (envelope.GlobalVars.Length > MaximumGlobalVarCount ||
                (envelope.SocialGroupReputations.Length != 0 &&
                 envelope.SocialGroupReputations.Length != 11) ||
                envelope.OneTimeQuestsAccepted.Length > MaximumOneTimeQuestCount ||
                envelope.GuildMemberships.Length > MaximumGuildMembershipCount ||
                envelope.VampireMemberships.Length > MaximumGuildMembershipCount)
            {
                reason = "quest-adjacent character state exceeds bounds";
                return false;
            }

            if (Encoding.UTF8.GetByteCount(Encode(envelope)) > MaximumEnvelopeBytes)
            {
                reason = "quest state envelope exceeds maximum size";
                return false;
            }

            return true;
        }

        public static bool TryRestore(DFMPQuestStateEnvelope envelope, out string reason)
        {
            if (!TryValidate(envelope, out reason))
                return false;

            if (!GameManager.HasInstance || !SaveLoadManager.HasInstance ||
                GameManager.Instance.PlayerEntity == null ||
                !QuestMachine.HasInstance || GameManager.Instance.TalkManager == null)
            {
                reason = "quest runtime is unavailable";
                return false;
            }

            SerializableStateManager stateManager = SaveLoadManager.StateManager;
            if (stateManager == null)
            {
                reason = "serializable state manager is unavailable";
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(envelope.FactionJson))
                {
                    var factionData = SaveLoadManager.Deserialize(
                        typeof(FactionData_v2),
                        envelope.FactionJson) as FactionData_v2;
                    stateManager.RestoreFactionData(factionData);
                }

                bool restoreGuildsFromAdjacent =
                    envelope.Version < DFMPQuestStateEnvelope.FirstClassGuildVersion;
                if (!restoreGuildsFromAdjacent && GameManager.Instance.GuildManager != null)
                {
                    GameManager.Instance.GuildManager.RestoreMembershipData(
                        ToMembershipData(envelope.GuildMemberships));
                    GameManager.Instance.GuildManager.RestoreMembershipData(
                        ToMembershipData(envelope.VampireMemberships),
                        true);
                }

                GameManager.Instance.PlayerEntity.GlobalVars.DeserializeGlobalVars(
                    DFMPQuestGlobalVar.ToGlobalVars(envelope.GlobalVars));
                if (envelope.SocialGroupReputations.Length == 11)
                {
                    GameManager.Instance.PlayerEntity.SGroupReputations =
                        (short[])envelope.SocialGroupReputations.Clone();
                }
                if (GameManager.Instance.QuestListsManager != null)
                {
                    GameManager.Instance.QuestListsManager.oneTimeQuestsAccepted =
                        new List<string>(envelope.OneTimeQuestsAccepted);
                }

                var questData = SaveLoadManager.Deserialize(
                    typeof(QuestMachine.QuestMachineData_v1),
                    envelope.QuestMachineJson,
                    true) as QuestMachine.QuestMachineData_v1;
                if (questData == null || questData.siteLinks == null || questData.quests == null)
                {
                    reason = "quest machine state is incomplete";
                    return false;
                }

                QuestMachine.Instance.ClearState();
                QuestMachine.Instance.RestoreSaveData(questData);

                if (!string.IsNullOrEmpty(envelope.ConversationJson))
                {
                    var conversationData = SaveLoadManager.Deserialize(
                        typeof(TalkManager.SaveDataConversation),
                        envelope.ConversationJson) as TalkManager.SaveDataConversation;
                    GameManager.Instance.TalkManager.RestoreConversationData(conversationData);
                }
                else
                {
                    GameManager.Instance.TalkManager.RestoreConversationData(null);
                }

                if (!string.IsNullOrEmpty(envelope.NotebookJson))
                {
                    var notebookData = SaveLoadManager.Deserialize(
                        typeof(PlayerNotebook.NotebookData_v1),
                        envelope.NotebookJson) as PlayerNotebook.NotebookData_v1;
                    if (notebookData != null)
                        GameManager.Instance.PlayerEntity.Notebook.RestoreNotebookData(notebookData);
                }

                if (!string.IsNullOrEmpty(envelope.DiscoveryJson))
                {
                    var discoveryData = SaveLoadManager.Deserialize(
                        typeof(Dictionary<int, PlayerGPS.DiscoveredLocation>),
                        envelope.DiscoveryJson) as Dictionary<int, PlayerGPS.DiscoveredLocation>;
                    if (discoveryData != null)
                        GameManager.Instance.PlayerGPS.RestoreDiscoveryData(discoveryData);
                }

                if (!string.IsNullOrEmpty(envelope.WorldVariationJson))
                {
                    var worldVariationData = SaveLoadManager.Deserialize(
                        typeof(WorldDataVariants.WorldVariationData_v1),
                        envelope.WorldVariationJson) as WorldDataVariants.WorldVariationData_v1;
                    if (worldVariationData != null)
                        WorldDataVariants.RestoreWorldVariationData(worldVariationData);
                }

                if (!string.IsNullOrEmpty(envelope.QuestAdjacentPlayerJson))
                {
                    var playerData = SaveLoadManager.Deserialize(
                        typeof(PlayerData_v1),
                        envelope.QuestAdjacentPlayerJson) as PlayerData_v1;
                    RestoreQuestAdjacentPlayerState(playerData, restoreGuildsFromAdjacent);
                }

                RestoreBankState(envelope);
                RestoreEscortingFaces(envelope);
            }
            catch (Exception ex)
            {
                reason = "quest state restore failed: " + ex.Message;
                return false;
            }

            return true;
        }

        static bool SectionIsOversized(string value)
        {
            return Encoding.UTF8.GetByteCount(value ?? string.Empty) > MaximumSectionBytes;
        }

        static string CaptureBankAccountsJson()
        {
            if (DaggerfallBankManager.BankAccounts == null)
                return string.Empty;

            var records = new List<BankRecordData_v1>();
            for (int index = 0; index < DaggerfallBankManager.BankAccounts.Length; index++)
            {
                BankRecordData_v1 record = DaggerfallBankManager.BankAccounts[index];
                if (record == null ||
                    (record.accountGold == 0 && record.loanTotal == 0 && record.loanDueDate == 0 && !record.hasDefaulted))
                    continue;
                records.Add(record);
            }

            return SaveLoadManager.Serialize(typeof(BankRecordData_v1[]), records.ToArray(), false);
        }

        static string CaptureBankDeedsJson()
        {
            var houses = new List<HouseData_v1>();
            if (DaggerfallBankManager.Houses != null)
            {
                for (int index = 0; index < DaggerfallBankManager.Houses.Length; index++)
                {
                    HouseData_v1 house = DaggerfallBankManager.Houses[index];
                    if (house == null || (house.mapID == 0 && house.buildingKey == 0))
                        continue;
                    houses.Add(house);
                }
            }

            var deeds = new BankDeedData_v1
            {
                shipType = (int)DaggerfallBankManager.OwnedShip,
                houses = houses.ToArray()
            };
            return SaveLoadManager.Serialize(typeof(BankDeedData_v1), deeds, false);
        }

        static string CaptureEscortingFacesJson()
        {
            if (DaggerfallUI.Instance == null || DaggerfallUI.Instance.DaggerfallHUD == null ||
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces == null)
                return string.Empty;

            FaceDetails[] faces = DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.GetSaveData();
            if (faces == null)
                return string.Empty;

            return SaveLoadManager.Serialize(typeof(FaceDetails[]), faces, false);
        }

        static void RestoreBankState(DFMPQuestStateEnvelope envelope)
        {
            DaggerfallBankManager.SetupAccounts();
            DaggerfallBankManager.SetupHouses();

            if (!string.IsNullOrEmpty(envelope.BankJson))
            {
                var bankData = SaveLoadManager.Deserialize(
                    typeof(BankRecordData_v1[]),
                    envelope.BankJson) as BankRecordData_v1[];
                if (bankData != null)
                {
                    for (int index = 0; index < bankData.Length; index++)
                    {
                        if (bankData[index] == null ||
                            bankData[index].regionIndex < 0 ||
                            bankData[index].regionIndex >= DaggerfallBankManager.BankAccounts.Length)
                            continue;
                        DaggerfallBankManager.BankAccounts[bankData[index].regionIndex] = bankData[index];
                    }
                }
            }

            if (string.IsNullOrEmpty(envelope.BankDeedJson))
            {
                DaggerfallBankManager.OwnedShip = ShipType.None;
                return;
            }

            var deedData = SaveLoadManager.Deserialize(
                typeof(BankDeedData_v1),
                envelope.BankDeedJson) as BankDeedData_v1;
            DaggerfallBankManager.OwnedShip = deedData == null
                ? ShipType.None
                : (ShipType)deedData.shipType;
            if (deedData == null || deedData.houses == null)
                return;

            for (int index = 0; index < deedData.houses.Length; index++)
            {
                HouseData_v1 house = deedData.houses[index];
                if (house == null ||
                    house.regionIndex < 0 ||
                    house.regionIndex >= DaggerfallBankManager.Houses.Length)
                    continue;
                DaggerfallBankManager.Houses[house.regionIndex] = house;
            }
        }

        static void RestoreEscortingFaces(DFMPQuestStateEnvelope envelope)
        {
            if (DaggerfallUI.Instance == null || DaggerfallUI.Instance.DaggerfallHUD == null ||
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces == null)
                return;

            if (string.IsNullOrEmpty(envelope.EscortingFacesJson))
            {
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.ClearFaces();
                return;
            }

            var faces = SaveLoadManager.Deserialize(
                typeof(FaceDetails[]),
                envelope.EscortingFacesJson) as FaceDetails[];
            if (faces == null)
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.ClearFaces();
            else
                DaggerfallUI.Instance.DaggerfallHUD.EscortingFaces.RestoreSaveData(faces);
        }

        static void RestoreQuestAdjacentPlayerState(PlayerData_v1 data, bool restoreGuilds)
        {
            if (data == null || data.playerEntity == null)
                return;

            var entity = GameManager.Instance.PlayerEntity;
            if (restoreGuilds && GameManager.Instance.GuildManager != null)
            {
                GameManager.Instance.GuildManager.RestoreMembershipData(data.guildMemberships);
                GameManager.Instance.GuildManager.RestoreMembershipData(data.vampireMemberships, true);
            }

            if (GameManager.Instance.QuestListsManager != null && data.oneTimeQuestsAccepted != null)
            {
                GameManager.Instance.QuestListsManager.oneTimeQuestsAccepted =
                    data.oneTimeQuestsAccepted;
            }

            entity.TimeOfLastSkillTraining = data.playerEntity.timeOfLastSkillTraining;
            entity.TimeOfLastSkillIncreaseCheck = data.playerEntity.timeOfLastSkillIncreaseCheck;
            if (data.playerEntity.skillUses != null &&
                data.playerEntity.skillUses.Length == DaggerfallSkills.Count)
                entity.SkillUses = data.playerEntity.skillUses;
            entity.SkillsRecentlyRaised = data.playerEntity.skillsRecentlyRaised != null
                ? data.playerEntity.skillsRecentlyRaised
                : new uint[2];
            entity.Reflexes = data.playerEntity.reflexes;
            if (data.playerEntity.resistances != null)
                entity.Resistances = data.playerEntity.resistances;
            entity.MinMetalToHit = data.playerEntity.minMetalToHit;
            entity.CurrentBreath = data.playerEntity.currentBreath;
            entity.BiographyResistDiseaseMod = data.playerEntity.biographyResistDiseaseMod;
            entity.BiographyResistMagicMod = data.playerEntity.biographyResistMagicMod;
            entity.BiographyAvoidHitMod = data.playerEntity.biographyAvoidHitMod;
            entity.BiographyResistPoisonMod = data.playerEntity.biographyResistPoisonMod;
            entity.BiographyFatigueMod = data.playerEntity.biographyFatigueMod;
            entity.BiographyReactionMod = data.playerEntity.biographyReactionMod;
            entity.TimeForThievesGuildLetter = data.playerEntity.timeForThievesGuildLetter;
            entity.TimeForDarkBrotherhoodLetter = data.playerEntity.timeForDarkBrotherhoodLetter;
            entity.ThievesGuildRequirementTally = data.playerEntity.thievesGuildRequirementTally;
            entity.DarkBrotherhoodRequirementTally = data.playerEntity.darkBrotherhoodRequirementTally;
            entity.LastTimePlayerAteOrDrankAtTavern = data.playerEntity.lastTimePlayerAteOrDrankAtTavern;
            entity.DaedraSummonDay = data.playerEntity.daedraSummonDay;
            entity.DaedraSummonIndex = data.playerEntity.daedraSummonIndex;
            entity.AnchorPosition = data.playerEntity.anchorPosition;
            entity.RentedRooms = data.playerEntity.rentedRooms != null
                ? new List<RoomRental_v1>(data.playerEntity.rentedRooms)
                : new List<RoomRental_v1>();

            if (data.playerEntity.wagonItems != null)
                entity.WagonItems.DeserializeItems(data.playerEntity.wagonItems);
            if (data.playerEntity.otherItems != null)
                entity.OtherItems.DeserializeItems(data.playerEntity.otherItems);

            entity.CrimeCommitted = data.playerEntity.crimeCommitted;
            entity.HaveShownSurrenderToGuardsDialogue = data.playerEntity.haveShownSurrenderToGuardsDialogue;
            if (data.playerEntity.regionData != null &&
                data.playerEntity.regionData.Length > 0 &&
                data.playerEntity.regionData[0].Values != null)
                entity.RegionData = data.playerEntity.regionData;

            entity.PreviousVampireClan = data.playerEntity.previousVampireClan;
            entity.TimeToBecomeVampireOrWerebeast = data.playerEntity.timeToBecomeVampireOrWerebeast;
            entity.DeserializeSpellbook(data.playerEntity.spellbook);
            if (GameManager.Instance.PlayerEffectManager != null)
                GameManager.Instance.PlayerEffectManager.RestoreInstancedBundleSaveData(data.playerEntity.instancedEffectBundles);
        }
    }
}

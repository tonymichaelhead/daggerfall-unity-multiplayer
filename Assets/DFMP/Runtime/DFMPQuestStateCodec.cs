using System;
using System.Collections.Generic;
using System.Text;
using DaggerfallConnect.FallExe;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
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
    public class DFMPQuestStateEnvelope
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string QuestMachineJson = string.Empty;
        public string FactionJson = string.Empty;
        public string ConversationJson = string.Empty;
        public string NotebookJson = string.Empty;
        public string DiscoveryJson = string.Empty;
        public string WorldVariationJson = string.Empty;
        public string QuestAdjacentPlayerJson = string.Empty;
        public DFMPQuestGlobalVar[] GlobalVars = new DFMPQuestGlobalVar[0];
        public short[] SocialGroupReputations = new short[0];
        public string[] OneTimeQuestsAccepted = new string[0];

        public void Normalize()
        {
            QuestMachineJson = QuestMachineJson ?? string.Empty;
            FactionJson = FactionJson ?? string.Empty;
            ConversationJson = ConversationJson ?? string.Empty;
            NotebookJson = NotebookJson ?? string.Empty;
            DiscoveryJson = DiscoveryJson ?? string.Empty;
            WorldVariationJson = WorldVariationJson ?? string.Empty;
            QuestAdjacentPlayerJson = QuestAdjacentPlayerJson ?? string.Empty;
            GlobalVars = GlobalVars ?? new DFMPQuestGlobalVar[0];
            SocialGroupReputations = SocialGroupReputations ?? new short[0];
            OneTimeQuestsAccepted = OneTimeQuestsAccepted ?? new string[0];
        }
    }

    public static class DFMPQuestStateCodec
    {
        public const int MaximumEnvelopeBytes = 4 * 1024 * 1024;
        public const int MaximumSectionBytes = 2 * 1024 * 1024;
        public const int MaximumGlobalVarCount = 1024;
        public const int MaximumOneTimeQuestCount = 4096;

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
                        false)
                };

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

            if (envelope.Version != DFMPQuestStateEnvelope.CurrentVersion)
            {
                reason = "unsupported quest state version";
                return false;
            }

            envelope.QuestMachineJson = envelope.QuestMachineJson ?? string.Empty;
            envelope.FactionJson = envelope.FactionJson ?? string.Empty;
            envelope.ConversationJson = envelope.ConversationJson ?? string.Empty;
            envelope.NotebookJson = envelope.NotebookJson ?? string.Empty;
            envelope.DiscoveryJson = envelope.DiscoveryJson ?? string.Empty;
            envelope.WorldVariationJson = envelope.WorldVariationJson ?? string.Empty;
            envelope.QuestAdjacentPlayerJson = envelope.QuestAdjacentPlayerJson ?? string.Empty;
            envelope.GlobalVars = envelope.GlobalVars ?? new DFMPQuestGlobalVar[0];
            envelope.SocialGroupReputations = envelope.SocialGroupReputations ?? new short[0];
            envelope.OneTimeQuestsAccepted = envelope.OneTimeQuestsAccepted ?? new string[0];
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
                SectionIsOversized(envelope.QuestAdjacentPlayerJson))
            {
                reason = "quest state section exceeds maximum size";
                return false;
            }

            if (envelope.GlobalVars.Length > MaximumGlobalVarCount ||
                (envelope.SocialGroupReputations.Length != 0 &&
                 envelope.SocialGroupReputations.Length != 11) ||
                envelope.OneTimeQuestsAccepted.Length > MaximumOneTimeQuestCount)
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
                    RestoreQuestAdjacentPlayerState(playerData);
                }
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

        static void RestoreQuestAdjacentPlayerState(PlayerData_v1 data)
        {
            if (data == null || data.playerEntity == null)
                return;

            var entity = GameManager.Instance.PlayerEntity;
            GameManager.Instance.GuildManager.RestoreMembershipData(data.guildMemberships);
            GameManager.Instance.GuildManager.RestoreMembershipData(data.vampireMemberships, true);
            GameManager.Instance.QuestListsManager.oneTimeQuestsAccepted =
                data.oneTimeQuestsAccepted ?? new List<string>();

            entity.TimeOfLastSkillTraining = data.playerEntity.timeOfLastSkillTraining;
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

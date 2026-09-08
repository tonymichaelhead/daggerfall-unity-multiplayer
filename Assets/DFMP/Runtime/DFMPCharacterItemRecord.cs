using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPCharacterItemRecord
    {
        public ulong ItemId;
        public int ItemGroup;
        public int TemplateIndex;
        public int StackCount = 1;
        public int CurrentCondition;
        public int MaxCondition;
        public int EnchantmentPoints;
        public string ClassName = string.Empty;
        public string ShortName = string.Empty;
        public int NativeMaterialValue;
        public int DyeColor;
        public int CurrentVariant;
        public int Message;
        public int[] LegacyMagic = new int[0];
        public int PlayerTextureArchive;
        public int PlayerTextureRecord;
        public int WorldTextureArchive;
        public int WorldTextureRecord;
        public float WeightInKg;
        public int DrawOrder;
        public int Value;
        public int PackedFlags;
        public int PackedTypeData;
        public bool IsQuestItem;
        public int TrappedSoulType;
        public int PoisonType;
        public int PotionRecipe;
        public int ArtifactIndexBitfield;
        public uint TimeForItemToDisappear;
        public uint TimeHealthLeechLastUsed;

        public static DFMPCharacterItemRecord FromItemData(ItemData_v1 data)
        {
            if (data == null)
                return null;

            var record = new DFMPCharacterItemRecord
            {
                ItemId = data.uid,
                ItemGroup = (int)data.itemGroup,
                TemplateIndex = data.groupIndex,
                StackCount = data.stackCount,
                CurrentCondition = data.hits1,
                MaxCondition = data.hits2,
                EnchantmentPoints = data.enchantmentPoints,
                ClassName = data.className,
                ShortName = data.shortName,
                NativeMaterialValue = data.nativeMaterialValue,
                DyeColor = (int)data.dyeColor,
                CurrentVariant = data.currentVariant,
                Message = data.message,
                LegacyMagic = data.legacyMagic,
                PlayerTextureArchive = data.playerTextureArchive,
                PlayerTextureRecord = data.playerTextureRecord,
                WorldTextureArchive = data.worldTextureArchive,
                WorldTextureRecord = data.worldTextureRecord,
                WeightInKg = data.weightInKg,
                DrawOrder = data.drawOrder,
                Value = data.value1,
                PackedFlags = data.value2,
                PackedTypeData = data.hits3,
                IsQuestItem = data.isQuestItem,
                TrappedSoulType = (int)data.trappedSoulType,
                PoisonType = (int)data.poisonType,
                PotionRecipe = data.potionRecipe,
                ArtifactIndexBitfield = data.artifactIndexBitfield,
                TimeForItemToDisappear = data.timeForItemToDisappear,
                TimeHealthLeechLastUsed = data.timeHealthLeechLastUsed
            };
            record.Normalize();
            return record;
        }

        public ItemData_v1 ToItemData()
        {
            Normalize();
            return new ItemData_v1
            {
                uid = ItemId,
                itemGroup = (ItemGroups)ItemGroup,
                groupIndex = TemplateIndex,
                stackCount = StackCount,
                hits1 = CurrentCondition,
                hits2 = MaxCondition,
                enchantmentPoints = EnchantmentPoints,
                className = ClassName,
                shortName = ShortName,
                nativeMaterialValue = NativeMaterialValue,
                dyeColor = (DyeColors)DyeColor,
                currentVariant = CurrentVariant,
                message = Message,
                // DFU treats legacyMagic as null-or-populated and indexes [0] without a length check.
                legacyMagic = LegacyMagic.Length == 0 ? null : LegacyMagic,
                playerTextureArchive = PlayerTextureArchive,
                playerTextureRecord = PlayerTextureRecord,
                worldTextureArchive = WorldTextureArchive,
                worldTextureRecord = WorldTextureRecord,
                weightInKg = WeightInKg,
                drawOrder = DrawOrder,
                value1 = Value,
                value2 = PackedFlags,
                hits3 = PackedTypeData,
                isQuestItem = IsQuestItem,
                trappedSoulType = (MobileTypes)TrappedSoulType,
                poisonType = (Poisons)PoisonType,
                potionRecipe = PotionRecipe,
                artifactIndexBitfield = ArtifactIndexBitfield,
                timeForItemToDisappear = TimeForItemToDisappear,
                timeHealthLeechLastUsed = TimeHealthLeechLastUsed
            };
        }

        public void Normalize()
        {
            StackCount = Mathf.Max(1, StackCount);
            CurrentCondition = Mathf.Max(0, CurrentCondition);
            MaxCondition = Mathf.Max(CurrentCondition, MaxCondition);
            EnchantmentPoints = Mathf.Max(0, EnchantmentPoints);
            ClassName = ClassName ?? string.Empty;
            ShortName = ShortName ?? string.Empty;
            LegacyMagic = LegacyMagic ?? new int[0];
            // DFU reads legacyMagic as type/param pairs, so an odd tail would desync every enchantment.
            if (LegacyMagic.Length % 2 != 0)
                LegacyMagic = new int[0];
        }
    }

    [Serializable]
    public class DFMPCharacterEquipmentRecord
    {
        public int EquipSlot;
        public ulong ItemId;
    }
}

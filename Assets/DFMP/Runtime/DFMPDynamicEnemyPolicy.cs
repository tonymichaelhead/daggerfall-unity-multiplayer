using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using UnityEngine;

namespace DFMP.Runtime
{
    public enum DFMPDynamicEnemyProviderKind
    {
        Dungeon,
        Wilderness,
        CityNight,
        Mod
    }

    public enum DFMPDynamicEnemyLifecycleState
    {
        SpawnedAlive,
        DespawnedAlive,
        Dead,
        Retired
    }

    public enum DFMPDynamicEnemyRegistryResult
    {
        Accepted,
        AlreadyRegistered,
        InvalidAuthority,
        InvalidRoster,
        ConflictingRoster,
        MissingEnemy,
        InvalidTransition
    }

    [Serializable]
    public struct DFMPDynamicEnemyDescriptor : IEquatable<DFMPDynamicEnemyDescriptor>
    {
        public Vector3 DungeonLocalPosition;
        public float FacingYaw;
        public int MobileType;
        public int Gender;
        public int Reaction;

        public bool Equals(DFMPDynamicEnemyDescriptor other)
        {
            return DungeonLocalPosition == other.DungeonLocalPosition &&
                Mathf.Approximately(FacingYaw, other.FacingYaw) &&
                MobileType == other.MobileType &&
                Gender == other.Gender &&
                Reaction == other.Reaction;
        }

        public override bool Equals(object obj)
        {
            return obj is DFMPDynamicEnemyDescriptor && Equals((DFMPDynamicEnemyDescriptor)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = DungeonLocalPosition.GetHashCode();
                hash = (hash * 397) ^ FacingYaw.GetHashCode();
                hash = (hash * 397) ^ MobileType;
                hash = (hash * 397) ^ Gender;
                hash = (hash * 397) ^ Reaction;
                return hash;
            }
        }
    }

    public struct DFMPDynamicEncounterKey : IEquatable<DFMPDynamicEncounterKey>
    {
        public DFMPDynamicEnemyProviderKind ProviderKind;
        public DFMPWorldContextKey Context;
        public string EncounterId;

        public bool Equals(DFMPDynamicEncounterKey other)
        {
            return ProviderKind == other.ProviderKind &&
                Context.Equals(other.Context) &&
                string.Equals(EncounterId ?? string.Empty, other.EncounterId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is DFMPDynamicEncounterKey && Equals((DFMPDynamicEncounterKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)ProviderKind;
                hash = (hash * 397) ^ Context.GetHashCode();
                hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(EncounterId ?? string.Empty);
                return hash;
            }
        }
    }

    public struct DFMPDynamicEnemyIdentity : IEquatable<DFMPDynamicEnemyIdentity>
    {
        public DFMPDynamicEncounterKey Encounter;
        public int RosterIndex;
        public string EnemyId;

        public bool Equals(DFMPDynamicEnemyIdentity other)
        {
            return Encounter.Equals(other.Encounter) &&
                RosterIndex == other.RosterIndex &&
                string.Equals(EnemyId ?? string.Empty, other.EnemyId ?? string.Empty, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DFMPDynamicEnemyIdentity && Equals((DFMPDynamicEnemyIdentity)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Encounter.GetHashCode();
                hash = (hash * 397) ^ RosterIndex;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EnemyId ?? string.Empty);
                return hash;
            }
        }
    }

    public struct DFMPDynamicEnemyRecord
    {
        public DFMPDynamicEnemyIdentity Identity;
        public DFMPDynamicEnemyLifecycleState LifecycleState;
        public DFMPDynamicEnemyDescriptor Descriptor;
        public int Health;
        public int MaxHealth;
        public int TargetConnectionId;
        public bool IsMoving;
    }

    public struct DFMPDynamicEnemySensoryTarget
    {
        public int ConnectionId;
        public Vector3 DungeonLocalPosition;
        public bool SpawnConfirmed;
        public bool IsDead;
        public bool HasLineOfSight;
    }

    public struct DFMPDynamicEnemyAiInput
    {
        public Vector3 EnemyPosition;
        public float FacingYaw;
        public int CurrentTargetConnectionId;
        public DFMPDynamicEnemySensoryTarget[] Targets;
        public float AwarenessRange;
        public float AttackRange;
        public float MoveSpeed;
        public float DeltaTime;
        public bool RequireLineOfSight;
        public Vector3 HomePosition;
        public float PursuitLeashRange;
        public int IgnoredTargetConnectionId;
        public bool IsPassive;
    }

    public struct DFMPDynamicEnemyAiDecision
    {
        public bool HasTarget;
        public int TargetConnectionId;
        public Vector3 NextDungeonLocalPosition;
        public float FacingYaw;
        public bool IsMoving;
        public bool InAttackRange;
    }

    public enum DFMPDynamicEnemyAttackKind
    {
        Melee,
        Ranged,
        Magic
    }

    public struct DFMPDynamicEnemyAttackProfile
    {
        public DFMPDynamicEnemyAttackKind Kind;
        public float Range;
    }

    public static class DFMPDynamicEnemyAttackPolicy
    {
        public static DFMPDynamicEnemyAttackProfile GetProfile(int mobileType, float meleeRange, float rangedRange, float magicRange)
        {
            MobileTypes type = (MobileTypes)mobileType;
            if (type == MobileTypes.OrcShaman || type == MobileTypes.Mage || type == MobileTypes.Battlemage || type == MobileTypes.Sorcerer || type == MobileTypes.Lich || type == MobileTypes.AncientLich)
            {
                return new DFMPDynamicEnemyAttackProfile
                {
                    Kind = DFMPDynamicEnemyAttackKind.Magic,
                    Range = Mathf.Max(0f, magicRange)
                };
            }

            if (type == MobileTypes.Harpy || type == MobileTypes.Archer || type == MobileTypes.Ranger)
            {
                return new DFMPDynamicEnemyAttackProfile
                {
                    Kind = DFMPDynamicEnemyAttackKind.Ranged,
                    Range = Mathf.Max(0f, rangedRange)
                };
            }

            return new DFMPDynamicEnemyAttackProfile
            {
                Kind = DFMPDynamicEnemyAttackKind.Melee,
                Range = Mathf.Max(0f, meleeRange)
            };
        }
    }

    public static class DFMPDynamicEnemyAiPolicy
    {
        public static DFMPDynamicEnemyAiDecision Evaluate(DFMPDynamicEnemyAiInput input)
        {
            var decision = new DFMPDynamicEnemyAiDecision
            {
                TargetConnectionId = -1,
                NextDungeonLocalPosition = input.EnemyPosition,
                FacingYaw = NormalizeYaw(input.FacingYaw)
            };

            int targetIndex;
            if (input.IsPassive && input.CurrentTargetConnectionId <= 0)
                return decision;

            float leashRange = Mathf.Max(0f, input.PursuitLeashRange);
            if (leashRange > 0f && GetPlanarDistanceSquared(input.EnemyPosition, input.HomePosition) > leashRange * leashRange)
            {
                Vector3 returnOffset = input.HomePosition - input.EnemyPosition;
                returnOffset.y = 0f;
                float returnDistance = returnOffset.magnitude;
                decision.FacingYaw = returnDistance > 0.0001f ? YawFromDirection(returnOffset) : decision.FacingYaw;
                if (returnDistance > 0.0001f && input.MoveSpeed > 0f && input.DeltaTime > 0f)
                {
                    float returnStep = Mathf.Min(input.MoveSpeed * input.DeltaTime, returnDistance);
                    decision.NextDungeonLocalPosition = input.EnemyPosition + returnOffset / returnDistance * returnStep;
                    decision.IsMoving = returnStep > 0.0001f;
                }

                return decision;
            }

            if (!TrySelectTarget(input, out targetIndex))
                return decision;

            Vector3 targetPosition = input.Targets[targetIndex].DungeonLocalPosition;
            Vector3 offset = targetPosition - input.EnemyPosition;
            offset.y = 0f;
            float distance = offset.magnitude;

            decision.HasTarget = true;
            decision.TargetConnectionId = input.Targets[targetIndex].ConnectionId;
            decision.FacingYaw = distance > 0.0001f ? YawFromDirection(offset) : decision.FacingYaw;
            decision.InAttackRange = distance <= Mathf.Max(0f, input.AttackRange);

            if (!decision.InAttackRange && distance > 0.0001f && input.MoveSpeed > 0f && input.DeltaTime > 0f)
            {
                float step = Mathf.Min(input.MoveSpeed * input.DeltaTime, Mathf.Max(0f, distance - Mathf.Max(0f, input.AttackRange)));
                decision.NextDungeonLocalPosition = input.EnemyPosition + offset / distance * step;
                decision.NextDungeonLocalPosition = ConstrainToPursuitLeash(input, decision.NextDungeonLocalPosition);
                decision.IsMoving = step > 0.0001f;
            }

            return decision;
        }

        static Vector3 ConstrainToPursuitLeash(DFMPDynamicEnemyAiInput input, Vector3 desiredPosition)
        {
            float leashRange = Mathf.Max(0f, input.PursuitLeashRange);
            if (leashRange <= 0f)
                return desiredPosition;

            Vector3 offset = desiredPosition - input.HomePosition;
            offset.y = 0f;
            if (offset.sqrMagnitude <= leashRange * leashRange)
                return desiredPosition;

            Vector3 constrainedPosition = input.HomePosition + offset.normalized * leashRange;
            constrainedPosition.y = desiredPosition.y;
            return constrainedPosition;
        }

        static bool TrySelectTarget(DFMPDynamicEnemyAiInput input, out int targetIndex)
        {
            targetIndex = -1;
            if (input.Targets == null || input.Targets.Length == 0 || input.AwarenessRange <= 0f)
                return false;

            float maximumDistanceSquared = input.AwarenessRange * input.AwarenessRange;
            if (input.IsPassive)
            {
                for (int index = 0; index < input.Targets.Length; index++)
                {
                    if (input.Targets[index].ConnectionId == input.CurrentTargetConnectionId && IsValidTarget(input, index, maximumDistanceSquared))
                    {
                        targetIndex = index;
                        return true;
                    }
                }

                return false;
            }

            for (int index = 0; index < input.Targets.Length; index++)
            {
                if (input.Targets[index].ConnectionId == input.CurrentTargetConnectionId && !IsIgnoredTarget(input, index) && IsValidTarget(input, index, maximumDistanceSquared))
                {
                    targetIndex = index;
                    return true;
                }
            }

            float bestDistanceSquared = float.MaxValue;
            for (int index = 0; index < input.Targets.Length; index++)
            {
                if (IsIgnoredTarget(input, index) || !IsValidTarget(input, index, maximumDistanceSquared))
                    continue;

                float distanceSquared = GetPlanarDistanceSquared(input.EnemyPosition, input.Targets[index].DungeonLocalPosition);
                if (distanceSquared > maximumDistanceSquared || distanceSquared >= bestDistanceSquared)
                    continue;

                bestDistanceSquared = distanceSquared;
                targetIndex = index;
            }

            return targetIndex >= 0;
        }

        static bool IsIgnoredTarget(DFMPDynamicEnemyAiInput input, int targetIndex)
        {
            return input.Targets[targetIndex].ConnectionId == input.IgnoredTargetConnectionId;
        }

        static bool IsValidTarget(DFMPDynamicEnemyAiInput input, int targetIndex, float maximumDistanceSquared)
        {
            DFMPDynamicEnemySensoryTarget target = input.Targets[targetIndex];
            if (target.ConnectionId <= 0 || !target.SpawnConfirmed || target.IsDead)
                return false;
            if (input.RequireLineOfSight && !target.HasLineOfSight)
                return false;

            return GetPlanarDistanceSquared(input.EnemyPosition, target.DungeonLocalPosition) <= maximumDistanceSquared;
        }

        static float GetPlanarDistanceSquared(Vector3 first, Vector3 second)
        {
            Vector3 offset = second - first;
            offset.y = 0f;
            return offset.sqrMagnitude;
        }

        static float YawFromDirection(Vector3 direction)
        {
            return NormalizeYaw(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
        }

        static float NormalizeYaw(float yaw)
        {
            yaw %= 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }
    }

    public static class DFMPDungeonRosterPolicy
    {
        public struct NativeDungeonGenerationInputs
        {
            public DFRegion.DungeonTypes DungeonType;
            public int DungeonRecordId;
            public int BlockSeed;
            public int BlockX;
            public int BlockZ;
            public int WaterLevel;
        }

        public struct NativeDungeonMarker
        {
            public Vector3 DungeonLocalPosition;
            public int TextureRecord;
            public int MarkerY;
            public int FixedMobileType;
            public byte SoundIndex;
            public int ClassicSlot;
            public bool IsCustomData;
            public int Reaction;

            public bool IsFixedMonster
            {
                get { return TextureRecord == FixedMonsterTextureRecord; }
            }
        }

        public const int MaximumRosterSize = 64;
        public const int SpawnMarkerTextureArchive = 199;
        public const int SpawnMarkerTextureRecord = 11;
        public const int RandomMonsterTextureRecord = 15;
        public const int FixedMonsterTextureRecord = 16;
        public const int ItemMarkerTextureRecord = 18;
        public const int StartMarkerTextureRecord = 10;
        public const int EnterMarkerTextureRecord = 8;

        public static bool IsSpawnMarker(int archive, int record)
        {
            if (archive != SpawnMarkerTextureArchive)
                return false;

            return record == RandomMonsterTextureRecord ||
                record == FixedMonsterTextureRecord ||
                record == SpawnMarkerTextureRecord ||
                record == ItemMarkerTextureRecord ||
                record == StartMarkerTextureRecord ||
                record == EnterMarkerTextureRecord;
        }

        public static readonly MobileTypes[] CuratedDungeonMobileTypes = new MobileTypes[]
        {
            MobileTypes.Rat,
            MobileTypes.GiantBat,
            MobileTypes.Spider,
            MobileTypes.SkeletalWarrior,
            MobileTypes.Zombie,
            MobileTypes.Orc,
            MobileTypes.Harpy,
            MobileTypes.OrcShaman,
            MobileTypes.Lich
        };

        public static bool TryResolveNativeEnemyType(
            DFRegion.DungeonTypes dungeonType,
            bool isFixedMarker,
            int fixedMobileType,
            int markerY,
            int waterLevel,
            float monsterPower,
            int monsterVariance,
            ulong nativeSeed,
            int markerOrdinal,
            out int mobileType)
        {
            mobileType = 0;
            if (isFixedMarker)
            {
                mobileType = fixedMobileType;
                return fixedMobileType >= 0 && fixedMobileType < 43 || fixedMobileType >= 128 && fixedMobileType <= 146;
            }

            int dungeonIndex = (int)dungeonType;
            if (dungeonIndex < 0 || dungeonIndex >= RandomEncounters.EncounterTables.Length)
                return false;

            bool useWaterTable = waterLevel < markerY;
            RandomEncounterTable table = useWaterTable
                ? RandomEncounters.EncounterTables[19]
                : RandomEncounters.EncounterTables[dungeonIndex];
            if (table.Enemies == null || table.Enemies.Length == 0)
                return false;

            int baseMonsterIndex = (int)(table.Enemies.Length * Mathf.Clamp01(monsterPower));
            int minimumIndex = Math.Max(0, baseMonsterIndex - Math.Max(0, monsterVariance));
            int maximumIndex = Math.Min(table.Enemies.Length - 1, baseMonsterIndex + Math.Max(0, monsterVariance));
            if (minimumIndex > maximumIndex)
                return false;

            ulong hash = ComputeStableHash(string.Format(
                CultureInfo.InvariantCulture,
                "dfmp-native-encounter-v1|{0}|{1}|{2}|{3}",
                nativeSeed,
                (int)dungeonType,
                markerOrdinal,
                markerY));
            int selectedIndex = minimumIndex + (int)(hash % (ulong)(maximumIndex - minimumIndex + 1));
            mobileType = (int)table.Enemies[selectedIndex];
            return true;
        }

        public static float CalculateNativeMonsterPower(int playerLevel)
        {
            return Mathf.Clamp01(Mathf.Max(1, playerLevel) / 20f);
        }

        static bool IsValidNativeMobileType(int mobileType)
        {
            return mobileType >= 0 && mobileType < 43 || mobileType >= 128 && mobileType <= 146;
        }

        public static bool TryResolveNativeDungeonGenerationInputs(
            DFLocation location,
            DFMPWorldContextKey context,
            out NativeDungeonGenerationInputs inputs)
        {
            inputs = new NativeDungeonGenerationInputs();
            if (context.Kind != DFMPWorldContextKind.Dungeon ||
                !location.Loaded ||
                !location.HasDungeon ||
                location.Dungeon.Blocks == null ||
                context.DungeonBlockIndex < 0 ||
                context.DungeonBlockIndex >= location.Dungeon.Blocks.Length)
                return false;

            DFLocation.DungeonBlock block = location.Dungeon.Blocks[context.DungeonBlockIndex];
            if (!string.IsNullOrWhiteSpace(context.DungeonBlockName) &&
                !string.Equals(block.BlockName, context.DungeonBlockName, StringComparison.OrdinalIgnoreCase))
                return false;

            inputs = new NativeDungeonGenerationInputs
            {
                DungeonType = location.MapTableData.DungeonType,
                DungeonRecordId = location.Dungeon.RecordElement.Header.LocationId,
                BlockSeed = CreateNativeBlockSeed(location.MapTableData.MapId, context.DungeonBlockIndex),
                BlockX = block.X,
                BlockZ = block.Z,
                WaterLevel = block.WaterLevel
            };
            return true;
        }

        public static bool TryCreateNativeDescriptors(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            NativeDungeonGenerationInputs inputs,
            NativeDungeonMarker[] markers,
            float monsterPower,
            int monsterVariance,
            out DFMPDynamicEnemyDescriptor[] descriptors)
        {
            descriptors = new DFMPDynamicEnemyDescriptor[0];
            if (markers == null || markers.Length == 0 || monsterPower < 0f || monsterPower > 1f || monsterVariance < 0)
                return false;

            var nativeDescriptors = new List<DFMPDynamicEnemyDescriptor>();
            MobileTypes[] nonWaterEnemies;
            MobileTypes[] waterEnemies;
            DFRandom.SaveSeed();
            UnityEngine.Random.State savedUnityRandomState = UnityEngine.Random.state;
            try
            {
                if (!TryCreateClassicEncounterSlots(inputs, monsterPower, monsterVariance, out nonWaterEnemies, out waterEnemies))
                    return false;

                DFRandom.srand(inputs.DungeonRecordId);
                UnityEngine.Random.InitState(inputs.BlockSeed);
                for (int index = 0; index < markers.Length; index++)
                {
                    NativeDungeonMarker marker = markers[index];
                    bool isRandomMarker = marker.TextureRecord == RandomMonsterTextureRecord;
                    bool isFixedMarker = marker.TextureRecord == FixedMonsterTextureRecord;
                    if (!isRandomMarker && !isFixedMarker)
                        continue;

                    int mobileType;
                    if (isFixedMarker)
                    {
                        if (!IsValidNativeMobileType(marker.FixedMobileType))
                            continue;
                        mobileType = marker.FixedMobileType;
                    }
                    else
                    {
                        int slot = marker.ClassicSlot == 0 ? UnityEngine.Random.Range(1, 7) : marker.ClassicSlot;
                        if (slot < 0 || slot >= nonWaterEnemies.Length)
                            return false;

                        bool underwater = inputs.WaterLevel < marker.MarkerY;
                        mobileType = (int)(underwater ? waterEnemies[slot] : nonWaterEnemies[slot]);
                    }

                    if (!ShouldSpawnNativeEnemy((MobileTypes)mobileType, inputs.WaterLevel, marker.MarkerY))
                        continue;

                    ulong descriptorSeed = ComputeStableHash(string.Format(
                        CultureInfo.InvariantCulture,
                        "dfmp-native-descriptor-v1|{0}|{1}|{2}",
                        serverWorldSeed,
                        CreateDungeonSeedMaterial(serverWorldSeed, context),
                        index));
                    nativeDescriptors.Add(new DFMPDynamicEnemyDescriptor
                    {
                        DungeonLocalPosition = marker.DungeonLocalPosition,
                        FacingYaw = (float)((descriptorSeed >> 16) % 360UL),
                        MobileType = mobileType,
                        Gender = isFixedMarker ? GetNativeGender(marker, mobileType) : (int)MobileGender.Unspecified,
                        Reaction = isFixedMarker ? marker.Reaction : (int)DFBlock.EnemyReactionTypes.Hostile
                    });
                }
            }
            finally
            {
                DFRandom.RestoreSeed();
                UnityEngine.Random.state = savedUnityRandomState;
            }

            if (nativeDescriptors.Count == 0)
                return false;

            descriptors = nativeDescriptors.ToArray();
            return true;
        }

        public static bool ShouldSpawnNativeEnemy(MobileTypes mobileType, int waterLevel, int markerY)
        {
            return mobileType != MobileTypes.Slaughterfish &&
                mobileType != MobileTypes.Dreugh &&
                mobileType != MobileTypes.Lamia ||
                waterLevel != 10000 && waterLevel - 20 <= markerY;
        }

        static int GetNativeGender(NativeDungeonMarker marker, int mobileType)
        {
            if (mobileType <= 43)
                return (int)MobileGender.Unspecified;

            if ((marker.ClassicSlot & (int)DFBlock.EnemyGenders.Female) == (int)DFBlock.EnemyGenders.Female)
                return (int)MobileGender.Female;
            if ((marker.ClassicSlot & (int)DFBlock.EnemyGenders.Male) == (int)DFBlock.EnemyGenders.Male)
                return (int)MobileGender.Male;
            return (int)MobileGender.Unspecified;
        }

        static int CreateNativeBlockSeed(int locationMapId, int dungeonBlockIndex)
        {
            unchecked
            {
                return locationMapId ^ (dungeonBlockIndex * 397);
            }
        }

        static bool TryCreateClassicEncounterSlots(
            NativeDungeonGenerationInputs inputs,
            float monsterPower,
            int monsterVariance,
            out MobileTypes[] nonWaterEnemies,
            out MobileTypes[] waterEnemies)
        {
            nonWaterEnemies = new MobileTypes[0];
            waterEnemies = new MobileTypes[0];
            int dungeonIndex = (int)inputs.DungeonType;
            if (dungeonIndex < 0 || dungeonIndex >= RandomEncounters.EncounterTables.Length)
                return false;

            int playerLevel = Mathf.Max(1, Mathf.RoundToInt(monsterPower * 20f));
            nonWaterEnemies = new MobileTypes[256];
            waterEnemies = new MobileTypes[256];
            DFRandom.srand(inputs.DungeonRecordId);
            for (int index = 0; index < 256; index++)
                nonWaterEnemies[index] = ChooseClassicRandomEnemyType(RandomEncounters.EncounterTables[dungeonIndex], playerLevel);

            for (int index = 0; index < 256; index++)
                waterEnemies[index] = ChooseClassicRandomEnemyType(RandomEncounters.EncounterTables[19], playerLevel);

            return true;
        }

        static MobileTypes ChooseClassicRandomEnemyType(RandomEncounterTable table, int playerLevel)
        {
            int minimumIndex = 0;
            int maximumIndex = table.Enemies.Length;
            int random = DFRandom.random_range_inclusive(1, 100);
            if (random > 95 && playerLevel <= 5)
                maximumIndex = playerLevel + 2;
            else if (random > 80)
                maximumIndex = playerLevel + 1;
            else
            {
                minimumIndex = playerLevel - 3;
                maximumIndex = playerLevel + 3;
            }

            if (minimumIndex < 0)
            {
                minimumIndex = 0;
                maximumIndex = 5;
            }
            else if (maximumIndex > 19)
            {
                minimumIndex = 14;
                maximumIndex = 19;
            }

            maximumIndex = Math.Min(maximumIndex, table.Enemies.Length - 1);
            return table.Enemies[DFRandom.random_range_inclusive(minimumIndex, maximumIndex)];
        }

        public static bool TryCreateRosterFromDescriptors(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            DFMPDynamicEnemyDescriptor[] descriptors,
            out DFMPDynamicEnemyRecord[] roster)
        {
            roster = new DFMPDynamicEnemyRecord[0];
            DFMPDynamicEncounterKey encounter;
            if (!TryCreateEncounterKey(serverWorldSeed, context, out encounter) ||
                descriptors == null ||
                descriptors.Length == 0 ||
                descriptors.Length > MaximumRosterSize)
                return false;

            roster = new DFMPDynamicEnemyRecord[descriptors.Length];
            for (int index = 0; index < descriptors.Length; index++)
            {
                int health = GetInitialEnemyHealth(descriptors[index].MobileType);
                roster[index] = new DFMPDynamicEnemyRecord
                {
                    Identity = new DFMPDynamicEnemyIdentity
                    {
                        Encounter = encounter,
                        RosterIndex = index,
                        EnemyId = encounter.EncounterId + "-enemy-" + index.ToString(CultureInfo.InvariantCulture)
                    },
                    LifecycleState = DFMPDynamicEnemyLifecycleState.SpawnedAlive,
                    Descriptor = descriptors[index],
                    Health = health,
                    MaxHealth = health
                };
            }

            return true;
        }

        public static bool TryCreateNativeRosterFromMarkers(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            NativeDungeonGenerationInputs inputs,
            NativeDungeonMarker[] markers,
            float monsterPower,
            int monsterVariance,
            out DFMPDynamicEnemyRecord[] roster)
        {
            roster = new DFMPDynamicEnemyRecord[0];
            DFMPDynamicEnemyDescriptor[] descriptors;
            if (!TryCreateNativeDescriptors(serverWorldSeed, context, inputs, markers, monsterPower, monsterVariance, out descriptors))
                return false;

            return TryCreateRosterFromDescriptors(serverWorldSeed, context, descriptors, out roster);
        }

        public static ulong CreateServerWorldSeed(string configuredSeed)
        {
            return ComputeStableHash(string.IsNullOrWhiteSpace(configuredSeed) ? "default" : configuredSeed.Trim());
        }

        public static bool TryCreateEncounterKey(ulong serverWorldSeed, DFMPWorldContextKey context, out DFMPDynamicEncounterKey encounter)
        {
            encounter = new DFMPDynamicEncounterKey();
            if (context.Kind != DFMPWorldContextKind.Dungeon ||
                string.IsNullOrWhiteSpace(context.LocationId) ||
                context.DungeonBlockIndex < 0 ||
                string.IsNullOrWhiteSpace(context.DungeonBlockName))
                return false;

            encounter = new DFMPDynamicEncounterKey
            {
                ProviderKind = DFMPDynamicEnemyProviderKind.Dungeon,
                Context = context,
                EncounterId = "dungeon-" + ComputeStableHash(CreateDungeonSeedMaterial(serverWorldSeed, context)).ToString("x16", CultureInfo.InvariantCulture)
            };
            return true;
        }

        public static bool TryScanSpawnMarkers(DFBlock blockData, int blockX, int blockZ, out Vector3[] candidatePositions)
        {
            NativeDungeonMarker[] markers;
            if (!TryScanNativeDungeonMarkers(blockData, blockX, blockZ, out markers))
            {
                candidatePositions = new Vector3[0];
                return false;
            }

            candidatePositions = new Vector3[markers.Length];
            for (int index = 0; index < markers.Length; index++)
                candidatePositions[index] = markers[index].DungeonLocalPosition;
            return true;
        }

        public static bool TryScanNativeDungeonMarkers(DFBlock blockData, int blockX, int blockZ, out NativeDungeonMarker[] markers)
        {
            markers = new NativeDungeonMarker[0];
            if (blockData.RdbBlock.ObjectRootList == null || blockData.RdbBlock.ObjectRootList.Length == 0)
                return false;

            var candidates = new List<NativeDungeonMarker>();
            Vector3 blockOffset = new Vector3(blockX * RDBLayout.RDBSide, 0f, blockZ * RDBLayout.RDBSide);
            for (int groupIndex = 0; groupIndex < blockData.RdbBlock.ObjectRootList.Length; groupIndex++)
            {
                var group = blockData.RdbBlock.ObjectRootList[groupIndex];
                if (group.RdbObjects == null)
                    continue;

                for (int objIndex = 0; objIndex < group.RdbObjects.Length; objIndex++)
                {
                    var obj = group.RdbObjects[objIndex];
                    int textureRecord = obj.Resources.FlatResource.TextureRecord;
                    if (obj.Type != DFBlock.RdbResourceTypes.Flat ||
                        !IsSpawnMarker(obj.Resources.FlatResource.TextureArchive, textureRecord))
                        continue;

                    Vector3 markerPosition = new Vector3(obj.XPos, -obj.YPos, obj.ZPos) * MeshReader.GlobalScale;
                    candidates.Add(new NativeDungeonMarker
                    {
                        DungeonLocalPosition = blockOffset + markerPosition,
                        TextureRecord = textureRecord,
                        MarkerY = obj.YPos,
                        FixedMobileType = obj.Resources.FlatResource.IsCustomData
                            ? (int)obj.Resources.FlatResource.FactionOrMobileId
                            : (int)(obj.Resources.FlatResource.FactionOrMobileId & 0xff),
                        SoundIndex = obj.Resources.FlatResource.SoundIndex,
                        ClassicSlot = obj.Resources.FlatResource.Flags,
                        IsCustomData = obj.Resources.FlatResource.IsCustomData,
                        Reaction = obj.Resources.FlatResource.Action
                    });
                }
            }

            if (candidates.Count == 0)
                return false;

            markers = candidates.ToArray();
            return true;
        }

        public static bool TryCreateDescriptor(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            int rosterIndex,
            Vector3[] candidatePositions,
            out DFMPDynamicEnemyDescriptor descriptor)
        {
            descriptor = new DFMPDynamicEnemyDescriptor();
            if (candidatePositions == null || candidatePositions.Length == 0 || rosterIndex < 0)
                return false;

            ulong hash = ComputeStableHash(CreateDungeonDescriptorSeedMaterial(serverWorldSeed, context, rosterIndex));
            int positionIndex = (int)(hash % (ulong)candidatePositions.Length);
            Vector3 position = candidatePositions[positionIndex];

            float facingYaw = (float)((hash >> 16) % 360UL);
            int typeIndex = (int)((hash >> 32) % (ulong)CuratedDungeonMobileTypes.Length);
            int mobileType = (int)CuratedDungeonMobileTypes[typeIndex];

            descriptor = new DFMPDynamicEnemyDescriptor
            {
                DungeonLocalPosition = position,
                FacingYaw = facingYaw,
                MobileType = mobileType
            };
            return true;
        }

        public static int GetInitialEnemyHealth(int mobileType)
        {
            MobileEnemy enemy;
            if (EnemyBasics.GetEnemy((MobileTypes)mobileType, out enemy) && enemy.MaxHealth > 0)
                return Math.Max(1, (enemy.MinHealth + enemy.MaxHealth) / 2);

            return 20;
        }

        public static bool TryCreateRoster(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            int rosterSize,
            Vector3[] candidatePositions,
            out DFMPDynamicEnemyRecord[] roster)
        {
            roster = new DFMPDynamicEnemyRecord[0];
            DFMPDynamicEncounterKey encounter;
            if (!TryCreateEncounterKey(serverWorldSeed, context, out encounter) ||
                rosterSize <= 0 ||
                rosterSize > MaximumRosterSize ||
                candidatePositions == null ||
                candidatePositions.Length == 0)
                return false;

            roster = new DFMPDynamicEnemyRecord[rosterSize];
            for (int rosterIndex = 0; rosterIndex < roster.Length; rosterIndex++)
            {
                DFMPDynamicEnemyDescriptor descriptor;
                if (!TryCreateDescriptor(serverWorldSeed, context, rosterIndex, candidatePositions, out descriptor))
                {
                    roster = new DFMPDynamicEnemyRecord[0];
                    return false;
                }

                int health = GetInitialEnemyHealth(descriptor.MobileType);
                roster[rosterIndex] = new DFMPDynamicEnemyRecord
                {
                    Identity = new DFMPDynamicEnemyIdentity
                    {
                        Encounter = encounter,
                        RosterIndex = rosterIndex,
                        EnemyId = encounter.EncounterId + "-enemy-" + rosterIndex.ToString(CultureInfo.InvariantCulture)
                    },
                    LifecycleState = DFMPDynamicEnemyLifecycleState.SpawnedAlive,
                    Descriptor = descriptor,
                    Health = health,
                    MaxHealth = health
                };
            }

            return true;
        }

        public static bool TryCreateRoster(ulong serverWorldSeed, DFMPWorldContextKey context, int rosterSize, out DFMPDynamicEnemyRecord[] roster)
        {
            Vector3[] candidatePositions;
            if (TryScanContextSpawnMarkers(context, out candidatePositions))
                return TryCreateRoster(serverWorldSeed, context, rosterSize, candidatePositions, out roster);

            roster = new DFMPDynamicEnemyRecord[0];
            return false;
        }

        public static bool TryCreateNativeRoster(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            float monsterPower,
            int monsterVariance,
            out DFMPDynamicEnemyRecord[] roster)
        {
            NativeDungeonGenerationInputs inputs;
            return TryCreateNativeRoster(serverWorldSeed, context, monsterPower, monsterVariance, out roster, out inputs);
        }

        public static bool TryCreateNativeRoster(
            ulong serverWorldSeed,
            DFMPWorldContextKey context,
            float monsterPower,
            int monsterVariance,
            out DFMPDynamicEnemyRecord[] roster,
            out NativeDungeonGenerationInputs inputs)
        {
            roster = new DFMPDynamicEnemyRecord[0];
            inputs = new NativeDungeonGenerationInputs();
            if (DaggerfallUnity.Instance == null ||
                !DaggerfallUnity.Instance.IsReady ||
                DaggerfallUnity.Instance.ContentReader == null)
                return false;

                try
                {
                    DFLocation location;
                    if (!DaggerfallUnity.Instance.ContentReader.GetLocation(context.RegionIndex, context.LocationIndex, out location))
                        return false;

                    if (!TryResolveNativeDungeonGenerationInputs(location, context, out inputs))
                        return false;

                    if (DaggerfallUnity.Instance.ContentReader.BlockFileReader == null)
                        return false;

                    DFBlock blockData = DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(context.DungeonBlockName);
                    NativeDungeonMarker[] markers;
                    if (!TryScanNativeDungeonMarkers(blockData, inputs.BlockX, inputs.BlockZ, out markers))
                        return false;

                    return TryCreateNativeRosterFromMarkers(
                        serverWorldSeed,
                        context,
                        inputs,
                        markers,
                        monsterPower,
                        monsterVariance,
                        out roster);
                }
                catch (Exception)
                {
                    roster = new DFMPDynamicEnemyRecord[0];
                    return false;
                }
        }

        public static Func<DFMPWorldContextKey, Vector3[]> MarkerProviderForTesting;

        public static bool TryScanContextSpawnMarkers(DFMPWorldContextKey context, out Vector3[] candidatePositions)
        {
            candidatePositions = new Vector3[0];
            if (MarkerProviderForTesting != null)
            {
                Vector3[] testCandidates = MarkerProviderForTesting(context);
                if (testCandidates != null && testCandidates.Length > 0)
                {
                    candidatePositions = testCandidates;
                    return true;
                }
            }

            if (context.Kind != DFMPWorldContextKind.Dungeon ||
                string.IsNullOrWhiteSpace(context.DungeonBlockName) ||
                context.DungeonBlockIndex < 0 ||
                DaggerfallUnity.Instance == null ||
                DaggerfallUnity.Instance.ContentReader == null)
                return false;

            ContentReader contentReader = DaggerfallUnity.Instance.ContentReader;
            if (contentReader.BlockFileReader == null)
                return false;

            int blockX = 0;
            int blockZ = 0;
            if (contentReader.MapFileReader != null)
            {
                DFLocation location = contentReader.MapFileReader.GetLocation(context.RegionIndex, context.LocationIndex);
                if (location.Loaded && location.HasDungeon && location.Dungeon.Blocks != null)
                {
                    if (context.DungeonBlockIndex >= 0 && context.DungeonBlockIndex < location.Dungeon.Blocks.Length)
                    {
                        var block = location.Dungeon.Blocks[context.DungeonBlockIndex];
                        blockX = block.X;
                        blockZ = block.Z;
                    }
                    else
                    {
                        for (int i = 0; i < location.Dungeon.Blocks.Length; i++)
                        {
                            if (string.Equals(location.Dungeon.Blocks[i].BlockName, context.DungeonBlockName, StringComparison.OrdinalIgnoreCase))
                            {
                                blockX = location.Dungeon.Blocks[i].X;
                                blockZ = location.Dungeon.Blocks[i].Z;
                                break;
                            }
                        }
                    }
                }
            }

            DFBlock blockData = contentReader.BlockFileReader.GetBlock(context.DungeonBlockName);
            return TryScanSpawnMarkers(blockData, blockX, blockZ, out candidatePositions);
        }

        static string CreateDungeonDescriptorSeedMaterial(ulong serverWorldSeed, DFMPWorldContextKey context, int rosterIndex)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "dfmp-dungeon-descriptor-v1|{0}|{1}|{2}",
                serverWorldSeed,
                CreateDungeonSeedMaterial(serverWorldSeed, context),
                rosterIndex);
        }

        static string CreateDungeonSeedMaterial(ulong serverWorldSeed, DFMPWorldContextKey context)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "dfmp-dungeon-roster-v1|{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}",
                serverWorldSeed,
                context.MapPixelX,
                context.MapPixelY,
                context.RegionIndex,
                context.LocationIndex,
                Normalize(context.LocationId),
                context.DungeonBlockIndex,
                Normalize(context.DungeonBlockName),
                Normalize(context.InstanceId),
                context.BuildingKey,
                context.Kind);
        }

        static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        static ulong ComputeStableHash(string value)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            for (int index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= prime;
            }

            return hash;
        }
    }

    public sealed class DFMPDynamicEnemyRegistry
    {
        readonly Dictionary<string, DFMPDynamicEnemyRecord> recordsByEnemyId = new Dictionary<string, DFMPDynamicEnemyRecord>(StringComparer.Ordinal);

        public int Count
        {
            get { return recordsByEnemyId.Count; }
        }

        public DFMPDynamicEnemyRegistryResult RegisterRoster(bool isServerAuthority, IList<DFMPDynamicEnemyRecord> roster)
        {
            if (!isServerAuthority)
                return DFMPDynamicEnemyRegistryResult.InvalidAuthority;
            if (!IsValidRoster(roster))
                return DFMPDynamicEnemyRegistryResult.InvalidRoster;

            bool containsExistingRecords = false;
            for (int index = 0; index < roster.Count; index++)
            {
                DFMPDynamicEnemyRecord existing;
                if (recordsByEnemyId.TryGetValue(roster[index].Identity.EnemyId, out existing))
                {
                    if (!existing.Identity.Equals(roster[index].Identity))
                        return DFMPDynamicEnemyRegistryResult.ConflictingRoster;

                    containsExistingRecords = true;
                }
            }

            if (containsExistingRecords)
                return DFMPDynamicEnemyRegistryResult.AlreadyRegistered;

            for (int index = 0; index < roster.Count; index++)
                recordsByEnemyId.Add(roster[index].Identity.EnemyId, roster[index]);

            return DFMPDynamicEnemyRegistryResult.Accepted;
        }

        public bool TryGetRecord(string enemyId, out DFMPDynamicEnemyRecord record)
        {
            return recordsByEnemyId.TryGetValue(enemyId ?? string.Empty, out record);
        }

        public DFMPDynamicEnemyRegistryResult TryTransition(bool isServerAuthority, string enemyId, DFMPDynamicEnemyLifecycleState nextState)
        {
            if (!isServerAuthority)
                return DFMPDynamicEnemyRegistryResult.InvalidAuthority;

            DFMPDynamicEnemyRecord record;
            if (!recordsByEnemyId.TryGetValue(enemyId ?? string.Empty, out record))
                return DFMPDynamicEnemyRegistryResult.MissingEnemy;
            if (!IsValidTransition(record.LifecycleState, nextState))
                return DFMPDynamicEnemyRegistryResult.InvalidTransition;

            record.LifecycleState = nextState;
            recordsByEnemyId[enemyId] = record;
            return DFMPDynamicEnemyRegistryResult.Accepted;
        }

        public DFMPDynamicEnemyRegistryResult TryApplyDamage(
            bool isServerAuthority,
            string enemyId,
            int amount,
            out DFMPDynamicEnemyRecord updatedRecord,
            out int appliedAmount,
            out bool killed)
        {
            updatedRecord = default(DFMPDynamicEnemyRecord);
            appliedAmount = 0;
            killed = false;

            if (!isServerAuthority)
                return DFMPDynamicEnemyRegistryResult.InvalidAuthority;
            if (amount <= 0)
                return DFMPDynamicEnemyRegistryResult.InvalidTransition;

            DFMPDynamicEnemyRecord record;
            if (!recordsByEnemyId.TryGetValue(enemyId ?? string.Empty, out record))
                return DFMPDynamicEnemyRegistryResult.MissingEnemy;
            if (record.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                return DFMPDynamicEnemyRegistryResult.InvalidTransition;

            appliedAmount = Math.Min(amount, Math.Max(0, record.Health));
            record.Health = Math.Max(0, record.Health - appliedAmount);
            if (record.Health == 0)
            {
                record.LifecycleState = DFMPDynamicEnemyLifecycleState.Dead;
                killed = true;
            }

            recordsByEnemyId[enemyId] = record;
            updatedRecord = record;
            return DFMPDynamicEnemyRegistryResult.Accepted;
        }

        public DFMPDynamicEnemyRegistryResult TryUpdateAiState(
            bool isServerAuthority,
            string enemyId,
            DFMPDynamicEnemyDescriptor descriptor,
            int targetConnectionId,
            bool isMoving,
            out DFMPDynamicEnemyRecord updatedRecord)
        {
            updatedRecord = default(DFMPDynamicEnemyRecord);
            if (!isServerAuthority)
                return DFMPDynamicEnemyRegistryResult.InvalidAuthority;

            DFMPDynamicEnemyRecord record;
            if (!recordsByEnemyId.TryGetValue(enemyId ?? string.Empty, out record))
                return DFMPDynamicEnemyRegistryResult.MissingEnemy;
            if (record.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                return DFMPDynamicEnemyRegistryResult.InvalidTransition;

            record.Descriptor = descriptor;
            record.TargetConnectionId = targetConnectionId;
            record.IsMoving = isMoving;
            recordsByEnemyId[enemyId] = record;
            updatedRecord = record;
            return DFMPDynamicEnemyRegistryResult.Accepted;
        }

        static bool IsValidRoster(IList<DFMPDynamicEnemyRecord> roster)
        {
            if (roster == null || roster.Count == 0 || roster.Count > DFMPDungeonRosterPolicy.MaximumRosterSize)
                return false;

            var enemyIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < roster.Count; index++)
            {
                DFMPDynamicEnemyRecord record = roster[index];
                if (string.IsNullOrWhiteSpace(record.Identity.EnemyId) ||
                    record.Identity.RosterIndex < 0 ||
                    !Enum.IsDefined(typeof(DFMPDynamicEnemyProviderKind), record.Identity.Encounter.ProviderKind) ||
                    !enemyIds.Add(record.Identity.EnemyId))
                    return false;
            }

            return true;
        }

        static bool IsValidTransition(DFMPDynamicEnemyLifecycleState currentState, DFMPDynamicEnemyLifecycleState nextState)
        {
            if (!Enum.IsDefined(typeof(DFMPDynamicEnemyLifecycleState), nextState))
                return false;

            switch (currentState)
            {
                case DFMPDynamicEnemyLifecycleState.SpawnedAlive:
                    return nextState == DFMPDynamicEnemyLifecycleState.DespawnedAlive ||
                        nextState == DFMPDynamicEnemyLifecycleState.Dead ||
                        nextState == DFMPDynamicEnemyLifecycleState.Retired;
                case DFMPDynamicEnemyLifecycleState.DespawnedAlive:
                    return nextState == DFMPDynamicEnemyLifecycleState.SpawnedAlive ||
                        nextState == DFMPDynamicEnemyLifecycleState.Dead ||
                        nextState == DFMPDynamicEnemyLifecycleState.Retired;
                case DFMPDynamicEnemyLifecycleState.Dead:
                    return nextState == DFMPDynamicEnemyLifecycleState.Retired;
                default:
                    return false;
            }
        }
    }
}

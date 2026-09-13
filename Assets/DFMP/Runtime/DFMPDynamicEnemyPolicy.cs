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

        public bool Equals(DFMPDynamicEnemyDescriptor other)
        {
            return DungeonLocalPosition == other.DungeonLocalPosition &&
                Mathf.Approximately(FacingYaw, other.FacingYaw) &&
                MobileType == other.MobileType;
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
    }

    public static class DFMPDungeonRosterPolicy
    {
        public const int MaximumRosterSize = 64;
        public const int SpawnMarkerTextureArchive = 199;
        public const int SpawnMarkerTextureRecord = 11;

        public static readonly MobileTypes[] CuratedDungeonMobileTypes = new MobileTypes[]
        {
            MobileTypes.Rat,
            MobileTypes.GiantBat,
            MobileTypes.Spider,
            MobileTypes.SkeletalWarrior,
            MobileTypes.Zombie,
            MobileTypes.Orc
        };

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
            candidatePositions = new Vector3[0];
            if (blockData.RdbBlock.ObjectRootList == null || blockData.RdbBlock.ObjectRootList.Length == 0)
                return false;

            var candidates = new List<Vector3>();
            Vector3 blockOffset = new Vector3(blockX * RDBLayout.RDBSide, 0f, blockZ * RDBLayout.RDBSide);

            for (int groupIndex = 0; groupIndex < blockData.RdbBlock.ObjectRootList.Length; groupIndex++)
            {
                var group = blockData.RdbBlock.ObjectRootList[groupIndex];
                if (group.RdbObjects == null)
                    continue;

                for (int objIndex = 0; objIndex < group.RdbObjects.Length; objIndex++)
                {
                    var obj = group.RdbObjects[objIndex];
                    if (obj.Type == DFBlock.RdbResourceTypes.Flat &&
                        obj.Resources.FlatResource.TextureArchive == SpawnMarkerTextureArchive &&
                        obj.Resources.FlatResource.TextureRecord == SpawnMarkerTextureRecord)
                    {
                        Vector3 markerPosition = new Vector3(obj.XPos, -obj.YPos, obj.ZPos) * MeshReader.GlobalScale;
                        candidates.Add(blockOffset + markerPosition);
                    }
                }
            }

            if (candidates.Count == 0)
                return false;

            candidatePositions = candidates.ToArray();
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

                roster[rosterIndex] = new DFMPDynamicEnemyRecord
                {
                    Identity = new DFMPDynamicEnemyIdentity
                    {
                        Encounter = encounter,
                        RosterIndex = rosterIndex,
                        EnemyId = encounter.EncounterId + "-enemy-" + rosterIndex.ToString(CultureInfo.InvariantCulture)
                    },
                    LifecycleState = DFMPDynamicEnemyLifecycleState.SpawnedAlive,
                    Descriptor = descriptor
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
            if (contentReader.MapFileReader == null || contentReader.BlockFileReader == null)
                return false;

            DFLocation location = contentReader.MapFileReader.GetLocation(context.RegionIndex, context.LocationIndex);
            if (!location.Loaded || !location.HasDungeon || location.Dungeon.Blocks == null || context.DungeonBlockIndex >= location.Dungeon.Blocks.Length)
                return false;

            var dungeonBlock = location.Dungeon.Blocks[context.DungeonBlockIndex];
            DFBlock blockData = contentReader.BlockFileReader.GetBlock(dungeonBlock.BlockName);
            return TryScanSpawnMarkers(blockData, dungeonBlock.X, dungeonBlock.Z, out candidatePositions);
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

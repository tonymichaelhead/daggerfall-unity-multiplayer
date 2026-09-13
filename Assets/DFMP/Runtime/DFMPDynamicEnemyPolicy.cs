using System;
using System.Collections.Generic;
using System.Globalization;

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
    }

    public static class DFMPDungeonRosterPolicy
    {
        public const int MaximumRosterSize = 64;

        public static ulong CreateServerWorldSeed(string configuredSeed)
        {
            return ComputeStableHash(string.IsNullOrWhiteSpace(configuredSeed) ? "default" : configuredSeed.Trim());
        }

        public static bool TryCreateEncounterKey(ulong serverWorldSeed, DFMPWorldContextKey context, out DFMPDynamicEncounterKey encounter)
        {
            encounter = new DFMPDynamicEncounterKey();
            if (context.Kind != DFMPWorldContextKind.Dungeon ||
                string.IsNullOrWhiteSpace(context.LocationId) ||
                context.DungeonBlockIndex <= 0 ||
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

        public static bool TryCreateRoster(ulong serverWorldSeed, DFMPWorldContextKey context, int rosterSize, out DFMPDynamicEnemyRecord[] roster)
        {
            roster = new DFMPDynamicEnemyRecord[0];
            DFMPDynamicEncounterKey encounter;
            if (!TryCreateEncounterKey(serverWorldSeed, context, out encounter) || rosterSize <= 0 || rosterSize > MaximumRosterSize)
                return false;

            roster = new DFMPDynamicEnemyRecord[rosterSize];
            for (int rosterIndex = 0; rosterIndex < roster.Length; rosterIndex++)
            {
                roster[rosterIndex] = new DFMPDynamicEnemyRecord
                {
                    Identity = new DFMPDynamicEnemyIdentity
                    {
                        Encounter = encounter,
                        RosterIndex = rosterIndex,
                        EnemyId = encounter.EncounterId + "-enemy-" + rosterIndex.ToString(CultureInfo.InvariantCulture)
                    },
                    LifecycleState = DFMPDynamicEnemyLifecycleState.SpawnedAlive
                };
            }

            return true;
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

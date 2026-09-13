using System.Collections.Generic;
using NUnit.Framework;
using DFMP.Runtime;
using Mirror;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class DynamicEnemyPolicyTests
    {
        [Test]
        public void DungeonRoster_IsDeterministicForEquivalentDungeonContext()
        {
            DFMPWorldContextKey firstContext = CreateDungeonContext();
            DFMPWorldContextKey equivalentContext = firstContext;
            equivalentContext.LocationId = "daggerfall dungeon";
            equivalentContext.DungeonBlockName = "s0000161.rdb";
            equivalentContext.InstanceId = "SHARED";

            DFMPDynamicEnemyRecord[] firstRoster;
            DFMPDynamicEnemyRecord[] secondRoster;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, firstContext, 3, out firstRoster));
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, equivalentContext, 3, out secondRoster));

            Assert.AreEqual(3, firstRoster.Length);
            for (int index = 0; index < firstRoster.Length; index++)
            {
                Assert.AreEqual(firstRoster[index].Identity.EnemyId, secondRoster[index].Identity.EnemyId);
                Assert.AreEqual(firstRoster[index].Identity.Encounter, secondRoster[index].Identity.Encounter);
                Assert.AreEqual(index, firstRoster[index].Identity.RosterIndex);
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, firstRoster[index].LifecycleState);
            }
        }

        [Test]
        public void DungeonRoster_SeparatesEnemyIdentityFromEncounterAndContext()
        {
            DFMPWorldContextKey firstContext = CreateDungeonContext();
            DFMPWorldContextKey otherContext = firstContext;
            otherContext.DungeonBlockIndex = 8;

            DFMPDynamicEnemyRecord[] firstRoster;
            DFMPDynamicEnemyRecord[] otherRoster;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, firstContext, 1, out firstRoster));
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, otherContext, 1, out otherRoster));

            Assert.AreNotEqual(firstRoster[0].Identity.Encounter.Context, otherRoster[0].Identity.Encounter.Context);
            Assert.AreNotEqual(firstRoster[0].Identity.Encounter, otherRoster[0].Identity.Encounter);
            Assert.AreNotEqual(firstRoster[0].Identity.EnemyId, otherRoster[0].Identity.EnemyId);
        }

        [Test]
        public void DungeonRoster_RejectsInvalidContextAndBounds()
        {
            DFMPWorldContextKey exterior = CreateDungeonContext();
            exterior.Kind = DFMPWorldContextKind.Exterior;
            DFMPDynamicEnemyRecord[] roster;

            Assert.IsFalse(DFMPDungeonRosterPolicy.TryCreateRoster(1UL, exterior, 1, out roster));
            Assert.IsFalse(DFMPDungeonRosterPolicy.TryCreateRoster(1UL, CreateDungeonContext(), 0, out roster));
            Assert.IsFalse(DFMPDungeonRosterPolicy.TryCreateRoster(1UL, CreateDungeonContext(), DFMPDungeonRosterPolicy.MaximumRosterSize + 1, out roster));
        }

        [Test]
        public void Registry_EnforcesServerAuthorityAndIdempotentRosterRegistration()
        {
            DFMPDynamicEnemyRecord[] roster = CreateRoster();
            var registry = new DFMPDynamicEnemyRegistry();

            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.InvalidAuthority, registry.RegisterRoster(false, roster));
            Assert.AreEqual(0, registry.Count);
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.RegisterRoster(true, roster));
            Assert.AreEqual(roster.Length, registry.Count);
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.AlreadyRegistered, registry.RegisterRoster(true, roster));
            Assert.AreEqual(roster.Length, registry.Count);
        }

        [Test]
        public void Registry_TransitionsSpawnDespawnDeathAndRetirement()
        {
            DFMPDynamicEnemyRecord[] roster = CreateRoster();
            var registry = new DFMPDynamicEnemyRegistry();
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.RegisterRoster(true, roster));
            string enemyId = roster[0].Identity.EnemyId;

            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.InvalidAuthority, registry.TryTransition(false, enemyId, DFMPDynamicEnemyLifecycleState.DespawnedAlive));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.DespawnedAlive));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.SpawnedAlive));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.Dead));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.InvalidTransition, registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.SpawnedAlive));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.Retired));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.InvalidTransition, registry.TryTransition(true, enemyId, DFMPDynamicEnemyLifecycleState.Retired));

            DFMPDynamicEnemyRecord record;
            Assert.IsTrue(registry.TryGetRecord(enemyId, out record));
            Assert.AreEqual(DFMPDynamicEnemyLifecycleState.Retired, record.LifecycleState);
        }

        [Test]
        public void Registry_KeepsDistinctDungeonContextsIsolated()
        {
            DFMPDynamicEnemyRecord[] firstRoster = CreateRoster();
            DFMPWorldContextKey otherContext = CreateDungeonContext();
            otherContext.DungeonBlockIndex = 8;
            DFMPDynamicEnemyRecord[] otherRoster;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, otherContext, 2, out otherRoster));

            var registry = new DFMPDynamicEnemyRegistry();
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.RegisterRoster(true, firstRoster));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.RegisterRoster(true, otherRoster));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryTransition(true, firstRoster[0].Identity.EnemyId, DFMPDynamicEnemyLifecycleState.Dead));

            DFMPDynamicEnemyRecord firstRecord;
            DFMPDynamicEnemyRecord otherRecord;
            Assert.IsTrue(registry.TryGetRecord(firstRoster[0].Identity.EnemyId, out firstRecord));
            Assert.IsTrue(registry.TryGetRecord(otherRoster[0].Identity.EnemyId, out otherRecord));
            Assert.AreEqual(DFMPDynamicEnemyLifecycleState.Dead, firstRecord.LifecycleState);
            Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, otherRecord.LifecycleState);
        }

        [Test]
        public void DungeonRosterService_UsesContextOccupancyForActivationAndDespawn()
        {
            GameObject serviceObject = new GameObject("DFMP_DungeonRosterServiceTest");
            GameObject firstSessionObject = new GameObject("DFMP_FirstDungeonSession");
            GameObject secondSessionObject = new GameObject("DFMP_SecondDungeonSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig { WorldSeed = "test-seed", DungeonRosterSize = 2 });
                var firstSession = firstSessionObject.AddComponent<DFMPPlayerSessionState>();
                var secondSession = secondSessionObject.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext();
                DFMPWorldContextKey exterior = dungeon;
                exterior.Kind = DFMPWorldContextKind.Exterior;
                exterior.DungeonBlockIndex = 0;
                exterior.DungeonBlockName = string.Empty;

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(81, firstSession, dungeon, "test"));
                Assert.AreEqual(2, service.RosterCount);
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(82, secondSession, dungeon, "test"));
                Assert.AreEqual(2, service.RosterCount);

                DFMPDynamicEnemyRecord[] expectedRoster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("test-seed"), dungeon, 2, out expectedRoster));
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(81, firstSession, exterior, "test"));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(82, secondSession, exterior, "test"));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.DespawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(81, firstSession, dungeon, "test"));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);
            }
            finally
            {
                Object.DestroyImmediate(serviceObject);
                Object.DestroyImmediate(firstSessionObject);
                Object.DestroyImmediate(secondSessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DynamicEnemyState_ReplicatesDurableIdentityAndLifecycleWithoutGameplayComponents()
        {
            GameObject enemyObject = new GameObject("DFMP_DynamicEnemyStateTest");
            try
            {
                enemyObject.AddComponent<NetworkIdentity>();
                var carrier = enemyObject.AddComponent<DFMPWorldContextCarrier>();
                var state = enemyObject.AddComponent<DFMPDynamicEnemyState>();
                DFMPDynamicEnemyRecord[] roster = CreateRoster();
                DFMPDynamicEnemyRecord record = roster[0];
                record.LifecycleState = DFMPDynamicEnemyLifecycleState.DespawnedAlive;

                carrier.Initialize(record.Identity.Encounter.Context);
                state.Initialize(record);

                Assert.AreEqual(record.Identity.Encounter.Context, carrier.Context);
                Assert.AreEqual(record.Identity.Encounter.ProviderKind, state.ProviderKind);
                Assert.AreEqual(record.Identity.Encounter.EncounterId, state.EncounterId);
                Assert.AreEqual(record.Identity.RosterIndex, state.RosterIndex);
                Assert.AreEqual(record.Identity.EnemyId, state.EnemyId);
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.DespawnedAlive, state.LifecycleState);
                Assert.AreEqual(4, enemyObject.GetComponents<Component>().Length);
            }
            finally
            {
                Object.DestroyImmediate(enemyObject);
            }
        }

        static DFMPDynamicEnemyRecord[] CreateRoster()
        {
            DFMPDynamicEnemyRecord[] roster;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, CreateDungeonContext(), 2, out roster));
            return roster;
        }

        static DFMPDynamicEnemyRecord GetRecord(DFMPDungeonEnemyRosterService service, string enemyId)
        {
            DFMPDynamicEnemyRecord record;
            Assert.IsTrue(service.TryGetRecord(enemyId, out record));
            return record;
        }

        static DFMPWorldContextKey CreateDungeonContext()
        {
            return new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Dungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 42,
                LocationId = "Daggerfall Dungeon",
                DungeonBlockIndex = 7,
                DungeonBlockName = "S0000161.RDB",
                InstanceId = "shared"
            };
        }
    }
}

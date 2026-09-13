using System;
using System.Collections.Generic;
using NUnit.Framework;
using DFMP.Runtime;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using Mirror;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace DFMP.Tests
{
    [TestFixture]
    public class DynamicEnemyPolicyTests
    {
        [SetUp]
        public void SetUp()
        {
            DFMPDungeonRosterPolicy.MarkerProviderForTesting = _ => new Vector3[]
            {
                new Vector3(10f, 0f, 20f),
                new Vector3(30f, 0f, 40f)
            };
        }

        [TearDown]
        public void TearDown()
        {
            DFMPDungeonRosterPolicy.MarkerProviderForTesting = null;
        }

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
                Assert.AreEqual(firstRoster[index].Descriptor, secondRoster[index].Descriptor);
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
        public void DungeonRoster_AcceptsFirstNativeDungeonBlock()
        {
            DFMPWorldContextKey firstBlock = CreateDungeonContext();
            firstBlock.DungeonBlockIndex = 0;
            DFMPDynamicEnemyRecord[] roster;

            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(123456UL, firstBlock, 1, out roster));
            Assert.AreEqual(0, roster[0].Identity.Encounter.Context.DungeonBlockIndex);
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
                dungeon.DungeonBlockIndex = 0;
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
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(firstSessionObject);
                UnityObject.DestroyImmediate(secondSessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRoster_ScanSpawnMarkers_ExtractsScaledCoordinatesAndFailsClosedWhenEmpty()
        {
            var validFlat = new DFBlock.RdbObject
            {
                Type = DFBlock.RdbResourceTypes.Flat,
                XPos = 512,
                YPos = -256,
                ZPos = 1024,
                Resources = new DFBlock.RdbResources
                {
                    FlatResource = new DFBlock.RdbFlatResource
                    {
                        TextureArchive = DFMPDungeonRosterPolicy.SpawnMarkerTextureArchive,
                        TextureRecord = DFMPDungeonRosterPolicy.SpawnMarkerTextureRecord
                    }
                }
            };

            var nonMarkerFlat = new DFBlock.RdbObject
            {
                Type = DFBlock.RdbResourceTypes.Flat,
                XPos = 100,
                YPos = 0,
                ZPos = 200,
                Resources = new DFBlock.RdbResources
                {
                    FlatResource = new DFBlock.RdbFlatResource
                    {
                        TextureArchive = 199,
                        TextureRecord = 15 // random enemy flat, not 11
                    }
                }
            };

            var modelObj = new DFBlock.RdbObject
            {
                Type = DFBlock.RdbResourceTypes.Model
            };

            var blockData = new DFBlock
            {
                RdbBlock = new DFBlock.RdbBlockDesc
                {
                    ObjectRootList = new DFBlock.RdbObjectRoot[]
                    {
                        new DFBlock.RdbObjectRoot { RdbObjects = new DFBlock.RdbObject[] { validFlat, nonMarkerFlat } },
                        new DFBlock.RdbObjectRoot { RdbObjects = new DFBlock.RdbObject[] { modelObj } }
                    }
                }
            };

            Vector3[] candidates;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryScanSpawnMarkers(blockData, 2, 3, out candidates));
            Assert.AreEqual(1, candidates.Length);

            Vector3 expectedBlockOffset = new Vector3(2 * RDBLayout.RDBSide, 0f, 3 * RDBLayout.RDBSide);
            Vector3 expectedMarkerOffset = new Vector3(512, 256, 1024) * MeshReader.GlobalScale;
            Vector3 expectedPosition = expectedBlockOffset + expectedMarkerOffset;
            Assert.AreEqual(expectedPosition, candidates[0]);

            var emptyBlock = new DFBlock
            {
                RdbBlock = new DFBlock.RdbBlockDesc
                {
                    ObjectRootList = new DFBlock.RdbObjectRoot[0]
                }
            };
            Assert.IsFalse(DFMPDungeonRosterPolicy.TryScanSpawnMarkers(emptyBlock, 0, 0, out candidates));
            Assert.AreEqual(0, candidates.Length);
        }

        [Test]
        public void DungeonRoster_Descriptor_IsDeterministicAndSelectsCuratedMobileType()
        {
            Vector3[] candidates = new Vector3[]
            {
                new Vector3(10f, 0f, 20f),
                new Vector3(30f, 0f, 40f)
            };

            DFMPWorldContextKey context = CreateDungeonContext();
            DFMPDynamicEnemyDescriptor desc1;
            DFMPDynamicEnemyDescriptor desc2;

            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateDescriptor(123456UL, context, 0, candidates, out desc1));
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateDescriptor(123456UL, context, 0, candidates, out desc2));

            Assert.AreEqual(desc1, desc2);
            Assert.IsTrue(desc1.FacingYaw >= 0f && desc1.FacingYaw < 360f);
            Assert.IsTrue(Array.IndexOf(DFMPDungeonRosterPolicy.CuratedDungeonMobileTypes, (MobileTypes)desc1.MobileType) >= 0);

            DFMPDynamicEnemyDescriptor invalidDesc;
            Assert.IsFalse(DFMPDungeonRosterPolicy.TryCreateDescriptor(123456UL, context, 0, null, out invalidDesc));
            Assert.IsFalse(DFMPDungeonRosterPolicy.TryCreateDescriptor(123456UL, context, 0, new Vector3[0], out invalidDesc));
        }

        [Test]
        public void DynamicEnemyPresentation_CalculatesLocalPositionsAndVisualEligibility()
        {
            Vector3 dungeonRoot = new Vector3(100f, 50f, 200f);
            Vector3 localPosition = new Vector3(10f, -2f, 30f);
            Vector3 scenePosition = DFMPDynamicEnemyPresentation.DungeonLocalToScenePosition(dungeonRoot, localPosition);
            Assert.AreEqual(new Vector3(110f, 48f, 230f), scenePosition);

            Assert.IsTrue(DFMPDynamicEnemyPresentation.IsVisibleInLocalDungeon(dungeonRoot, dungeonRoot + new Vector3(10f, 0f, 10f), 20f));
            Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisibleInLocalDungeon(dungeonRoot, dungeonRoot + new Vector3(50f, 0f, 50f), 20f));

            GameObject enemyObject = new GameObject("DFMP_PresentationEligibilityTest");
            try
            {
                enemyObject.AddComponent<NetworkIdentity>();
                var carrier = enemyObject.AddComponent<DFMPWorldContextCarrier>();
                var state = enemyObject.AddComponent<DFMPDynamicEnemyState>();
                DFMPDynamicEnemyRecord record = CreateRoster()[0];
                carrier.Initialize(record.Identity.Encounter.Context);
                state.Initialize(record);

                DFMPWorldContextKey matchingContext = record.Identity.Encounter.Context;
                DFMPWorldContextKey otherContext = matchingContext;
                otherContext.DungeonBlockIndex = 99;

                Assert.IsTrue(DFMPDynamicEnemyPresentation.IsVisualEligible(state, true, matchingContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisualEligible(state, false, matchingContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisualEligible(state, true, otherContext));

                record.LifecycleState = DFMPDynamicEnemyLifecycleState.DespawnedAlive;
                state.Initialize(record);
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisualEligible(state, true, matchingContext));
            }
            finally
            {
                UnityObject.DestroyImmediate(enemyObject);
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
                Assert.AreEqual(record.Descriptor.DungeonLocalPosition, state.DungeonLocalPosition);
                Assert.AreEqual(record.Descriptor.FacingYaw, state.FacingYaw);
                Assert.AreEqual(record.Descriptor.MobileType, state.MobileType);
                Assert.AreEqual(record.Descriptor, state.Descriptor);
                Assert.AreEqual(4, enemyObject.GetComponents<Component>().Length);
            }
            finally
            {
                UnityObject.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void Registry_AppliesDamageToEnemyHealth_AndTransitionsToDeadOnLethalDamage()
        {
            DFMPDynamicEnemyRecord[] roster = CreateRoster();
            var registry = new DFMPDynamicEnemyRegistry();
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.RegisterRoster(true, roster));
            string enemyId = roster[0].Identity.EnemyId;
            int initialHealth = roster[0].Health;
            Assert.IsTrue(initialHealth > 0);

            DFMPDynamicEnemyRecord updatedRecord;
            int appliedAmount;
            bool killed;

            // Non-lethal damage (1 HP)
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryApplyDamage(true, enemyId, 1, out updatedRecord, out appliedAmount, out killed));
            Assert.AreEqual(1, appliedAmount);
            Assert.IsFalse(killed);
            Assert.AreEqual(initialHealth - 1, updatedRecord.Health);
            Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, updatedRecord.LifecycleState);

            // Lethal damage
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryApplyDamage(true, enemyId, initialHealth * 2, out updatedRecord, out appliedAmount, out killed));
            Assert.AreEqual(initialHealth - 1, appliedAmount);
            Assert.IsTrue(killed);
            Assert.AreEqual(0, updatedRecord.Health);
            Assert.AreEqual(DFMPDynamicEnemyLifecycleState.Dead, updatedRecord.LifecycleState);

            // Cannot damage already dead enemy
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.InvalidTransition, registry.TryApplyDamage(true, enemyId, 5, out updatedRecord, out appliedAmount, out killed));
        }

        [Test]
        public void DamagePolicy_ValidatesDynamicEnemyTarget_SameContextAndRange()
        {
            var request = new DFMPDamageValidationRequest
            {
                RequestId = 101,
                Sequence = 1,
                SourceKind = DFMPDamageSourceKind.Player,
                VitalKind = DFMPVitalKind.Health,
                SourceConnectionId = 42,
                TargetConnectionId = 0,
                TargetEnemyId = "test-enemy-0",
                Amount = 10
            };

            var context = new DFMPDamageValidationContext
            {
                HasSession = true,
                SpawnConfirmed = true,
                TargetExists = true,
                TargetIsDead = false,
                HasPendingTransition = false,
                SameWorldContext = true,
                InRange = true,
                PvpEnabled = false, // Dynamic enemy damage works even when PvP is disabled!
                CooldownElapsed = true,
                AuthoritativeSourceConnectionId = 42,
                LastAcceptedSequence = 0,
                MaximumAmount = 100,
                IsDynamicEnemyTarget = true
            };

            Assert.AreEqual(DFMPDamageRejectionReason.None, DFMPDamagePolicy.GetRejectionReason(request, context));

            context.SameWorldContext = false;
            Assert.AreEqual(DFMPDamageRejectionReason.ContextMismatch, DFMPDamagePolicy.GetRejectionReason(request, context));

            context.SameWorldContext = true;
            context.InRange = false;
            Assert.AreEqual(DFMPDamageRejectionReason.OutOfRange, DFMPDamagePolicy.GetRejectionReason(request, context));

            context.InRange = true;
            context.TargetIsDead = true;
            Assert.AreEqual(DFMPDamageRejectionReason.TargetDead, DFMPDamagePolicy.GetRejectionReason(request, context));
        }

        [Test]
        public void DungeonRosterService_AppliesDamageAndPublishesEnemyDiedEvent()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceDamageTest");
            GameObject sessionObject = new GameObject("DFMP_RosterSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig { WorldSeed = "damage-test-seed", DungeonRosterSize = 2 });
                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext();

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(55, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("damage-test-seed"), dungeon, 2, out roster));
                string enemyId = roster[0].Identity.EnemyId;

                DFMPEnemyDiedEvent deathEvent = null;
                DFMPEventBus.Instance.EnemyDied += e => deathEvent = e;

                DFMPDynamicEnemyRecord updatedRecord;
                int appliedAmount;
                bool killed;
                Assert.IsTrue(service.TryApplyDamage(enemyId, 999, 55, out updatedRecord, out appliedAmount, out killed));
                Assert.IsTrue(killed);
                Assert.AreEqual(0, updatedRecord.Health);
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.Dead, updatedRecord.LifecycleState);

                Assert.IsNotNull(deathEvent);
                Assert.AreEqual(enemyId, deathEvent.EnemyId);
                Assert.AreEqual(55, deathEvent.KillerConnectionId);
                Assert.AreEqual(appliedAmount, deathEvent.DamageAmount);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
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

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
                service.Initialize(new DFMPServerEnemyConfig { WorldSeed = "test-seed", EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold, DungeonRosterSize = 2 });
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
        public void DungeonRosterService_DelayedDespawn_CancelsOnReentryAndDespawnsOnTimer()
        {
            GameObject serviceObject = new GameObject("DFMP_DelayedDespawnTest");
            GameObject sessionObject = new GameObject("DFMP_DelayedDespawnSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig { WorldSeed = "delayed-seed", EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold, DungeonRosterSize = 2, DespawnDelaySeconds = 10f });
                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext();
                DFMPWorldContextKey exterior = dungeon;
                exterior.Kind = DFMPWorldContextKind.Exterior;
                exterior.DungeonBlockName = string.Empty;

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(91, session, dungeon, "test"));
                DFMPDynamicEnemyRecord[] expectedRoster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("delayed-seed"), dungeon, 2, out expectedRoster));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);

                // Vacate dungeon -> should schedule delayed despawn, not immediately despawn
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(91, session, exterior, "test"));
                Assert.IsTrue(service.HasPendingDespawn(dungeon));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);

                // Re-enter before timer -> cancels pending despawn
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(91, session, dungeon, "test"));
                Assert.IsFalse(service.HasPendingDespawn(dungeon));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);

                // Vacate again -> pending despawn scheduled
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(91, session, exterior, "test"));
                Assert.IsTrue(service.HasPendingDespawn(dungeon));

                // Process timers before elapsed -> still pending
                service.ProcessPendingDespawns(Time.unscaledTime + 5f);
                Assert.IsTrue(service.HasPendingDespawn(dungeon));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.SpawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);

                // Process timers after elapsed -> despawns roster
                service.ProcessPendingDespawns(Time.unscaledTime + 11f);
                Assert.IsFalse(service.HasPendingDespawn(dungeon));
                Assert.AreEqual(DFMPDynamicEnemyLifecycleState.DespawnedAlive, GetRecord(service, expectedRoster[0].Identity.EnemyId).LifecycleState);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
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
                        TextureArchive = 200,
                        TextureRecord = 0 // non-marker archive
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

            DFMPDungeonRosterPolicy.NativeDungeonMarker[] markers;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryScanNativeDungeonMarkers(blockData, 2, 3, out markers));
            Assert.AreEqual(1, markers.Length);
            Assert.AreEqual(DFMPDungeonRosterPolicy.SpawnMarkerTextureRecord, markers[0].TextureRecord);
            Assert.AreEqual(validFlat.YPos, markers[0].MarkerY);
            Assert.AreEqual(0, markers[0].FixedMobileType);

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
        public void DungeonRoster_IsSpawnMarker_RecognizesAllNativeMonsterAndLayoutRecords()
        {
            Assert.IsTrue(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 15)); // Random Monster
            Assert.IsTrue(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 16)); // Fixed Monster
            Assert.IsTrue(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 11)); // Quest Marker
            Assert.IsTrue(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 18)); // Item Marker
            Assert.IsTrue(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 10)); // Start Marker
            Assert.IsTrue(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 8));  // Enter Marker

            Assert.IsFalse(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 0));
            Assert.IsFalse(DFMPDungeonRosterPolicy.IsSpawnMarker(199, 99));
            Assert.IsFalse(DFMPDungeonRosterPolicy.IsSpawnMarker(200, 15));
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
            Assert.AreEqual(Vector3.zero, DFMPDynamicEnemyPresentation.GetAvatarLocalPosition(0f));
            Assert.AreEqual(new Vector3(0f, 1.5f, 0f), DFMPDynamicEnemyPresentation.GetAvatarLocalPosition(1.5f));
            Assert.AreEqual(new Vector3(10f, 4f, 30f), DFMPDynamicEnemyPresentation.ResolveCorpseGroundPosition(null, new Vector3(10f, 4f, 30f)));

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
                DFMPWorldContextKey otherBlockContext = matchingContext;
                otherBlockContext.DungeonBlockIndex = 99;
                DFMPWorldContextKey otherDungeonContext = matchingContext;
                otherDungeonContext.LocationIndex = 999;

                Assert.IsTrue(DFMPDynamicEnemyPresentation.IsVisualEligible(state, true, matchingContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisualEligible(state, false, matchingContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisualEligible(state, true, otherBlockContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsVisualEligible(state, true, otherDungeonContext));

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
                Assert.AreEqual(record.Descriptor.Gender, state.Gender);
                Assert.AreEqual(record.Descriptor.Reaction, state.Reaction);
                Assert.AreEqual(record.Descriptor.ClassicSpawnDistanceType, state.ClassicSpawnDistanceType);
                Assert.AreEqual(record.TargetConnectionId, state.TargetConnectionId);
                Assert.AreEqual(record.IsMoving, state.IsMoving);
                Assert.AreEqual(0, state.AttackSequence);
                Assert.AreEqual(DFMPDynamicEnemyAttackKind.Melee, state.AttackKind);
                Assert.AreEqual(record.Descriptor, state.Descriptor);
                Assert.AreEqual(4, enemyObject.GetComponents<Component>().Length);

                state.SetAttackState(DFMPDynamicEnemyAttackKind.Magic);
                Assert.AreEqual(1, state.AttackSequence);
                Assert.AreEqual(DFMPDynamicEnemyAttackKind.Magic, state.AttackKind);
            }
            finally
            {
                UnityObject.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void DynamicEnemyHitTarget_RequiresPresentationEligibilityBeforeDamage()
        {
            GameObject enemyObject = new GameObject("DFMP_DynamicEnemyHitTargetTest");
            try
            {
                var hitTarget = enemyObject.AddComponent<DFMPDynamicEnemyHitTarget>();
                hitTarget.Initialize("enemy-test");

                Assert.AreEqual("enemy-test", hitTarget.EnemyId);
                Assert.IsFalse(hitTarget.IsDamageable);

                hitTarget.SetDamageable(true);
                Assert.IsTrue(hitTarget.IsDamageable);

                hitTarget.SetDamageable(false);
                Assert.IsFalse(hitTarget.IsDamageable);
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
        public void Registry_UpdatesAiStateUnderServerAuthorityOnly()
        {
            DFMPDynamicEnemyRecord[] roster = CreateRoster();
            var registry = new DFMPDynamicEnemyRegistry();
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.RegisterRoster(true, roster));
            string enemyId = roster[0].Identity.EnemyId;
            DFMPDynamicEnemyDescriptor descriptor = roster[0].Descriptor;
            descriptor.DungeonLocalPosition += new Vector3(1f, 0f, 0f);
            descriptor.FacingYaw = 90f;

            DFMPDynamicEnemyRecord updatedRecord;
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.InvalidAuthority, registry.TryUpdateAiState(false, enemyId, descriptor, 44, true, out updatedRecord));
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, registry.TryUpdateAiState(true, enemyId, descriptor, 44, true, out updatedRecord));

            Assert.AreEqual(descriptor.DungeonLocalPosition, updatedRecord.Descriptor.DungeonLocalPosition);
            Assert.AreEqual(90f, updatedRecord.Descriptor.FacingYaw);
            Assert.AreEqual(44, updatedRecord.TargetConnectionId);
            Assert.IsTrue(updatedRecord.IsMoving);
        }

        [Test]
        public void DynamicEnemyAi_SelectsNearestVisibleLiveTargetAndMovesTowardAttackRange()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                FacingYaw = 270f,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 11, DungeonLocalPosition = new Vector3(12f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true },
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 12, DungeonLocalPosition = new Vector3(4f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true },
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 13, DungeonLocalPosition = new Vector3(2f, 0f, 0f), SpawnConfirmed = true, IsDead = true, HasLineOfSight = true }
                },
                AwarenessRange = 16f,
                AttackRange = 2f,
                MoveSpeed = 3f,
                DeltaTime = 0.5f,
                RequireLineOfSight = true
            });

            Assert.IsTrue(decision.HasTarget);
            Assert.AreEqual(12, decision.TargetConnectionId);
            Assert.IsTrue(decision.IsMoving);
            Assert.IsFalse(decision.InAttackRange);
            Assert.AreEqual(new Vector3(1.5f, 0f, 0f), decision.NextDungeonLocalPosition);
            Assert.AreEqual(90f, decision.FacingYaw);
        }

        [Test]
        public void DynamicEnemyAi_RequiresLineOfSightAndStopsAtAttackRange()
        {
            DFMPDynamicEnemyAiDecision blocked = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                FacingYaw = 45f,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 21, DungeonLocalPosition = new Vector3(1f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = false }
                },
                AwarenessRange = 8f,
                AttackRange = 2f,
                MoveSpeed = 3f,
                DeltaTime = 1f,
                RequireLineOfSight = true
            });

            Assert.IsFalse(blocked.HasTarget);
            Assert.AreEqual(Vector3.zero, blocked.NextDungeonLocalPosition);
            Assert.AreEqual(45f, blocked.FacingYaw);

            DFMPDynamicEnemyAiDecision inRange = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                FacingYaw = 0f,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 22, DungeonLocalPosition = new Vector3(1f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = false }
                },
                AwarenessRange = 8f,
                AttackRange = 2f,
                MoveSpeed = 3f,
                DeltaTime = 1f,
                RequireLineOfSight = false
            });

            Assert.IsTrue(inRange.HasTarget);
            Assert.AreEqual(22, inRange.TargetConnectionId);
            Assert.IsTrue(inRange.InAttackRange);
            Assert.IsFalse(inRange.IsMoving);
            Assert.AreEqual(Vector3.zero, inRange.NextDungeonLocalPosition);
        }

        [Test]
        public void DynamicEnemyAi_RetainsCurrentTargetWhenStillValid()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                FacingYaw = 0f,
                CurrentTargetConnectionId = 31,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 30, DungeonLocalPosition = new Vector3(2f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true },
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 31, DungeonLocalPosition = new Vector3(6f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true }
                },
                AwarenessRange = 8f,
                AttackRange = 2f,
                MoveSpeed = 10f,
                DeltaTime = 0.1f,
                RequireLineOfSight = true
            });

            Assert.IsTrue(decision.HasTarget);
            Assert.AreEqual(31, decision.TargetConnectionId);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), decision.NextDungeonLocalPosition);
        }

        [Test]
        public void DynamicEnemyAi_PursuitLeashReturnsEnemyHomeBeforeTargeting()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = new Vector3(5f, 0f, 0f),
                HomePosition = Vector3.zero,
                PursuitLeashRange = 3f,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 41, DungeonLocalPosition = new Vector3(6f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true }
                },
                AwarenessRange = 64f,
                AttackRange = 1f,
                MoveSpeed = 4f,
                DeltaTime = 0.5f,
                RequireLineOfSight = false
            });

            Assert.IsFalse(decision.HasTarget);
            Assert.IsTrue(decision.IsMoving);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), decision.NextDungeonLocalPosition);
            Assert.AreEqual(270f, decision.FacingYaw);
        }

        [Test]
        public void DynamicEnemyAi_PursuitLeashClampsOutboundMovement()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = new Vector3(2f, 0f, 0f),
                HomePosition = Vector3.zero,
                PursuitLeashRange = 3f,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 42, DungeonLocalPosition = new Vector3(10f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true }
                },
                AwarenessRange = 64f,
                AttackRange = 1f,
                MoveSpeed = 4f,
                DeltaTime = 1f,
                RequireLineOfSight = false
            });

            Assert.IsTrue(decision.HasTarget);
            Assert.AreEqual(new Vector3(3f, 0f, 0f), decision.NextDungeonLocalPosition);
        }

        [Test]
        public void DynamicEnemyAi_IgnoresTemporarilyBlockedTarget()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                IgnoredTargetConnectionId = 43,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 43, DungeonLocalPosition = new Vector3(4f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true },
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 44, DungeonLocalPosition = new Vector3(6f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true }
                },
                AwarenessRange = 64f,
                AttackRange = 1f,
                MoveSpeed = 4f,
                DeltaTime = 0.1f,
                RequireLineOfSight = false
            });

            Assert.IsTrue(decision.HasTarget);
            Assert.AreEqual(44, decision.TargetConnectionId);
        }

        [Test]
        public void DynamicEnemyAi_PassiveEnemyDoesNotAcquireTarget()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                IsPassive = true,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 45, DungeonLocalPosition = new Vector3(1f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true }
                },
                AwarenessRange = 64f,
                AttackRange = 2f,
                MoveSpeed = 4f,
                DeltaTime = 1f,
                RequireLineOfSight = false
            });

            Assert.IsFalse(decision.HasTarget);
            Assert.AreEqual(-1, decision.TargetConnectionId);
            Assert.IsFalse(decision.InAttackRange);
            Assert.IsFalse(decision.IsMoving);
        }

        [Test]
        public void DynamicEnemyAi_ProvokedPassiveEnemyRetainsOnlyCurrentTarget()
        {
            DFMPDynamicEnemyAiDecision decision = DFMPDynamicEnemyAiPolicy.Evaluate(new DFMPDynamicEnemyAiInput
            {
                EnemyPosition = Vector3.zero,
                CurrentTargetConnectionId = 46,
                IsPassive = true,
                Targets = new DFMPDynamicEnemySensoryTarget[]
                {
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 46, DungeonLocalPosition = new Vector3(1f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true },
                    new DFMPDynamicEnemySensoryTarget { ConnectionId = 47, DungeonLocalPosition = new Vector3(2f, 0f, 0f), SpawnConfirmed = true, HasLineOfSight = true }
                },
                AwarenessRange = 64f,
                AttackRange = 2f,
                MoveSpeed = 4f,
                DeltaTime = 0.1f,
                RequireLineOfSight = false
            });

            Assert.IsTrue(decision.HasTarget);
            Assert.AreEqual(46, decision.TargetConnectionId);
            Assert.IsTrue(decision.InAttackRange);
        }

        [Test]
        public void DynamicEnemyAttackPolicy_SelectsMeleeRangedAndMagicProfiles()
        {
            DFMPDynamicEnemyAttackProfile melee = DFMPDynamicEnemyAttackPolicy.GetProfile((int)MobileTypes.Orc, 2f, 8f, 12f);
            DFMPDynamicEnemyAttackProfile ranged = DFMPDynamicEnemyAttackPolicy.GetProfile((int)MobileTypes.Harpy, 2f, 8f, 12f);
            DFMPDynamicEnemyAttackProfile magic = DFMPDynamicEnemyAttackPolicy.GetProfile((int)MobileTypes.OrcShaman, 2f, 8f, 12f);

            Assert.AreEqual(DFMPDynamicEnemyAttackKind.Melee, melee.Kind);
            Assert.AreEqual(2f, melee.Range);
            Assert.AreEqual(DFMPDynamicEnemyAttackKind.Ranged, ranged.Kind);
            Assert.AreEqual(8f, ranged.Range);
            Assert.AreEqual(DFMPDynamicEnemyAttackKind.Magic, magic.Kind);
            Assert.AreEqual(12f, magic.Range);
        }

        [Test]
        public void DungeonRoster_NativeEnemyResolverUsesFixedTypeAndDungeonEncounterTable()
        {
            int mobileType;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryResolveNativeEnemyType(
                DFRegion.DungeonTypes.HumanStronghold,
                true,
                (int)MobileTypes.SkeletalWarrior,
                0,
                10000,
                0f,
                4,
                1234UL,
                0,
                out mobileType));
            Assert.AreEqual((int)MobileTypes.SkeletalWarrior, mobileType);

            Assert.IsTrue(DFMPDungeonRosterPolicy.TryResolveNativeEnemyType(
                DFRegion.DungeonTypes.HumanStronghold,
                false,
                0,
                0,
                10000,
                0f,
                4,
                1234UL,
                1,
                out mobileType));
            Assert.IsTrue(EnemyBasics.GetEnemy((MobileTypes)mobileType, out _));
        }

        [Test]
        public void DungeonRoster_CalculatesNativeMonsterPowerFromPlayerLevel()
        {
            Assert.AreEqual(0.05f, DFMPDungeonRosterPolicy.CalculateNativeMonsterPower(1));
            Assert.AreEqual(0.5f, DFMPDungeonRosterPolicy.CalculateNativeMonsterPower(10));
            Assert.AreEqual(1f, DFMPDungeonRosterPolicy.CalculateNativeMonsterPower(30));
        }

        [Test]
        public void DungeonRoster_NativeWaterEnemyEligibilityMatchesDfu()
        {
            Assert.IsTrue(DFMPDungeonRosterPolicy.ShouldSpawnNativeEnemy(MobileTypes.Rat, 10000, 0));
            Assert.IsFalse(DFMPDungeonRosterPolicy.ShouldSpawnNativeEnemy(MobileTypes.Slaughterfish, 10000, 0));
            Assert.IsFalse(DFMPDungeonRosterPolicy.ShouldSpawnNativeEnemy(MobileTypes.Dreugh, 100, 50));
            Assert.IsTrue(DFMPDungeonRosterPolicy.ShouldSpawnNativeEnemy(MobileTypes.Dreugh, 100, 110));
        }

        [Test]
        public void DungeonRoster_NativeAwarenessRangeMatchesDfuSpawnDistanceTable()
        {
            Assert.AreEqual(25.6f, DFMPDungeonRosterPolicy.GetNativeAwarenessRange(0));
            Assert.AreEqual(9.6f, DFMPDungeonRosterPolicy.GetNativeAwarenessRange(1));
            Assert.AreEqual(19.2f, DFMPDungeonRosterPolicy.GetNativeAwarenessRange(3));
            Assert.AreEqual(19.2f, DFMPDungeonRosterPolicy.GetNativeAwarenessRange(99));
        }

        [Test]
        public void DungeonRoster_FlyingEnemiesReceiveHoverOffsetAfterGrounding()
        {
            Assert.AreEqual(0.75f, DFMPDungeonRosterPolicy.GetNativeFlyingHeightOffset((int)MobileTypes.GiantBat));
            Assert.AreEqual(0.75f, DFMPDungeonRosterPolicy.GetNativeFlyingHeightOffset((int)MobileTypes.Imp));
            Assert.AreEqual(0f, DFMPDungeonRosterPolicy.GetNativeFlyingHeightOffset((int)MobileTypes.Rat));
        }

        [Test]
        public void DungeonRoster_NativeGenderFlagsApplyOnlyToHumanoidFixedEnemies()
        {
            DFMPWorldContextKey context = CreateDungeonContext();
            var inputs = new DFMPDungeonRosterPolicy.NativeDungeonGenerationInputs
            {
                DungeonType = DFRegion.DungeonTypes.Crypt,
                DungeonRecordId = 42,
                BlockSeed = 123,
                WaterLevel = 10000
            };
            var markers = new DFMPDungeonRosterPolicy.NativeDungeonMarker[]
            {
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = Vector3.zero,
                    TextureRecord = DFMPDungeonRosterPolicy.FixedMonsterTextureRecord,
                    FixedMobileType = (int)MobileTypes.Orc,
                    ClassicSlot = (int)DFBlock.EnemyGenders.Female
                },
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = Vector3.one,
                    TextureRecord = DFMPDungeonRosterPolicy.FixedMonsterTextureRecord,
                    FixedMobileType = (int)MobileTypes.Archer,
                    ClassicSlot = (int)DFBlock.EnemyGenders.Female
                }
            };

            DFMPDynamicEnemyDescriptor[] descriptors;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateNativeDescriptors(1234UL, context, inputs, markers, 0f, 4, out descriptors));
            Assert.AreEqual((int)MobileGender.Unspecified, descriptors[0].Gender);
            Assert.AreEqual((int)MobileGender.Female, descriptors[1].Gender);
        }

        [Test]
        public void DungeonRoster_ResolvesNativeDungeonTypeBlockCoordinatesAndWaterLevel()
        {
            DFLocation location = new DFLocation
            {
                Loaded = true,
                HasDungeon = true,
                MapTableData = new DFRegion.RegionMapTable
                {
                    DungeonType = DFRegion.DungeonTypes.Crypt
                },
                Dungeon = new DFLocation.LocationDungeon
                {
                    Blocks = new DFLocation.DungeonBlock[]
                    {
                        new DFLocation.DungeonBlock
                        {
                            X = -2,
                            Z = 3,
                            BlockName = "S0000161.RDB",
                            WaterLevel = 144
                        }
                    }
                }
            };
            DFMPWorldContextKey context = CreateDungeonContext();
            context.DungeonBlockIndex = 0;

            DFMPDungeonRosterPolicy.NativeDungeonGenerationInputs inputs;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryResolveNativeDungeonGenerationInputs(location, context, out inputs));
            Assert.AreEqual(DFRegion.DungeonTypes.Crypt, inputs.DungeonType);
            Assert.AreEqual(0, inputs.DungeonRecordId);
            Assert.AreEqual(0, inputs.BlockSeed);
            Assert.AreEqual(-2, inputs.BlockX);
            Assert.AreEqual(3, inputs.BlockZ);
            Assert.AreEqual(144, inputs.WaterLevel);
        }

        [Test]
        public void DungeonRoster_NativeDescriptorsKeepOnlyMonsterMarkersAndPreservePositions()
        {
            DFMPWorldContextKey context = CreateDungeonContext();
            var inputs = new DFMPDungeonRosterPolicy.NativeDungeonGenerationInputs
            {
                DungeonType = DFRegion.DungeonTypes.Crypt,
                BlockX = 0,
                BlockZ = 0,
                WaterLevel = 10000
            };
            var markers = new DFMPDungeonRosterPolicy.NativeDungeonMarker[]
            {
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = new Vector3(-1f, 0f, -2f),
                    TextureRecord = DFMPDungeonRosterPolicy.FixedMonsterTextureRecord,
                    FixedMobileType = 99,
                    MarkerY = 0
                },
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = new Vector3(1f, 2f, 3f),
                    TextureRecord = DFMPDungeonRosterPolicy.FixedMonsterTextureRecord,
                    FixedMobileType = (int)MobileTypes.SkeletalWarrior,
                    MarkerY = 0
                },
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = new Vector3(4f, 5f, 6f),
                    TextureRecord = DFMPDungeonRosterPolicy.ItemMarkerTextureRecord,
                    MarkerY = 0
                },
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = new Vector3(7f, 8f, 9f),
                    TextureRecord = DFMPDungeonRosterPolicy.RandomMonsterTextureRecord,
                    MarkerY = 0
                }
            };

            DFMPDynamicEnemyDescriptor[] descriptors;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateNativeDescriptors(1234UL, context, inputs, markers, 0f, 4, out descriptors));
            Assert.AreEqual(2, descriptors.Length);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), descriptors[0].DungeonLocalPosition);
            Assert.AreEqual((int)MobileTypes.SkeletalWarrior, descriptors[0].MobileType);
            Assert.AreEqual(new Vector3(7f, 8f, 9f), descriptors[1].DungeonLocalPosition);
        }

        [Test]
        public void DungeonRoster_NativeDescriptorRecordsUseNativeDescriptorCount()
        {
            DFMPWorldContextKey context = CreateDungeonContext();
            DFMPDynamicEnemyDescriptor[] descriptors = new DFMPDynamicEnemyDescriptor[]
            {
                new DFMPDynamicEnemyDescriptor { DungeonLocalPosition = new Vector3(1f, 0f, 2f), MobileType = (int)MobileTypes.Rat },
                new DFMPDynamicEnemyDescriptor { DungeonLocalPosition = new Vector3(3f, 0f, 4f), MobileType = (int)MobileTypes.SkeletalWarrior }
            };

            DFMPDynamicEnemyRecord[] roster;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRosterFromDescriptors(1234UL, context, descriptors, out roster));
            Assert.AreEqual(2, roster.Length);
            Assert.AreEqual(descriptors[0], roster[0].Descriptor);
            Assert.AreEqual(descriptors[1], roster[1].Descriptor);
            Assert.AreEqual(0, roster[0].Identity.RosterIndex);
            Assert.AreEqual(1, roster[1].Identity.RosterIndex);
        }

        [Test]
        public void DungeonRoster_NativeMarkerPathBuildsEndToEndRecords()
        {
            DFMPWorldContextKey context = CreateDungeonContext();
            var inputs = new DFMPDungeonRosterPolicy.NativeDungeonGenerationInputs
            {
                DungeonType = DFRegion.DungeonTypes.Crypt,
                DungeonRecordId = 42,
                BlockSeed = 123,
                BlockX = 0,
                BlockZ = 0,
                WaterLevel = 10000
            };
            var markers = new DFMPDungeonRosterPolicy.NativeDungeonMarker[]
            {
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = new Vector3(2f, 0f, 3f),
                    TextureRecord = DFMPDungeonRosterPolicy.FixedMonsterTextureRecord,
                    FixedMobileType = (int)MobileTypes.Rat,
                    MarkerY = 0
                },
                new DFMPDungeonRosterPolicy.NativeDungeonMarker
                {
                    DungeonLocalPosition = new Vector3(5f, 0f, 6f),
                    TextureRecord = DFMPDungeonRosterPolicy.ItemMarkerTextureRecord,
                    MarkerY = 0
                }
            };

            DFMPDynamicEnemyRecord[] roster;
            Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateNativeRosterFromMarkers(1234UL, context, inputs, markers, 0f, 4, out roster));
            Assert.AreEqual(1, roster.Length);
            Assert.AreEqual(new Vector3(2f, 0f, 3f), roster[0].Descriptor.DungeonLocalPosition);
            Assert.AreEqual((int)MobileTypes.Rat, roster[0].Descriptor.MobileType);
            Assert.AreEqual(0, roster[0].Identity.RosterIndex);
            Assert.Greater(roster[0].MaxHealth, 0);
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
                SourceIsDead = false,
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

            context.TargetIsDead = false;
            context.SourceIsDead = true;
            Assert.AreEqual(DFMPDamageRejectionReason.SourceDead, DFMPDamagePolicy.GetRejectionReason(request, context));

            context.SourceIsDead = false;
            context.HasPendingTransition = true;
            Assert.AreEqual(DFMPDamageRejectionReason.TransitionPending, DFMPDamagePolicy.GetRejectionReason(request, context));

            context.HasPendingTransition = false;
            request.SourceKind = DFMPDamageSourceKind.NativeEnemy;
            Assert.AreEqual(DFMPDamageRejectionReason.InvalidSource, DFMPDamagePolicy.GetRejectionReason(request, context));
        }

        [Test]
        public void DungeonRosterService_AppliesDamageAndPublishesEnemyDiedEvent()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceDamageTest");
            GameObject sessionObject = new GameObject("DFMP_RosterSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig { WorldSeed = "damage-test-seed", EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold, DungeonRosterSize = 2 });
                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext();

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(55, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("damage-test-seed"), dungeon, 2, out roster));
                string enemyId = roster[0].Identity.EnemyId;

                DFMPEnemyDiedEvent deathEvent = null;
                DFMPLootGeneratedEvent lootEvent = null;
                DFMPEventBus.Instance.EnemyDied += e => deathEvent = e;
                DFMPEventBus.Instance.LootGenerated += e => lootEvent = e;

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

                Assert.IsNotNull(lootEvent);
                Assert.AreEqual(enemyId, lootEvent.EnemyId);
                Assert.AreEqual(55, lootEvent.KillerConnectionId);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRosterService_AiTick_AcquiresCoLocatedPlayerAndMovesEnemy()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceAiTickTest");
            GameObject sessionObject = new GameObject("DFMP_RosterServiceAiSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig
                {
                    WorldSeed = "ai-tick-seed",
                    EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold,
                    DungeonRosterSize = 1,
                    AwarenessRange = 64f,
                    AttackRange = 2f,
                    MoveSpeed = 4f,
                    RequireLineOfSight = false
                });

                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(66, 0, 0f, 0);
                session.ConfirmSpawn();
                DFMPWorldContextKey dungeon = CreateDungeonContext();
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(66, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("ai-tick-seed"), dungeon, 1, out roster));
                string enemyId = roster[0].Identity.EnemyId;
                Vector3 initialPosition = roster[0].Descriptor.DungeonLocalPosition;
                session.SetDungeonLocalPosition(true, initialPosition + new Vector3(10f, 0f, 0f));

                service.ProcessAiTick(10f, 0.5f);
                DFMPDynamicEnemyRecord updatedRecord = GetRecord(service, enemyId);

                Assert.AreEqual(66, updatedRecord.TargetConnectionId);
                Assert.IsTrue(updatedRecord.IsMoving);
                Assert.AreEqual(initialPosition + new Vector3(2f, 0f, 0f), updatedRecord.Descriptor.DungeonLocalPosition);
                Assert.AreEqual(90f, updatedRecord.Descriptor.FacingYaw);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRosterService_AiTick_GeometryStopsAtHostedBlocker()
        {
            GameObject geometryObject = new GameObject("DFMP_RosterServiceBlockedGeometryTest");
            GameObject serviceObject = new GameObject("DFMP_RosterServiceBlockedGeometryRoster");
            GameObject sessionObject = new GameObject("DFMP_RosterServiceBlockedGeometrySession");
            try
            {
                var geometry = geometryObject.AddComponent<DFMPDungeonGeometryService>();
                geometry.Initialize();
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.SetDungeonGeometryServiceForTesting(geometry);
                service.Initialize(new DFMPServerEnemyConfig
                {
                    WorldSeed = "blocked-geometry-seed",
                    EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold,
                    DungeonRosterSize = 1,
                    AwarenessRange = 64f,
                    AttackRange = 2f,
                    MoveSpeed = 4f,
                    RequireLineOfSight = false
                });

                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(116, 0, 0f, 0);
                session.ConfirmSpawn();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "blocked-geometry");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(116, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("blocked-geometry-seed"), dungeon, 1, out roster));
                string enemyId = roster[0].Identity.EnemyId;
                Vector3 initialPosition = roster[0].Descriptor.DungeonLocalPosition;
                session.SetDungeonLocalPosition(true, initialPosition + new Vector3(10f, 0f, 0f));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(geometry.TryGetRoot(scope, out root));
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = initialPosition + new Vector3(1f, 1f, 0f);
                wall.transform.localScale = new Vector3(0.25f, 3f, 3f);
                Physics.SyncTransforms();
                Assert.IsTrue(geometry.TryMarkGeometryAvailableForTesting(scope));

                service.ProcessAiTick(10f, 0.5f);
                service.ProcessAiTick(10.5f, 0.5f);
                service.ProcessAiTick(11f, 0.5f);
                service.ProcessAiTick(11.5f, 0.5f);
                DFMPDynamicEnemyRecord updatedRecord = GetRecord(service, enemyId);

                Assert.AreEqual(-1, updatedRecord.TargetConnectionId);
                Assert.IsFalse(updatedRecord.IsMoving);
                Assert.Less(updatedRecord.Descriptor.DungeonLocalPosition.x, initialPosition.x + 1f);
                Assert.Greater(updatedRecord.Descriptor.DungeonLocalPosition.x, initialPosition.x);
            }
            finally
            {
                UnityObject.DestroyImmediate(geometryObject);
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRosterService_AiTick_NonStrictGeometryUnavailableRetainsMovementFallback()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceUnavailableGeometryRoster");
            GameObject sessionObject = new GameObject("DFMP_RosterServiceUnavailableGeometrySession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig
                {
                    WorldSeed = "unavailable-geometry-seed",
                    EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold,
                    DungeonRosterSize = 1,
                    AwarenessRange = 64f,
                    AttackRange = 2f,
                    MoveSpeed = 4f,
                    RequireLineOfSight = false
                });

                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(117, 0, 0f, 0);
                session.ConfirmSpawn();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "unavailable-geometry");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(117, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("unavailable-geometry-seed"), dungeon, 1, out roster));
                string enemyId = roster[0].Identity.EnemyId;
                Vector3 initialPosition = roster[0].Descriptor.DungeonLocalPosition;
                session.SetDungeonLocalPosition(true, initialPosition + new Vector3(10f, 0f, 0f));

                service.ProcessAiTick(10f, 0.5f);
                DFMPDynamicEnemyRecord updatedRecord = GetRecord(service, enemyId);

                Assert.IsTrue(updatedRecord.IsMoving);
                Assert.AreEqual(initialPosition + new Vector3(2f, 0f, 0f), updatedRecord.Descriptor.DungeonLocalPosition);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRosterService_AiTick_WithStrictLineOfSightDoesNotAcquireWithoutGeometry()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceAiStrictLosTest");
            GameObject sessionObject = new GameObject("DFMP_RosterServiceAiStrictLosSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig
                {
                    WorldSeed = "ai-strict-los-seed",
                    EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold,
                    DungeonRosterSize = 1,
                    AwarenessRange = 64f,
                    AttackRange = 2f,
                    MoveSpeed = 4f,
                    RequireLineOfSight = true
                });

                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(69, 0, 0f, 0);
                session.ConfirmSpawn();
                DFMPWorldContextKey dungeon = CreateDungeonContext();
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(69, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("ai-strict-los-seed"), dungeon, 1, out roster));
                string enemyId = roster[0].Identity.EnemyId;
                Vector3 initialPosition = roster[0].Descriptor.DungeonLocalPosition;
                session.SetDungeonLocalPosition(true, initialPosition + new Vector3(10f, 0f, 0f));

                service.ProcessAiTick(10f, 0.5f);
                DFMPDynamicEnemyRecord updatedRecord = GetRecord(service, enemyId);

                Assert.AreEqual(-1, updatedRecord.TargetConnectionId);
                Assert.IsFalse(updatedRecord.IsMoving);
                Assert.AreEqual(initialPosition, updatedRecord.Descriptor.DungeonLocalPosition);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRosterService_AiTickCadence_WaitsForConfiguredInterval()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceAiCadenceTest");
            GameObject sessionObject = new GameObject("DFMP_RosterServiceAiCadenceSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                service.Initialize(new DFMPServerEnemyConfig
                {
                    WorldSeed = "ai-cadence-seed",
                    EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold,
                    DungeonRosterSize = 1,
                    AwarenessRange = 64f,
                    AttackRange = 2f,
                    MoveSpeed = 10f,
                    AiTickIntervalSeconds = 0.25f,
                    RequireLineOfSight = false
                });

                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(67, 0, 0f, 0);
                session.ConfirmSpawn();
                DFMPWorldContextKey dungeon = CreateDungeonContext();
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(67, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("ai-cadence-seed"), dungeon, 1, out roster));
                string enemyId = roster[0].Identity.EnemyId;
                Vector3 initialPosition = roster[0].Descriptor.DungeonLocalPosition;
                session.SetDungeonLocalPosition(true, initialPosition + new Vector3(10f, 0f, 0f));

                service.ProcessAiTickCadence(10f, 0.1f);
                Assert.AreEqual(initialPosition, GetRecord(service, enemyId).Descriptor.DungeonLocalPosition);

                service.ProcessAiTickCadence(10.1f, 0.1f);
                Assert.AreEqual(initialPosition, GetRecord(service, enemyId).Descriptor.DungeonLocalPosition);

                service.ProcessAiTickCadence(10.25f, 0.05f);
                Assert.AreEqual(initialPosition + new Vector3(2.5f, 0f, 0f), GetRecord(service, enemyId).Descriptor.DungeonLocalPosition);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonRosterService_AiTick_AttacksTargetOnCooldownThroughDamageApplier()
        {
            GameObject serviceObject = new GameObject("DFMP_RosterServiceAiAttackTest");
            GameObject sessionObject = new GameObject("DFMP_RosterServiceAiAttackSession");
            try
            {
                var service = serviceObject.AddComponent<DFMPDungeonEnemyRosterService>();
                int attackCount = 0;
                string attackedEnemyId = string.Empty;
                int attackedConnectionId = -1;
                int attackedAmount = 0;
                service.SetServerEnemyDamageApplierForTesting((appliedEnemyId, appliedTargetConnectionId, appliedAmount) =>
                {
                    attackCount++;
                    attackedEnemyId = appliedEnemyId;
                    attackedConnectionId = appliedTargetConnectionId;
                    attackedAmount = appliedAmount;
                    return true;
                });

                service.Initialize(new DFMPServerEnemyConfig
                {
                    WorldSeed = "ai-attack-seed",
                    EnemyRosterMode = DFMPEnemyRosterModes.DevelopmentScaffold,
                    DungeonRosterSize = 1,
                    AwarenessRange = 64f,
                    AttackRange = 2f,
                    MoveSpeed = 10f,
                    AttackCooldownSeconds = 1.5f,
                    AttackDamage = 7,
                    RequireLineOfSight = false
                });

                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(68, 0, 0f, 0);
                session.ConfirmSpawn();
                DFMPWorldContextKey dungeon = CreateDungeonContext();
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(68, session, dungeon, "test"));

                DFMPDynamicEnemyRecord[] roster;
                Assert.IsTrue(DFMPDungeonRosterPolicy.TryCreateRoster(DFMPDungeonRosterPolicy.CreateServerWorldSeed("ai-attack-seed"), dungeon, 1, out roster));
                string enemyId = roster[0].Identity.EnemyId;
                session.SetDungeonLocalPosition(true, roster[0].Descriptor.DungeonLocalPosition + new Vector3(1f, 0f, 0f));

                service.ProcessAiTick(10f, 0.1f);
                service.ProcessAiTick(11f, 0.1f);
                service.ProcessAiTick(11.5f, 0.1f);

                Assert.AreEqual(2, attackCount);
                Assert.AreEqual(enemyId, attackedEnemyId);
                Assert.AreEqual(68, attackedConnectionId);
                Assert.AreEqual(7, attackedAmount);
                Assert.IsFalse(GetRecord(service, enemyId).IsMoving);
            }
            finally
            {
                UnityObject.DestroyImmediate(serviceObject);
                UnityObject.DestroyImmediate(sessionObject);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DynamicEnemyPresentation_CalculatesCorpseEligibility()
        {
            GameObject enemyObject = new GameObject("DFMP_CorpseEligibilityTest");
            try
            {
                enemyObject.AddComponent<NetworkIdentity>();
                var carrier = enemyObject.AddComponent<DFMPWorldContextCarrier>();
                var state = enemyObject.AddComponent<DFMPDynamicEnemyState>();
                DFMPDynamicEnemyRecord record = CreateRoster()[0];
                carrier.Initialize(record.Identity.Encounter.Context);
                state.Initialize(record);

                DFMPWorldContextKey matchingContext = record.Identity.Encounter.Context;
                DFMPWorldContextKey otherBlockContext = matchingContext;
                otherBlockContext.DungeonBlockIndex = 99;
                DFMPWorldContextKey otherDungeonContext = matchingContext;
                otherDungeonContext.LocationIndex = 999;

                // When alive, corpse is not eligible
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsCorpseEligible(state, true, matchingContext));

                // When dead and co-located in dungeon block, corpse is eligible
                record.LifecycleState = DFMPDynamicEnemyLifecycleState.Dead;
                state.Initialize(record);
                Assert.IsTrue(DFMPDynamicEnemyPresentation.IsCorpseEligible(state, true, matchingContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsCorpseEligible(state, true, otherBlockContext));

                // When player is not in dungeon or in another context
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsCorpseEligible(state, false, matchingContext));
                Assert.IsFalse(DFMPDynamicEnemyPresentation.IsCorpseEligible(state, true, otherDungeonContext));
            }
            finally
            {
                UnityObject.DestroyImmediate(enemyObject);
            }
        }

        [Test]
        public void WorldContext_SharesInterestScope_MatchesExpectedRules()
        {
            DFMPWorldContextKey dungeonBlock0 = CreateDungeonContext();
            dungeonBlock0.DungeonBlockIndex = 0;
            DFMPWorldContextKey dungeonBlock2 = dungeonBlock0;
            dungeonBlock2.DungeonBlockIndex = 2;
            dungeonBlock2.DungeonBlockName = "M0000004.RDB";

            DFMPWorldContextKey differentDungeon = dungeonBlock0;
            differentDungeon.LocationIndex = 999;

            DFMPWorldContextKey exterior = dungeonBlock0;
            exterior.Kind = DFMPWorldContextKind.Exterior;

            Assert.IsTrue(dungeonBlock0.SharesInterestScope(dungeonBlock2));
            Assert.IsTrue(dungeonBlock2.SharesInterestScope(dungeonBlock0));
            Assert.IsFalse(dungeonBlock0.SharesInterestScope(differentDungeon));
            Assert.IsFalse(dungeonBlock0.SharesInterestScope(exterior));

            DFMPWorldContextKey interiorA = new DFMPWorldContextKey { Kind = DFMPWorldContextKind.BuildingInterior, MapPixelX = 10, MapPixelY = 20, BuildingKey = 100 };
            DFMPWorldContextKey interiorSame = new DFMPWorldContextKey { Kind = DFMPWorldContextKind.BuildingInterior, MapPixelX = 10, MapPixelY = 20, BuildingKey = 100 };
            DFMPWorldContextKey interiorDifferent = new DFMPWorldContextKey { Kind = DFMPWorldContextKind.BuildingInterior, MapPixelX = 10, MapPixelY = 20, BuildingKey = 101 };

            Assert.IsTrue(interiorA.SharesInterestScope(interiorSame));
            Assert.IsFalse(interiorA.SharesInterestScope(interiorDifferent));

            var registry = new DFMPWorldOccupancyRegistry();
            registry.SetContext(1, dungeonBlock0);
            registry.SetContext(2, dungeonBlock2);
            registry.SetContext(3, differentDungeon);

            int[] dungeonOccupants = registry.GetConnectionsInInterestScope(dungeonBlock0);
            Assert.AreEqual(2, dungeonOccupants.Length);
            CollectionAssert.AreEquivalent(new int[] { 1, 2 }, dungeonOccupants);
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

        static DFMPWorldContextKey CreateDungeonContext(int dungeonBlockIndex, string dungeonBlockName, string instanceId)
        {
            DFMPWorldContextKey context = CreateDungeonContext();
            context.DungeonBlockIndex = dungeonBlockIndex;
            context.DungeonBlockName = dungeonBlockName;
            context.InstanceId = instanceId;
            return context;
        }
    }
}

using NUnit.Framework;
using DFMP.Runtime;
using DaggerfallWorkshop;
using Mirror;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class WorldContextTests
    {
        [Test]
        public void ContextKey_DistinguishesLocationAndInstance()
        {
            var exterior = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall"
            };
            var sameExterior = exterior;
            var dungeon = exterior;
            dungeon.Kind = DFMPWorldContextKind.Dungeon;
            dungeon.InstanceId = "dungeon-seed-1";

            Assert.AreEqual(exterior, sameExterior);
            Assert.AreNotEqual(exterior, dungeon);
        }

        [Test]
        public void ContextKey_MatchesLocationAndInstanceCaseInsensitively()
        {
            var first = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Dungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall Dungeon",
                InstanceId = "Seed-1"
            };
            var second = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Dungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "daggerfall dungeon",
                InstanceId = "seed-1"
            };

            Assert.AreEqual(first, second);
            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void ContextKey_DistinguishesBuildingAndDungeonBlockIdentity()
        {
            var building = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.BuildingInterior,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 41,
                LocationId = "Daggerfall",
                BuildingKey = 12345
            };
            var otherBuilding = building;
            otherBuilding.BuildingKey = 12346;
            var dungeonBlock = new DFMPWorldContextKey
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
            var otherDungeonBlock = dungeonBlock;
            otherDungeonBlock.DungeonBlockIndex = 8;

            Assert.AreNotEqual(building, otherBuilding);
            Assert.AreNotEqual(dungeonBlock, otherDungeonBlock);
        }

        [Test]
        public void ContextRecord_RoundTripsCanonicalKey()
        {
            var key = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Dungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 42,
                LocationId = " Daggerfall Dungeon ",
                DungeonBlockIndex = 7,
                DungeonBlockName = " S0000161.RDB ",
                InstanceId = " shared "
            };

            DFMPWorldContextRecord record = DFMPWorldContextRecord.FromKey(key);
            DFMPWorldContextKey restored = record.ToKey();

            Assert.AreEqual(DFMPWorldContextKind.Dungeon.ToString(), record.Kind);
            Assert.AreEqual("Daggerfall Dungeon", record.LocationId);
            Assert.AreEqual("S0000161.RDB", record.DungeonBlockName);
            Assert.AreEqual("shared", record.InstanceId);
            Assert.AreEqual(DFMPWorldContextKind.Dungeon, restored.Kind);
            Assert.AreEqual(207, restored.MapPixelX);
            Assert.AreEqual(213, restored.MapPixelY);
            Assert.AreEqual(3, restored.RegionIndex);
            Assert.AreEqual(42, restored.LocationIndex);
            Assert.AreEqual(7, restored.DungeonBlockIndex);
        }

        [Test]
        public void OccupancyRegistry_MovesConnectionsBetweenContexts()
        {
            var registry = new DFMPWorldOccupancyRegistry();
            var city = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall"
            };
            var dungeon = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Dungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall Dungeon",
                InstanceId = "seed-1"
            };

            registry.SetContext(10, city);
            registry.SetContext(11, city);
            Assert.IsTrue(registry.AreCoLocated(10, 11));
            Assert.AreEqual(2, registry.GetConnectionsInContext(city).Length);

            registry.SetContext(11, dungeon);
            Assert.IsFalse(registry.AreCoLocated(10, 11));
            Assert.AreEqual(1, registry.GetConnectionsInContext(city).Length);
            Assert.AreEqual(1, registry.GetConnectionsInContext(dungeon).Length);

            registry.Remove(10);
            Assert.AreEqual(0, registry.GetConnectionsInContext(city).Length);
        }

        [Test]
        public void OccupancyRegistry_RepeatedSetDoesNotDuplicateConnection()
        {
            var registry = new DFMPWorldOccupancyRegistry();
            var city = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall"
            };

            registry.SetContext(10, city);
            registry.SetContext(10, city);

            Assert.AreEqual(1, registry.ConnectionCount);
            Assert.AreEqual(1, registry.ContextCount);
            Assert.AreEqual(1, registry.GetConnectionsInContext(city).Length);
        }

        [Test]
        public void OccupancyRegistry_RemoveUnknownConnectionIsNoOp()
        {
            var registry = new DFMPWorldOccupancyRegistry();
            var city = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall"
            };

            registry.SetContext(10, city);
            registry.Remove(99);

            Assert.AreEqual(1, registry.ConnectionCount);
            Assert.AreEqual(1, registry.ContextCount);
            Assert.AreEqual(1, registry.GetConnectionsInContext(city).Length);
        }

        [Test]
        public void OccupancyRegistry_ClearRemovesConnectionsAndContexts()
        {
            var registry = new DFMPWorldOccupancyRegistry();
            var city = new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Exterior,
                MapPixelX = 207,
                MapPixelY = 213,
                LocationId = "Daggerfall"
            };

            registry.SetContext(10, city);
            registry.Clear();

            DFMPWorldContextKey context;
            Assert.IsFalse(registry.TryGetContext(10, out context));
            Assert.AreEqual(0, registry.ConnectionCount);
            Assert.AreEqual(0, registry.ContextCount);
            Assert.AreEqual(0, registry.GetConnectionsInContext(city).Length);
        }

        [Test]
        public void NetworkServer_CreateExteriorWorldContext_UsesWorldCoordinateMapPixel()
        {
            DFMPWorldContextKey context = DFMPNetworkServer.CreateExteriorWorldContext(6799360, 9388032, "Daggerfall");

            Assert.AreEqual(DFMPWorldContextKind.Exterior, context.Kind);
            Assert.AreEqual(207, context.MapPixelX);
            Assert.AreEqual(213, context.MapPixelY);
            Assert.AreEqual("Daggerfall", context.LocationId);
            Assert.IsTrue(string.IsNullOrEmpty(context.InstanceId));
        }

        [Test]
        public void ContextProtocol_RejectsInvalidReports()
        {
            Assert.AreEqual(
                DFMPWorldContextRejectionReason.MissingSession,
                DFMPWorldContextProtocol.GetRejectionReason(null, new DFMPWorldContextReport()));

            GameObject go = new GameObject("DFMP_ContextValidationTest");
            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                var report = new DFMPWorldContextReport
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 207,
                    MapPixelY = 213
                };

                Assert.AreEqual(DFMPWorldContextRejectionReason.SpawnNotConfirmed, DFMPWorldContextProtocol.GetRejectionReason(session, report));
                session.ConfirmSpawn();

                report.MapPixelX = -1;
                Assert.AreEqual(DFMPWorldContextRejectionReason.InvalidMapPixel, DFMPWorldContextProtocol.GetRejectionReason(session, report));

                report.MapPixelX = 207;
                report.Kind = DFMPWorldContextKind.BuildingInterior;
                report.LocationId = "Daggerfall";
                report.BuildingKey = 0;
                Assert.AreEqual(DFMPWorldContextRejectionReason.MissingBuildingKey, DFMPWorldContextProtocol.GetRejectionReason(session, report));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ContextProtocol_CreatesCanonicalKeyFromReport()
        {
            var report = new DFMPWorldContextReport
            {
                Kind = DFMPWorldContextKind.BuildingInterior,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 41,
                LocationId = " Daggerfall ",
                BuildingKey = 12345,
                InstanceId = " shared "
            };

            DFMPWorldContextKey key;
            Assert.IsTrue(DFMPWorldContextProtocol.TryCreateKey(report, out key));
            Assert.AreEqual(DFMPWorldContextKind.BuildingInterior, key.Kind);
            Assert.AreEqual(207, key.MapPixelX);
            Assert.AreEqual(213, key.MapPixelY);
            Assert.AreEqual(3, key.RegionIndex);
            Assert.AreEqual(41, key.LocationIndex);
            Assert.AreEqual("Daggerfall", key.LocationId);
            Assert.AreEqual(12345, key.BuildingKey);
            Assert.AreEqual("shared", key.InstanceId);
        }

        [Test]
        public void DungeonGeometryService_UsesDungeonWideScopeAcrossBlocks()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryServiceTest");
            GameObject firstSessionGo = new GameObject("DFMP_DungeonGeometryFirstSession");
            GameObject secondSessionGo = new GameObject("DFMP_DungeonGeometrySecondSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var firstSession = firstSessionGo.AddComponent<DFMPPlayerSessionState>();
                var secondSession = secondSessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey firstBlock = CreateDungeonContext(7, "S0000161.RDB", "shared");
                DFMPWorldContextKey secondBlock = CreateDungeonContext(8, "M0000004.RDB", "shared");
                DFMPWorldContextKey exterior = firstBlock;
                exterior.Kind = DFMPWorldContextKind.Exterior;
                exterior.DungeonBlockIndex = 0;
                exterior.DungeonBlockName = string.Empty;

                DFMPDungeonGeometryScopeKey scope;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(firstBlock, out scope));
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(101, firstSession, firstBlock, "test"));
                Assert.AreEqual(1, service.HostedScopeCount);
                Assert.AreEqual(1, service.GetOccupantCount(scope));

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(102, secondSession, secondBlock, "test"));
                Assert.AreEqual(1, service.HostedScopeCount);
                Assert.AreEqual(2, service.GetOccupantCount(scope));

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(101, firstSession, secondBlock, "test"));
                Assert.AreEqual(1, service.HostedScopeCount);
                Assert.AreEqual(2, service.GetOccupantCount(scope));

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(101, firstSession, exterior, "test"));
                Assert.AreEqual(1, service.HostedScopeCount);
                Assert.AreEqual(1, service.GetOccupantCount(scope));

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(102, secondSession, exterior, "test"));
                Assert.AreEqual(0, service.HostedScopeCount);
                Assert.AreEqual(0, service.GetOccupantCount(scope));
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(firstSessionGo);
                Object.DestroyImmediate(secondSessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_SeparatesSharedDungeonInstances()
        {
            DFMPWorldContextKey shared = CreateDungeonContext(7, "S0000161.RDB", "shared");
            DFMPWorldContextKey other = CreateDungeonContext(7, "S0000161.RDB", "other");

            DFMPDungeonGeometryScopeKey sharedScope;
            DFMPDungeonGeometryScopeKey otherScope;
            Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(shared, out sharedScope));
            Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(other, out otherScope));

            Assert.AreNotEqual(sharedScope, otherScope);
        }

        [Test]
        public void DungeonGeometryService_LineOfSight_RequiresHostedSolidGeometry()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryLineOfSightTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryLineOfSightSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "shared");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(111, session, dungeon, "test"));

                bool hasLineOfSight;
                Assert.IsFalse(service.TryHasLineOfSight(dungeon, Vector3.zero, new Vector3(0f, 0f, 4f), out hasLineOfSight));
                Assert.IsFalse(hasLineOfSight);

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                BoxCollider floor = root.AddComponent<BoxCollider>();
                floor.center = new Vector3(0f, -2f, 0f);
                floor.size = new Vector3(8f, 0.25f, 8f);
                floor.isTrigger = true;
                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Assert.IsTrue(service.TryHasLineOfSight(dungeon, Vector3.zero, new Vector3(0f, 0f, 4f), out hasLineOfSight));
                Assert.IsTrue(hasLineOfSight);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_LineOfSight_BlocksOnHostedSolidCollider()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryBlockedLineOfSightTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryBlockedLineOfSightSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "shared");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(112, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = new Vector3(0f, 1f, 2f);
                wall.transform.localScale = new Vector3(3f, 3f, 0.25f);
                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                bool hasLineOfSight;
                Assert.IsTrue(service.TryHasLineOfSight(dungeon, Vector3.zero, new Vector3(0f, 0f, 4f), out hasLineOfSight));
                Assert.IsFalse(hasLineOfSight);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_AppliesHostedActionDoorByLoadId()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryActionDoorTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryActionDoorSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "action-door");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(118, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                GameObject doorGo = new GameObject("DFMP_HostedActionDoor");
                doorGo.transform.SetParent(root.transform, false);
                doorGo.transform.localPosition = new Vector3(0f, 1f, 2f);
                doorGo.AddComponent<AudioSource>();
                BoxCollider doorCollider = doorGo.AddComponent<BoxCollider>();
                doorCollider.size = new Vector3(1f, 2f, 0.25f);
                DaggerfallActionDoor door = doorGo.AddComponent<DaggerfallActionDoor>();
                door.LoadID = 29540811UL;
                door.PlaySounds = false;
                Physics.SyncTransforms();

                Assert.IsFalse(service.TryApplyActionDoor(dungeon, 1UL, true));
                Assert.IsTrue(service.TryApplyActionDoor(dungeon, 29540811UL, true));
                Assert.IsTrue(door.IsOpen);
                Assert.IsTrue(doorCollider.isTrigger);

                Assert.IsTrue(service.TryApplyActionDoor(dungeon, 29540811UL, false));
                Assert.IsTrue(door.IsClosed);
                Assert.IsFalse(doorCollider.isTrigger);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_Movement_ReachesDesiredPositionWhenClear()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryClearMovementTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryClearMovementSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "clear-movement");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(113, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 resolvedPosition;
                bool blocked;
                Assert.IsTrue(service.TryResolveMovement(dungeon, Vector3.zero, new Vector3(0f, 0f, 4f), 0.35f, 1.8f, out resolvedPosition, out blocked));
                Assert.AreEqual(new Vector3(0f, 0f, 4f), resolvedPosition);
                Assert.IsFalse(blocked);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_GroundedMovement_FollowsSlopeToDesiredPosition()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometrySlopeMovementTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometrySlopeMovementSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "slope-movement");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(116, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                GameObject slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slope.transform.SetParent(root.transform, false);
                slope.transform.localPosition = new Vector3(0f, 1f, 0f);
                slope.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
                slope.transform.localScale = new Vector3(4f, 0.2f, 8f);
                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 resolvedPosition;
                bool blocked;
                Assert.IsTrue(service.TryResolveGroundedMovement(
                    dungeon,
                    new Vector3(0f, 0f, -2f),
                    new Vector3(0f, 0f, 2f),
                    0.35f,
                    1.8f,
                    out resolvedPosition,
                    out blocked));
                Assert.IsFalse(blocked);
                Assert.AreEqual(2f, resolvedPosition.z, 0.01f);
                Assert.Greater(resolvedPosition.y, 0.5f);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_Movement_StopsBeforeHostedSolidCollider()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryBlockedMovementTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryBlockedMovementSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "blocked-movement");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(114, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = new Vector3(0f, 1f, 2f);
                wall.transform.localScale = new Vector3(3f, 3f, 0.25f);
                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 resolvedPosition;
                bool blocked;
                Assert.IsTrue(service.TryResolveMovement(dungeon, Vector3.zero, new Vector3(0f, 0f, 4f), 0.35f, 1.8f, out resolvedPosition, out blocked));
                Assert.IsTrue(blocked);
                Assert.Less(resolvedPosition.z, 2f);
                Assert.Greater(resolvedPosition.z, 0f);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_Movement_FailsClosedWhenUnavailable()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryUnavailableMovementTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryUnavailableMovementSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "unavailable-movement");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(115, session, dungeon, "test"));

                Vector3 resolvedPosition;
                bool blocked;
                Assert.IsFalse(service.TryResolveMovement(dungeon, Vector3.zero, new Vector3(0f, 0f, 4f), 0.35f, 1.8f, out resolvedPosition, out blocked));
                Assert.AreEqual(Vector3.zero, resolvedPosition);
                Assert.IsFalse(blocked);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_Grounding_FindsNearestFloorAndPreservesXz()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryGroundingTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryGroundingSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "grounding-floor");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(116, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.SetParent(root.transform, false);
                floor.transform.localPosition = new Vector3(0f, -1f, 0f);
                floor.transform.localScale = new Vector3(10f, 0.25f, 10f);
                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 grounded;
                bool foundGround;
                Vector3 aboveFloor = new Vector3(3f, 4f, 5f);
                Assert.IsTrue(service.TryResolveGroundedDungeonLocalPosition(dungeon, aboveFloor, out grounded, out foundGround));
                Assert.IsTrue(foundGround);
                Assert.AreEqual(3f, grounded.x);
                Assert.AreEqual(5f, grounded.z);
                Assert.AreEqual(-0.875f, grounded.y, 0.02f);

                Vector3 belowFloor = new Vector3(-2f, -6f, -3f);
                Assert.IsTrue(service.TryResolveGroundedDungeonLocalPosition(dungeon, belowFloor, out grounded, out foundGround));
                Assert.IsTrue(foundGround);
                Assert.AreEqual(-2f, grounded.x);
                Assert.AreEqual(-3f, grounded.z);
                Assert.AreEqual(-0.875f, grounded.y, 0.02f);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_Grounding_PrefersFloorNearRequestedHeight()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryMultiLevelGroundingTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryMultiLevelGroundingSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "multi-level-grounding");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(117, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                GameObject lowerFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lowerFloor.transform.SetParent(root.transform, false);
                lowerFloor.transform.localPosition = new Vector3(0f, -1f, 0f);
                lowerFloor.transform.localScale = new Vector3(10f, 0.25f, 10f);

                GameObject upperFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                upperFloor.transform.SetParent(root.transform, false);
                upperFloor.transform.localPosition = new Vector3(0f, 6f, 0f);
                upperFloor.transform.localScale = new Vector3(10f, 0.25f, 10f);

                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 grounded;
                bool foundGround;
                Assert.IsTrue(service.TryResolveGroundedDungeonLocalPosition(
                    dungeon,
                    new Vector3(2f, 0f, 3f),
                    out grounded,
                    out foundGround));
                Assert.IsTrue(foundGround);
                Assert.AreEqual(-0.875f, grounded.y, 0.02f);

                Vector3 resolvedPosition;
                bool blocked;
                Assert.IsTrue(service.TryResolveGroundedMovement(
                    dungeon,
                    new Vector3(2f, -0.875f, 3f),
                    new Vector3(3f, -0.875f, 3f),
                    0.35f,
                    1.8f,
                    out resolvedPosition,
                    out blocked));
                Assert.IsFalse(blocked);
                Assert.AreEqual(-0.875f, resolvedPosition.y, 0.02f);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_GroundedMovement_StopsAtLedgeInsteadOfDroppingToFloorBelow()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryLedgeDescentTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryLedgeDescentSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "ledge-descent");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(118, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                GameObject lowerFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lowerFloor.transform.SetParent(root.transform, false);
                lowerFloor.transform.localPosition = new Vector3(0f, -0.125f, 0f);
                lowerFloor.transform.localScale = new Vector3(20f, 0.25f, 20f);

                // Ledge top sits 3 units above the lower floor, a drop far steeper than a walkable ramp.
                GameObject ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ledge.transform.SetParent(root.transform, false);
                ledge.transform.localPosition = new Vector3(0f, 2.875f, -5.1f);
                ledge.transform.localScale = new Vector3(20f, 0.25f, 10f);

                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 currentPosition = new Vector3(0f, 3f, -1f);
                float lowestHeight = currentPosition.y;
                bool blockedAtLedge = false;
                for (int tick = 0; tick < 20; tick++)
                {
                    Vector3 resolvedPosition;
                    bool blocked;
                    Assert.IsTrue(service.TryResolveGroundedMovement(
                        dungeon,
                        currentPosition,
                        currentPosition + new Vector3(0f, 0f, 0.25f),
                        0.35f,
                        1.8f,
                        out resolvedPosition,
                        out blocked));
                    currentPosition = resolvedPosition;
                    lowestHeight = Mathf.Min(lowestHeight, currentPosition.y);
                    blockedAtLedge |= blocked;
                }

                Assert.IsTrue(blockedAtLedge, "Grounded movement never reported the ledge as blocking.");
                Assert.GreaterOrEqual(lowestHeight, 2.95f, "Grounded movement teleported off the ledge to the floor below.");
                Assert.AreEqual(3f, currentPosition.y, 0.05f, "Enemy should remain standing on the ledge.");
                Assert.LessOrEqual(currentPosition.z, 0f, "Enemy should stop at the ledge edge instead of walking past it.");
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void DungeonGeometryService_Grounding_FindsFloorUnderfootWhenOneColliderSpansLevels()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometrySharedColliderGroundingTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometrySharedColliderGroundingSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "shared-collider-grounding");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(119, session, dungeon, "test"));

                DFMPDungeonGeometryScopeKey scope;
                GameObject root;
                Assert.IsTrue(DFMPDungeonGeometryService.TryCreateScope(dungeon, out scope));
                Assert.IsTrue(service.TryGetRoot(scope, out root));

                // Daggerfall combines a whole block into one mesh collider, and RaycastAll reports only one hit per
                // collider, so both storeys must share a single collider for this to reproduce.
                var combined = new GameObject("CombinedModels");
                combined.transform.SetParent(root.transform, false);
                var mesh = new Mesh();
                CombineInstance[] storeys =
                {
                    BuildBoxInstance(new Vector3(0f, -0.125f, 0f), new Vector3(20f, 0.25f, 20f)),
                    BuildBoxInstance(new Vector3(0f, 6.4f, 0f), new Vector3(20f, 0.25f, 20f))
                };
                mesh.CombineMeshes(storeys, true, true);
                combined.AddComponent<MeshCollider>().sharedMesh = mesh;

                Physics.SyncTransforms();
                Assert.IsTrue(service.TryMarkGeometryAvailableForTesting(scope));

                Vector3 grounded;
                bool foundGround;
                Assert.IsTrue(service.TryResolveGroundedDungeonLocalPosition(
                    dungeon,
                    new Vector3(2f, 0f, 3f),
                    out grounded,
                    out foundGround));
                Assert.IsTrue(foundGround);
                Assert.AreEqual(0f, grounded.y, 0.02f, "Grounding selected the storey above instead of the floor underfoot.");
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        static CombineInstance BuildBoxInstance(Vector3 center, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                return new CombineInstance
                {
                    mesh = box.GetComponent<MeshFilter>().sharedMesh,
                    transform = Matrix4x4.TRS(center, Quaternion.identity, size)
                };
            }
            finally
            {
                Object.DestroyImmediate(box);
            }
        }

        [Test]
        public void DungeonGeometryService_Grounding_FailsClosedWhenGeometryUnavailable()
        {
            GameObject serviceGo = new GameObject("DFMP_DungeonGeometryGroundingUnavailableTest");
            GameObject sessionGo = new GameObject("DFMP_DungeonGeometryGroundingUnavailableSession");
            try
            {
                var service = serviceGo.AddComponent<DFMPDungeonGeometryService>();
                service.Initialize();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                DFMPWorldContextKey dungeon = CreateDungeonContext(7, "S0000161.RDB", "grounding-unavailable");
                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(117, session, dungeon, "test"));

                Vector3 grounded;
                bool foundGround;
                Vector3 original = new Vector3(2f, 9f, 4f);
                Assert.IsFalse(service.TryResolveGroundedDungeonLocalPosition(dungeon, original, out grounded, out foundGround));
                Assert.IsFalse(foundGround);
                Assert.AreEqual(original, grounded);
            }
            finally
            {
                Object.DestroyImmediate(serviceGo);
                Object.DestroyImmediate(sessionGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void NetworkServer_TryApplyWorldContextReport_UpdatesOccupancy()
        {
            GameObject go = new GameObject("DFMP_ContextApplyTest");
            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(70, 6799360, 0f, 9388032);
                session.ConfirmSpawn();
                var report = new DFMPWorldContextReport
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

                DFMPWorldContextRejectionReason reason;
                Assert.IsTrue(DFMPNetworkServer.TryApplyWorldContextReport(70, session, report, out reason));
                Assert.AreEqual(DFMPWorldContextRejectionReason.None, reason);

                DFMPWorldContextKey context;
                Assert.IsTrue(DFMPNetworkServer.TryGetSessionWorldContext(70, out context));
                Assert.AreEqual(DFMPWorldContextKind.Dungeon, context.Kind);
                Assert.AreEqual("Daggerfall Dungeon", context.LocationId);
                Assert.AreEqual(7, context.DungeonBlockIndex);
                Assert.AreEqual("S0000161.RDB", context.DungeonBlockName);
            }
            finally
            {
                Object.DestroyImmediate(go);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void NetworkServer_SetSessionWorldContext_PublishesChangeAndLocationEventsOnce()
        {
            int contextChangedCount = 0;
            int locationEnteredCount = 0;
            DFMPPlayerWorldContextChangedEvent changedEvent = null;
            DFMPLocationEnteredEvent locationEvent = null;
            System.Action<DFMPPlayerWorldContextChangedEvent> changedHandler = e =>
            {
                contextChangedCount++;
                changedEvent = e;
            };
            System.Action<DFMPLocationEnteredEvent> locationHandler = e =>
            {
                locationEnteredCount++;
                locationEvent = e;
            };

            DFMPEventBus.Instance.PlayerWorldContextChanged += changedHandler;
            DFMPEventBus.Instance.LocationEntered += locationHandler;
            GameObject go = new GameObject("DFMP_ContextEventTest");
            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(71, 6799360, 0f, 9388032);
                var context = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall"
                };

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(71, session, context, "test"));
                Assert.IsFalse(DFMPNetworkServer.SetSessionWorldContext(71, session, context, "test"));

                Assert.AreEqual(1, contextChangedCount);
                Assert.AreEqual(1, locationEnteredCount);
                Assert.NotNull(changedEvent);
                Assert.IsFalse(changedEvent.HadPreviousContext);
                Assert.AreEqual(71, changedEvent.ConnectionId);
                Assert.AreEqual("test", changedEvent.Reason);
                Assert.AreEqual(context, changedEvent.CurrentContext);
                Assert.NotNull(locationEvent);
                Assert.AreEqual(context, locationEvent.Context);
            }
            finally
            {
                DFMPEventBus.Instance.PlayerWorldContextChanged -= changedHandler;
                DFMPEventBus.Instance.LocationEntered -= locationHandler;
                Object.DestroyImmediate(go);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void NetworkServer_SetSessionWorldContext_PublishesDungeonBlockEvent()
        {
            int dungeonBlockEnteredCount = 0;
            DFMPDungeonBlockEnteredEvent dungeonEvent = null;
            System.Action<DFMPDungeonBlockEnteredEvent> dungeonHandler = e =>
            {
                dungeonBlockEnteredCount++;
                dungeonEvent = e;
            };

            DFMPEventBus.Instance.DungeonBlockEntered += dungeonHandler;
            GameObject go = new GameObject("DFMP_DungeonBlockEventTest");
            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(72, 6799360, 0f, 9388032);
                var context = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Dungeon,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall Dungeon",
                    DungeonBlockIndex = 7,
                    DungeonBlockName = "S0000161.RDB"
                };

                Assert.IsTrue(DFMPNetworkServer.SetSessionWorldContext(72, session, context, "dungeon-report"));

                Assert.AreEqual(1, dungeonBlockEnteredCount);
                Assert.NotNull(dungeonEvent);
                Assert.AreEqual(72, dungeonEvent.ConnectionId);
                Assert.AreEqual("dungeon-report", dungeonEvent.Reason);
                Assert.AreEqual(context, dungeonEvent.Context);
            }
            finally
            {
                DFMPEventBus.Instance.DungeonBlockEntered -= dungeonHandler;
                Object.DestroyImmediate(go);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void WorldInterestManagement_AllowsGlobalTimeStateForReadyObservers()
        {
            GameObject interestGo = new GameObject("DFMP_InterestManagementTest");
            GameObject timeGo = new GameObject("DFMP_TimeIdentityTest");
            try
            {
                var interest = interestGo.AddComponent<DFMPWorldInterestManagement>();
                var timeIdentity = timeGo.AddComponent<NetworkIdentity>();
                timeGo.AddComponent<DFMPTimeState>();
                var readyConnection = new NetworkConnectionToClient(80);
                readyConnection.isReady = true;
                var notReadyConnection = new NetworkConnectionToClient(81);

                Assert.IsTrue(interest.ShouldObserve(timeIdentity, readyConnection));
                Assert.IsFalse(interest.ShouldObserve(timeIdentity, notReadyConnection));
            }
            finally
            {
                Object.DestroyImmediate(timeGo);
                Object.DestroyImmediate(interestGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void WorldInterestManagement_AllowsGlobalWorldSettingsForReadyObservers()
        {
            GameObject interestGo = new GameObject("DFMP_InterestManagementTest");
            GameObject settingsGo = new GameObject("DFMP_WorldSettingsIdentityTest");
            try
            {
                var interest = interestGo.AddComponent<DFMPWorldInterestManagement>();
                var settingsIdentity = settingsGo.AddComponent<NetworkIdentity>();
                settingsGo.AddComponent<DFMPWorldSettings>();
                var readyConnection = new NetworkConnectionToClient(82);
                readyConnection.isReady = true;
                var notReadyConnection = new NetworkConnectionToClient(83);

                Assert.IsTrue(interest.ShouldObserve(settingsIdentity, readyConnection));
                Assert.IsFalse(interest.ShouldObserve(settingsIdentity, notReadyConnection));
            }
            finally
            {
                Object.DestroyImmediate(settingsGo);
                Object.DestroyImmediate(interestGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void WorldInterestManagement_AllowsOnlyCoLocatedSessionObservers()
        {
            GameObject interestGo = new GameObject("DFMP_InterestManagementTest");
            GameObject sessionGo = new GameObject("DFMP_SessionIdentityTest");
            GameObject ownerGo = new GameObject("DFMP_OwnerSessionTest");
            GameObject observerGo = new GameObject("DFMP_ObserverSessionTest");
            try
            {
                var interest = interestGo.AddComponent<DFMPWorldInterestManagement>();
                var sessionIdentity = sessionGo.AddComponent<NetworkIdentity>();
                var session = sessionGo.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(82, 6799360, 0f, 9388032);
                var ownerSession = ownerGo.AddComponent<DFMPPlayerSessionState>();
                ownerSession.Initialize(82, 6799360, 0f, 9388032);
                var observerSession = observerGo.AddComponent<DFMPPlayerSessionState>();
                observerSession.Initialize(83, 6799360, 0f, 9388032);
                var ownerConnection = new NetworkConnectionToClient(82);
                ownerConnection.isReady = true;
                var observerConnection = new NetworkConnectionToClient(83);
                observerConnection.isReady = true;
                var distantConnection = new NetworkConnectionToClient(84);
                distantConnection.isReady = true;
                var city = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall"
                };
                var dungeon = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Dungeon,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    LocationId = "Daggerfall Dungeon",
                    DungeonBlockIndex = 7,
                    DungeonBlockName = "S0000161.RDB"
                };

                DFMPNetworkServer.SetSessionWorldContext(82, ownerSession, city, "test");
                DFMPNetworkServer.SetSessionWorldContext(83, observerSession, city, "test");
                DFMPNetworkServer.SetSessionWorldContext(84, null, dungeon, "test");

                Assert.IsTrue(interest.ShouldObserve(sessionIdentity, ownerConnection));
                Assert.IsTrue(interest.ShouldObserve(sessionIdentity, observerConnection));
                Assert.IsFalse(interest.ShouldObserve(sessionIdentity, distantConnection));
            }
            finally
            {
                Object.DestroyImmediate(observerGo);
                Object.DestroyImmediate(ownerGo);
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(interestGo);
                DFMPNetworkServer.Stop();
            }
        }

        [Test]
        public void WorldInterestManagement_AllowsOnlyMatchingContextCarrierObservers()
        {
            GameObject interestGo = new GameObject("DFMP_CarrierInterestManagementTest");
            GameObject identityGo = new GameObject("DFMP_ContextCarrierIdentityTest");
            try
            {
                var interest = interestGo.AddComponent<DFMPWorldInterestManagement>();
                var identity = identityGo.AddComponent<NetworkIdentity>();
                var carrier = identityGo.AddComponent<DFMPWorldContextCarrier>();
                var context = new DFMPWorldContextKey
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
                carrier.Initialize(context);
                var matchingConnection = new NetworkConnectionToClient(91);
                matchingConnection.isReady = true;
                var differentBlockConnection = new NetworkConnectionToClient(92);
                differentBlockConnection.isReady = true;
                var differentInstanceConnection = new NetworkConnectionToClient(93);
                differentInstanceConnection.isReady = true;
                var differentBlock = context;
                differentBlock.DungeonBlockIndex = 8;
                var differentInstance = context;
                differentInstance.InstanceId = "other";

                DFMPNetworkServer.SetSessionWorldContext(91, null, context, "test");
                DFMPNetworkServer.SetSessionWorldContext(92, null, differentBlock, "test");
                DFMPNetworkServer.SetSessionWorldContext(93, null, differentInstance, "test");

                Assert.AreEqual(context, carrier.Context);
                Assert.IsTrue(interest.ShouldObserve(identity, matchingConnection));
                Assert.IsFalse(interest.ShouldObserve(identity, differentBlockConnection));
                Assert.IsFalse(interest.ShouldObserve(identity, differentInstanceConnection));
            }
            finally
            {
                Object.DestroyImmediate(identityGo);
                Object.DestroyImmediate(interestGo);
                DFMPNetworkServer.Stop();
            }
        }

        static DFMPWorldContextKey CreateDungeonContext(int blockIndex, string blockName, string instanceId)
        {
            return new DFMPWorldContextKey
            {
                Kind = DFMPWorldContextKind.Dungeon,
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 42,
                LocationId = "Daggerfall Dungeon",
                DungeonBlockIndex = blockIndex,
                DungeonBlockName = blockName,
                InstanceId = instanceId
            };
        }
    }
}

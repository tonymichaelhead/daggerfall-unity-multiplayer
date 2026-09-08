using NUnit.Framework;
using DFMP.Runtime;
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
    }
}

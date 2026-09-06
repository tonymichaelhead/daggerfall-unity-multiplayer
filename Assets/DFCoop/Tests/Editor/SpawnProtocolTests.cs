using DFCoop.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFCoop.Tests
{
    [TestFixture]
    public class SpawnProtocolTests
    {
        [Test]
        public void AssignmentState_ReceiveRequeueAndReset_TracksLifecycle()
        {
            var state = new DFCoopSpawnAssignmentState();
            var assignment = new DFCoopSpawnAssignment { MapPixelX = 207, MapPixelY = 213 };

            state.Receive(assignment);

            Assert.IsTrue(state.HasAssignment);
            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
            Assert.AreEqual(207, state.Assignment.MapPixelX);
            Assert.AreEqual(213, state.Assignment.MapPixelY);

            Assert.IsTrue(state.TryRequestTeleport());
            state.Requeue();

            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);

            state.Reset();

            Assert.IsFalse(state.HasAssignment);
            Assert.IsFalse(state.HasPendingAssignment);
            Assert.IsFalse(state.TeleportRequested);
        }

        [Test]
        public void AssignmentState_RequestsTeleportOnlyOnceUntilRequeued()
        {
            var state = new DFCoopSpawnAssignmentState();
            state.Receive(new DFCoopSpawnAssignment { MapPixelX = 207, MapPixelY = 213 });

            Assert.IsTrue(state.TryRequestTeleport());
            Assert.IsFalse(state.TryRequestTeleport());
            Assert.IsFalse(state.TryAcknowledge(true, 207, 213));
            Assert.IsTrue(state.TryAcknowledge(false, 207, 213));
            Assert.IsFalse(state.TryRequestTeleport());
        }

        [Test]
        public void AssignmentState_DoesNotAcknowledgeWrongMapPixel()
        {
            var state = new DFCoopSpawnAssignmentState();
            state.Receive(new DFCoopSpawnAssignment { MapPixelX = 207, MapPixelY = 213 });
            state.TryRequestTeleport();

            Assert.IsFalse(state.TryAcknowledge(false, 208, 213));
            Assert.IsTrue(state.HasPendingAssignment);
            Assert.IsTrue(state.TeleportRequested);
        }

        [Test]
        public void SpawnProtocol_ValidatesAcknowledgementWithinAssignedMapPixel()
        {
            Assert.IsTrue(DFCoopSpawnProtocol.IsWithinMapPixel(6792821, 9374554, 207, 213));
            Assert.IsFalse(DFCoopSpawnProtocol.IsWithinMapPixel(6782975, 9374554, 207, 213));
            Assert.IsFalse(DFCoopSpawnProtocol.IsWithinMapPixel(6792821, 9338879, 207, 213));
        }

        [Test]
        public void SpawnProtocol_RejectsAcknowledgementWithoutSession()
        {
            var acknowledgement = new DFCoopSpawnAcknowledgement { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554 };

            Assert.IsFalse(DFCoopSpawnProtocol.TryConfirmSpawn(null, acknowledgement));
        }

        [Test]
        public void SpawnProtocol_RejectsAcknowledgementOutsideAssignedMapPixel()
        {
            GameObject go = new GameObject("DFCoop_SpawnAcknowledgementMismatchTest");

            try
            {
                var session = go.AddComponent<DFCoopPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                var acknowledgement = new DFCoopSpawnAcknowledgement { WorldX = 6782975, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFCoopSpawnProtocol.TryConfirmSpawn(session, acknowledgement));
                Assert.IsFalse(session.SpawnConfirmed);
                Assert.AreEqual(6799360, session.WorldX);
                Assert.AreEqual(9388032, session.WorldZ);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SpawnProtocol_ConfirmsMatchingAcknowledgementAndPersistsPosition()
        {
            GameObject go = new GameObject("DFCoop_SpawnAcknowledgementTest");

            try
            {
                var session = go.AddComponent<DFCoopPlayerSessionState>();
                session.Initialize(7, 6799360, 0f, 9388032);
                var acknowledgement = new DFCoopSpawnAcknowledgement { WorldX = 6792821, WorldY = 12.5f, WorldZ = 9374554 };

                Assert.IsTrue(DFCoopSpawnProtocol.TryConfirmSpawn(session, acknowledgement));
                Assert.IsTrue(session.SpawnConfirmed);
                Assert.AreEqual(6792821, session.WorldX);
                Assert.AreEqual(12.5f, session.WorldY);
                Assert.AreEqual(9374554, session.WorldZ);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SpawnProtocol_DaggerfallCityLocationCenter_IsInsideExpectedMapPixel()
        {
            var daggerfallCityRect = new Rect(6782976, 9371648, 32768, 32768);

            DFCoopWorldPosition spawn = DFCoopSpawnProtocol.GetLocationCenter(daggerfallCityRect);

            Assert.AreEqual(6799360, spawn.WorldX);
            Assert.AreEqual(9388032, spawn.WorldZ);
            Assert.AreEqual(0f, spawn.WorldY);
            Assert.IsTrue(DFCoopSpawnProtocol.IsWithinMapPixel(spawn.WorldX, spawn.WorldZ, 207, 213));
        }
    }
}
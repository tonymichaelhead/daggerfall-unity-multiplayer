using DFCoop.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFCoop.Tests
{
    [TestFixture]
    public class PositionProtocolTests
    {
        [Test]
        public void IsValidWorldPosition_AcceptsDaggerfallCityCoordinate()
        {
            var report = new DFCoopPlayerPositionReport { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554 };

            Assert.IsTrue(DFCoopPositionProtocol.IsValidWorldPosition(report));
        }

        [Test]
        public void IsValidWorldPosition_RejectsOutOfBoundsAndInvalidHeight()
        {
            Assert.IsFalse(DFCoopPositionProtocol.IsValidWorldPosition(new DFCoopPlayerPositionReport { WorldX = 0, WorldY = 0f, WorldZ = 0 }));
            Assert.IsFalse(DFCoopPositionProtocol.IsValidWorldPosition(new DFCoopPlayerPositionReport { WorldX = 6792821, WorldY = float.NaN, WorldZ = 9374554 }));
            Assert.IsFalse(DFCoopPositionProtocol.IsValidWorldPosition(new DFCoopPlayerPositionReport { WorldX = 6792821, WorldY = float.PositiveInfinity, WorldZ = 9374554 }));
        }

        [Test]
        public void IsAccepted_RejectsUnconfirmedSessionAndReportsTooSoon()
        {
            GameObject go = new GameObject("DFCoop_PositionValidationTest");

            try
            {
                var session = go.AddComponent<DFCoopPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                var report = new DFCoopPlayerPositionReport { WorldX = 6792921, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFCoopPositionProtocol.IsAccepted(session, report, 1f));

                session.ConfirmSpawn();
                Assert.IsFalse(DFCoopPositionProtocol.IsAccepted(session, report, 0.09f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsAccepted_AcceptsPlausibleConfirmedMovement()
        {
            GameObject go = new GameObject("DFCoop_PositionMovementTest");

            try
            {
                var session = go.AddComponent<DFCoopPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                session.ConfirmSpawn();
                var report = new DFCoopPlayerPositionReport { WorldX = 6793221, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsTrue(DFCoopPositionProtocol.IsAccepted(session, report, 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsAccepted_RejectsExcessiveMovement()
        {
            GameObject go = new GameObject("DFCoop_PositionSpeedTest");

            try
            {
                var session = go.AddComponent<DFCoopPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                session.ConfirmSpawn();
                var report = new DFCoopPlayerPositionReport { WorldX = 6800000, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFCoopPositionProtocol.IsAccepted(session, report, 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
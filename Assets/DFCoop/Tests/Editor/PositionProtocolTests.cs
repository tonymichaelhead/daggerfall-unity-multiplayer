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
            Assert.IsFalse(DFCoopPositionProtocol.IsValidWorldPosition(new DFCoopPlayerPositionReport { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554, FacingYaw = float.NaN }));
        }

        [Test]
        public void DisplayNameSanitization_ProducesBoundedReadableNames()
        {
            Assert.AreEqual("Alyx-42", DFCoopPositionProtocol.SanitizeDisplayName("  Alyx-42  "));
            Assert.AreEqual("Player", DFCoopPositionProtocol.SanitizeDisplayName("<>"));
            Assert.AreEqual("abcdefghijklmnopqrstuvwx", DFCoopPositionProtocol.SanitizeDisplayName("abcdefghijklmnopqrstuvwxyz"));
        }

        [Test]
        public void FacingYaw_NormalizesFiniteAnglesAndRejectsInvalidValues()
        {
            Assert.AreEqual(270f, DFCoopPositionProtocol.NormalizeFacingYaw(-90f));
            Assert.AreEqual(45f, DFCoopPositionProtocol.NormalizeFacingYaw(405f));
            Assert.IsFalse(DFCoopPositionProtocol.IsValidFacingYaw(float.PositiveInfinity));
        }

        [Test]
        public void AppearanceSelection_MapsAndClampsForNativeBillboards()
        {
            Assert.AreEqual(1, DFCoopPositionProtocol.GetDisplayRace(4));
            Assert.AreEqual(2, DFCoopPositionProtocol.GetDisplayRace(2));
            Assert.AreEqual(0, DFCoopPositionProtocol.GetDisplayGender(-1));
            Assert.AreEqual(1, DFCoopPositionProtocol.GetDisplayGender(1));
            Assert.AreEqual(0, DFCoopPositionProtocol.GetOutfitVariant(-1));
            Assert.AreEqual(3, DFCoopPositionProtocol.GetOutfitVariant(4));
            Assert.AreEqual(23, DFCoopPositionProtocol.GetFaceVariant(99));
            Assert.AreEqual(2, DFCoopPositionProtocol.GetInitialOutfitVariant(6));
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
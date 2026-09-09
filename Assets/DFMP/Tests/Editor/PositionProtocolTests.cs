using DFMP.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class PositionProtocolTests
    {
        [Test]
        public void IsValidWorldPosition_AcceptsDaggerfallCityCoordinate()
        {
            var report = new DFMPPlayerPositionReport { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554 };

            Assert.IsTrue(DFMPPositionProtocol.IsValidWorldPosition(report));
        }

        [Test]
        public void IsValidWorldPosition_RejectsOutOfBoundsAndInvalidHeight()
        {
            Assert.IsFalse(DFMPPositionProtocol.IsValidWorldPosition(new DFMPPlayerPositionReport { WorldX = 0, WorldY = 0f, WorldZ = 0 }));
            Assert.IsFalse(DFMPPositionProtocol.IsValidWorldPosition(new DFMPPlayerPositionReport { WorldX = 6792821, WorldY = float.NaN, WorldZ = 9374554 }));
            Assert.IsFalse(DFMPPositionProtocol.IsValidWorldPosition(new DFMPPlayerPositionReport { WorldX = 6792821, WorldY = float.PositiveInfinity, WorldZ = 9374554 }));
            Assert.IsFalse(DFMPPositionProtocol.IsValidWorldPosition(new DFMPPlayerPositionReport { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554, FacingYaw = float.NaN }));
        }

        [Test]
        public void DisplayNameSanitization_ProducesBoundedReadableNames()
        {
            Assert.AreEqual("Alyx-42", DFMPPositionProtocol.SanitizeDisplayName("  Alyx-42  "));
            Assert.AreEqual("Player", DFMPPositionProtocol.SanitizeDisplayName("<>"));
            Assert.AreEqual("abcdefghijklmnopqrstuvwx", DFMPPositionProtocol.SanitizeDisplayName("abcdefghijklmnopqrstuvwxyz"));
        }

        [Test]
        public void FacingYaw_NormalizesFiniteAnglesAndRejectsInvalidValues()
        {
            Assert.AreEqual(270f, DFMPPositionProtocol.NormalizeFacingYaw(-90f));
            Assert.AreEqual(45f, DFMPPositionProtocol.NormalizeFacingYaw(405f));
            Assert.IsFalse(DFMPPositionProtocol.IsValidFacingYaw(float.PositiveInfinity));
        }

        [Test]
        public void SceneHeightOffset_IsRelativeAndBounded()
        {
            Assert.AreEqual(0f, DFMPPositionProtocol.GetSceneHeightOffset(11.08f, 10f, 0f, 2f, 0.08f), 0.0001f);
            Assert.AreEqual(1.25f, DFMPPositionProtocol.GetSceneHeightOffset(12.33f, 10f, 0f, 2f, 0.08f), 0.0001f);
            Assert.AreEqual(DFMPPositionProtocol.MaximumSceneHeightOffset, DFMPPositionProtocol.GetSceneHeightOffset(100f, 10f, 0f, 2f, 0.08f));
            Assert.AreEqual(-DFMPPositionProtocol.MaximumSceneHeightOffset, DFMPPositionProtocol.GetSceneHeightOffset(-100f, 10f, 0f, 2f, 0.08f));

            Assert.IsFalse(DFMPPositionProtocol.IsValidWorldPosition(new DFMPPlayerPositionReport
            {
                WorldX = 6792821,
                WorldY = 0f,
                WorldZ = 9374554,
                SceneHeightOffset = DFMPPositionProtocol.MaximumSceneHeightOffset + 1f
            }));
        }

        [Test]
        public void AppearanceSelection_MapsAndClampsForNativeBillboards()
        {
            Assert.AreEqual(1, DFMPPositionProtocol.GetDisplayRace(4));
            Assert.AreEqual(2, DFMPPositionProtocol.GetDisplayRace(2));
            Assert.AreEqual(4, DFMPPositionProtocol.GetPlayerRace(4));
            Assert.AreEqual(8, DFMPPositionProtocol.GetPlayerRace(99));
            Assert.AreEqual(0, DFMPPositionProtocol.GetDisplayGender(-1));
            Assert.AreEqual(1, DFMPPositionProtocol.GetDisplayGender(1));
            Assert.AreEqual(0, DFMPPositionProtocol.GetOutfitVariant(-1));
            Assert.AreEqual(3, DFMPPositionProtocol.GetOutfitVariant(4));
            Assert.AreEqual(23, DFMPPositionProtocol.GetFaceVariant(99));
            Assert.AreEqual(2, DFMPPositionProtocol.GetInitialOutfitVariant(6));
        }

        [Test]
        public void IsAccepted_RejectsUnconfirmedSessionAndReportsTooSoon()
        {
            GameObject go = new GameObject("DFMP_PositionValidationTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                var report = new DFMPPlayerPositionReport { WorldX = 6792921, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFMPPositionProtocol.IsAccepted(session, report, 1f));

                session.ConfirmSpawn();
                Assert.IsFalse(DFMPPositionProtocol.IsAccepted(session, report, 0.04f));
                Assert.AreEqual(DFMPPositionRejectionReason.TooSoon, DFMPPositionProtocol.GetRejectionReason(session, report, 0.04f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsAccepted_AcceptsPlausibleConfirmedMovement()
        {
            GameObject go = new GameObject("DFMP_PositionMovementTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                session.ConfirmSpawn();
                var report = new DFMPPlayerPositionReport { WorldX = 6793221, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsTrue(DFMPPositionProtocol.IsAccepted(session, report, 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PositionRejectionReason_IdentifiesInvalidReport()
        {
            GameObject go = new GameObject("DFMP_PositionRejectionReasonTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                session.ConfirmSpawn();
                var report = new DFMPPlayerPositionReport { WorldX = 0, WorldY = 0f, WorldZ = 0 };

                Assert.AreEqual(DFMPPositionRejectionReason.InvalidWorldPosition, DFMPPositionProtocol.GetRejectionReason(session, report, 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MovementProtocol_StationaryReportRemainsIdle()
        {
            Assert.IsFalse(DFMPMovementProtocol.IsMoving(100, 200, 100, 200));
            Assert.IsFalse(DFMPMovementProtocol.IsMoving(100, 200, 100 + DFMPMovementProtocol.MovementThreshold, 200));
        }

        [Test]
        public void MovementProtocol_MovementAboveThresholdIsMoving()
        {
            Assert.IsTrue(DFMPMovementProtocol.IsMoving(100, 200, 100 + DFMPMovementProtocol.MovementThreshold + 1, 200));
        }

        [Test]
        public void MovementProtocol_ReturnsToIdleWhenAcceptedReportStops()
        {
            Assert.IsTrue(DFMPMovementProtocol.IsMoving(100, 200, 200, 200));
            Assert.IsFalse(DFMPMovementProtocol.IsMoving(200, 200, 200, 200));
        }

        [Test]
        public void SessionMovementState_StartsIdle()
        {
            GameObject go = new GameObject("DFMP_MovementStateTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);

                Assert.IsFalse(session.IsMoving);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SessionActionSequence_ChangesForEachAcceptedPresentationAction()
        {
            GameObject go = new GameObject("DFMP_ActionStateTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();

                session.PlayAction(DFMPPlayerActionKind.PrimaryAttack);
                Assert.AreEqual(1u, session.ActionSequence);
                Assert.AreEqual(DFMPPlayerActionKind.PrimaryAttack, session.ActionKind);

                session.PlayAction(DFMPPlayerActionKind.Spell);
                Assert.AreEqual(2u, session.ActionSequence);
                Assert.AreEqual(DFMPPlayerActionKind.Spell, session.ActionKind);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AvatarProtocol_RejectsUnknownPresentationActions()
        {
            Assert.IsTrue(DFMPAvatarProtocol.IsValidAction(DFMPPlayerActionKind.PrimaryAttack));
            Assert.IsTrue(DFMPAvatarProtocol.IsValidAction(DFMPPlayerActionKind.RangedAttack));
            Assert.IsTrue(DFMPAvatarProtocol.IsValidAction(DFMPPlayerActionKind.Spell));
            Assert.IsFalse(DFMPAvatarProtocol.IsValidAction(DFMPPlayerActionKind.None));
            Assert.IsFalse(DFMPAvatarProtocol.IsValidAction((DFMPPlayerActionKind)99));
        }

        [Test]
        public void AvatarProtocol_AcceptsActionsOnlyForConfirmedRateLimitedSessions()
        {
            GameObject go = new GameObject("DFMP_ActionValidationTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                Assert.IsFalse(DFMPAvatarProtocol.IsActionAccepted(session, DFMPPlayerActionKind.PrimaryAttack, 1f));

                session.ConfirmSpawn();
                Assert.IsFalse(DFMPAvatarProtocol.IsActionAccepted(session, DFMPPlayerActionKind.PrimaryAttack, 0.09f));
                Assert.IsFalse(DFMPAvatarProtocol.IsActionAccepted(session, DFMPPlayerActionKind.None, 1f));
                Assert.IsTrue(DFMPAvatarProtocol.IsActionAccepted(session, DFMPPlayerActionKind.PrimaryAttack, 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RejectedPositionReport_DoesNotChangeMovementState()
        {
            GameObject go = new GameObject("DFMP_RejectedMovementStateTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                session.ConfirmSpawn();
                session.SetMovement(true);
                var rejectedReport = new DFMPPlayerPositionReport { WorldX = 6800000, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFMPPositionProtocol.IsAccepted(session, rejectedReport, 0.1f));
                Assert.IsTrue(session.IsMoving);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsAccepted_RejectsExcessiveMovement()
        {
            GameObject go = new GameObject("DFMP_PositionSpeedTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);
                session.ConfirmSpawn();
                var report = new DFMPPlayerPositionReport { WorldX = 6800000, WorldY = 0f, WorldZ = 9374554 };

                Assert.IsFalse(DFMPPositionProtocol.IsAccepted(session, report, 0.1f));
                Assert.AreEqual(DFMPPositionRejectionReason.ExcessiveDisplacement, DFMPPositionProtocol.GetRejectionReason(session, report, 0.1f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
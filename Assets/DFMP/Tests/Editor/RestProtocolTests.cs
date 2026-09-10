using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class RestProtocolTests
    {
        [Test]
        public void SpawnedStationarySession_AcceptsRestRequest()
        {
            DFMPRestRequestContext context = CreateReadyContext();

            DFMPRestRequestRejectionReason reason = DFMPRestProtocol.GetRejectionReason(context, "FullRest");

            Assert.AreEqual(DFMPRestRequestRejectionReason.None, reason);
            Assert.IsTrue(DFMPRestProtocol.IsAccepted(reason));
        }

        [TestCase(DFMPRestRequestRejectionReason.MissingSession, false, true, false, false, false)]
        [TestCase(DFMPRestRequestRejectionReason.SpawnNotConfirmed, true, false, false, false, false)]
        [TestCase(DFMPRestRequestRejectionReason.Moving, true, true, true, false, false)]
        [TestCase(DFMPRestRequestRejectionReason.AlreadyResting, true, true, false, true, false)]
        [TestCase(DFMPRestRequestRejectionReason.Interrupted, true, true, false, false, true)]
        public void InvalidContext_RejectsRestRequest(
            DFMPRestRequestRejectionReason expectedReason,
            bool hasSession,
            bool spawnConfirmed,
            bool isMoving,
            bool isResting,
            bool hasInterruption)
        {
            DFMPRestRequestContext context = new DFMPRestRequestContext
            {
                HasSession = hasSession,
                SpawnConfirmed = spawnConfirmed,
                IsMoving = isMoving,
                IsResting = isResting,
                HasInterruption = hasInterruption
            };

            DFMPRestRequestRejectionReason reason = DFMPRestProtocol.GetRejectionReason(context, "TimedRest");

            Assert.AreEqual(expectedReason, reason);
            Assert.IsFalse(DFMPRestProtocol.IsAccepted(reason));
        }

        [Test]
        public void UnknownRestMode_RejectsBeforeContextValidation()
        {
            DFMPRestRequestRejectionReason reason = DFMPRestProtocol.GetRejectionReason(
                CreateReadyContext(),
                "WaitUntilDawn");

            Assert.AreEqual(DFMPRestRequestRejectionReason.UnsupportedMode, reason);
        }

        [Test]
        public void DisabledServerPolicy_RejectsRestRequest()
        {
            DFMPRestRequestRejectionReason reason = DFMPRestProtocol.GetRejectionReason(
                CreateReadyContext(),
                "TimedRest",
                DFMPRestPolicies.Disabled);

            Assert.AreEqual(DFMPRestRequestRejectionReason.DisabledByServer, reason);
            Assert.IsFalse(DFMPRestProtocol.IsAccepted(reason));
        }

        [Test]
        public void ServerManagedPolicy_IsReservedUntilRecoveryIsImplemented()
        {
            DFMPRestRequestRejectionReason reason = DFMPRestProtocol.GetRejectionReason(
                CreateReadyContext(),
                "TimedRest",
                DFMPRestPolicies.ServerManaged);

            Assert.AreEqual(DFMPRestRequestRejectionReason.ServerManagedUnavailable, reason);
        }

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        public void MovementInterruptsOnlyAnActiveRest(bool isResting, bool isMoving, bool expectedInterrupted)
        {
            Assert.AreEqual(
                expectedInterrupted,
                DFMPRestProtocol.ShouldInterruptOnMovement(isResting, isMoving));
        }

        [Test]
        public void RecoveryUsesElapsedRealTimeAndCapsAtMaximums()
        {
            DFMPRestVitals vitals = new DFMPRestVitals
            {
                Health = 8,
                MaxHealth = 10,
                Fatigue = 4,
                MaxFatigue = 10,
                SpellPoints = 2,
                MaxSpellPoints = 10,
                NoSpellPointRegeneration = false
            };

            DFMPRestRecoveryResult result = DFMPRestRecoveryPolicy.Calculate(
                vitals,
                3,
                6,
                12,
                DFMPRestRecoveryPolicy.SecondsPerRestHour,
                1f);

            Assert.AreEqual(2, result.HealthRecovered);
            Assert.AreEqual(6, result.FatigueRecovered);
            Assert.AreEqual(8, result.SpellPointsRecovered);
            Assert.IsTrue(result.IsComplete);
        }

        [Test]
        public void RecoveryMultiplierScalesRatesWithoutAdvancingWorldTime()
        {
            DFMPRestVitals vitals = new DFMPRestVitals
            {
                Health = 0,
                MaxHealth = 20,
                Fatigue = 0,
                MaxFatigue = 20,
                SpellPoints = 0,
                MaxSpellPoints = 20
            };

            DFMPRestRecoveryResult result = DFMPRestRecoveryPolicy.Calculate(
                vitals,
                4,
                8,
                10,
                DFMPRestRecoveryPolicy.SecondsPerRestHour / 2f,
                2f);

            Assert.AreEqual(4, result.HealthRecovered);
            Assert.AreEqual(8, result.FatigueRecovered);
            Assert.AreEqual(10, result.SpellPointsRecovered);
            Assert.IsFalse(result.IsComplete);
        }

        [Test]
        public void NoSpellPointRegenerationStillAllowsRestCompletion()
        {
            DFMPRestVitals vitals = new DFMPRestVitals
            {
                Health = 10,
                MaxHealth = 10,
                Fatigue = 10,
                MaxFatigue = 10,
                SpellPoints = 0,
                MaxSpellPoints = 20,
                NoSpellPointRegeneration = true
            };

            DFMPRestRecoveryResult result = DFMPRestRecoveryPolicy.Calculate(
                vitals,
                1,
                1,
                1,
                0f,
                1f);

            Assert.AreEqual(0, result.SpellPointsRecovered);
            Assert.IsTrue(result.IsComplete);
        }

        static DFMPRestRequestContext CreateReadyContext()
        {
            return new DFMPRestRequestContext
            {
                HasSession = true,
                SpawnConfirmed = true,
                IsMoving = false,
                IsResting = false,
                HasInterruption = false
            };
        }
    }
}

using DaggerfallConnect;
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
        public void ServerManagedPolicy_AcceptsTimedAndFullRest()
        {
            DFMPRestRequestContext context = CreateReadyContext();

            Assert.AreEqual(
                DFMPRestRequestRejectionReason.None,
                DFMPRestProtocol.GetRejectionReason(context, "TimedRest", DFMPRestPolicies.ServerManaged));
            Assert.AreEqual(
                DFMPRestRequestRejectionReason.None,
                DFMPRestProtocol.GetRejectionReason(context, "FullRest", DFMPRestPolicies.ServerManaged));
        }

        [Test]
        public void Loiter_IsAlwaysRejected()
        {
            DFMPRestRequestContext context = CreateReadyContext();

            Assert.AreEqual(
                DFMPRestRequestRejectionReason.LoiterDisabled,
                DFMPRestProtocol.GetRejectionReason(context, "Loiter", DFMPRestPolicies.Disabled));
            Assert.AreEqual(
                DFMPRestRequestRejectionReason.LoiterDisabled,
                DFMPRestProtocol.GetRejectionReason(context, "Loiter", DFMPRestPolicies.ServerManaged));
        }

        [Test]
        public void HourElapsed_RequiresActiveRest()
        {
            DFMPRestRequestContext context = CreateReadyContext();
            DFMPRestRequestRejectionReason notResting = DFMPRestProtocol.GetRejectionReason(
                context,
                "TimedRest",
                DFMPRestPolicies.ServerManaged,
                DFMPRestRequestKind.HourElapsed);
            Assert.AreEqual(DFMPRestRequestRejectionReason.NotResting, notResting);

            context.IsResting = true;
            Assert.AreEqual(
                DFMPRestRequestRejectionReason.None,
                DFMPRestProtocol.GetRejectionReason(
                    context,
                    "FullRest",
                    DFMPRestPolicies.ServerManaged,
                    DFMPRestRequestKind.HourElapsed));
        }

        [Test]
        public void Stop_ClearsRestWithoutPolicyCheck()
        {
            DFMPRestRequestContext context = CreateReadyContext();
            context.IsResting = true;

            Assert.AreEqual(
                DFMPRestRequestRejectionReason.None,
                DFMPRestProtocol.GetRejectionReason(
                    context,
                    "TimedRest",
                    DFMPRestPolicies.Disabled,
                    DFMPRestRequestKind.Stop));
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
        public void HourTickRateLimit_RejectsBursts()
        {
            Assert.IsTrue(DFMPRestProtocol.IsHourTickTooSoon(0.1f));
            Assert.IsFalse(DFMPRestProtocol.IsHourTickTooSoon(0.5f));
            Assert.IsFalse(DFMPRestProtocol.IsHourTickTooSoon(0.75f));
        }

        [Test]
        public void VanillaRestHour_RecoveriesMatchFormulaAndCapAtMaximums()
        {
            DFMPRestRecoveryResult result = DFMPRestRecoveryPolicy.ApplyHour(new DFMPRestHourRecoveryContext
            {
                Health = 48,
                MaxHealth = 50,
                Fatigue = 14,
                MaxFatigue = 16,
                SpellPoints = 14,
                MaxSpellPoints = 16,
                MedicalSkill = 30,
                Endurance = 50,
                NoSpellPointRegeneration = false,
                RapidHealing = DFCareer.RapidHealingFlags.None,
                IsDay = false,
                IsInside = true
            });

            Assert.AreEqual(2, result.HealthRecovered);
            Assert.AreEqual(2, result.FatigueRecovered);
            Assert.AreEqual(2, result.SpellPointsRecovered);
            Assert.IsTrue(result.IsComplete);
        }

        [Test]
        public void NoSpellPointRegenerationStillAllowsRestCompletion()
        {
            DFMPRestRecoveryResult result = DFMPRestRecoveryPolicy.ApplyHour(new DFMPRestHourRecoveryContext
            {
                Health = 10,
                MaxHealth = 10,
                Fatigue = 10,
                MaxFatigue = 10,
                SpellPoints = 0,
                MaxSpellPoints = 20,
                MedicalSkill = 1,
                Endurance = 50,
                NoSpellPointRegeneration = true,
                RapidHealing = DFCareer.RapidHealingFlags.None,
                IsDay = true,
                IsInside = false
            });

            Assert.AreEqual(0, result.SpellPointsRecovered);
            Assert.IsTrue(result.IsComplete);
        }

        [Test]
        public void ClassicMinutes_ClassifyDayAndNight()
        {
            Assert.IsTrue(DFMPRestRecoveryPolicy.IsDayFromClassicMinutes(8 * 60));
            Assert.IsFalse(DFMPRestRecoveryPolicy.IsDayFromClassicMinutes(20 * 60));
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

using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPDamagePolicyTests
    {
        static DFMPDamageValidationContext ReadyContext()
        {
            return new DFMPDamageValidationContext
            {
                HasSession = true,
                SpawnConfirmed = true,
                TargetExists = true,
                SameWorldContext = true,
                InRange = true,
                PvpEnabled = true,
                CooldownElapsed = true,
                AuthoritativeSourceConnectionId = 7,
                LastAcceptedSequence = 10,
                MaximumAmount = 100
            };
        }

        static DFMPDamageValidationRequest LocalQuestIntent()
        {
            return new DFMPDamageValidationRequest
            {
                RequestId = 1,
                Sequence = 11,
                SourceKind = DFMPDamageSourceKind.LocalQuestPve,
                VitalKind = DFMPVitalKind.Health,
                SourceConnectionId = 7,
                TargetConnectionId = 7,
                Amount = 5
            };
        }

        [Test]
        public void LocalQuestPve_AcceptsOwnerOnlyTarget()
        {
            Assert.AreEqual(
                DFMPDamageRejectionReason.None,
                DFMPDamagePolicy.GetRejectionReason(LocalQuestIntent(), ReadyContext()));
        }

        [Test]
        public void LocalQuestPve_RejectsAnotherPlayerTarget()
        {
            DFMPDamageValidationRequest intent = LocalQuestIntent();
            intent.TargetConnectionId = 8;

            Assert.AreEqual(
                DFMPDamageRejectionReason.LocalQuestTargetMismatch,
                DFMPDamagePolicy.GetRejectionReason(intent, ReadyContext()));
        }

        [Test]
        public void ForgedSourceConnection_IsRejected()
        {
            DFMPDamageValidationRequest intent = LocalQuestIntent();
            intent.SourceConnectionId = 8;

            Assert.AreEqual(
                DFMPDamageRejectionReason.ForgedSource,
                DFMPDamagePolicy.GetRejectionReason(intent, ReadyContext()));
        }

        [Test]
        public void InvalidRequestId_IsRejected()
        {
            DFMPDamageValidationRequest intent = LocalQuestIntent();
            intent.RequestId = 0;

            Assert.AreEqual(DFMPDamageRejectionReason.InvalidRequestId, DFMPDamagePolicy.GetRejectionReason(intent, ReadyContext()));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(101)]
        public void InvalidAmount_IsRejected(int amount)
        {
            DFMPDamageValidationRequest intent = LocalQuestIntent();
            intent.Amount = amount;

            Assert.AreEqual(DFMPDamageRejectionReason.InvalidAmount, DFMPDamagePolicy.GetRejectionReason(intent, ReadyContext()));
        }

        [Test]
        public void DuplicateOrRateLimitedRequest_IsRejected()
        {
            DFMPDamageValidationRequest intent = LocalQuestIntent();
            DFMPDamageValidationContext context = ReadyContext();
            context.LastAcceptedSequence = intent.Sequence;
            Assert.AreEqual(DFMPDamageRejectionReason.DuplicateRequest, DFMPDamagePolicy.GetRejectionReason(intent, context));

            context = ReadyContext();
            context.CooldownElapsed = false;
            Assert.AreEqual(DFMPDamageRejectionReason.RateLimited, DFMPDamagePolicy.GetRejectionReason(intent, context));
        }

        [Test]
        public void DeadOrTransitioningTarget_IsRejected()
        {
            DFMPDamageValidationContext context = ReadyContext();
            context.TargetIsDead = true;
            Assert.AreEqual(DFMPDamageRejectionReason.TargetDead, DFMPDamagePolicy.GetRejectionReason(LocalQuestIntent(), context));

            context = ReadyContext();
            context.HasPendingTransition = true;
            Assert.AreEqual(DFMPDamageRejectionReason.TransitionPending, DFMPDamagePolicy.GetRejectionReason(LocalQuestIntent(), context));
        }

        [Test]
        public void PlayerDamage_RequiresEnabledPvpAndValidRelationship()
        {
            DFMPDamageValidationRequest intent = LocalQuestIntent();
            intent.SourceKind = DFMPDamageSourceKind.Player;
            intent.TargetConnectionId = 8;
            DFMPDamageValidationContext context = ReadyContext();
            context.PvpEnabled = false;
            Assert.AreEqual(DFMPDamageRejectionReason.PvpDisabled, DFMPDamagePolicy.GetRejectionReason(intent, context));

            context.PvpEnabled = true;
            context.SameWorldContext = false;
            Assert.AreEqual(DFMPDamageRejectionReason.ContextMismatch, DFMPDamagePolicy.GetRejectionReason(intent, context));

            context.SameWorldContext = true;
            context.InRange = false;
            Assert.AreEqual(DFMPDamageRejectionReason.OutOfRange, DFMPDamagePolicy.GetRejectionReason(intent, context));
        }

        [TestCase(false, true, DFMPDamageRejectionReason.MissingSession)]
        [TestCase(true, false, DFMPDamageRejectionReason.SpawnNotConfirmed)]
        public void LifecycleContext_IsRequired(bool hasSession, bool spawnConfirmed, DFMPDamageRejectionReason expectedReason)
        {
            DFMPDamageValidationContext context = ReadyContext();
            context.HasSession = hasSession;
            context.SpawnConfirmed = spawnConfirmed;

            Assert.AreEqual(expectedReason, DFMPDamagePolicy.GetRejectionReason(LocalQuestIntent(), context));
        }

        [Test]
        public void VitalState_ClampsDamageAndDetectsDeath()
        {
            var state = new DFMPVitalState
            {
                Health = 7,
                MaxHealth = 20,
                Fatigue = 40,
                MaxFatigue = 100,
                SpellPoints = 12,
                MaxSpellPoints = 30
            };

            DFMPVitalApplicationResult result = state.ApplyDamage(DFMPVitalKind.Health, 10);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(7, result.AppliedAmount);
            Assert.AreEqual(0, result.CurrentValue);
            Assert.IsTrue(result.Killed);
            Assert.AreEqual(0, state.Health);
        }

        [Test]
        public void VitalState_DoesNotApplyInvalidDamage()
        {
            var state = new DFMPVitalState { Health = 10, MaxHealth = 20 };

            DFMPVitalApplicationResult result = state.ApplyDamage(DFMPVitalKind.Health, 0);

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(DFMPDamageRejectionReason.InvalidAmount, result.RejectionReason);
            Assert.AreEqual(10, state.Health);
        }

        [Test]
        public void VitalState_TracksNonHealthVitalsWithoutDeath()
        {
            var state = new DFMPVitalState
            {
                Fatigue = 3,
                MaxFatigue = 100,
                SpellPoints = 4,
                MaxSpellPoints = 20
            };

            DFMPVitalApplicationResult fatigue = state.ApplyDamage(DFMPVitalKind.Fatigue, 8);
            DFMPVitalApplicationResult spellPoints = state.ApplyDamage(DFMPVitalKind.SpellPoints, 2);

            Assert.AreEqual(3, fatigue.AppliedAmount);
            Assert.AreEqual(0, fatigue.CurrentValue);
            Assert.IsFalse(fatigue.Killed);
            Assert.AreEqual(2, spellPoints.CurrentValue);
            Assert.AreEqual(2, state.SpellPoints);
        }
    }
}

using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class VampirismTransformationPolicyTests
    {
        [Test]
        public void ConnectedClient_DefersTransformationToServerTransition()
        {
            Assert.AreEqual(
                DFMPVampirismTransformationAction.DeferToServerTransition,
                DFMPVampirismTransformationPolicy.GetAction(true));
        }

        [Test]
        public void SinglePlayer_AllowsVanillaTransformation()
        {
            Assert.AreEqual(
                DFMPVampirismTransformationAction.AllowVanilla,
                DFMPVampirismTransformationPolicy.GetAction(false));
        }

        [Test]
        public void SpawnedClientWithCurrentRegion_AcceptsTransformationRequest()
        {
            var context = new DFMPVampirismTransformationContext
            {
                HasSession = true,
                SpawnConfirmed = true,
                HasWorldContext = true,
                RegionIndex = 3
            };

            Assert.AreEqual(
                DFMPVampirismTransformationRejectionReason.None,
                DFMPVampirismTransformationPolicy.GetRejectionReason(context));
            Assert.IsTrue(DFMPVampirismTransformationPolicy.IsAccepted(
                DFMPVampirismTransformationPolicy.GetRejectionReason(context)));
        }

        [TestCase(DFMPVampirismTransformationRejectionReason.MissingSession, false, true, false, true, 3)]
        [TestCase(DFMPVampirismTransformationRejectionReason.SpawnNotConfirmed, true, false, false, true, 3)]
        [TestCase(DFMPVampirismTransformationRejectionReason.TransitionAlreadyPending, true, true, true, true, 3)]
        [TestCase(DFMPVampirismTransformationRejectionReason.MissingWorldContext, true, true, false, false, 3)]
        [TestCase(DFMPVampirismTransformationRejectionReason.InvalidRegion, true, true, false, true, -1)]
        public void InvalidTransformationContext_RejectsRequest(
            DFMPVampirismTransformationRejectionReason expectedReason,
            bool hasSession,
            bool spawnConfirmed,
            bool hasPendingTransition,
            bool hasWorldContext,
            int regionIndex)
        {
            var context = new DFMPVampirismTransformationContext
            {
                HasSession = hasSession,
                SpawnConfirmed = spawnConfirmed,
                HasPendingTransition = hasPendingTransition,
                HasWorldContext = hasWorldContext,
                RegionIndex = regionIndex
            };

            Assert.AreEqual(expectedReason, DFMPVampirismTransformationPolicy.GetRejectionReason(context));
            Assert.IsFalse(DFMPVampirismTransformationPolicy.IsAccepted(expectedReason));
        }
    }
}
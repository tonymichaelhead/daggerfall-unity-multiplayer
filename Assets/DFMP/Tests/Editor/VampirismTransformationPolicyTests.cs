using DFMP.Runtime;
using NUnit.Framework;
using System.Collections.Generic;

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

        [Test]
        public void CemeteryCandidates_SelectsServerProvidedExteriorDestination()
        {
            var candidates = new List<DFMPVampirismCemeteryCandidate>
            {
                CreateCemeteryCandidate("Cemetery A", 12),
                CreateCemeteryCandidate("Cemetery B", 24)
            };

            DFMPVampirismCemeteryCandidate candidate;
            DFMPVampirismCemeteryRejectionReason reason;
            Assert.IsTrue(DFMPVampirismCemeteryPolicy.TrySelectCandidate(candidates, 1, out candidate, out reason));
            Assert.AreEqual(DFMPVampirismCemeteryRejectionReason.None, reason);
            Assert.AreEqual("Cemetery B", candidate.LocationId);
            Assert.AreEqual(24, candidate.Context.LocationIndex);
        }

        [TestCase(-1, DFMPVampirismCemeteryRejectionReason.InvalidSelection)]
        [TestCase(1, DFMPVampirismCemeteryRejectionReason.InvalidSelection)]
        public void CemeteryCandidates_RejectsInvalidSelection(int selectionIndex, DFMPVampirismCemeteryRejectionReason expectedReason)
        {
            var candidates = new List<DFMPVampirismCemeteryCandidate>
            {
                CreateCemeteryCandidate("Cemetery A", 12)
            };

            DFMPVampirismCemeteryCandidate candidate;
            DFMPVampirismCemeteryRejectionReason reason;
            Assert.IsFalse(DFMPVampirismCemeteryPolicy.TrySelectCandidate(candidates, selectionIndex, out candidate, out reason));
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CemeteryCandidates_RejectsNonExteriorDestination()
        {
            var candidate = CreateCemeteryCandidate("Cemetery A", 12);
            candidate.Context.Kind = DFMPWorldContextKind.Dungeon;

            DFMPVampirismCemeteryCandidate selected;
            DFMPVampirismCemeteryRejectionReason reason;
            Assert.IsFalse(DFMPVampirismCemeteryPolicy.TrySelectCandidate(
                new List<DFMPVampirismCemeteryCandidate> { candidate }, 0, out selected, out reason));
            Assert.AreEqual(DFMPVampirismCemeteryRejectionReason.InvalidDestination, reason);
        }

        [Test]
        public void CemeteryResolution_RejectsInvalidRegionBeforeContentLookup()
        {
            DFMPVampirismCemeteryCandidate candidate;
            DFMPVampirismTransformationRejectionReason reason;
            Assert.IsFalse(DFMPSpawnProtocol.TryResolveRandomCemetery(-1, out candidate, out reason));
            Assert.AreEqual(DFMPVampirismTransformationRejectionReason.InvalidRegion, reason);
        }

        [TestCase(12, 18, 20580)]
        [TestCase(20, 18, 20100)]
        public void VampirismTime_AdvancesToTwoWeeksAfterOneHourPastDusk(int currentHour, int duskHour, int expectedAdvanceMinutes)
        {
            uint targetClassicMinutes;
            Assert.IsTrue(DFMPVampirismTimePolicy.TryGetTargetClassicMinutes(100, currentHour, duskHour, out targetClassicMinutes));
            Assert.AreEqual((uint)(100 + expectedAdvanceMinutes), targetClassicMinutes);
        }

        [TestCase(-1, 18)]
        [TestCase(24, 18)]
        [TestCase(12, -1)]
        [TestCase(12, 24)]
        public void VampirismTime_RejectsInvalidHours(int currentHour, int duskHour)
        {
            uint targetClassicMinutes;
            Assert.IsFalse(DFMPVampirismTimePolicy.TryGetTargetClassicMinutes(100, currentHour, duskHour, out targetClassicMinutes));
        }

        static DFMPVampirismCemeteryCandidate CreateCemeteryCandidate(string locationId, int locationIndex)
        {
            return new DFMPVampirismCemeteryCandidate
            {
                LocationId = locationId,
                Position = new DFMPWorldPosition { WorldX = 6792821, WorldY = 0f, WorldZ = 9374554 },
                Context = new DFMPWorldContextKey
                {
                    Kind = DFMPWorldContextKind.Exterior,
                    MapPixelX = 207,
                    MapPixelY = 213,
                    RegionIndex = 3,
                    LocationIndex = locationIndex,
                    LocationId = locationId
                }
            };
        }
    }
}
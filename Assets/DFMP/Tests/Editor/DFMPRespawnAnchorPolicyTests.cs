using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPRespawnAnchorPolicyTests
    {
        [Test]
        public void DefaultAnchor_PrefersLastVisitedInn()
        {
            Assert.AreEqual(
                DFMPRespawnAnchorKind.Inn,
                DFMPRespawnAnchorPolicy.ResolveDefaultAnchor(new DFMPRespawnAnchorContext
                {
                    HasInnAnchor = true,
                    HasStartingLocation = true
                }));
        }

        [Test]
        public void DefaultAnchor_FallsBackToStartingLocation()
        {
            Assert.AreEqual(
                DFMPRespawnAnchorKind.StartingLocation,
                DFMPRespawnAnchorPolicy.ResolveDefaultAnchor(new DFMPRespawnAnchorContext
                {
                    HasStartingLocation = true
                }));
        }

        [Test]
        public void DefaultAnchor_RejectsMissingFallback()
        {
            Assert.AreEqual(
                DFMPRespawnAnchorKind.None,
                DFMPRespawnAnchorPolicy.ResolveDefaultAnchor(new DFMPRespawnAnchorContext()));
        }

        [TestCase(15, true)]
        [TestCase(0, false)]
        [TestCase(16, false)]
        public void BuildingType_RecognizesOnlyTavernsAsInns(int buildingType, bool expected)
        {
            Assert.AreEqual(expected, DFMPRespawnAnchorPolicy.IsInnBuildingType(buildingType));
        }
    }
}
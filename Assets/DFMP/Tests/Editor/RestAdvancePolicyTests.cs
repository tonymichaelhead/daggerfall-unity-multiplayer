using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class RestAdvancePolicyTests
    {
        [TestCase(DFMPRestAdvanceMode.TimedRest)]
        [TestCase(DFMPRestAdvanceMode.FullRest)]
        [TestCase(DFMPRestAdvanceMode.Loiter)]
        public void ConnectedMultiplayerClient_ConsumesRestAdvanceModes(DFMPRestAdvanceMode mode)
        {
            Assert.IsTrue(DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(true, mode));
        }

        [TestCase(DFMPRestAdvanceMode.TimedRest)]
        [TestCase(DFMPRestAdvanceMode.FullRest)]
        [TestCase(DFMPRestAdvanceMode.Loiter)]
        public void DisconnectedOrSinglePlayer_DoesNotConsumeRestAdvanceModes(DFMPRestAdvanceMode mode)
        {
            Assert.IsFalse(DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(false, mode));
        }

        [TestCase("TimedRest", DFMPRestAdvanceMode.TimedRest)]
        [TestCase("FullRest", DFMPRestAdvanceMode.FullRest)]
        [TestCase("Loiter", DFMPRestAdvanceMode.Loiter)]
        public void RestModeNames_ParseKnownVanillaModes(string restModeName, DFMPRestAdvanceMode expectedMode)
        {
            DFMPRestAdvanceMode mode;
            Assert.IsTrue(DFMPRestAdvancePolicy.TryParseMode(restModeName, out mode));
            Assert.AreEqual(expectedMode, mode);
        }

        [Test]
        public void RestModeNames_RejectUnknownMode()
        {
            DFMPRestAdvanceMode mode;
            Assert.IsFalse(DFMPRestAdvancePolicy.TryParseMode("Selection", out mode));
        }
    }
}

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
        public void DisabledPolicy_ConsumesRestAdvanceModes(DFMPRestAdvanceMode mode)
        {
            Assert.IsTrue(DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(true, mode, DFMPRestPolicies.Disabled));
        }

        [TestCase(DFMPRestAdvanceMode.TimedRest, false)]
        [TestCase(DFMPRestAdvanceMode.FullRest, false)]
        [TestCase(DFMPRestAdvanceMode.Loiter, true)]
        public void ServerManagedPolicy_ConsumesOnlyLoiter(DFMPRestAdvanceMode mode, bool expectedConsume)
        {
            Assert.AreEqual(
                expectedConsume,
                DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(true, mode, DFMPRestPolicies.ServerManaged));
        }

        [TestCase(DFMPRestAdvanceMode.TimedRest)]
        [TestCase(DFMPRestAdvanceMode.FullRest)]
        [TestCase(DFMPRestAdvanceMode.Loiter)]
        public void DisconnectedOrSinglePlayer_DoesNotConsumeRestAdvanceModes(DFMPRestAdvanceMode mode)
        {
            Assert.IsFalse(DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(false, mode, DFMPRestPolicies.Disabled));
            Assert.IsFalse(DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(false, mode, DFMPRestPolicies.ServerManaged));
        }

        [Test]
        public void ConnectedClient_SkipsRestWorldTime()
        {
            Assert.IsTrue(DFMPRestAdvancePolicy.ShouldSkipRestWorldTime(true));
            Assert.IsFalse(DFMPRestAdvancePolicy.ShouldSkipRestWorldTime(false));
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

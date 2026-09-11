using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class TimeAdvancePolicyTests
    {
        [TestCase(DFMPTimeAdvanceSources.QuestTraining)]
        [TestCase(DFMPTimeAdvanceSources.GuildTraining)]
        [TestCase(DFMPTimeAdvanceSources.PrisonSentence)]
        [TestCase(DFMPTimeAdvanceSources.PrisonRelease)]
        public void ConnectedClient_ConsumesProtectedTimeAdvance(string source)
        {
            Assert.IsTrue(DFMPTimeAdvancePolicy.ShouldConsumeClientAdvance(true, source, 10800));
        }

        [TestCase(false, DFMPTimeAdvanceSources.QuestTraining, 10800)]
        [TestCase(true, "Rest", 10800)]
        [TestCase(true, DFMPTimeAdvanceSources.GuildTraining, 0)]
        public void InvalidOrSinglePlayerAdvance_RemainsVanilla(bool connected, string source, int seconds)
        {
            Assert.IsFalse(DFMPTimeAdvancePolicy.ShouldConsumeClientAdvance(connected, source, seconds));
        }
    }
}
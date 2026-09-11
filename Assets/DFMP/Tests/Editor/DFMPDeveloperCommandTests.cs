using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPDeveloperCommandTests
    {
        [Test]
        public void DeveloperInfectSelf_RequiresExplicitEnablement()
        {
            DFMPDeveloperCommandRejectionReason reason = DFMPDeveloperCommandPolicy.GetInfectSelfRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = false,
                HasSession = true,
                SpawnConfirmed = true
            });

            Assert.AreEqual(DFMPDeveloperCommandRejectionReason.CommandsDisabled, reason);
            Assert.IsFalse(DFMPDeveloperCommandPolicy.IsAccepted(reason));
        }

        [TestCase(0, DFMPDeveloperCommandRejectionReason.InvalidMinutes)]
        [TestCase(-1, DFMPDeveloperCommandRejectionReason.InvalidMinutes)]
        [TestCase(10081, DFMPDeveloperCommandRejectionReason.MinutesLimitExceeded)]
        public void DeveloperAdvanceTime_RejectsInvalidAmounts(int minutes, DFMPDeveloperCommandRejectionReason expectedReason)
        {
            DFMPDeveloperCommandRejectionReason reason = DFMPDeveloperCommandPolicy.GetAdvanceTimeRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = true,
                HasSession = true,
                SpawnConfirmed = true
            }, minutes, 10080);

            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void DeveloperAdvanceTime_AcceptsBoundedPositiveAmount()
        {
            DFMPDeveloperCommandRejectionReason reason = DFMPDeveloperCommandPolicy.GetAdvanceTimeRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = true,
                HasSession = true,
                SpawnConfirmed = true
            }, 4320, 10080);

            Assert.AreEqual(DFMPDeveloperCommandRejectionReason.None, reason);
        }

        [Test]
        public void DeveloperInfectSelf_AcceptsReadySpawnedSessionWhenEnabled()
        {
            DFMPDeveloperCommandRejectionReason reason = DFMPDeveloperCommandPolicy.GetInfectSelfRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = true,
                HasSession = true,
                SpawnConfirmed = true
            });

            Assert.AreEqual(DFMPDeveloperCommandRejectionReason.None, reason);
            Assert.IsTrue(DFMPDeveloperCommandPolicy.IsAccepted(reason));
        }

        [TestCase(false, true, true, DFMPDeveloperCommandRejectionReason.CommandsDisabled)]
        [TestCase(true, false, true, DFMPDeveloperCommandRejectionReason.MissingSession)]
        [TestCase(true, true, false, DFMPDeveloperCommandRejectionReason.SpawnNotConfirmed)]
        public void DeveloperInfectSelf_RejectsInvalidContext(
            bool commandsEnabled,
            bool hasSession,
            bool spawnConfirmed,
            DFMPDeveloperCommandRejectionReason expectedReason)
        {
            DFMPDeveloperCommandRejectionReason reason = DFMPDeveloperCommandPolicy.GetInfectSelfRejectionReason(new DFMPDeveloperCommandContext
            {
                CommandsEnabled = commandsEnabled,
                HasSession = hasSession,
                SpawnConfirmed = spawnConfirmed
            });

            Assert.AreEqual(expectedReason, reason);
        }
    }
}
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
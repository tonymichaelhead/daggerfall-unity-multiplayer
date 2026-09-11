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
    }
}
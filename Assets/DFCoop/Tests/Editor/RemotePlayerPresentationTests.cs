using DFCoop.Runtime;
using DaggerfallWorkshop;
using NUnit.Framework;
using UnityEngine;

namespace DFCoop.Tests
{
    [TestFixture]
    public class RemotePlayerPresentationTests
    {
        [Test]
        public void IsRemoteSession_ExcludesLocalAndUnconfirmedSessions()
        {
            GameObject go = new GameObject("DFCoop_RemoteSessionTest");

            try
            {
                var session = go.AddComponent<DFCoopPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);

                Assert.IsFalse(DFCoopRemotePlayerPresentation.IsRemoteSession(session, 4));

                session.ConfirmSpawn();
                Assert.IsFalse(DFCoopRemotePlayerPresentation.IsRemoteSession(session, -1));
                Assert.IsFalse(DFCoopRemotePlayerPresentation.IsRemoteSession(session, 7));
                Assert.IsTrue(DFCoopRemotePlayerPresentation.IsRemoteSession(session, 4));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WorldToScenePosition_PreservesLocalFloatingOrigin()
        {
            GameObject go = new GameObject("DFCoop_RemotePositionTest");

            try
            {
                var localGps = go.AddComponent<PlayerGPS>();
                localGps.WorldX = 6792821;
                localGps.WorldZ = 9374554;
                Vector3 localScenePosition = new Vector3(12f, 4f, -8f);

                Vector3 remotePosition = DFCoopRemotePlayerPresentation.WorldToScenePosition(localGps, localScenePosition, 6793021, 9374154);

                Assert.AreEqual(17f, remotePosition.x);
                Assert.AreEqual(4f, remotePosition.y);
                Assert.AreEqual(-18f, remotePosition.z);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsVisibleInLocalMapPixel_UsesPresentationDistanceCap()
        {
            Assert.IsTrue(DFCoopRemotePlayerPresentation.IsVisibleInLocalMapPixel(Vector3.zero, new Vector3(149f, 0f, 0f)));
            Assert.IsFalse(DFCoopRemotePlayerPresentation.IsVisibleInLocalMapPixel(Vector3.zero, new Vector3(151f, 0f, 0f)));
        }

        [Test]
        public void InterpolatePosition_MovesTowardTargetWithoutOvershooting()
        {
            Vector3 position = DFCoopRemotePlayerPresentation.InterpolatePosition(Vector3.zero, new Vector3(10f, 0f, 0f), 0.1f);

            Assert.Greater(position.x, 0f);
            Assert.Less(position.x, 10f);
            Assert.AreEqual(0f, position.y);
            Assert.AreEqual(0f, position.z);
        }
    }
}
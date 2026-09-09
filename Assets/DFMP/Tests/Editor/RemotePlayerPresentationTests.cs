using DFMP.Runtime;
using DaggerfallWorkshop;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class RemotePlayerPresentationTests
    {
        [Test]
        public void IsRemoteSession_ExcludesLocalAndUnconfirmedSessions()
        {
            GameObject go = new GameObject("DFMP_RemoteSessionTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 6792821, 0f, 9374554);

                Assert.IsFalse(DFMPRemotePlayerPresentation.IsRemoteSession(session, 4));

                session.ConfirmSpawn();
                Assert.IsFalse(DFMPRemotePlayerPresentation.IsRemoteSession(session, -1));
                Assert.IsFalse(DFMPRemotePlayerPresentation.IsRemoteSession(session, 7));
                Assert.IsTrue(DFMPRemotePlayerPresentation.IsRemoteSession(session, 4));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void WorldToScenePosition_PreservesLocalFloatingOrigin()
        {
            GameObject go = new GameObject("DFMP_RemotePositionTest");

            try
            {
                var localGps = go.AddComponent<PlayerGPS>();
                localGps.WorldX = 6792821;
                localGps.WorldZ = 9374554;
                Vector3 localScenePosition = new Vector3(12f, 4f, -8f);

                Vector3 remotePosition = DFMPRemotePlayerPresentation.WorldToScenePosition(localGps, localScenePosition, 6793021, 9374154);

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
            Assert.IsTrue(DFMPRemotePlayerPresentation.IsVisibleInLocalMapPixel(Vector3.zero, new Vector3(149f, 0f, 0f)));
            Assert.IsFalse(DFMPRemotePlayerPresentation.IsVisibleInLocalMapPixel(Vector3.zero, new Vector3(151f, 0f, 0f)));
        }

        [Test]
        public void InterpolatePosition_MovesTowardTargetWithoutOvershooting()
        {
            Vector3 position = DFMPRemotePlayerPresentation.InterpolatePosition(Vector3.zero, new Vector3(10f, 0f, 0f), 0.1f);

            Assert.Greater(position.x, 0f);
            Assert.Less(position.x, 10f);
            Assert.AreEqual(0f, position.y);
            Assert.AreEqual(0f, position.z);
        }

        [Test]
        public void ApplySceneHeightOffset_RaisesGroundedPositionByReplicatedAmount()
        {
            Vector3 position = DFMPRemotePlayerPresentation.ApplySceneHeightOffset(new Vector3(4f, 10f, 8f), 1.25f);

            Assert.AreEqual(new Vector3(4f, 11.25f, 8f), position);
        }

        [Test]
        public void GetLabelBillboardRotation_ShowsTextFrontToCameraWithoutPitch()
        {
            Quaternion rotation = DFMPRemotePlayerPresentation.GetLabelBillboardRotation(new Vector3(2f, 1f, 3f), new Vector3(2f, 100f, 13f));

            Assert.AreEqual(Vector3.back, rotation * Vector3.forward);
            Assert.AreEqual(Vector3.up, rotation * Vector3.up);
        }

        [Test]
        public void IsAppearanceChanged_UpdatesOnlyWhenReplicatedAvatarChanges()
        {
            GameObject go = new GameObject("DFMP_AppearanceCacheTest");

            try
            {
                var session = go.AddComponent<DFMPPlayerSessionState>();
                session.SetAppearance(1, 0, 2, 6);
                session.SetAvatarMobileType((int)MobileTypes.Spellsword);

                Assert.IsFalse(DFMPRemotePlayerPresentation.IsAppearanceChanged((int)MobileTypes.Spellsword, 0, session));
                Assert.IsTrue(DFMPRemotePlayerPresentation.IsAppearanceChanged((int)MobileTypes.Mage, 0, session));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AvatarMovementState_MapsReplicatedMovementState()
        {
            Assert.AreEqual(MobileStates.Move, DFMPRemotePlayerPresentation.GetMovementState(true));
            Assert.AreEqual(MobileStates.Idle, DFMPRemotePlayerPresentation.GetMovementState(false));
        }

        [Test]
        public void AvatarClassMapping_MapsStandardClassesAndFallsBackForCustomClasses()
        {
            Assert.AreEqual((int)MobileTypes.Mage, DFMPAvatarProtocol.GetMobileTypeFromCareerName("Mage"));
            Assert.AreEqual((int)MobileTypes.Spellsword, DFMPAvatarProtocol.GetMobileTypeFromCareerName("Spellsword"));
            Assert.AreEqual((int)MobileTypes.Nightblade, DFMPAvatarProtocol.GetMobileTypeFromCareerName("Night Blade"));
            Assert.AreEqual((int)MobileTypes.Knight, DFMPAvatarProtocol.GetMobileTypeFromCareerName("Knight"));
            Assert.AreEqual((int)MobileTypes.Warrior, DFMPAvatarProtocol.GetMobileTypeFromCareerName("Spellblade"));
            Assert.AreEqual((int)MobileTypes.Warrior, DFMPAvatarProtocol.GetMobileTypeFromCareerName("Bladesinger"));
        }

        [Test]
        public void AvatarActionState_MapsPresentationActions()
        {
            Assert.AreEqual(MobileStates.PrimaryAttack, DFMPRemotePlayerPresentation.GetActionState(DFMPPlayerActionKind.PrimaryAttack));
            Assert.AreEqual(MobileStates.RangedAttack1, DFMPRemotePlayerPresentation.GetActionState(DFMPPlayerActionKind.RangedAttack));
            Assert.AreEqual(MobileStates.Spell, DFMPRemotePlayerPresentation.GetActionState(DFMPPlayerActionKind.Spell));
        }
    }
}
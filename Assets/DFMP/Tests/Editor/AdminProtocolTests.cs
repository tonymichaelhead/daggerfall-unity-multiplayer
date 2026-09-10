using System.Collections.Generic;
using DFMP.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class AdminProtocolTests
    {
        [Test]
        public void CreateRoster_SortsPlayersAndPreservesConnectionIds()
        {
            GameObject firstObject = new GameObject("DFMP_Test_AdminFirstSession");
            GameObject secondObject = new GameObject("DFMP_Test_AdminSecondSession");

            try
            {
                var firstSession = firstObject.AddComponent<DFMPPlayerSessionState>();
                firstSession.Initialize(9, 0, 0f, 0);
                firstSession.SetDisplayName("Zara");

                var secondSession = secondObject.AddComponent<DFMPPlayerSessionState>();
                secondSession.Initialize(3, 0, 0f, 0);
                secondSession.SetDisplayName("Alyx");

                var sessions = new Dictionary<int, DFMPPlayerSessionState>
                {
                    { 9, firstSession },
                    { 3, secondSession },
                    { 12, null }
                };
                var accountIds = new Dictionary<int, string>
                {
                    { 9, "local:zara" },
                    { 3, "local:alyx" }
                };

                DFMPAdminRosterResponse roster = DFMPAdminProtocol.CreateRoster(sessions, accountIds, 3);

                Assert.AreEqual(2, roster.Players.Length);
                Assert.AreEqual(3, roster.Players[0].ConnectionId);
                Assert.AreEqual("Alyx", roster.Players[0].DisplayName);
                Assert.AreEqual("local:alyx", roster.Players[0].AccountId);
                Assert.IsTrue(roster.Players[0].IsAdmin);
                Assert.AreEqual(9, roster.Players[1].ConnectionId);
                Assert.AreEqual("Zara", roster.Players[1].DisplayName);
                Assert.AreEqual("local:zara", roster.Players[1].AccountId);
                Assert.IsFalse(roster.Players[1].IsAdmin);
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void CreateRoster_CanExcludeDisconnectedPlayer()
        {
            GameObject sessionObject = new GameObject("DFMP_Test_AdminSession");

            try
            {
                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(4, 0, 0f, 0);

                var sessions = new Dictionary<int, DFMPPlayerSessionState> { { 4, session } };
                var accountIds = new Dictionary<int, string> { { 4, "local:admin" } };
                DFMPAdminRosterResponse roster = DFMPAdminProtocol.CreateRoster(sessions, accountIds, 4, 4);

                Assert.IsNotNull(roster.Players);
                Assert.AreEqual(0, roster.Players.Length);
            }
            finally
            {
                Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void RequesterValidation_RequiresMatchingSession()
        {
            GameObject sessionObject = new GameObject("DFMP_Test_AdminRequester");

            try
            {
                var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                session.Initialize(7, 0, 0f, 0);

                Assert.IsTrue(DFMPAdminProtocol.TryValidateRequester(session, 7, out string reason));
                Assert.IsTrue(string.IsNullOrEmpty(reason));
                Assert.IsFalse(DFMPAdminProtocol.TryValidateRequester(session, 8, out reason));
                Assert.AreEqual("session mismatch", reason);
                Assert.IsFalse(DFMPAdminProtocol.TryValidateRequester(null, 7, out reason));
                Assert.AreEqual("session missing", reason);
            }
            finally
            {
                Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void KickNotice_UsesServerMessageOrSafeDefault()
        {
            Assert.AreEqual(DFMPAdminProtocol.KickNoticeText, DFMPAdminProtocol.GetKickNoticeText(null));
            Assert.AreEqual(DFMPAdminProtocol.KickNoticeText, DFMPAdminProtocol.GetKickNoticeText("   "));
            Assert.AreEqual("Maintenance removal.", DFMPAdminProtocol.GetKickNoticeText("  Maintenance removal.  "));
        }

        [Test]
        public void ClientJoinFlow_MarksDisconnectedAfterKick()
        {
            var flow = new DFMPClientJoinFlow();
            flow.MarkConnecting();
            flow.MarkDisconnected();

            Assert.AreEqual(DFMPClientJoinState.Disconnected, flow.State);
        }
    }
}
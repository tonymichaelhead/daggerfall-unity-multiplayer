using System.Collections.Generic;
using NUnit.Framework;
using DFMP.Runtime;

namespace DFMP.Tests
{
    [TestFixture]
    public class AuthPolicyTests
    {
        sealed class InMemoryLocalAccountStore : IDFMPLocalAccountStore
        {
            public readonly Dictionary<string, DFMPLocalAccountRecord> Records = new Dictionary<string, DFMPLocalAccountRecord>();
            public int SaveCount;

            public bool TryLoad(string accountId, out DFMPLocalAccountRecord record)
            {
                return Records.TryGetValue(accountId, out record);
            }

            public void Save(DFMPLocalAccountRecord record)
            {
                SaveCount++;
                Records[record.AccountId] = record;
            }
        }

        const string Nonce = "0123456789abcdef0123456789abcdef";
        const string CredentialA = "1111111111111111111111111111111111111111111111111111111111111111";
        const string CredentialB = "2222222222222222222222222222222222222222222222222222222222222222";

        static DFMPServerConfig CreateConfig(string mode = DFMPAuthModes.ServerLocal)
        {
            var config = new DFMPServerConfig();
            config.Identity.Mode = mode;
            config.Identity.WhitelistEnabled = false;
            config.Identity.AllowSelfRegistration = true;
            return config;
        }

        static DFMPAuthRequestMessage CreateRequest(string accountId = "tester", string credential = CredentialA, string nonce = Nonce)
        {
            return new DFMPAuthRequestMessage
            {
                ProtocolVersion = DFMPProtocol.Version,
                ClientBuildId = "test",
                AccountId = accountId,
                Credential = credential,
                Nonce = nonce
            };
        }

        [Test]
        public void Resolve_RejectsProtocolVersionMismatch()
        {
            var request = CreateRequest();
            request.ProtocolVersion = DFMPProtocol.Version + 1;

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(request, Nonce, CreateConfig(), new InMemoryLocalAccountStore());

            Assert.AreEqual(DFMPAuthResultCode.VersionMismatch, decision.Code);
        }

        [Test]
        public void Resolve_RejectsMismatchedNonce()
        {
            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(
                CreateRequest(nonce: "ffffffffffffffffffffffffffffffff"),
                Nonce,
                CreateConfig(),
                new InMemoryLocalAccountStore());

            Assert.AreEqual(DFMPAuthResultCode.InvalidRequest, decision.Code);
        }

        [Test]
        public void Resolve_RejectsMalformedCredential()
        {
            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(
                CreateRequest(credential: "not-a-credential"),
                Nonce,
                CreateConfig(),
                new InMemoryLocalAccountStore());

            Assert.AreEqual(DFMPAuthResultCode.InvalidRequest, decision.Code);
        }

        [Test]
        public void Resolve_RejectsAccountOutsideWhitelist()
        {
            DFMPServerConfig config = CreateConfig();
            config.Identity.WhitelistEnabled = true;
            config.Identity.AllowedAccountIds = new[] { "allowed" };

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(CreateRequest("blocked"), Nonce, config, new InMemoryLocalAccountStore());

            Assert.AreEqual(DFMPAuthResultCode.NotWhitelisted, decision.Code);
        }

        [Test]
        public void Resolve_RegistersUnknownAccountWhenSelfRegistrationIsEnabled()
        {
            var store = new InMemoryLocalAccountStore();

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(CreateRequest(), Nonce, CreateConfig(), store);

            Assert.AreEqual(DFMPAuthResultCode.Accepted, decision.Code);
            Assert.IsTrue(decision.Registered);
            Assert.AreEqual("tester", decision.AccountId);
            Assert.IsTrue(store.Records.ContainsKey("tester"));
        }

        [Test]
        public void Resolve_RejectsUnknownAccountWhenSelfRegistrationIsDisabled()
        {
            DFMPServerConfig config = CreateConfig();
            config.Identity.AllowSelfRegistration = false;

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(CreateRequest(), Nonce, config, new InMemoryLocalAccountStore());

            Assert.AreEqual(DFMPAuthResultCode.RegistrationClosed, decision.Code);
        }

        [Test]
        public void Resolve_AcceptsReturningAccountWithMatchingCredential()
        {
            var store = new InMemoryLocalAccountStore();
            DFMPAuthPolicy.Resolve(CreateRequest(), Nonce, CreateConfig(), store);

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(CreateRequest(), Nonce, CreateConfig(), store);

            Assert.AreEqual(DFMPAuthResultCode.Accepted, decision.Code);
            Assert.IsFalse(decision.Registered);
        }

        [Test]
        public void Resolve_RejectsReturningAccountWithWrongCredential()
        {
            var store = new InMemoryLocalAccountStore();
            DFMPAuthPolicy.Resolve(CreateRequest(), Nonce, CreateConfig(), store);

            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(
                CreateRequest(credential: CredentialB),
                Nonce,
                CreateConfig(),
                store);

            Assert.AreEqual(DFMPAuthResultCode.InvalidCredentials, decision.Code);
        }

        [Test]
        public void Resolve_OpenModeIgnoresCredentialsButStillNormalizesAccount()
        {
            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(
                CreateRequest("Tester", credential: string.Empty, nonce: string.Empty),
                Nonce,
                CreateConfig(DFMPAuthModes.Open),
                null);

            Assert.AreEqual(DFMPAuthResultCode.Accepted, decision.Code);
            Assert.AreEqual("tester", decision.AccountId);
        }

        [Test]
        public void Resolve_RejectsIdentityServiceModeUntilItExists()
        {
            DFMPAuthDecision decision = DFMPAuthPolicy.Resolve(CreateRequest(), Nonce, CreateConfig(DFMPAuthModes.Dfmp), new InMemoryLocalAccountStore());

            Assert.AreEqual(DFMPAuthResultCode.ModeUnsupported, decision.Code);
        }

        [Test]
        public void AuthModes_UnknownModeFallsBackToServerLocal()
        {
            Assert.AreEqual(DFMPAuthModes.ServerLocal, DFMPAuthModes.Normalize("nonsense"));
            Assert.AreEqual(DFMPAuthModes.ServerLocal, DFMPAuthModes.Normalize(null));
            Assert.AreEqual(DFMPAuthModes.Open, DFMPAuthModes.Normalize("OPEN"));
        }
    }

    [TestFixture]
    public class CredentialTests
    {
        // Uses a low work factor so the fixture stays fast; the algorithm is identical at any count.
        const int TestIterations = 1000;

        [Test]
        public void DeriveClientCredential_IsDeterministicAndCaseInsensitiveOnAccountId()
        {
            string first = DFMPCredential.DeriveClientCredential("tester", "correct horse battery", TestIterations);
            string second = DFMPCredential.DeriveClientCredential("Tester", "correct horse battery", TestIterations);

            Assert.AreEqual(first, second);
            Assert.IsTrue(DFMPCredential.IsValidCredentialFormat(first));
        }

        [Test]
        public void DeriveClientCredential_DiffersByPassword()
        {
            string first = DFMPCredential.DeriveClientCredential("tester", "password-one", TestIterations);
            string second = DFMPCredential.DeriveClientCredential("tester", "password-two", TestIterations);

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void DeriveClientCredential_DiffersByAccountId()
        {
            string first = DFMPCredential.DeriveClientCredential("one", "shared-password", TestIterations);
            string second = DFMPCredential.DeriveClientCredential("two", "shared-password", TestIterations);

            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void Pbkdf2HmacSha256_MatchesRfc6070StyleKnownAnswer()
        {
            // PBKDF2-HMAC-SHA256, P="password", S="salt", dkLen=32. Two iterations exercise the XOR loop.
            byte[] salt = System.Text.Encoding.UTF8.GetBytes("salt");

            Assert.AreEqual(
                "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b",
                DFMPCredential.HashCredential("password", salt, 1));
            Assert.AreEqual(
                "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43",
                DFMPCredential.HashCredential("password", salt, 2));
        }

        [Test]
        public void IsValidCredentialFormat_RejectsWrongLengthAndNonHex()
        {
            Assert.IsFalse(DFMPCredential.IsValidCredentialFormat(null));
            Assert.IsFalse(DFMPCredential.IsValidCredentialFormat("abc"));
            Assert.IsFalse(DFMPCredential.IsValidCredentialFormat(new string('g', 64)));
            Assert.IsFalse(DFMPCredential.IsValidCredentialFormat(new string('A', 64)));
            Assert.IsTrue(DFMPCredential.IsValidCredentialFormat(new string('a', 64)));
        }

        [Test]
        public void LocalAccountRecord_UsesUniqueSaltPerAccount()
        {
            string credential = new string('a', 64);
            DFMPLocalAccountRecord first = DFMPLocalAccountRecord.Create("one", credential);
            DFMPLocalAccountRecord second = DFMPLocalAccountRecord.Create("two", credential);

            Assert.AreNotEqual(first.Salt, second.Salt);
            Assert.AreNotEqual(first.Hash, second.Hash);
        }

        [Test]
        public void LocalAccountRecord_MatchesOnlyTheIssuingCredential()
        {
            DFMPLocalAccountRecord record = DFMPLocalAccountRecord.Create("one", new string('a', 64));

            Assert.IsTrue(record.Matches(new string('a', 64)));
            Assert.IsFalse(record.Matches(new string('b', 64)));
        }

        [Test]
        public void LocalAccountRecord_SurvivesJsonRoundTrip()
        {
            DFMPLocalAccountRecord record = DFMPLocalAccountRecord.Create("one", new string('a', 64));
            DFMPLocalAccountRecord restored = DFMPLocalAccountRecord.FromJson(record.ToJson(), "one");

            Assert.IsNotNull(restored);
            Assert.IsTrue(restored.Matches(new string('a', 64)));
        }

        [Test]
        public void LocalAccountRecord_RejectsRecordBelongingToAnotherAccount()
        {
            DFMPLocalAccountRecord record = DFMPLocalAccountRecord.Create("one", new string('a', 64));

            Assert.IsNull(DFMPLocalAccountRecord.FromJson(record.ToJson(), "two"));
        }
    }

    [TestFixture]
    public class AuthThrottleTests
    {
        [Test]
        public void LocksOutAfterConfiguredAttempts()
        {
            double now = 0;
            var throttle = new DFMPAuthThrottle(() => now, 3, 60, 60);

            throttle.RecordFailure("a");
            throttle.RecordFailure("a");
            Assert.IsFalse(throttle.IsLockedOut("a"));

            throttle.RecordFailure("a");
            Assert.IsTrue(throttle.IsLockedOut("a"));
        }

        [Test]
        public void LockoutExpiresAfterTheLockoutWindow()
        {
            double now = 0;
            var throttle = new DFMPAuthThrottle(() => now, 2, 60, 60);

            throttle.RecordFailure("a");
            throttle.RecordFailure("a");
            Assert.IsTrue(throttle.IsLockedOut("a"));

            now = 61;
            Assert.IsFalse(throttle.IsLockedOut("a"));
        }

        [Test]
        public void AttemptsOutsideTheWindowDoNotAccumulate()
        {
            double now = 0;
            var throttle = new DFMPAuthThrottle(() => now, 2, 60, 60);

            throttle.RecordFailure("a");
            now = 120;
            throttle.RecordFailure("a");

            Assert.IsFalse(throttle.IsLockedOut("a"));
        }

        [Test]
        public void SuccessClearsAccumulatedFailures()
        {
            double now = 0;
            var throttle = new DFMPAuthThrottle(() => now, 2, 60, 60);

            throttle.RecordFailure("a");
            throttle.RecordSuccess("a");
            throttle.RecordFailure("a");

            Assert.IsFalse(throttle.IsLockedOut("a"));
        }

        [Test]
        public void KeysAreTrackedIndependently()
        {
            double now = 0;
            var throttle = new DFMPAuthThrottle(() => now, 2, 60, 60);

            throttle.RecordFailure("a");
            throttle.RecordFailure("a");

            Assert.IsTrue(throttle.IsLockedOut("a"));
            Assert.IsFalse(throttle.IsLockedOut("b"));
        }
    }

    [TestFixture]
    public class AccountRoleTests
    {
        [Test]
        public void Resolve_PrefersAdminOverModerator()
        {
            var config = new DFMPServerConfig();
            config.Identity.ModeratorAccountIds = new[] { "tony" };
            config.Identity.AdminAccountIds = new[] { "tony" };

            Assert.AreEqual(DFMPAccountRole.Admin, DFMPAccountRoles.Resolve("tony", config));
        }

        [Test]
        public void Resolve_MatchesRegardlessOfConfiguredCasing()
        {
            var config = new DFMPServerConfig();
            config.Identity.AdminAccountIds = new[] { "Tony" };

            Assert.AreEqual(DFMPAccountRole.Admin, DFMPAccountRoles.Resolve("tony", config));
        }

        [Test]
        public void Resolve_DefaultsToPlayer()
        {
            Assert.AreEqual(DFMPAccountRole.Player, DFMPAccountRoles.Resolve("stranger", new DFMPServerConfig()));
            Assert.AreEqual(DFMPAccountRole.Player, DFMPAccountRoles.Resolve("stranger", null));
        }

        [Test]
        public void IsAtLeast_OrdersPlayerBelowModeratorBelowAdmin()
        {
            Assert.IsTrue(DFMPAccountRoles.IsAtLeast(DFMPAccountRole.Admin, DFMPAccountRole.Moderator));
            Assert.IsTrue(DFMPAccountRoles.IsAtLeast(DFMPAccountRole.Moderator, DFMPAccountRole.Moderator));
            Assert.IsFalse(DFMPAccountRoles.IsAtLeast(DFMPAccountRole.Player, DFMPAccountRole.Moderator));
        }

        [Test]
        public void AuthenticatedConnections_GateOnRole()
        {
            DFMPAuthenticatedConnections.Clear();
            DFMPAuthenticatedConnections.Set(1, "tony", DFMPAccountRole.Admin);
            DFMPAuthenticatedConnections.Set(2, "guest", DFMPAccountRole.Player);

            Assert.IsTrue(DFMPAuthenticatedConnections.HasRole(1, DFMPAccountRole.Admin));
            Assert.IsFalse(DFMPAuthenticatedConnections.HasRole(2, DFMPAccountRole.Moderator));
            Assert.IsFalse(DFMPAuthenticatedConnections.HasRole(3, DFMPAccountRole.Player));

            DFMPAuthenticatedConnections.Remove(1);
            Assert.IsFalse(DFMPAuthenticatedConnections.HasRole(1, DFMPAccountRole.Admin));
            DFMPAuthenticatedConnections.Clear();
        }
    }
}

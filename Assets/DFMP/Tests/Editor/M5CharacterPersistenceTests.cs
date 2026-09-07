using System.IO;
using NUnit.Framework;
using DFMP.Runtime;

namespace DFMP.Tests
{
    [TestFixture]
    public class M5CharacterPersistenceTests
    {
        string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "DFMP_M5_" + Path.GetRandomFileName());
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }

        [Test]
        public void CharacterRecord_NormalizesAndRoundTripsStructuredFields()
        {
            var original = DFMPCharacterRecord.CreateNew("account-a", "world-a", "  Alyx  ");
            original.Health = 99;
            original.MaxHealth = 10;
            original.Gold = -1;
            original.Inventory = new[] { "iron-sword" };
            original.Equipment = new[] { "leather" };

            var restored = DFMPCharacterRecord.FromJson(original.ToJson(), "account-a", "world-a");

            Assert.NotNull(restored);
            Assert.AreEqual(DFMPCharacterRecord.CurrentSchemaVersion, restored.SchemaVersion);
            Assert.AreEqual("account-a", restored.AccountId);
            Assert.AreEqual("world-a", restored.ServerWorldId);
            Assert.AreEqual("Alyx", restored.CharacterName);
            Assert.AreEqual(10, restored.Health);
            Assert.AreEqual(0, restored.Gold);
            CollectionAssert.AreEqual(new[] { "iron-sword" }, restored.Inventory);
            CollectionAssert.AreEqual(new[] { "leather" }, restored.Equipment);
        }

        [Test]
        public void CharacterRecord_MigratesMissingOptionalFieldsAndClampsValues()
        {
            const string legacyJson = "{\"SchemaVersion\":0,\"CharacterName\":\"Legacy\",\"Health\":99,\"MaxHealth\":10}";

            var migrated = DFMPCharacterRecord.FromJson(legacyJson, "account-b", "world-b");

            Assert.NotNull(migrated);
            Assert.AreEqual(DFMPCharacterRecord.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.AreEqual("account-b", migrated.AccountId);
            Assert.AreEqual("world-b", migrated.ServerWorldId);
            Assert.AreEqual(10, migrated.Health);
            Assert.NotNull(migrated.Attributes);
            Assert.NotNull(migrated.Skills);
            Assert.NotNull(migrated.Inventory);
            Assert.NotNull(migrated.Equipment);
        }

        [Test]
        public void FileCharacterStore_SavesLoadsAndSeparatesWorlds()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var character = DFMPCharacterRecord.CreateNew("account-c", "world-a", "Traveler");
            character.WorldX = 123;
            character.WorldZ = 456;
            store.Save(character);

            DFMPCharacterRecord loaded;
            Assert.IsTrue(store.TryLoad("account-c", "world-a", out loaded));
            Assert.AreEqual("Traveler", loaded.CharacterName);
            Assert.AreEqual(123, loaded.WorldX);
            Assert.AreEqual(456, loaded.WorldZ);
            Assert.IsFalse(store.TryLoad("account-c", "world-b", out loaded));
            Assert.IsFalse(File.Exists(store.GetRecordPath("account-c", "world-a") + ".tmp"));
        }

        [Test]
        public void AccountPolicy_NormalizesIdentityAndAppliesOptionalWhitelist()
        {
            string normalized;
            string reason;
            Assert.IsTrue(DFMPAccountPolicy.TryNormalize(" Steam:Player-42 ", out normalized, out reason));
            Assert.AreEqual("steam:player-42", normalized);

            var config = new DFMPServerConfig();
            config.Identity.WhitelistEnabled = true;
            config.Identity.AllowedAccountIds = new[] { "STEAM:PLAYER-42" };
            Assert.IsTrue(DFMPAccountPolicy.IsAllowed("steam:player-42", config));
            Assert.IsFalse(DFMPAccountPolicy.IsAllowed("steam:other", config));

            config.Identity.WhitelistEnabled = false;
            Assert.IsTrue(DFMPAccountPolicy.IsAllowed("steam:other", config));
        }

        [Test]
        public void JoinPolicy_ClassifiesFirstAndReturningPlayers()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var config = new DFMPServerConfig();
            config.Identity.ServerWorldId = "world-m5";

            DFMPJoinDecision firstJoin = DFMPJoinPolicy.Resolve(" Steam:New ", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.FirstJoin, firstJoin.Kind);
            Assert.AreEqual("steam:new", firstJoin.AccountId);
            Assert.IsNull(firstJoin.CharacterRecord);

            store.Save(DFMPCharacterRecord.CreateNew("steam:new", "world-m5", "Returning"));
            DFMPJoinDecision returningPlayer = DFMPJoinPolicy.Resolve("steam:new", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.ReturningPlayer, returningPlayer.Kind);
            Assert.NotNull(returningPlayer.CharacterRecord);
            Assert.AreEqual("Returning", returningPlayer.CharacterRecord.CharacterName);
        }

        [Test]
        public void JoinPolicy_RejectsInvalidAndUnapprovedPlayers()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var config = new DFMPServerConfig();
            config.Identity.WhitelistEnabled = true;
            config.Identity.AllowedAccountIds = new[] { "steam:allowed" };

            DFMPJoinDecision invalid = DFMPJoinPolicy.Resolve("bad account", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.Rejected, invalid.Kind);
            Assert.IsFalse(string.IsNullOrEmpty(invalid.Reason));

            DFMPJoinDecision unapproved = DFMPJoinPolicy.Resolve("steam:blocked", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.Rejected, unapproved.Kind);
            Assert.AreEqual("account is not whitelisted", unapproved.Reason);
        }

        [Test]
        public void ActiveAccountRegistry_PreventsSimultaneousConnectionsAndReleasesOnDisconnect()
        {
            var registry = new DFMPActiveAccountRegistry();

            Assert.IsTrue(registry.TryClaim("steam:player", 10));
            Assert.IsTrue(registry.TryClaim("steam:player", 10));
            Assert.IsFalse(registry.TryClaim("steam:player", 11));
            Assert.IsTrue(registry.IsClaimed("steam:player"));

            registry.Release(10);

            Assert.IsFalse(registry.IsClaimed("steam:player"));
            Assert.IsTrue(registry.TryClaim("steam:player", 11));
        }

        [Test]
        public void CharacterRecord_PersistsSanitizedIdentityFields()
        {
            var record = DFMPCharacterRecord.CreateNew("steam:player", "world-m5", "  Cool Guy  ");
            record.Race = 2;
            record.Gender = 1;
            record.OutfitVariant = 3;
            record.FaceVariant = 23;

            var restored = DFMPCharacterRecord.FromJson(record.ToJson(), "steam:player", "world-m5");

            Assert.AreEqual("Cool Guy", restored.CharacterName);
            Assert.AreEqual(2, restored.Race);
            Assert.AreEqual(1, restored.Gender);
            Assert.AreEqual(3, restored.OutfitVariant);
            Assert.AreEqual(23, restored.FaceVariant);
        }

        [Test]
        public void ClientJoinFlow_MapsJoinResultsToExpectedStates()
        {
            var flow = new DFMPClientJoinFlow();

            flow.MarkConnecting();
            Assert.AreEqual(DFMPClientJoinState.AwaitingIdentityResult, flow.State);

            flow.ApplyJoinResult(new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.FirstJoin });
            Assert.AreEqual(DFMPClientJoinState.FirstJoinCharacterCreation, flow.State);

            flow.ApplyJoinResult(new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.ReturningPlayer });
            Assert.AreEqual(DFMPClientJoinState.ReturningPlayerRestore, flow.State);
            Assert.IsFalse(flow.ShouldReportLocalIdentity(false));

            flow.ApplyJoinResult(new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.Rejected });
            Assert.AreEqual(DFMPClientJoinState.Rejected, flow.State);
            Assert.IsTrue(flow.ShouldReportLocalIdentity(false));
        }

        [Test]
        public void ClientJoinFlow_LoadsGameSceneOnlyAfterAcceptedStartupJoin()
        {
            Assert.IsTrue(DFMPClientJoinFlow.ShouldLoadGameScene(
                new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.FirstJoin }, 0));
            Assert.IsTrue(DFMPClientJoinFlow.ShouldLoadGameScene(
                new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.ReturningPlayer }, 0));
            Assert.IsFalse(DFMPClientJoinFlow.ShouldLoadGameScene(
                new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.Rejected }, 0));
            Assert.IsFalse(DFMPClientJoinFlow.ShouldLoadGameScene(
                new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.ReturningPlayer }, 1));
        }

        [Test]
        public void CharacterSnapshot_NormalizesAndValidatesSavedIdentity()
        {
            var record = DFMPCharacterRecord.CreateNew("steam:charlie", "world-m5", "  Charlie  ");
            record.Race = 99;
            record.Gender = 4;
            record.OutfitVariant = 99;
            record.FaceVariant = 99;

            DFMPCharacterSnapshotMessage snapshot = DFMPCharacterSnapshotProtocol.FromRecord(record);

            Assert.AreEqual("steam:charlie", snapshot.AccountId);
            Assert.AreEqual("Charlie", snapshot.CharacterName);
            Assert.AreEqual(1, snapshot.Race);
            Assert.AreEqual(0, snapshot.Gender);
            Assert.AreEqual(3, snapshot.OutfitVariant);
            Assert.AreEqual(23, snapshot.FaceVariant);
            Assert.IsTrue(DFMPCharacterSnapshotProtocol.IsValid(snapshot));

            snapshot.FaceVariant = 24;
            Assert.IsFalse(DFMPCharacterSnapshotProtocol.IsValid(snapshot));
        }

        [Test]
        public void CharacterSnapshot_IsOnlyProducedForReturningCharacters()
        {
            var firstJoin = new DFMPJoinDecision { Kind = DFMPJoinDecisionKind.FirstJoin };
            var returning = new DFMPJoinDecision
            {
                Kind = DFMPJoinDecisionKind.ReturningPlayer,
                CharacterRecord = DFMPCharacterRecord.CreateNew("steam:charlie", "world-m5", "Charlie")
            };

            Assert.IsTrue(firstJoin.Accepted);
            Assert.IsNull(firstJoin.CharacterRecord);
            Assert.IsTrue(returning.Accepted);
            Assert.NotNull(returning.CharacterRecord);
            Assert.IsTrue(DFMPCharacterSnapshotProtocol.IsValid(
                DFMPCharacterSnapshotProtocol.FromRecord(returning.CharacterRecord)));
        }

            [Test]
            public void JoinResult_DefaultsBeginnerTutorialToDisabled()
            {
                var config = new DFMPServerConfig();
                config.Normalize();

                Assert.IsFalse(config.Gameplay.EnableBeginnerTutorial);
                Assert.IsFalse(new DFMPJoinResultMessage().EnableBeginnerTutorial);
            }
    }
}

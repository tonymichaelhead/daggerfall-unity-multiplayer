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
            record.Level = 4;
            record.MaxHealth = 20;
            record.Health = 17;
            record.MaxSpellPoints = 12;
            record.SpellPoints = 8;
            record.MaxFatigue = 100;
            record.Fatigue = 75;
            record.Attributes[0] = 91;
            record.Skills[0] = 73;
            record.Gold = 456;
            record.Experience = 7890;

            DFMPCharacterSnapshotMessage snapshot = DFMPCharacterSnapshotProtocol.FromRecord(record);

            Assert.AreEqual("steam:charlie", snapshot.AccountId);
            Assert.AreEqual("Charlie", snapshot.CharacterName);
            Assert.AreEqual(8, snapshot.Race);
            Assert.AreEqual(0, snapshot.Gender);
            Assert.AreEqual(3, snapshot.OutfitVariant);
            Assert.AreEqual(9, snapshot.FaceVariant);
            Assert.AreEqual(4, snapshot.Level);
            Assert.AreEqual(17, snapshot.Health);
            Assert.AreEqual(20, snapshot.MaxHealth);
            Assert.AreEqual(8, snapshot.SpellPoints);
            Assert.AreEqual(12, snapshot.MaxSpellPoints);
            Assert.AreEqual(75, snapshot.Fatigue);
            Assert.AreEqual(100, snapshot.MaxFatigue);
            Assert.AreEqual(91, snapshot.Attributes[0]);
            Assert.AreEqual(73, snapshot.Skills[0]);
            Assert.AreEqual(456, snapshot.Gold);
            Assert.AreEqual(7890, snapshot.Experience);
            Assert.IsTrue(DFMPCharacterSnapshotProtocol.IsValid(snapshot));

            snapshot.FaceVariant = 10;
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

            [Test]
            public void ClientJoinFlow_IdentifiesMultiplayerIntroQuests()
            {
                Assert.IsTrue(DFMPClientJoinFlowController.IsMultiplayerIntroQuest("_TUTOR__"));
                Assert.IsTrue(DFMPClientJoinFlowController.IsMultiplayerIntroQuest("_BRISIEN"));
                Assert.IsFalse(DFMPClientJoinFlowController.IsMultiplayerIntroQuest("S0000001"));
            }

            [Test]
            public void CharacterPersistence_AppliesCanonicalSessionPositionAndIdentity()
            {
                var record = DFMPCharacterRecord.CreateNew("steam:player", "world-m5", "Player");
                var sessionObject = new UnityEngine.GameObject("DFMP_Test_PersistenceSession");

                try
                {
                    var session = sessionObject.AddComponent<DFMPPlayerSessionState>();
                    session.Initialize(7, 6799360, 12.5f, 9388032);
                    session.SetDisplayName("  Saved Hero  ");
                    session.SetAppearance(3, 1, 2, 6);

                    DFMPCharacterPersistence.ApplySessionState(record, session);

                    Assert.AreEqual("Saved Hero", record.CharacterName);
                    Assert.AreEqual(6799360, record.WorldX);
                    Assert.AreEqual(12.5f, record.WorldY);
                    Assert.AreEqual(9388032, record.WorldZ);
                    Assert.AreEqual(3, record.Race);
                    Assert.AreEqual(1, record.Gender);
                    Assert.AreEqual(2, record.OutfitVariant);
                    Assert.AreEqual(6, record.FaceVariant);
                    Assert.Greater(record.MapPixelX, 0);
                    Assert.Greater(record.MapPixelY, 0);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(sessionObject);
                }
            }

            [Test]
            public void CharacterPersistence_AppliesFirstJoinIdentityReport()
            {
                var record = DFMPCharacterRecord.CreateNew("steam:first", "world-m5", "Player");
                var report = new DFMPPlayerIdentityReport
                {
                    DisplayName = "Created Hero",
                    Race = 3,
                    Gender = 1,
                    FaceVariant = 11,
                    Level = 4,
                    Health = 17,
                    MaxHealth = 20,
                    SpellPoints = 8,
                    MaxSpellPoints = 12,
                    Fatigue = 75,
                    MaxFatigue = 100,
                    Gold = 456,
                    Attributes = new[] { 91, 82, 73, 64, 55, 46, 37, 28 },
                    Skills = new[] { 73, 62, 51, 40, 39, 38, 37, 36, 35, 34, 33, 32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9 }
                };

                DFMPCharacterPersistence.ApplyIdentityReport(record, report);

                Assert.AreEqual("Created Hero", record.CharacterName);
                Assert.AreEqual(3, record.Race);
                Assert.AreEqual(1, record.Gender);
                Assert.AreEqual(9, record.FaceVariant);
                Assert.AreEqual(4, record.Level);
                Assert.AreEqual(17, record.Health);
                Assert.AreEqual(456, record.Gold);
                Assert.AreEqual(91, record.Attributes[0]);
                Assert.AreEqual(73, record.Skills[0]);
            }
    }
}

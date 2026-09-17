using System;
using System.IO;
using NUnit.Framework;
using DFMP.Runtime;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPCharacterSelectTests
    {
        string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "DFMP_CharSelect_" + Path.GetRandomFileName());
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }

        [Test]
        public void FileCharacterStore_ListsSavesAndDeletesMultipleCharacters()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var first = DFMPCharacterRecord.CreateNew("steam:player", "world-a", "Alpha");
            first.Level = 2;
            first.LastPlayedUtc = "2026-01-01T00:00:00.0000000Z";
            var second = DFMPCharacterRecord.CreateNew("steam:player", "world-a", "Bravo");
            second.Level = 8;
            second.LastPlayedUtc = "2026-06-01T00:00:00.0000000Z";
            store.Save(first);
            store.Save(second);

            DFMPCharacterSummary[] listed = store.List("steam:player", "world-a");
            Assert.AreEqual(2, listed.Length);
            Assert.AreEqual("Bravo", listed[0].CharacterName);
            Assert.AreEqual("Alpha", listed[1].CharacterName);

            DFMPCharacterRecord loaded;
            Assert.IsTrue(store.TryLoadMostRecentlyPlayed("steam:player", "world-a", out loaded));
            Assert.AreEqual("Bravo", loaded.CharacterName);

            Assert.IsTrue(store.Delete("steam:player", "world-a", second.CharacterId));
            listed = store.List("steam:player", "world-a");
            Assert.AreEqual(1, listed.Length);
            Assert.AreEqual("Alpha", listed[0].CharacterName);
            Assert.IsFalse(store.TryLoad("steam:player", "world-a", second.CharacterId, out loaded));
        }

        [Test]
        public void FileCharacterStore_MigratesLegacySingleFileRecord()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            const string legacyJson = "{\"SchemaVersion\":5,\"AccountId\":\"steam:legacy\",\"ServerWorldId\":\"world-a\",\"CharacterName\":\"Old Hero\",\"Level\":11}";
            File.WriteAllText(store.GetLegacyRecordPath("steam:legacy", "world-a"), legacyJson);

            DFMPCharacterSummary[] listed = store.List("steam:legacy", "world-a");
            Assert.AreEqual(1, listed.Length);
            Assert.AreEqual("Old Hero", listed[0].CharacterName);
            Assert.IsFalse(File.Exists(store.GetLegacyRecordPath("steam:legacy", "world-a")));

            DFMPCharacterRecord loaded;
            Assert.IsTrue(store.TryLoad("steam:legacy", "world-a", listed[0].CharacterId, out loaded));
            Assert.AreEqual(11, loaded.Level);
            Assert.IsFalse(string.IsNullOrEmpty(loaded.CharacterId));
            Assert.IsFalse(string.IsNullOrEmpty(loaded.LastPlayedUtc));
        }

        [Test]
        public void FileCharacterStore_DoesNotAdoptLocalPrefixedAccountRecords()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            const string leftoverJson = "{\"SchemaVersion\":5,\"AccountId\":\"local:tony\",\"ServerWorldId\":\"default\",\"CharacterName\":\"Alens Bjarsen\",\"Level\":7}";
            File.WriteAllText(store.GetLegacyRecordPath("local:tony", "default"), leftoverJson);

            var current = DFMPCharacterRecord.CreateNew("tony", "default", "Player");
            current.Level = 1;
            store.Save(current);

            DFMPCharacterSummary[] listed = store.List("tony", "default");
            Assert.AreEqual(1, listed.Length);
            Assert.AreEqual("Player", listed[0].CharacterName);
            Assert.IsTrue(File.Exists(store.GetLegacyRecordPath("local:tony", "default")));
        }

        [Test]
        public void FileCharacterStore_KeepsWorldsIsolatedAfterMultiCharacterSave()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            store.Save(DFMPCharacterRecord.CreateNew("steam:player", "world-a", "World A"));
            store.Save(DFMPCharacterRecord.CreateNew("steam:player", "world-b", "World B"));

            Assert.AreEqual(1, store.List("steam:player", "world-a").Length);
            Assert.AreEqual("World A", store.List("steam:player", "world-a")[0].CharacterName);
            Assert.AreEqual(1, store.List("steam:player", "world-b").Length);
            Assert.AreEqual("World B", store.List("steam:player", "world-b")[0].CharacterName);
        }

        [Test]
        public void JoinPolicy_SelectUiEnabledWaitsForCharacterSelection()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var config = new DFMPServerConfig();
            config.Identity.ServerWorldId = "world-a";
            config.Identity.CharacterSelectEnabled = true;
            store.Save(DFMPCharacterRecord.CreateNew("steam:player", "world-a", "Existing"));

            DFMPJoinDecision decision = DFMPJoinPolicy.Resolve("steam:player", config, store);

            Assert.AreEqual(DFMPJoinDecisionKind.AwaitingCharacterSelection, decision.Kind);
            Assert.IsTrue(decision.Accepted);
            Assert.IsFalse(decision.IsCharacterBound);
            Assert.IsNull(decision.CharacterRecord);
        }

        [Test]
        public void JoinPolicy_SelectUiDisabledAutoJoinsMostRecentlyPlayed()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var config = new DFMPServerConfig();
            config.Identity.ServerWorldId = "world-a";
            config.Identity.CharacterSelectEnabled = false;

            DFMPJoinDecision firstJoin = DFMPJoinPolicy.Resolve("steam:player", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.FirstJoin, firstJoin.Kind);
            Assert.IsNull(firstJoin.CharacterRecord);

            var older = DFMPCharacterRecord.CreateNew("steam:player", "world-a", "Older");
            older.LastPlayedUtc = "2026-01-01T00:00:00.0000000Z";
            var newer = DFMPCharacterRecord.CreateNew("steam:player", "world-a", "Newer");
            newer.LastPlayedUtc = "2026-09-01T00:00:00.0000000Z";
            store.Save(older);
            store.Save(newer);

            DFMPJoinDecision returning = DFMPJoinPolicy.Resolve("steam:player", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.ReturningPlayer, returning.Kind);
            Assert.AreEqual("Newer", returning.CharacterRecord.CharacterName);
        }

        [Test]
        public void CharacterSelectPolicy_RejectsCreateAtCapSelectUnknownAndDeleteWhenDisabled()
        {
            var summaries = new[]
            {
                new DFMPCharacterSummary { CharacterId = Guid.NewGuid().ToString("N"), CharacterName = "Hero" }
            };

            Assert.IsNull(DFMPCharacterSelectPolicy.GetSelectRejectionReason(
                DFMPJoinDecisionKind.AwaitingCharacterSelection,
                summaries[0].CharacterId,
                summaries));
            Assert.AreEqual(
                DFMPCharacterSelectPolicy.CharacterNotFoundReason,
                DFMPCharacterSelectPolicy.GetSelectRejectionReason(
                    DFMPJoinDecisionKind.AwaitingCharacterSelection,
                    Guid.NewGuid().ToString("N"),
                    summaries));
            Assert.AreEqual(
                DFMPCharacterSelectPolicy.CharacterAlreadyBoundReason,
                DFMPCharacterSelectPolicy.GetSelectRejectionReason(
                    DFMPJoinDecisionKind.ReturningPlayer,
                    summaries[0].CharacterId,
                    summaries));

            Assert.IsNull(DFMPCharacterSelectPolicy.GetCreateRejectionReason(
                DFMPJoinDecisionKind.AwaitingCharacterSelection, 1, 4));
            Assert.AreEqual(
                DFMPCharacterSelectPolicy.CharacterLimitReachedReason,
                DFMPCharacterSelectPolicy.GetCreateRejectionReason(
                    DFMPJoinDecisionKind.AwaitingCharacterSelection, 4, 4));
            Assert.IsFalse(DFMPCharacterSelectPolicy.CanCreate(4, 4));

            Assert.AreEqual(
                DFMPCharacterSelectPolicy.DeleteDisabledReason,
                DFMPCharacterSelectPolicy.GetDeleteRejectionReason(
                    DFMPJoinDecisionKind.AwaitingCharacterSelection,
                    false,
                    summaries[0].CharacterId,
                    summaries));
            Assert.IsNull(DFMPCharacterSelectPolicy.GetDeleteRejectionReason(
                DFMPJoinDecisionKind.AwaitingCharacterSelection,
                true,
                summaries[0].CharacterId,
                summaries));
        }

        [Test]
        public void CharacterSelectPolicy_CreateRosterUsesStoreSummariesAndConfig()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var record = DFMPCharacterRecord.CreateNew("steam:player", "world-a", "Listed");
            record.Level = 5;
            store.Save(record);

            var config = new DFMPServerConfig();
            config.Identity.ServerWorldId = "world-a";
            config.Identity.MaxCharactersPerAccount = 3;
            config.Identity.AllowCharacterDelete = false;
            config.Identity.CharacterSelectEnabled = true;

            DFMPCharacterRosterMessage roster = DFMPCharacterSelectPolicy.CreateRoster(store, "steam:player", "world-a", config);

            Assert.AreEqual(1, roster.Characters.Length);
            Assert.AreEqual("Listed", roster.Characters[0].CharacterName);
            Assert.AreEqual(5, roster.Characters[0].Level);
            Assert.AreEqual(3, roster.MaxCharacters);
            Assert.IsFalse(roster.AllowCharacterDelete);
            Assert.IsTrue(roster.CharacterSelectEnabled);
        }

        [Test]
        public void FirstJoinDoesNotPersistACharacterUntilSaved()
        {
            var store = new DFMPFileCharacterStore(temporaryDirectory);
            var config = new DFMPServerConfig();
            config.Identity.ServerWorldId = "world-a";
            config.Identity.CharacterSelectEnabled = false;

            DFMPJoinDecision decision = DFMPJoinPolicy.Resolve("steam:new", config, store);
            Assert.AreEqual(DFMPJoinDecisionKind.FirstJoin, decision.Kind);
            Assert.AreEqual(0, store.List("steam:new", "world-a").Length);

            var created = DFMPCharacterRecord.CreateNew(decision.AccountId, decision.ServerWorldId, "Created Hero");
            created.MarkPlayed();
            store.Save(created);

            Assert.AreEqual(1, store.List("steam:new", "world-a").Length);
            Assert.AreEqual("Created Hero", store.List("steam:new", "world-a")[0].CharacterName);
        }
    }
}

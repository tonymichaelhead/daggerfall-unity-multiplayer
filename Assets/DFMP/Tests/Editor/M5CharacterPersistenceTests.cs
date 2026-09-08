using System.IO;
using NUnit.Framework;
using DaggerfallWorkshop.Game.Serialization;
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
            original.Inventory = new[]
            {
                new DFMPCharacterItemRecord { ItemId = 42, ItemGroup = 5, TemplateIndex = 7, StackCount = 2, ShortName = "iron-sword" }
            };
            original.Equipment = new[]
            {
                new DFMPCharacterEquipmentRecord { EquipSlot = 3, ItemId = 42 }
            };

            var restored = DFMPCharacterRecord.FromJson(original.ToJson(), "account-a", "world-a");

            Assert.NotNull(restored);
            Assert.AreEqual(DFMPCharacterRecord.CurrentSchemaVersion, restored.SchemaVersion);
            Assert.AreEqual("account-a", restored.AccountId);
            Assert.AreEqual("world-a", restored.ServerWorldId);
            Assert.AreEqual("Alyx", restored.CharacterName);
            Assert.NotNull(restored.Context);
            Assert.AreEqual("Exterior", restored.Context.Kind);
            Assert.AreEqual(10, restored.Health);
            Assert.AreEqual(0, restored.Gold);
            Assert.AreEqual(1, restored.Inventory.Length);
            Assert.AreEqual(42u, restored.Inventory[0].ItemId);
            Assert.AreEqual(2, restored.Inventory[0].StackCount);
            Assert.AreEqual("iron-sword", restored.Inventory[0].ShortName);
            Assert.AreEqual(1, restored.Equipment.Length);
            Assert.AreEqual(3, restored.Equipment[0].EquipSlot);
            Assert.AreEqual(42u, restored.Equipment[0].ItemId);
        }

        [Test]
        public void CharacterItemRecord_ConvertsItemDataWithoutLosingCoreFields()
        {
            var itemData = new ItemData_v1
            {
                uid = 42,
                itemGroup = DaggerfallWorkshop.Game.Items.ItemGroups.Weapons,
                groupIndex = 7,
                stackCount = 2,
                hits1 = 11,
                hits2 = 22,
                enchantmentPoints = 3,
                shortName = "iron sword",
                nativeMaterialValue = 4,
                currentVariant = 1,
                legacyMagic = new[] { 5, 6 }
            };

            var record = DFMPCharacterItemRecord.FromItemData(itemData);
            var restored = record.ToItemData();

            Assert.AreEqual(42u, record.ItemId);
            Assert.AreEqual(itemData.itemGroup, restored.itemGroup);
            Assert.AreEqual(7, restored.groupIndex);
            Assert.AreEqual(2, restored.stackCount);
            Assert.AreEqual(11, restored.hits1);
            Assert.AreEqual(22, restored.hits2);
            Assert.AreEqual(3, restored.enchantmentPoints);
            Assert.AreEqual("iron sword", restored.shortName);
            CollectionAssert.AreEqual(new[] { 5, 6 }, restored.legacyMagic);
        }

        [Test]
        public void CharacterItemRecord_LeavesLegacyMagicNullWhenItemIsUnenchanted()
        {
            var itemData = new ItemData_v1
            {
                uid = 42,
                itemGroup = DaggerfallWorkshop.Game.Items.ItemGroups.MensClothing,
                groupIndex = 7,
                stackCount = 1,
                legacyMagic = null
            };

            var restored = DFMPCharacterItemRecord.FromItemData(itemData).ToItemData();

            // DFU indexes legacyMagic[0] without a length check, so an empty array crashes item info panels.
            Assert.IsNull(restored.legacyMagic);
        }

        [Test]
        public void CharacterItemRecord_RoundTripsFieldsNeededForIconsAndValue()
        {
            var itemData = new ItemData_v1
            {
                uid = 42,
                itemGroup = DaggerfallWorkshop.Game.Items.ItemGroups.MensClothing,
                groupIndex = 7,
                stackCount = 1,
                weightInKg = 1.5f,
                drawOrder = 4,
                value1 = 120,
                value2 = 0x00020003,
                hits3 = 0x0102,
                isQuestItem = true,
                poisonType = DaggerfallWorkshop.Game.Items.Poisons.Moonseed,
                potionRecipe = 9,
                artifactIndexBitfield = 3,
                timeForItemToDisappear = 777u,
                timeHealthLeechLastUsed = 888u
            };

            var restored = DFMPCharacterItemRecord.FromItemData(itemData).ToItemData();

            Assert.AreEqual(1.5f, restored.weightInKg);
            Assert.AreEqual(4, restored.drawOrder);
            Assert.AreEqual(120, restored.value1);
            Assert.AreEqual(0x00020003, restored.value2);
            Assert.AreEqual(0x0102, restored.hits3);
            Assert.IsTrue(restored.isQuestItem);
            Assert.AreEqual(DaggerfallWorkshop.Game.Items.Poisons.Moonseed, restored.poisonType);
            Assert.AreEqual(9, restored.potionRecipe);
            Assert.AreEqual(3, restored.artifactIndexBitfield);
            Assert.AreEqual(777u, restored.timeForItemToDisappear);
            Assert.AreEqual(888u, restored.timeHealthLeechLastUsed);
        }

        [Test]
        public void CharacterPersistence_RoundTripsItemAndEquipmentArrays()
        {
            var itemData = new[]
            {
                new ItemData_v1 { uid = 42, itemGroup = DaggerfallWorkshop.Game.Items.ItemGroups.Weapons, groupIndex = 7, stackCount = 2 },
                new ItemData_v1 { uid = 43, itemGroup = DaggerfallWorkshop.Game.Items.ItemGroups.Armor, groupIndex = 3, stackCount = 1 }
            };
            var equipmentIds = new ulong[] { 0, 42, 0, 43 };

            var itemRecords = DFMPCharacterPersistence.CaptureItems(itemData);
            var restoredItems = DFMPCharacterPersistence.RestoreItems(itemRecords);
            var equipmentRecords = DFMPCharacterPersistence.CaptureEquipment(equipmentIds);
            var restoredEquipment = DFMPCharacterPersistence.RestoreEquipment(equipmentRecords);

            Assert.AreEqual(2, restoredItems.Length);
            Assert.AreEqual(42u, restoredItems[0].uid);
            Assert.AreEqual(2, restoredItems[0].stackCount);
            Assert.AreEqual(27, restoredEquipment.Length);
            Assert.AreEqual(42u, restoredEquipment[1]);
            Assert.AreEqual(43u, restoredEquipment[3]);
        }

        [Test]
        public void InventorySnapshotCodec_RoundTripsAndRejectsOversizedPayloads()
        {
            var inventory = new[]
            {
                new DFMPCharacterItemRecord { ItemId = 42, ItemGroup = 5, TemplateIndex = 7, StackCount = 2 }
            };
            var equipment = new[]
            {
                new DFMPCharacterEquipmentRecord { EquipSlot = 3, ItemId = 42 }
            };

            string json = DFMPInventorySnapshotCodec.Encode(inventory, equipment);
            DFMPCharacterItemRecord[] decodedInventory;
            DFMPCharacterEquipmentRecord[] decodedEquipment;
            string reason;

            Assert.IsTrue(DFMPInventorySnapshotCodec.TryDecode(json, out decodedInventory, out decodedEquipment, out reason));
            Assert.AreEqual(42u, decodedInventory[0].ItemId);
            Assert.AreEqual(3, decodedEquipment[0].EquipSlot);

            Assert.IsFalse(DFMPInventorySnapshotCodec.TryDecode(new string('x', DFMPInventorySnapshotCodec.MaximumJsonLength + 1), out decodedInventory, out decodedEquipment, out reason));
            Assert.IsTrue(reason.Contains("maximum size"));
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
            Assert.NotNull(migrated.Context);
            Assert.AreEqual("Exterior", migrated.Context.Kind);
        }

        [Test]
        public void CharacterRecord_MigratesLegacyWorldContextFields()
        {
            const string legacyJson = "{\"SchemaVersion\":3,\"CharacterName\":\"Legacy\",\"WorldContext\":\"Dungeon\",\"MapPixelX\":207,\"MapPixelY\":213}";

            var migrated = DFMPCharacterRecord.FromJson(legacyJson, "account-context", "world-b");

            Assert.NotNull(migrated.Context);
            Assert.AreEqual(DFMPCharacterRecord.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.AreEqual("Dungeon", migrated.WorldContext);
            Assert.AreEqual("Dungeon", migrated.Context.Kind);
            Assert.AreEqual(207, migrated.Context.MapPixelX);
            Assert.AreEqual(213, migrated.Context.MapPixelY);
        }

        [Test]
        public void CharacterRecord_RoundTripsStructuredWorldContext()
        {
            var original = DFMPCharacterRecord.CreateNew("account-context", "world-b", "Context Tester");
            original.Context = new DFMPWorldContextRecord
            {
                Kind = "BuildingInterior",
                MapPixelX = 207,
                MapPixelY = 213,
                RegionIndex = 3,
                LocationIndex = 41,
                LocationId = "Daggerfall",
                BuildingKey = 12345,
                InstanceId = "shared"
            };

            var restored = DFMPCharacterRecord.FromJson(original.ToJson(), "account-context", "world-b");

            Assert.NotNull(restored.Context);
            Assert.AreEqual("BuildingInterior", restored.WorldContext);
            Assert.AreEqual("BuildingInterior", restored.Context.Kind);
            Assert.AreEqual(207, restored.MapPixelX);
            Assert.AreEqual(213, restored.MapPixelY);
            Assert.AreEqual(3, restored.Context.RegionIndex);
            Assert.AreEqual(41, restored.Context.LocationIndex);
            Assert.AreEqual("Daggerfall", restored.Context.LocationId);
            Assert.AreEqual(12345, restored.Context.BuildingKey);
            Assert.AreEqual("shared", restored.Context.InstanceId);
        }

        [Test]
        public void CharacterRecord_DropsSchemaV1ExperienceAndDefersSkillSumToClient()
        {
            const string schemaV1Json = "{\"SchemaVersion\":1,\"CharacterName\":\"Veteran\",\"Level\":7," +
                "\"Health\":37,\"MaxHealth\":80,\"Experience\":7890,\"Gold\":456}";

            var migrated = DFMPCharacterRecord.FromJson(schemaV1Json, "account-v1", "world-b");

            Assert.AreEqual(DFMPCharacterRecord.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.AreEqual(7, migrated.Level);
            Assert.AreEqual(456, migrated.Gold);
            // Zero signals the client to re-estimate rather than trusting a stale sum.
            Assert.AreEqual(0, migrated.StartingLevelUpSkillSum);
            Assert.AreEqual(string.Empty, migrated.CareerJson);
            Assert.IsFalse(migrated.ToJson().Contains("Experience"));
        }

        [Test]
        public void CareerCodec_RoundTripsCustomClassAndRejectsBadPayloads()
        {
            var custom = new DaggerfallConnect.DFCareer
            {
                Name = "Bladesinger",
                HitPointsPerLevel = 18,
                SpellPointMultiplierValue = 1.5f,
                AdvancementMultiplier = 0.4f,
                PrimarySkill1 = DaggerfallConnect.DFCareer.Skills.LongBlade,
                MajorSkill1 = DaggerfallConnect.DFCareer.Skills.Destruction,
                MinorSkill1 = DaggerfallConnect.DFCareer.Skills.Stealth,
                SpellAbsorption = DaggerfallConnect.DFCareer.SpellAbsorptionFlags.InDarkness,
                AcuteHearing = true
            };

            DaggerfallConnect.DFCareer decoded;
            string reason;
            Assert.IsTrue(DFMPCareerCodec.TryDecode(DFMPCareerCodec.Encode(custom), out decoded, out reason));

            Assert.AreEqual("Bladesinger", decoded.Name);
            Assert.AreEqual(18, decoded.HitPointsPerLevel);
            Assert.AreEqual(1.5f, decoded.SpellPointMultiplierValue);
            Assert.AreEqual(DaggerfallConnect.DFCareer.Skills.LongBlade, decoded.PrimarySkill1);
            Assert.AreEqual(DaggerfallConnect.DFCareer.Skills.Destruction, decoded.MajorSkill1);
            Assert.AreEqual(DaggerfallConnect.DFCareer.Skills.Stealth, decoded.MinorSkill1);
            Assert.AreEqual(DaggerfallConnect.DFCareer.SpellAbsorptionFlags.InDarkness, decoded.SpellAbsorption);
            Assert.IsTrue(decoded.AcuteHearing);

            Assert.IsFalse(DFMPCareerCodec.TryDecode(string.Empty, out decoded, out reason));
            Assert.IsFalse(DFMPCareerCodec.TryDecode(new string('x', DFMPCareerCodec.MaximumJsonLength + 1), out decoded, out reason));
            Assert.IsTrue(reason.Contains("maximum size"));

            // A nameless career would silently replace the class with a blank one.
            Assert.IsFalse(DFMPCareerCodec.TryDecode("{\"Name\":\"\"}", out decoded, out reason));
        }

        [Test]
        public void CareerCodec_ClampsImplausibleClassPower()
        {
            var exploit = new DaggerfallConnect.DFCareer
            {
                Name = "Exploit",
                SpellPointMultiplierValue = 9999f,
                HitPointsPerLevel = 9999
            };

            DaggerfallConnect.DFCareer decoded;
            string reason;
            Assert.IsTrue(DFMPCareerCodec.TryDecode(DFMPCareerCodec.Encode(exploit), out decoded, out reason));

            Assert.AreEqual(DFMPCareerCodec.MaximumSpellPointMultiplier, decoded.SpellPointMultiplierValue);
            Assert.AreEqual(DFMPCareerCodec.MaximumHitPointsPerLevel, decoded.HitPointsPerLevel);
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
        public void InventorySignature_ChangesWhenEquipmentOrItemsChange()
        {
            var equipTable = new ulong[27];
            int baseline = DFMPPositionReporter.GetInventorySignature(10, 456, equipTable);

            Assert.AreEqual(baseline, DFMPPositionReporter.GetInventorySignature(10, 456, new ulong[27]));

            equipTable[19] = 33554442;
            Assert.AreNotEqual(baseline, DFMPPositionReporter.GetInventorySignature(10, 456, equipTable));
            Assert.AreNotEqual(baseline, DFMPPositionReporter.GetInventorySignature(11, 456, new ulong[27]));
            Assert.AreNotEqual(baseline, DFMPPositionReporter.GetInventorySignature(10, 457, new ulong[27]));
        }

        [Test]
        public void ClientJoinFlow_MapsJoinResultsToExpectedStates()
        {
            var flow = new DFMPClientJoinFlow();

            flow.MarkConnecting();
            Assert.AreEqual(DFMPClientJoinState.AwaitingIdentityResult, flow.State);

            flow.ApplyJoinResult(new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.FirstJoin });
            Assert.AreEqual(DFMPClientJoinState.FirstJoinCharacterCreation, flow.State);
            Assert.IsTrue(flow.ShouldReportLocalIdentity());

            flow.ApplyJoinResult(new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.ReturningPlayer });
            Assert.AreEqual(DFMPClientJoinState.ReturningPlayerRestore, flow.State);
            Assert.IsFalse(flow.ShouldReportLocalIdentity());

            flow.MarkInGame();
            Assert.IsTrue(flow.ShouldReportLocalIdentity());

            flow.ApplyJoinResult(new DFMPJoinResultMessage { Decision = DFMPJoinDecisionKind.Rejected });
            Assert.AreEqual(DFMPClientJoinState.Rejected, flow.State);
            Assert.IsFalse(flow.ShouldReportLocalIdentity());
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
            record.StartingLevelUpSkillSum = 145;
            record.CareerJson = DFMPCareerCodec.Encode(new DaggerfallConnect.DFCareer { Name = "Bladesinger" });

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
            Assert.AreEqual(145, snapshot.StartingLevelUpSkillSum);
            Assert.IsTrue(snapshot.CareerJson.Contains("Bladesinger"));
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
                    StartingLevelUpSkillSum = 145,
                    CareerJson = DFMPCareerCodec.Encode(new DaggerfallConnect.DFCareer { Name = "Bladesinger", HitPointsPerLevel = 18 }),
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
                Assert.AreEqual(145, record.StartingLevelUpSkillSum);
                Assert.IsTrue(record.CareerJson.Contains("Bladesinger"));
                Assert.AreEqual(91, record.Attributes[0]);
                Assert.AreEqual(73, record.Skills[0]);
            }
    }
}

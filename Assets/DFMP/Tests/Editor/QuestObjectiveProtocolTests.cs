using NUnit.Framework;
using UnityEngine;
using DFMP.Runtime;

namespace DFMP.Tests
{
    [TestFixture]
    public class QuestObjectiveProtocolTests
    {
        static readonly DFMPWorldContextKey Context = new DFMPWorldContextKey
        {
            Kind = DFMPWorldContextKind.Dungeon,
            RegionIndex = 1,
            LocationIndex = 2,
            LocationId = "quest-dungeon",
            InstanceId = "shared"
        };

        [Test]
        public void Register_DerivesStableOwnerScopedIdentity_AndRejectsReplay()
        {
            var service = new DFMPQuestObjectiveService();
            DFMPQuestObjectiveRecord objective;
            DFMPQuestObjectiveRegistrationMessage request = CreateRequest(1);

            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                service.Register("character-a", 10, "Alice", Context, request, new DFMPServerQuestConfig(), out objective));
            Assert.IsNotNull(objective);
            Assert.AreEqual(
                DFMPQuestObjectiveIdentity.Create("character-a", request.QuestUid, request.FoeSymbol, request.ActionGeneration),
                objective.ObjectiveId);

            DFMPQuestObjectiveRecord duplicate;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.AlreadyRegistered,
                service.Register("character-a", 10, "Alice", Context, request, new DFMPServerQuestConfig(), out duplicate));
            Assert.AreEqual(objective.ObjectiveId, duplicate.ObjectiveId);
        }

        [Test]
        public void Register_RejectsWrongContextAndInvalidBounds()
        {
            var service = new DFMPQuestObjectiveService();
            DFMPQuestObjectiveRecord objective;
            DFMPQuestObjectiveRegistrationMessage request = CreateRequest(1);
            request.Context = new DFMPWorldContextKey { Kind = DFMPWorldContextKind.Exterior };

            Assert.AreEqual(
                DFMPQuestObjectiveResult.InvalidContext,
                service.Register("character-a", 10, "Alice", Context, request, new DFMPServerQuestConfig(), out objective));

            request = CreateRequest(2);
            request.SpawnCount = 9;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.InvalidDescriptor,
                service.Register("character-a", 10, "Alice", Context, request, new DFMPServerQuestConfig(), out objective));
        }

        [Test]
        public void Proximity_UsesActivationHysteresisAndGrace()
        {
            var service = new DFMPQuestObjectiveService();
            var config = new DFMPServerQuestConfig
            {
                EnemyActivationRadius = 30f,
                EnemyDespawnRadius = 45f,
                EnemyDespawnGraceSeconds = 10f
            };
            DFMPQuestObjectiveRecord objective;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                service.Register("character-a", 10, "Alice", Context, CreateRequest(1), config, out objective));

            Assert.IsTrue(service.ShouldSpawn(objective, Context, new Vector3(29f, 0f, 0f), config));
            Assert.IsFalse(service.ShouldSpawn(objective, Context, new Vector3(31f, 0f, 0f), config));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.MarkSpawned(objective.ObjectiveId));

            Assert.IsFalse(service.ShouldBeginDespawn(objective, Context, new Vector3(46f, 0f, 0f), 100f, config));
            Assert.IsFalse(service.ShouldBeginDespawn(objective, Context, new Vector3(46f, 0f, 0f), 109.9f, config));
            Assert.IsTrue(service.ShouldBeginDespawn(objective, Context, new Vector3(46f, 0f, 0f), 110f, config));

            Assert.IsFalse(service.ShouldBeginDespawn(objective, Context, Vector3.zero, 111f, config));
            Assert.AreEqual(-1f, objective.OwnerOutsideSince);
        }

        [Test]
        public void KillCredit_IsOwnerAcknowledgedAndIdempotent()
        {
            var service = new DFMPQuestObjectiveService();
            DFMPQuestObjectiveRecord objective;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                service.Register("character-a", 10, "Alice", Context, CreateRequest(1), new DFMPServerQuestConfig(), out objective));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.MarkSpawned(objective.ObjectiveId));

            DFMPQuestObjectiveResultMessage result;
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.MarkKilled(objective.ObjectiveId, 1, out result));
            Assert.AreNotEqual(0ul, result.ResultId);
            Assert.AreEqual(DFMPQuestObjectiveResult.InvalidOwner, service.AcknowledgeResult(objective.ObjectiveId, 11, result.ResultId));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.AcknowledgeResult(objective.ObjectiveId, 10, result.ResultId));
            Assert.AreEqual(DFMPQuestObjectiveResult.InvalidTransition, service.AcknowledgeResult(objective.ObjectiveId, 10, result.ResultId));
        }

        [Test]
        public void Reentry_PreservesGenerationAndHealth()
        {
            var service = new DFMPQuestObjectiveService();
            var registry = new DFMPDynamicEnemyRegistry();
            var provider = new DFMPQuestEnemyProvider(registry);
            DFMPQuestObjectiveRecord objective;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                service.Register("character-a", 10, "Alice", Context, CreateRequest(1), new DFMPServerQuestConfig(), out objective));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.MarkSpawned(objective.ObjectiveId));
            DFMPDynamicEnemyRecord record;
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, provider.Spawn(objective, out record));
            int generation = objective.EncounterGeneration;
            int health = record.Health;

            registry.TryApplyDamage(true, record.Identity.EnemyId, 3, out record, out _, out _);
            service.PreservePhysicalState(objective.ObjectiveId, record.Health, record.MaxHealth);
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, provider.DespawnAlive(objective.ObjectiveId));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.MarkDespawnedAlive(objective.ObjectiveId));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, service.MarkSpawned(objective.ObjectiveId));
            Assert.AreEqual(generation, objective.EncounterGeneration);
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, provider.Spawn(objective, out record));
            Assert.AreEqual(health - 3, record.Health);
        }

        [Test]
        public void HiddenObjective_DoesNotActivate()
        {
            var service = new DFMPQuestObjectiveService();
            var config = new DFMPServerQuestConfig();
            DFMPQuestObjectiveRecord objective;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                service.Register("character-a", 10, "Alice", Context, CreateRequest(1), config, out objective));
            objective.Hidden = true;
            Assert.IsFalse(service.ShouldSpawn(objective, Context, Vector3.zero, config));
        }

        [Test]
        public void QuestTeleport_RejectsInvalidMarkerBounds()
        {
            string reason;
            Assert.IsFalse(DFMPQuestObjectiveService.TryValidateTeleport("Daggerfall", "Privateer's Hold", 99, Vector3.zero, out reason));
            Assert.IsTrue(reason.Contains("marker"));
            Assert.IsTrue(DFMPQuestObjectiveService.TryValidateTeleport("Daggerfall", "Privateer's Hold", 0, Vector3.one, out reason));
        }

        [Test]
        public void QuestEnemyProvider_UsesDistinctOwnerScopedEnemyIdentity()
        {
            var objectiveService = new DFMPQuestObjectiveService();
            DFMPQuestObjectiveRecord objective;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                objectiveService.Register("character-a", 10, "Alice", Context, CreateRequest(1), new DFMPServerQuestConfig(), out objective));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, objectiveService.MarkSpawned(objective.ObjectiveId));

            var registry = new DFMPDynamicEnemyRegistry();
            var provider = new DFMPQuestEnemyProvider(registry);
            DFMPDynamicEnemyRecord record;
            Assert.AreEqual(DFMPDynamicEnemyRegistryResult.Accepted, provider.Spawn(objective, out record));
            Assert.AreEqual(DFMPDynamicEnemyProviderKind.Quest, record.Identity.Encounter.ProviderKind);
            Assert.AreEqual(objective.ObjectiveId, record.QuestObjectiveId);
            Assert.AreEqual(10, record.QuestOwnerConnectionId);
            Assert.AreEqual("Alice", record.QuestOwnerDisplayName);
            Assert.AreNotEqual(objective.ObjectiveId, record.Identity.EnemyId);
        }

        [Test]
        public void ObjectivePersistence_RebindsOwnerAndPreservesPendingCredit()
        {
            var first = new DFMPQuestObjectiveService();
            DFMPQuestObjectiveRecord objective;
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                first.Register("character-a", 10, "Alice", Context, CreateRequest(1), new DFMPServerQuestConfig(), out objective));
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, first.MarkSpawned(objective.ObjectiveId));
            DFMPQuestObjectiveResultMessage result;
            Assert.AreEqual(DFMPQuestObjectiveResult.Accepted, first.MarkKilled(objective.ObjectiveId, 1, out result));

            var restored = new DFMPQuestObjectiveService();
            Assert.AreEqual(
                1,
                restored.RestoreForCharacter("character-a", 22, "Alice Reconnected", first.GetForCharacter("character-a"), 64));

            DFMPQuestObjectiveRecord restoredObjective;
            Assert.IsTrue(restored.TryGet(objective.ObjectiveId, out restoredObjective));
            Assert.AreEqual(22, restoredObjective.OwnerConnectionId);
            Assert.AreEqual("Alice Reconnected", restoredObjective.OwnerDisplayName);
            Assert.AreEqual(DFMPQuestObjectiveLifecycle.CreditPending, restoredObjective.Lifecycle);
            Assert.AreEqual(result.ResultId, restoredObjective.PendingResultId);
            Assert.AreEqual(
                DFMPQuestObjectiveResult.Accepted,
                restored.AcknowledgeResult(restoredObjective.ObjectiveId, 22, result.ResultId));
        }

        static DFMPQuestObjectiveRegistrationMessage CreateRequest(ulong requestId)
        {
            return new DFMPQuestObjectiveRegistrationMessage
            {
                RequestId = requestId,
                QuestUid = 42,
                FoeSymbol = "target_foe",
                ActionGeneration = 0,
                ObjectiveKind = (int)DFMPQuestObjectiveKind.MarkerBound,
                Context = Context,
                ObjectivePosition = Vector3.zero,
                MobileType = 0,
                SpawnCount = 1
            };
        }
    }
}

using DFMP.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPCombatProtocolTests
    {
        [Test]
        public void DamageIntent_AcceptsBoundedWireValues()
        {
            var intent = new DFMPDamageIntent
            {
                RequestId = 1,
                Sequence = 1,
                SourceKind = DFMPDamageSourceKind.LocalQuestPve,
                VitalKind = DFMPVitalKind.Health,
                TargetConnectionId = 7,
                Amount = 10
            };

            Assert.IsTrue(DFMPCombatProtocol.IsValidDamageIntent(intent));
        }

        [Test]
        public void TargetSelection_PicksClosestAlignedRemotePlayer()
        {
            int connectionId;
            bool selected = DFMPCombatProtocol.TrySelectTarget(
                Vector3.zero,
                Vector3.forward,
                new[]
                {
                    new DFMPCombatTargetCandidate { ConnectionId = 8, ScenePosition = new Vector3(0f, 0f, 8f) },
                    new DFMPCombatTargetCandidate { ConnectionId = 9, ScenePosition = new Vector3(0f, 0f, 4f) }
                },
                out connectionId);

            Assert.IsTrue(selected);
            Assert.AreEqual(9, connectionId);
        }

        [Test]
        public void TargetSelection_RejectsOutOfConeAndOutOfRangePlayers()
        {
            int connectionId;
            Assert.IsFalse(DFMPCombatProtocol.TrySelectTarget(
                Vector3.zero,
                Vector3.forward,
                new[] { new DFMPCombatTargetCandidate { ConnectionId = 8, ScenePosition = Vector3.right * 4f } },
                out connectionId));

            Assert.IsFalse(DFMPCombatProtocol.TrySelectTarget(
                Vector3.zero,
                Vector3.forward,
                new[] { new DFMPCombatTargetCandidate { ConnectionId = 8, ScenePosition = Vector3.forward * 5f } },
                out connectionId));
        }

        [Test]
        public void DamageIntent_RejectsMissingIdentityAndInvalidEnums()
        {
            var intent = new DFMPDamageIntent
            {
                RequestId = 0,
                Sequence = 1,
                SourceKind = (DFMPDamageSourceKind)99,
                VitalKind = DFMPVitalKind.Health,
                Amount = 10
            };

            Assert.IsFalse(DFMPCombatProtocol.IsValidDamageIntent(intent));

            intent.RequestId = 1;
            intent.SourceKind = DFMPDamageSourceKind.LocalQuestPve;
            intent.VitalKind = (DFMPVitalKind)99;
            Assert.IsFalse(DFMPCombatProtocol.IsValidDamageIntent(intent));

            intent.VitalKind = DFMPVitalKind.Health;
            intent.AttackKind = (DFMPCombatAttackKind)99;
            Assert.IsFalse(DFMPCombatProtocol.IsValidDamageIntent(intent));
        }

        [Test]
        public void DamageIntent_RejectsNonPositiveAmount()
        {
            var intent = new DFMPDamageIntent
            {
                RequestId = 1,
                Sequence = 1,
                SourceKind = DFMPDamageSourceKind.Player,
                VitalKind = DFMPVitalKind.Health,
                Amount = 0
            };

            Assert.IsFalse(DFMPCombatProtocol.IsValidDamageIntent(intent));
        }

        [Test]
        public void DamageIntent_RequiresExactlyOneTargetKind()
        {
            var intent = new DFMPDamageIntent
            {
                RequestId = 1,
                Sequence = 1,
                SourceKind = DFMPDamageSourceKind.Player,
                VitalKind = DFMPVitalKind.Health,
                AttackKind = DFMPCombatAttackKind.Melee,
                Amount = 5
            };

            Assert.IsFalse(DFMPCombatProtocol.IsValidDamageIntent(intent));

            intent.TargetConnectionId = 7;
            intent.TargetEnemyId = "enemy-1";
            Assert.IsFalse(DFMPCombatProtocol.IsValidDamageIntent(intent));

            intent.TargetConnectionId = 0;
            Assert.IsTrue(DFMPCombatProtocol.IsValidDamageIntent(intent));
        }

        [Test]
        public void DamageIntent_PlayerRangedHitUsesHealthVital()
        {
            var intent = new DFMPDamageIntent
            {
                RequestId = 1,
                Sequence = 1,
                SourceKind = DFMPDamageSourceKind.Player,
                VitalKind = DFMPVitalKind.Health,
                TargetConnectionId = 7,
                Amount = 5
            };

            Assert.IsTrue(DFMPCombatProtocol.IsValidDamageIntent(intent));
        }

        [Test]
        public void VitalSnapshot_AcceptsBoundedLiveAndDeadValues()
        {
            Assert.IsTrue(DFMPCombatProtocol.IsValidVitalSnapshot(new DFMPVitalSnapshot
            {
                ConnectionId = 7,
                Health = 20,
                MaxHealth = 20,
                Fatigue = 50,
                MaxFatigue = 100,
                SpellPoints = 10,
                MaxSpellPoints = 30
            }));

            Assert.IsTrue(DFMPCombatProtocol.IsValidVitalSnapshot(new DFMPVitalSnapshot
            {
                ConnectionId = 7,
                MaxHealth = 20,
                MaxFatigue = 100,
                MaxSpellPoints = 30,
                IsDead = true
            }));
        }

        [Test]
        public void VitalSnapshot_RejectsOutOfBoundsValues()
        {
            Assert.IsFalse(DFMPCombatProtocol.IsValidVitalSnapshot(new DFMPVitalSnapshot
            {
                ConnectionId = 7,
                Health = 21,
                MaxHealth = 20,
                MaxFatigue = 100,
                MaxSpellPoints = 30
            }));

            Assert.IsFalse(DFMPCombatProtocol.IsValidVitalSnapshot(new DFMPVitalSnapshot
            {
                ConnectionId = 7,
                Health = 1,
                MaxHealth = 20,
                MaxFatigue = 100,
                MaxSpellPoints = 30,
                IsDead = true
            }));
        }

        [Test]
        public void ServerCombatConfig_DefaultsToSafeBoundedValues()
        {
            var combat = new DFMPServerCombatConfig();

            Assert.IsFalse(combat.PvpEnabled);
            Assert.AreEqual(100, combat.MaximumDamagePerHit);
            Assert.AreEqual(800, combat.MaximumRangedPvpRange);
            Assert.AreEqual(0.1f, combat.DamageCooldownSeconds);
            Assert.AreEqual(1.0f, combat.DamageRateWindowSeconds);
            Assert.AreEqual(10, combat.MaximumDamageRequestsPerWindow);
        }

        [Test]
        public void ServerConfig_NormalizesInvalidCombatValues()
        {
            var config = new DFMPServerConfig
            {
                Combat = new DFMPServerCombatConfig
                {
                    PvpEnabled = true,
                    MaximumDamagePerHit = -1,
                    MaximumRangedPvpRange = 159,
                    DamageCooldownSeconds = 0f,
                    DamageRateWindowSeconds = 301f,
                    MaximumDamageRequestsPerWindow = 1001
                }
            };

            config.Normalize();

            Assert.IsTrue(config.Combat.PvpEnabled);
            Assert.AreEqual(100, config.Combat.MaximumDamagePerHit);
            Assert.AreEqual(800, config.Combat.MaximumRangedPvpRange);
            Assert.AreEqual(0.1f, config.Combat.DamageCooldownSeconds);
            Assert.AreEqual(1.0f, config.Combat.DamageRateWindowSeconds);
            Assert.AreEqual(10, config.Combat.MaximumDamageRequestsPerWindow);
        }

        [Test]
        public void VitalApplication_LethalDamageProducesDamageThenDeathData()
        {
            var state = new DFMPVitalState { Health = 5, MaxHealth = 20 };

            DFMPVitalApplicationResult result = state.ApplyDamage(DFMPVitalKind.Health, 5);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(5, result.AppliedAmount);
            Assert.AreEqual(0, result.CurrentValue);
            Assert.IsTrue(result.Killed);
        }

        [Test]
        public void EventBus_PublishesM7CombatLifecycleEvents()
        {
            int damagedCount = 0;
            int diedCount = 0;
            int respawnedCount = 0;
            DFMPPlayerDamagedEvent damaged = null;
            DFMPPlayerDiedEvent died = null;
            DFMPPlayerRespawnedEvent respawned = null;

            System.Action<DFMPPlayerDamagedEvent> damagedHandler = value => { damagedCount++; damaged = value; };
            System.Action<DFMPPlayerDiedEvent> diedHandler = value => { diedCount++; died = value; };
            System.Action<DFMPPlayerRespawnedEvent> respawnedHandler = value => { respawnedCount++; respawned = value; };
            DFMPEventBus.Instance.PlayerDamaged += damagedHandler;
            DFMPEventBus.Instance.PlayerDied += diedHandler;
            DFMPEventBus.Instance.PlayerRespawned += respawnedHandler;

            try
            {
                DFMPEventBus.Instance.PublishPlayerDamaged(new DFMPPlayerDamagedEvent
                {
                    SourceConnectionId = 1,
                    TargetConnectionId = 2,
                    SourceKind = DFMPDamageSourceKind.Player,
                    VitalKind = DFMPVitalKind.Health,
                    RequestedAmount = 5,
                    AppliedAmount = 5,
                    CurrentValue = 0,
                    MaximumValue = 20
                });
                DFMPEventBus.Instance.PublishPlayerDied(new DFMPPlayerDiedEvent
                {
                    ConnectionId = 2,
                    SourceKind = DFMPDamageSourceKind.Player,
                    VitalKind = DFMPVitalKind.Health,
                    AppliedAmount = 5
                });
                DFMPEventBus.Instance.PublishPlayerRespawned(new DFMPPlayerRespawnedEvent
                {
                    ConnectionId = 2,
                    Reason = "death-respawn"
                });

                Assert.AreEqual(1, damagedCount);
                Assert.AreEqual(1, diedCount);
                Assert.AreEqual(1, respawnedCount);
                Assert.AreEqual(2, damaged.TargetConnectionId);
                Assert.AreEqual(0, damaged.CurrentValue);
                Assert.AreEqual(2, died.ConnectionId);
                Assert.AreEqual("death-respawn", respawned.Reason);
            }
            finally
            {
                DFMPEventBus.Instance.PlayerDamaged -= damagedHandler;
                DFMPEventBus.Instance.PlayerDied -= diedHandler;
                DFMPEventBus.Instance.PlayerRespawned -= respawnedHandler;
            }
        }
    }
}

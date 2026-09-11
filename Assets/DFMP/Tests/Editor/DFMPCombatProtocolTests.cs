using DFMP.Runtime;
using NUnit.Framework;

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
                    DamageCooldownSeconds = 0f,
                    DamageRateWindowSeconds = 301f,
                    MaximumDamageRequestsPerWindow = 1001
                }
            };

            config.Normalize();

            Assert.IsTrue(config.Combat.PvpEnabled);
            Assert.AreEqual(100, config.Combat.MaximumDamagePerHit);
            Assert.AreEqual(0.1f, config.Combat.DamageCooldownSeconds);
            Assert.AreEqual(1.0f, config.Combat.DamageRateWindowSeconds);
            Assert.AreEqual(10, config.Combat.MaximumDamageRequestsPerWindow);
        }
    }
}
using System;
using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPDamageIntent : NetworkMessage
    {
        public ulong RequestId;
        public uint Sequence;
        public DFMPDamageSourceKind SourceKind;
        public DFMPVitalKind VitalKind;
        public int TargetConnectionId;
        public int Amount;
    }

    public struct DFMPVitalSnapshot : NetworkMessage
    {
        public int ConnectionId;
        public int Health;
        public int MaxHealth;
        public int Fatigue;
        public int MaxFatigue;
        public int SpellPoints;
        public int MaxSpellPoints;
        public bool IsDead;
    }

    public static class DFMPCombatProtocol
    {
        public const float MaximumPvpRange = 1024f;

        public static bool IsValidDamageIntent(DFMPDamageIntent intent)
        {
            return intent.RequestId != 0 &&
                intent.Sequence != 0 &&
                Enum.IsDefined(typeof(DFMPDamageSourceKind), intent.SourceKind) &&
                Enum.IsDefined(typeof(DFMPVitalKind), intent.VitalKind) &&
                intent.Amount > 0;
        }

        public static bool IsValidVitalSnapshot(DFMPVitalSnapshot snapshot)
        {
            return snapshot.ConnectionId > 0 &&
                snapshot.MaxHealth >= 1 &&
                snapshot.Health >= 0 && snapshot.Health <= snapshot.MaxHealth &&
                snapshot.MaxFatigue >= 1 &&
                snapshot.Fatigue >= 0 && snapshot.Fatigue <= snapshot.MaxFatigue &&
                snapshot.MaxSpellPoints >= 0 &&
                snapshot.SpellPoints >= 0 && snapshot.SpellPoints <= snapshot.MaxSpellPoints &&
                (!snapshot.IsDead || snapshot.Health == 0);
        }
    }
}

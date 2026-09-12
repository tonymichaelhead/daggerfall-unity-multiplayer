using System;
using Mirror;
using UnityEngine;

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

    public struct DFMPCombatTargetCandidate
    {
        public int ConnectionId;
        public Vector3 ScenePosition;
    }

    public static class DFMPCombatProtocol
    {
        // DFU maps 40 native world units to one Unity scene unit. Keep the client
        // target cone and server authority range aligned at roughly four scene units.
        public const float MaximumPvpRange = 160f;
        public const float MaximumClientTargetDistance = 4f;
        public const float MinimumTargetAlignment = 0.5f;

        public static bool TrySelectTarget(
            Vector3 attackerPosition,
            Vector3 aimDirection,
            DFMPCombatTargetCandidate[] candidates,
            out int connectionId)
        {
            connectionId = -1;
            if (candidates == null || candidates.Length == 0 || aimDirection.sqrMagnitude < 0.0001f)
                return false;

            Vector3 normalizedAim = aimDirection.normalized;
            float closestDistance = float.MaxValue;
            for (int index = 0; index < candidates.Length; index++)
            {
                Vector3 offset = candidates[index].ScenePosition - attackerPosition;
                float distance = offset.magnitude;
                if (distance <= 0.0001f || distance > MaximumClientTargetDistance)
                    continue;

                float alignment = Vector3.Dot(normalizedAim, offset / distance);
                if (alignment < MinimumTargetAlignment || distance >= closestDistance)
                    continue;

                closestDistance = distance;
                connectionId = candidates[index].ConnectionId;
            }

            return connectionId >= 0;
        }

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

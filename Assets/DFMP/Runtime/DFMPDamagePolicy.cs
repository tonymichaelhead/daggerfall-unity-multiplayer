using System;

namespace DFMP.Runtime
{
    public enum DFMPDamageSourceKind
    {
        LocalQuestPve,
        Player,
        NativeEnemy,
        Environmental,
        Developer
    }

    public enum DFMPVitalKind
    {
        Health,
        Fatigue,
        SpellPoints
    }

    public enum DFMPDamageRejectionReason
    {
        None,
        MissingSession,
        SpawnNotConfirmed,
        TargetMissing,
        TargetDead,
        TransitionPending,
        InvalidRequestId,
        InvalidSequence,
        InvalidSource,
        InvalidVital,
        InvalidAmount,
        ForgedSource,
        DuplicateRequest,
        RateLimited,
        LocalQuestTargetMismatch,
        PvpDisabled,
        SelfTarget,
        ContextMismatch,
        OutOfRange
    }

    public struct DFMPDamageValidationRequest
    {
        public ulong RequestId;
        public uint Sequence;
        public DFMPDamageSourceKind SourceKind;
        public DFMPVitalKind VitalKind;
        public int SourceConnectionId;
        public int TargetConnectionId;
        public string TargetEnemyId;
        public int Amount;
    }

    public struct DFMPDamageValidationContext
    {
        public bool HasSession;
        public bool SpawnConfirmed;
        public bool TargetExists;
        public bool TargetIsDead;
        public bool HasPendingTransition;
        public bool SameWorldContext;
        public bool InRange;
        public bool PvpEnabled;
        public bool CooldownElapsed;
        public int AuthoritativeSourceConnectionId;
        public uint LastAcceptedSequence;
        public int MaximumAmount;
        public bool IsDynamicEnemyTarget;
    }

    public struct DFMPVitalState
    {
        public int Health;
        public int MaxHealth;
        public int Fatigue;
        public int MaxFatigue;
        public int SpellPoints;
        public int MaxSpellPoints;

        public DFMPVitalApplicationResult ApplyDamage(DFMPVitalKind vitalKind, int amount)
        {
            if (!Enum.IsDefined(typeof(DFMPVitalKind), vitalKind))
                return DFMPVitalApplicationResult.Rejected(DFMPDamageRejectionReason.InvalidVital);

            if (amount <= 0)
                return DFMPVitalApplicationResult.Rejected(DFMPDamageRejectionReason.InvalidAmount);

            int previousValue = GetCurrentValue(vitalKind);
            int maximumValue = GetMaximumValue(vitalKind);
            int appliedAmount = Math.Min(amount, Math.Max(0, previousValue));
            int currentValue = previousValue - appliedAmount;
            SetCurrentValue(vitalKind, currentValue);

            return new DFMPVitalApplicationResult
            {
                Accepted = true,
                RejectionReason = DFMPDamageRejectionReason.None,
                PreviousValue = previousValue,
                AppliedAmount = appliedAmount,
                CurrentValue = currentValue,
                MaximumValue = maximumValue,
                Killed = vitalKind == DFMPVitalKind.Health && previousValue > 0 && currentValue == 0
            };
        }

        int GetCurrentValue(DFMPVitalKind vitalKind)
        {
            switch (vitalKind)
            {
                case DFMPVitalKind.Health:
                    return Math.Max(0, Math.Min(Health, Math.Max(0, MaxHealth)));
                case DFMPVitalKind.Fatigue:
                    return Math.Max(0, Math.Min(Fatigue, Math.Max(0, MaxFatigue)));
                case DFMPVitalKind.SpellPoints:
                    return Math.Max(0, Math.Min(SpellPoints, Math.Max(0, MaxSpellPoints)));
                default:
                    throw new ArgumentOutOfRangeException("vitalKind");
            }
        }

        int GetMaximumValue(DFMPVitalKind vitalKind)
        {
            switch (vitalKind)
            {
                case DFMPVitalKind.Health:
                    return Math.Max(0, MaxHealth);
                case DFMPVitalKind.Fatigue:
                    return Math.Max(0, MaxFatigue);
                case DFMPVitalKind.SpellPoints:
                    return Math.Max(0, MaxSpellPoints);
                default:
                    throw new ArgumentOutOfRangeException("vitalKind");
            }
        }

        void SetCurrentValue(DFMPVitalKind vitalKind, int value)
        {
            switch (vitalKind)
            {
                case DFMPVitalKind.Health:
                    Health = value;
                    break;
                case DFMPVitalKind.Fatigue:
                    Fatigue = value;
                    break;
                case DFMPVitalKind.SpellPoints:
                    SpellPoints = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("vitalKind");
            }
        }
    }

    public struct DFMPVitalApplicationResult
    {
        public bool Accepted;
        public DFMPDamageRejectionReason RejectionReason;
        public int PreviousValue;
        public int AppliedAmount;
        public int CurrentValue;
        public int MaximumValue;
        public bool Killed;

        public static DFMPVitalApplicationResult Rejected(DFMPDamageRejectionReason reason)
        {
            return new DFMPVitalApplicationResult
            {
                Accepted = false,
                RejectionReason = reason
            };
        }
    }

    public static class DFMPDamagePolicy
    {
        public static DFMPDamageRejectionReason GetRejectionReason(
            DFMPDamageValidationRequest intent,
            DFMPDamageValidationContext context)
        {
            if (!context.HasSession)
                return DFMPDamageRejectionReason.MissingSession;
            if (!context.SpawnConfirmed)
                return DFMPDamageRejectionReason.SpawnNotConfirmed;
            if (!context.TargetExists)
                return DFMPDamageRejectionReason.TargetMissing;
            if (context.TargetIsDead)
                return DFMPDamageRejectionReason.TargetDead;
            if (context.HasPendingTransition)
                return DFMPDamageRejectionReason.TransitionPending;
            if (intent.RequestId == 0)
                return DFMPDamageRejectionReason.InvalidRequestId;
            if (intent.Sequence == 0)
                return DFMPDamageRejectionReason.InvalidSequence;
            if (!Enum.IsDefined(typeof(DFMPDamageSourceKind), intent.SourceKind))
                return DFMPDamageRejectionReason.InvalidSource;
            if (!Enum.IsDefined(typeof(DFMPVitalKind), intent.VitalKind))
                return DFMPDamageRejectionReason.InvalidVital;
            if (intent.Amount <= 0 || context.MaximumAmount <= 0 || intent.Amount > context.MaximumAmount)
                return DFMPDamageRejectionReason.InvalidAmount;
            if (intent.SourceConnectionId != context.AuthoritativeSourceConnectionId)
                return DFMPDamageRejectionReason.ForgedSource;
            if (intent.Sequence <= context.LastAcceptedSequence)
                return DFMPDamageRejectionReason.DuplicateRequest;
            if (!context.CooldownElapsed)
                return DFMPDamageRejectionReason.RateLimited;

            if (context.IsDynamicEnemyTarget)
            {
                if (!context.SameWorldContext)
                    return DFMPDamageRejectionReason.ContextMismatch;
                if (!context.InRange)
                    return DFMPDamageRejectionReason.OutOfRange;
            }
            else
            {
                switch (intent.SourceKind)
                {
                    case DFMPDamageSourceKind.LocalQuestPve:
                        if (intent.TargetConnectionId != intent.SourceConnectionId)
                            return DFMPDamageRejectionReason.LocalQuestTargetMismatch;
                        break;
                    case DFMPDamageSourceKind.Player:
                        if (!context.PvpEnabled)
                            return DFMPDamageRejectionReason.PvpDisabled;
                        if (intent.TargetConnectionId == intent.SourceConnectionId)
                            return DFMPDamageRejectionReason.SelfTarget;
                        if (!context.SameWorldContext)
                            return DFMPDamageRejectionReason.ContextMismatch;
                        if (!context.InRange)
                            return DFMPDamageRejectionReason.OutOfRange;
                        break;
                    default:
                        return DFMPDamageRejectionReason.InvalidSource;
                }
            }

            return DFMPDamageRejectionReason.None;
        }

        public static bool IsAccepted(DFMPDamageRejectionReason reason)
        {
            return reason == DFMPDamageRejectionReason.None;
        }
    }
}

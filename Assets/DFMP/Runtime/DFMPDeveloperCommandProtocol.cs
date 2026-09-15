using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPDeveloperInfectSelfRequest : NetworkMessage
    {
    }

    public struct DFMPDeveloperInfectSelfResponse : NetworkMessage
    {
        public bool Accepted;
        public string Reason;
    }

    public struct DFMPDeveloperAdvanceTimeRequest : NetworkMessage
    {
        public int Minutes;
    }

    public struct DFMPDeveloperAdvanceTimeResponse : NetworkMessage
    {
        public bool Accepted;
        public int Minutes;
        public string Reason;
    }

    public struct DFMPDeveloperDamagePlayerRequest : NetworkMessage
    {
        public int TargetConnectionId;
        public int Amount;
    }

    public struct DFMPDeveloperDamagePlayerResponse : NetworkMessage
    {
        public bool Accepted;
        public int TargetConnectionId;
        public int Amount;
        public string Reason;
    }

    public struct DFMPDeveloperDamageSelfRequest : NetworkMessage
    {
        public int Amount;
    }

    public struct DFMPDeveloperGodModeRequest : NetworkMessage
    {
        public bool Enabled;
    }

    public struct DFMPDeveloperGodModeResponse : NetworkMessage
    {
        public bool Accepted;
        public bool Enabled;
        public string Reason;
    }

    public struct DFMPDeveloperTeleportDungeonRequest : NetworkMessage
    {
        public string RegionName;
        public string LocationName;
    }

    public struct DFMPDeveloperTeleportDungeonResponse : NetworkMessage
    {
        public bool Accepted;
        public string RegionName;
        public string LocationName;
        public string Reason;
    }

    public struct DFMPDeveloperCommandContext
    {
        public bool CommandsEnabled;
        public bool HasSession;
        public bool SpawnConfirmed;
    }

    public enum DFMPDeveloperCommandRejectionReason
    {
        None,
        CommandsDisabled,
        MissingSession,
        SpawnNotConfirmed,
        InvalidMinutes,
        MinutesLimitExceeded,
        TimeUnavailable,
        InvalidTarget,
        InvalidDamage
    }

    public static class DFMPDeveloperCommandPolicy
    {
        public static DFMPDeveloperCommandRejectionReason GetInfectSelfRejectionReason(DFMPDeveloperCommandContext context)
        {
            if (!context.CommandsEnabled)
                return DFMPDeveloperCommandRejectionReason.CommandsDisabled;
            if (!context.HasSession)
                return DFMPDeveloperCommandRejectionReason.MissingSession;
            if (!context.SpawnConfirmed)
                return DFMPDeveloperCommandRejectionReason.SpawnNotConfirmed;

            return DFMPDeveloperCommandRejectionReason.None;
        }

        public static bool IsAccepted(DFMPDeveloperCommandRejectionReason reason)
        {
            return reason == DFMPDeveloperCommandRejectionReason.None;
        }

        public static DFMPDeveloperCommandRejectionReason GetDamagePlayerRejectionReason(
            DFMPDeveloperCommandContext context,
            int targetConnectionId,
            int amount,
            int maximumAmount)
        {
            DFMPDeveloperCommandRejectionReason contextReason = GetInfectSelfRejectionReason(context);
            if (contextReason != DFMPDeveloperCommandRejectionReason.None)
                return contextReason;
            if (targetConnectionId <= 0)
                return DFMPDeveloperCommandRejectionReason.InvalidTarget;
            if (amount <= 0 || amount > maximumAmount)
                return DFMPDeveloperCommandRejectionReason.InvalidDamage;

            return DFMPDeveloperCommandRejectionReason.None;
        }

        public static DFMPDeveloperCommandRejectionReason GetDamageSelfRejectionReason(
            DFMPDeveloperCommandContext context,
            int amount,
            int maximumAmount)
        {
            DFMPDeveloperCommandRejectionReason contextReason = GetInfectSelfRejectionReason(context);
            if (contextReason != DFMPDeveloperCommandRejectionReason.None)
                return contextReason;
            if (amount <= 0 || amount > maximumAmount)
                return DFMPDeveloperCommandRejectionReason.InvalidDamage;

            return DFMPDeveloperCommandRejectionReason.None;
        }

        public static DFMPDeveloperCommandRejectionReason GetAdvanceTimeRejectionReason(
            DFMPDeveloperCommandContext context,
            int minutes,
            int maximumMinutes)
        {
            DFMPDeveloperCommandRejectionReason contextReason = GetInfectSelfRejectionReason(context);
            if (contextReason != DFMPDeveloperCommandRejectionReason.None)
                return contextReason;
            if (minutes <= 0)
                return DFMPDeveloperCommandRejectionReason.InvalidMinutes;
            if (minutes > maximumMinutes)
                return DFMPDeveloperCommandRejectionReason.MinutesLimitExceeded;

            return DFMPDeveloperCommandRejectionReason.None;
        }
    }
}

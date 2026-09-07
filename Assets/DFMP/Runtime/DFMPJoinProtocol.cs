using Mirror;

namespace DFMP.Runtime
{
    public struct DFMPAccountIdentityMessage : NetworkMessage
    {
        public string AccountId;
    }

    public struct DFMPJoinResultMessage : NetworkMessage
    {
        public DFMPJoinDecisionKind Decision;
        public string AccountId;
        public string ServerWorldId;
        public string Reason;
        public bool EnableBeginnerTutorial;
    }

    public struct DFMPCharacterSnapshotMessage : NetworkMessage
    {
        public string AccountId;
        public string CharacterName;
        public int Race;
        public int Gender;
        public int OutfitVariant;
        public int FaceVariant;
    }

    public static class DFMPCharacterSnapshotProtocol
    {
        public static DFMPCharacterSnapshotMessage FromRecord(DFMPCharacterRecord record)
        {
            return new DFMPCharacterSnapshotMessage
            {
                AccountId = record.AccountId,
                CharacterName = DFMPPositionProtocol.SanitizeDisplayName(record.CharacterName),
                Race = DFMPPositionProtocol.GetDisplayRace(record.Race),
                Gender = DFMPPositionProtocol.GetDisplayGender(record.Gender),
                OutfitVariant = DFMPPositionProtocol.GetOutfitVariant(record.OutfitVariant),
                FaceVariant = DFMPPositionProtocol.GetFaceVariant(record.FaceVariant)
            };
        }

        public static bool IsValid(DFMPCharacterSnapshotMessage snapshot)
        {
            return !string.IsNullOrWhiteSpace(snapshot.AccountId) &&
                DFMPPositionProtocol.SanitizeDisplayName(snapshot.CharacterName) == snapshot.CharacterName &&
                snapshot.Race == DFMPPositionProtocol.GetDisplayRace(snapshot.Race) &&
                snapshot.Gender == DFMPPositionProtocol.GetDisplayGender(snapshot.Gender) &&
                snapshot.OutfitVariant == DFMPPositionProtocol.GetOutfitVariant(snapshot.OutfitVariant) &&
                snapshot.FaceVariant == DFMPPositionProtocol.GetFaceVariant(snapshot.FaceVariant);
        }
    }

    public enum DFMPJoinDecisionKind
    {
        Rejected,
        FirstJoin,
        ReturningPlayer
    }

    public sealed class DFMPJoinDecision
    {
        public DFMPJoinDecisionKind Kind;
        public string AccountId;
        public string ServerWorldId;
        public string Reason;
        public DFMPCharacterRecord CharacterRecord;

        public bool Accepted
        {
            get { return Kind != DFMPJoinDecisionKind.Rejected; }
        }
    }

    public static class DFMPJoinPolicy
    {
        public static DFMPJoinDecision Resolve(string accountId, DFMPServerConfig config, IDFMPCharacterStore characterStore)
        {
            string normalizedAccountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(accountId, out normalizedAccountId, out reason))
                return Reject(reason);

            if (config == null)
                return Reject("server configuration unavailable");

            config.Normalize();
            if (!DFMPAccountPolicy.IsAllowed(normalizedAccountId, config))
                return Reject("account is not whitelisted");

            if (characterStore == null)
                return Reject("character store unavailable");

            DFMPCharacterRecord record;
            if (characterStore.TryLoad(normalizedAccountId, config.Identity.ServerWorldId, out record))
            {
                return new DFMPJoinDecision
                {
                    Kind = DFMPJoinDecisionKind.ReturningPlayer,
                    AccountId = normalizedAccountId,
                    ServerWorldId = config.Identity.ServerWorldId,
                    CharacterRecord = record
                };
            }

            return new DFMPJoinDecision
            {
                Kind = DFMPJoinDecisionKind.FirstJoin,
                AccountId = normalizedAccountId,
                ServerWorldId = config.Identity.ServerWorldId
            };
        }

        static DFMPJoinDecision Reject(string reason)
        {
            return new DFMPJoinDecision
            {
                Kind = DFMPJoinDecisionKind.Rejected,
                Reason = reason
            };
        }
    }
}

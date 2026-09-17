using System;
using System.Collections.Generic;
using Mirror;

namespace DFMP.Runtime
{
    public enum DFMPCharacterActionKind
    {
        Select,
        Create,
        Delete
    }

    public struct DFMPCharacterRosterMessage : NetworkMessage
    {
        public DFMPCharacterSummary[] Characters;
        public int MaxCharacters;
        public bool AllowCharacterDelete;
        public bool CharacterSelectEnabled;
    }

    public struct DFMPSelectCharacterMessage : NetworkMessage
    {
        public string CharacterId;
    }

    public struct DFMPCreateCharacterMessage : NetworkMessage
    {
    }

    public struct DFMPDeleteCharacterMessage : NetworkMessage
    {
        public string CharacterId;
    }

    public struct DFMPCharacterActionResultMessage : NetworkMessage
    {
        public DFMPCharacterActionKind Action;
        public bool Accepted;
        public string Reason;
        public string CharacterId;
    }

    public static class DFMPCharacterSelectPolicy
    {
        public const string CharacterAlreadyBoundReason = "character already selected";
        public const string SelectionNotActiveReason = "character selection is not active";
        public const string CharacterNotFoundReason = "character was not found";
        public const string CharacterLimitReachedReason = "character limit reached";
        public const string DeleteDisabledReason = "character deletion is disabled";
        public const string InvalidCharacterIdReason = "character id is invalid";

        public static DFMPCharacterRosterMessage CreateRoster(
            IDFMPCharacterStore characterStore,
            string accountId,
            string serverWorldId,
            DFMPServerConfig config)
        {
            DFMPCharacterSummary[] characters = characterStore != null
                ? characterStore.List(accountId, serverWorldId)
                : new DFMPCharacterSummary[0];

            int maxCharacters = DFMPServerIdentityConfig.DefaultMaxCharactersPerAccount;
            bool allowDelete = true;
            bool selectEnabled = true;
            if (config != null)
            {
                config.Normalize();
                maxCharacters = config.Identity.MaxCharactersPerAccount;
                allowDelete = config.Identity.AllowCharacterDelete;
                selectEnabled = config.Identity.CharacterSelectEnabled;
            }

            return new DFMPCharacterRosterMessage
            {
                Characters = characters ?? new DFMPCharacterSummary[0],
                MaxCharacters = maxCharacters,
                AllowCharacterDelete = allowDelete,
                CharacterSelectEnabled = selectEnabled
            };
        }

        public static bool IsCharacterBound(DFMPJoinDecisionKind kind)
        {
            return kind == DFMPJoinDecisionKind.FirstJoin || kind == DFMPJoinDecisionKind.ReturningPlayer;
        }

        public static bool CanCreate(int currentCount, int maxCharacters)
        {
            return currentCount >= 0 && currentCount < maxCharacters;
        }

        public static string GetSelectRejectionReason(
            DFMPJoinDecisionKind currentKind,
            string characterId,
            IList<DFMPCharacterSummary> characters)
        {
            if (currentKind != DFMPJoinDecisionKind.AwaitingCharacterSelection)
                return currentKind == DFMPJoinDecisionKind.Rejected ? SelectionNotActiveReason : CharacterAlreadyBoundReason;

            Guid parsed;
            if (string.IsNullOrWhiteSpace(characterId) || !Guid.TryParse(characterId, out parsed))
                return InvalidCharacterIdReason;

            if (!ContainsCharacter(characters, parsed.ToString("N")))
                return CharacterNotFoundReason;

            return null;
        }

        public static string GetCreateRejectionReason(
            DFMPJoinDecisionKind currentKind,
            int currentCount,
            int maxCharacters)
        {
            if (currentKind != DFMPJoinDecisionKind.AwaitingCharacterSelection)
                return currentKind == DFMPJoinDecisionKind.Rejected ? SelectionNotActiveReason : CharacterAlreadyBoundReason;

            if (!CanCreate(currentCount, maxCharacters))
                return CharacterLimitReachedReason;

            return null;
        }

        public static string GetDeleteRejectionReason(
            DFMPJoinDecisionKind currentKind,
            bool allowCharacterDelete,
            string characterId,
            IList<DFMPCharacterSummary> characters)
        {
            if (currentKind != DFMPJoinDecisionKind.AwaitingCharacterSelection)
                return currentKind == DFMPJoinDecisionKind.Rejected ? SelectionNotActiveReason : CharacterAlreadyBoundReason;

            if (!allowCharacterDelete)
                return DeleteDisabledReason;

            Guid parsed;
            if (string.IsNullOrWhiteSpace(characterId) || !Guid.TryParse(characterId, out parsed))
                return InvalidCharacterIdReason;

            if (!ContainsCharacter(characters, parsed.ToString("N")))
                return CharacterNotFoundReason;

            return null;
        }

        public static string FormatCharacterLimitStatus(int currentCount, int maxCharacters)
        {
            return string.Format("Character limit reached ({0}/{1})", currentCount, maxCharacters);
        }

        static bool ContainsCharacter(IList<DFMPCharacterSummary> characters, string characterId)
        {
            if (characters == null)
                return false;

            for (int i = 0; i < characters.Count; i++)
            {
                Guid parsed;
                if (Guid.TryParse(characters[i].CharacterId, out parsed) && parsed.ToString("N") == characterId)
                    return true;
            }

            return false;
        }
    }
}

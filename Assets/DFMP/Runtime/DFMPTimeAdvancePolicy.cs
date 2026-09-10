namespace DFMP.Runtime
{
    public static class DFMPTimeAdvanceSources
    {
        public const string QuestTraining = "QuestTraining";
        public const string GuildTraining = "GuildTraining";
    }

    public static class DFMPTimeAdvancePolicy
    {
        public static bool ShouldConsumeClientAdvance(bool isMultiplayerClientConnected, string source, int seconds)
        {
            if (!isMultiplayerClientConnected || seconds <= 0)
                return false;

            return source == DFMPTimeAdvanceSources.QuestTraining ||
                   source == DFMPTimeAdvanceSources.GuildTraining;
        }
    }
}
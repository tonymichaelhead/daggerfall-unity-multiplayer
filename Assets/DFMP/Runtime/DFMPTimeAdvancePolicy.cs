namespace DFMP.Runtime
{
    public static class DFMPTimeAdvanceSources
    {
        public const string QuestTraining = "QuestTraining";
        public const string GuildTraining = "GuildTraining";
        public const string PrisonSentence = "PrisonSentence";
        public const string PrisonRelease = "PrisonRelease";
    }

    public static class DFMPTimeAdvancePolicy
    {
        public static bool ShouldConsumeClientAdvance(bool isMultiplayerClientConnected, string source, int seconds)
        {
            if (!isMultiplayerClientConnected || seconds <= 0)
                return false;

            return source == DFMPTimeAdvanceSources.QuestTraining ||
                                     source == DFMPTimeAdvanceSources.GuildTraining ||
                                     source == DFMPTimeAdvanceSources.PrisonSentence ||
                                     source == DFMPTimeAdvanceSources.PrisonRelease;
        }
    }
}
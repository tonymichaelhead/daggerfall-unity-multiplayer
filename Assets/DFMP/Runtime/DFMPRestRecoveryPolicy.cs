namespace DFMP.Runtime
{
    public struct DFMPRestVitals
    {
        public int Health;
        public int MaxHealth;
        public int Fatigue;
        public int MaxFatigue;
        public int SpellPoints;
        public int MaxSpellPoints;
        public bool NoSpellPointRegeneration;
    }

    public struct DFMPRestRecoveryResult
    {
        public int HealthRecovered;
        public int FatigueRecovered;
        public int SpellPointsRecovered;
        public bool IsComplete;
    }

    public static class DFMPRestRecoveryPolicy
    {
        public const float SecondsPerRestHour = 3600f;

        public static DFMPRestRecoveryResult Calculate(
            DFMPRestVitals vitals,
            int healthRecoveryPerHour,
            int fatigueRecoveryPerHour,
            int spellPointsRecoveryPerHour,
            float elapsedRealSeconds,
            float recoveryRateMultiplier)
        {
            float boundedSeconds = elapsedRealSeconds < 0f ? 0f : elapsedRealSeconds;
            float boundedMultiplier = recoveryRateMultiplier < 0f ? 0f : recoveryRateMultiplier;
            float recoveryHours = boundedSeconds / SecondsPerRestHour * boundedMultiplier;

            int healthRecovered = GetBoundedRecovery(
                vitals.Health,
                vitals.MaxHealth,
                healthRecoveryPerHour,
                recoveryHours);
            int fatigueRecovered = GetBoundedRecovery(
                vitals.Fatigue,
                vitals.MaxFatigue,
                fatigueRecoveryPerHour,
                recoveryHours);
            int spellPointsRecovered = vitals.NoSpellPointRegeneration
                ? 0
                : GetBoundedRecovery(
                    vitals.SpellPoints,
                    vitals.MaxSpellPoints,
                    spellPointsRecoveryPerHour,
                    recoveryHours);

            DFMPRestVitals recoveredVitals = vitals;
            recoveredVitals.Health += healthRecovered;
            recoveredVitals.Fatigue += fatigueRecovered;
            recoveredVitals.SpellPoints += spellPointsRecovered;

            return new DFMPRestRecoveryResult
            {
                HealthRecovered = healthRecovered,
                FatigueRecovered = fatigueRecovered,
                SpellPointsRecovered = spellPointsRecovered,
                IsComplete = IsComplete(recoveredVitals)
            };
        }

        public static bool IsComplete(DFMPRestVitals vitals)
        {
            return vitals.Health >= vitals.MaxHealth &&
                   vitals.Fatigue >= vitals.MaxFatigue &&
                   (vitals.NoSpellPointRegeneration || vitals.SpellPoints >= vitals.MaxSpellPoints);
        }

        static int GetBoundedRecovery(int current, int maximum, int recoveryPerHour, float recoveryHours)
        {
            if (maximum <= current || recoveryPerHour <= 0 || recoveryHours <= 0f)
                return 0;

            int available = maximum - current;
            int recovered = (int)(recoveryPerHour * recoveryHours);
            if (recovered < 0)
                return 0;

            return recovered > available ? available : recovered;
        }
    }
}

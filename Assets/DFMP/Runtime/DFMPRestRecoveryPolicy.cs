using DaggerfallConnect;
using UnityEngine;

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

    public struct DFMPRestHourRecoveryContext
    {
        public int Health;
        public int MaxHealth;
        public int Fatigue;
        public int MaxFatigue;
        public int SpellPoints;
        public int MaxSpellPoints;
        public int MedicalSkill;
        public int Endurance;
        public bool NoSpellPointRegeneration;
        public DFCareer.RapidHealingFlags RapidHealing;
        public bool IsDay;
        public bool IsInside;
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
        public const int MinutesPerClassicDay = 1440;
        public const int DawnHour = 6;
        public const int DuskHour = 18;

        public static bool IsDayFromClassicMinutes(uint classicMinutes)
        {
            int hourOfDay = (int)((classicMinutes % MinutesPerClassicDay) / 60);
            return hourOfDay >= DawnHour && hourOfDay < DuskHour;
        }

        public static int CalculateHealthRecoveryRate(
            int medicalSkill,
            int endurance,
            int maxHealth,
            DFCareer.RapidHealingFlags rapidHealing,
            bool isDay,
            bool isInside)
        {
            int medical = medicalSkill < 0 ? 0 : medicalSkill;
            int addToMedical = 60;

            if (rapidHealing == DFCareer.RapidHealingFlags.Always)
                addToMedical = 100;
            else if (isDay && !isInside)
            {
                if (rapidHealing == DFCareer.RapidHealingFlags.InLight)
                    addToMedical = 100;
            }
            else if (rapidHealing == DFCareer.RapidHealingFlags.InDarkness)
            {
                addToMedical = 100;
            }

            medical += addToMedical;
            int healingRateModifier = (int)Mathf.Floor((float)endurance / 10f) - 5;
            return Mathf.Max((int)Mathf.Floor(healingRateModifier + medical * Mathf.Max(1, maxHealth) / 1000), 1);
        }

        public static int CalculateFatigueRecoveryRate(int maxFatigue)
        {
            return Mathf.Max((int)Mathf.Floor(Mathf.Max(1, maxFatigue) / 8), 1);
        }

        public static int CalculateSpellPointRecoveryRate(int maxSpellPoints, bool noSpellPointRegeneration)
        {
            if (noSpellPointRegeneration)
                return 0;

            return Mathf.Max((int)Mathf.Floor(Mathf.Max(0, maxSpellPoints) / 8), 1);
        }

        public static DFMPRestRecoveryResult ApplyHour(DFMPRestHourRecoveryContext context)
        {
            int healthRecovered = GetBoundedRecovery(
                context.Health,
                context.MaxHealth,
                CalculateHealthRecoveryRate(
                    context.MedicalSkill,
                    context.Endurance,
                    context.MaxHealth,
                    context.RapidHealing,
                    context.IsDay,
                    context.IsInside));
            int fatigueRecovered = GetBoundedRecovery(
                context.Fatigue,
                context.MaxFatigue,
                CalculateFatigueRecoveryRate(context.MaxFatigue));
            int spellPointsRecovered = GetBoundedRecovery(
                context.SpellPoints,
                context.MaxSpellPoints,
                CalculateSpellPointRecoveryRate(context.MaxSpellPoints, context.NoSpellPointRegeneration));

            DFMPRestVitals recoveredVitals = new DFMPRestVitals
            {
                Health = context.Health + healthRecovered,
                MaxHealth = context.MaxHealth,
                Fatigue = context.Fatigue + fatigueRecovered,
                MaxFatigue = context.MaxFatigue,
                SpellPoints = context.SpellPoints + spellPointsRecovered,
                MaxSpellPoints = context.MaxSpellPoints,
                NoSpellPointRegeneration = context.NoSpellPointRegeneration
            };

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

        static int GetBoundedRecovery(int current, int maximum, int recoveryPerHour)
        {
            if (maximum <= current || recoveryPerHour <= 0)
                return 0;

            int available = maximum - current;
            return recoveryPerHour > available ? available : recoveryPerHour;
        }
    }
}

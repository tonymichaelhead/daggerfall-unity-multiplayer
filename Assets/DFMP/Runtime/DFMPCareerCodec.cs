using System;
using DaggerfallConnect;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// Transports a character's class. DFCareer is a flat serializable class, so it round-trips
    /// through JsonUtility and covers custom classes without a per-field mirror record.
    /// </summary>
    public static class DFMPCareerCodec
    {
        public const int MaximumJsonLength = 64 * 1024;
        public const float MaximumSpellPointMultiplier = 3f;
        public const int MaximumHitPointsPerLevel = 100;
        public const int MaximumAdvancementMultiplier = 10;

        public static string Encode(DFCareer career)
        {
            return career == null ? string.Empty : JsonUtility.ToJson(career, false);
        }

        public static bool TryDecode(string json, out DFCareer career, out string reason)
        {
            career = null;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "career payload is empty";
                return false;
            }

            if (json.Length > MaximumJsonLength)
            {
                reason = "career payload exceeds maximum size";
                return false;
            }

            try
            {
                career = JsonUtility.FromJson<DFCareer>(json);
            }
            catch (Exception ex)
            {
                reason = "career payload is malformed: " + ex.Message;
                return false;
            }

            if (career == null)
            {
                reason = "career payload is malformed";
                return false;
            }

            if (string.IsNullOrWhiteSpace(career.Name))
            {
                reason = "career is missing a class name";
                career = null;
                return false;
            }

            Clamp(career);
            return true;
        }

        static void Clamp(DFCareer career)
        {
            career.Name = career.Name.Trim();
            career.SpellPointMultiplierValue = Mathf.Clamp(career.SpellPointMultiplierValue, 0f, MaximumSpellPointMultiplier);
            career.HitPointsPerLevel = Mathf.Clamp(career.HitPointsPerLevel, 0, MaximumHitPointsPerLevel);
            career.AdvancementMultiplier = Mathf.Clamp(career.AdvancementMultiplier, 0f, MaximumAdvancementMultiplier);
        }
    }
}

using DaggerfallConnect.Arena2;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPCharacterPersistence
    {
        public static void ApplyIdentityReport(DFMPCharacterRecord record, DFMPPlayerIdentityReport report)
        {
            if (record == null || report.Attributes == null || report.Skills == null || report.Attributes.Length != 8 || report.Skills.Length != 35)
                return;

            record.CharacterName = DFMPPositionProtocol.SanitizeDisplayName(report.DisplayName);
            record.Race = DFMPPositionProtocol.GetPlayerRace(report.Race);
            record.Gender = DFMPPositionProtocol.GetDisplayGender(report.Gender);
            record.OutfitVariant = DFMPPositionProtocol.GetOutfitVariant(report.OutfitVariant);
            record.FaceVariant = Mathf.Clamp(report.FaceVariant, 0, 9);
            record.Level = Mathf.Max(1, report.Level);
            record.MaxHealth = Mathf.Max(1, report.MaxHealth);
            record.Health = Mathf.Clamp(report.Health, 0, record.MaxHealth);
            record.MaxSpellPoints = Mathf.Max(0, report.MaxSpellPoints);
            record.SpellPoints = Mathf.Clamp(report.SpellPoints, 0, record.MaxSpellPoints);
            record.MaxFatigue = Mathf.Max(1, report.MaxFatigue);
            record.Fatigue = Mathf.Clamp(report.Fatigue, 0, record.MaxFatigue);
            record.Gold = Mathf.Max(0, report.Gold);
            for (int index = 0; index < 8; index++)
                record.Attributes[index] = Mathf.Clamp(report.Attributes[index], 0, 100);
            for (int index = 0; index < 35; index++)
                record.Skills[index] = Mathf.Clamp(report.Skills[index], 0, 100);
        }

        public static void ApplySessionState(DFMPCharacterRecord record, DFMPPlayerSessionState sessionState)
        {
            if (record == null || sessionState == null)
                return;

            record.CharacterName = DFMPPositionProtocol.SanitizeDisplayName(sessionState.DisplayName);
            record.WorldX = sessionState.WorldX;
            record.WorldY = sessionState.WorldY;
            record.WorldZ = sessionState.WorldZ;
            record.WorldContext = "Exterior";

            var mapPixel = MapsFile.WorldCoordToMapPixel(sessionState.WorldX, sessionState.WorldZ);
            record.MapPixelX = mapPixel.X;
            record.MapPixelY = mapPixel.Y;
            record.Race = DFMPPositionProtocol.GetPlayerRace(sessionState.Race);
            record.Gender = DFMPPositionProtocol.GetDisplayGender(sessionState.Gender);
            record.OutfitVariant = DFMPPositionProtocol.GetOutfitVariant(sessionState.OutfitVariant);
            record.FaceVariant = DFMPPositionProtocol.GetFaceVariant(sessionState.FaceVariant);
        }
    }
}

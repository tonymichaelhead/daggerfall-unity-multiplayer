using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public struct DFMPCharacterSummary
    {
        public string CharacterId;
        public string CharacterName;
        public int Level;
        public string ClassName;
        public string LastPlayedUtc;
    }

    public interface IDFMPCharacterStore
    {
        DFMPCharacterSummary[] List(string accountId, string serverWorldId);
        bool TryLoad(string accountId, string serverWorldId, string characterId, out DFMPCharacterRecord record);
        bool TryLoadMostRecentlyPlayed(string accountId, string serverWorldId, out DFMPCharacterRecord record);
        void Save(DFMPCharacterRecord record);
        bool Delete(string accountId, string serverWorldId, string characterId);
    }

    public sealed class DFMPFileCharacterStore : IDFMPCharacterStore
    {
        readonly string rootDirectory;

        public DFMPFileCharacterStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Character store directory is required.", "rootDirectory");

            this.rootDirectory = rootDirectory;
            Directory.CreateDirectory(rootDirectory);
        }

        public DFMPCharacterSummary[] List(string accountId, string serverWorldId)
        {
            MigrateLegacyRecord(accountId, serverWorldId);

            var summaries = new List<DFMPCharacterSummary>();
            CollectSummaries(GetAccountWorldDirectory(accountId, serverWorldId), accountId, serverWorldId, summaries);
            summaries.Sort(CompareSummaries);
            if (summaries.Count == 0)
                Debug.LogWarning($"[DFMP Character] No readable characters for account '{accountId}' world '{serverWorldId}' under '{GetAccountWorldDirectory(accountId, serverWorldId)}'.");

            return summaries.ToArray();
        }

        public bool TryLoad(string accountId, string serverWorldId, string characterId, out DFMPCharacterRecord record)
        {
            record = null;
            MigrateLegacyRecord(accountId, serverWorldId);

            string path;
            if (!TryGetRecordPath(accountId, serverWorldId, characterId, out path))
                return false;

            return TryReadRecord(path, accountId, serverWorldId, out record);
        }

        public bool TryLoadMostRecentlyPlayed(string accountId, string serverWorldId, out DFMPCharacterRecord record)
        {
            record = null;
            DFMPCharacterSummary[] summaries = List(accountId, serverWorldId);
            if (summaries == null || summaries.Length == 0)
                return false;

            return TryLoad(accountId, serverWorldId, summaries[0].CharacterId, out record);
        }

        public void Save(DFMPCharacterRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            record.EnsureCharacterId();
            record.Normalize(record.AccountId, record.ServerWorldId);

            string path;
            if (!TryGetRecordPath(record.AccountId, record.ServerWorldId, record.CharacterId, out path))
                throw new InvalidOperationException("Character id is invalid.");

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            WriteAtomically(path, record.ToJson());
        }

        public bool Delete(string accountId, string serverWorldId, string characterId)
        {
            MigrateLegacyRecord(accountId, serverWorldId);

            string path;
            if (!TryGetRecordPath(accountId, serverWorldId, characterId, out path) || !File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }

        public string GetLegacyRecordPath(string accountId, string serverWorldId)
        {
            return Path.Combine(rootDirectory, GetAccountWorldHash(accountId, serverWorldId) + ".json");
        }

        public string GetAccountWorldDirectory(string accountId, string serverWorldId)
        {
            return Path.Combine(rootDirectory, GetAccountWorldHash(accountId, serverWorldId));
        }

        public string GetRecordPath(string accountId, string serverWorldId, string characterId)
        {
            string path;
            if (!TryGetRecordPath(accountId, serverWorldId, characterId, out path))
                return null;

            return path;
        }

        public static string GetAccountWorldHash(string accountId, string serverWorldId)
        {
            return HashKey((accountId ?? string.Empty) + "\n" + (serverWorldId ?? string.Empty));
        }

        public static string GetRecordFileName(string accountId, string serverWorldId)
        {
            return GetAccountWorldHash(accountId, serverWorldId) + ".json";
        }

        public static DFMPCharacterSummary ToSummary(DFMPCharacterRecord record)
        {
            if (record == null)
                return new DFMPCharacterSummary();

            return new DFMPCharacterSummary
            {
                CharacterId = record.CharacterId ?? string.Empty,
                CharacterName = string.IsNullOrWhiteSpace(record.CharacterName) ? "Player" : record.CharacterName,
                Level = Mathf.Max(1, record.Level),
                ClassName = GetClassName(record.CareerJson),
                LastPlayedUtc = record.LastPlayedUtc ?? string.Empty
            };
        }

        static string GetClassName(string careerJson)
        {
            DaggerfallConnect.DFCareer career;
            string reason;
            if (!DFMPCareerCodec.TryDecode(careerJson, out career, out reason) || career == null)
                return string.Empty;

            return career.Name;
        }

        static int CompareSummaries(DFMPCharacterSummary left, DFMPCharacterSummary right)
        {
            int played = GetLastPlayedTimeUtc(right.LastPlayedUtc).CompareTo(GetLastPlayedTimeUtc(left.LastPlayedUtc));
            if (played != 0)
                return played;

            return string.Compare(left.CharacterName, right.CharacterName, StringComparison.OrdinalIgnoreCase);
        }

        static DateTime GetLastPlayedTimeUtc(string lastPlayedUtc)
        {
            DateTime parsed;
            if (DateTime.TryParse(lastPlayedUtc, null, DateTimeStyles.RoundtripKind, out parsed))
                return parsed.ToUniversalTime();

            return DateTime.MinValue;
        }

        void MigrateLegacyRecord(string accountId, string serverWorldId)
        {
            RekeyRecordFile(GetLegacyRecordPath(accountId, serverWorldId), accountId, serverWorldId);
        }

        void CollectSummaries(string directory, string accountId, string serverWorldId, List<DFMPCharacterSummary> summaries)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory) || summaries == null)
                return;

            string[] files = Directory.GetFiles(directory, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                DFMPCharacterRecord record;
                if (!TryReadRecord(files[i], accountId, serverWorldId, out record) || record == null)
                    continue;

                try
                {
                    summaries.Add(ToSummary(record));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DFMP Character] Failed to summarize '{files[i]}': {ex.Message}");
                }
            }
        }

        void RekeyRecordFile(string path, string accountId, string serverWorldId)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            string json;
            if (!TryReadJson(path, out json))
                return;

            DFMPCharacterRecord record = DFMPCharacterRecord.FromJson(json, accountId, serverWorldId);
            if (record == null)
            {
                Debug.LogWarning($"[DFMP Character] Failed to parse migrated record '{path}'.");
                return;
            }

            record.EnsureCharacterId();
            if (string.IsNullOrWhiteSpace(record.LastPlayedUtc))
                record.MarkPlayed();

            Save(record);
            DeleteFileQuiet(path);
        }

        bool TryReadRecord(string path, string accountId, string serverWorldId, out DFMPCharacterRecord record)
        {
            record = null;
            string json;
            if (!TryReadJson(path, out json))
                return false;

            try
            {
                record = DFMPCharacterRecord.FromJson(json, accountId, serverWorldId);
                if (record == null)
                    Debug.LogWarning($"[DFMP Character] Failed to parse '{path}'.");

                return record != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Character] Failed to load '{path}': {ex.Message}");
                return false;
            }
        }

        bool TryGetRecordPath(string accountId, string serverWorldId, string characterId, out string path)
        {
            path = null;
            Guid parsed;
            if (string.IsNullOrWhiteSpace(characterId) || !Guid.TryParse(characterId.Trim(), out parsed))
                return false;

            path = Path.Combine(GetAccountWorldDirectory(accountId, serverWorldId), parsed.ToString("N") + ".json");
            return true;
        }

        static bool TryReadJson(string path, out string json)
        {
            json = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                json = File.ReadAllText(path);
                return !string.IsNullOrWhiteSpace(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Character] Failed to read '{path}': {ex.Message}");
                return false;
            }
        }

        static void DeleteFileQuiet(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
                string backupPath = path + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Character] Failed to remove '{path}': {ex.Message}");
            }
        }

        static void WriteAtomically(string path, string json)
        {
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json, Encoding.UTF8);

            if (File.Exists(path))
            {
                string backupPath = path + ".bak";
                File.Replace(temporaryPath, path, backupPath, true);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }

        static string HashKey(string key)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}

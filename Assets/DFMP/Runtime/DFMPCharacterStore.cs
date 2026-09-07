using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DFMP.Runtime
{
    public interface IDFMPCharacterStore
    {
        bool TryLoad(string accountId, string serverWorldId, out DFMPCharacterRecord record);
        void Save(DFMPCharacterRecord record);
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

        public bool TryLoad(string accountId, string serverWorldId, out DFMPCharacterRecord record)
        {
            record = null;
            string path = GetRecordPath(accountId, serverWorldId);
            if (!File.Exists(path))
                return false;

            try
            {
                record = DFMPCharacterRecord.FromJson(File.ReadAllText(path), accountId, serverWorldId);
                return record != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Character] Failed to load '{path}': {ex.Message}");
                return false;
            }
        }

        public void Save(DFMPCharacterRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            record.Normalize(record.AccountId, record.ServerWorldId);
            string path = GetRecordPath(record.AccountId, record.ServerWorldId);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, record.ToJson(), Encoding.UTF8);

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

        public string GetRecordPath(string accountId, string serverWorldId)
        {
            return Path.Combine(rootDirectory, GetRecordFileName(accountId, serverWorldId));
        }

        public static string GetRecordFileName(string accountId, string serverWorldId)
        {
            string key = (accountId ?? string.Empty) + "\n" + (serverWorldId ?? string.Empty);
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));
                return builder + ".json";
            }
        }
    }
}

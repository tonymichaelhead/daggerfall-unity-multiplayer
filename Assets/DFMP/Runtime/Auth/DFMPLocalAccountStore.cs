using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPLocalAccountRecord
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string AccountId = string.Empty;
        public string Salt = string.Empty;
        public string Hash = string.Empty;
        public int Iterations = DFMPCredential.ServerIterations;
        public string CreatedUtc = string.Empty;
        public string LastSeenUtc = string.Empty;

        public static DFMPLocalAccountRecord Create(string accountId, string credential)
        {
            byte[] salt = DFMPCredential.CreateSalt();
            string nowUtc = DateTime.UtcNow.ToString("o");

            return new DFMPLocalAccountRecord
            {
                SchemaVersion = CurrentSchemaVersion,
                AccountId = accountId,
                Salt = DFMPCredential.ToHex(salt),
                Hash = DFMPCredential.HashCredential(credential, salt, DFMPCredential.ServerIterations),
                Iterations = DFMPCredential.ServerIterations,
                CreatedUtc = nowUtc,
                LastSeenUtc = nowUtc
            };
        }

        public bool Matches(string credential)
        {
            byte[] salt = DFMPCredential.FromHex(Salt);
            if (salt.Length == 0 || string.IsNullOrEmpty(Hash))
                return false;

            int iterations = Iterations > 0 ? Iterations : DFMPCredential.ServerIterations;
            return DFMPCredential.FixedTimeEquals(DFMPCredential.HashCredential(credential, salt, iterations), Hash);
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        public static DFMPLocalAccountRecord FromJson(string json, string expectedAccountId)
        {
            var record = JsonUtility.FromJson<DFMPLocalAccountRecord>(json);
            if (record == null || record.AccountId != expectedAccountId)
                return null;

            return record;
        }
    }

    public interface IDFMPLocalAccountStore
    {
        bool TryLoad(string accountId, out DFMPLocalAccountRecord record);
        void Save(DFMPLocalAccountRecord record);
    }

    public sealed class DFMPFileLocalAccountStore : IDFMPLocalAccountStore
    {
        readonly string rootDirectory;

        public DFMPFileLocalAccountStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("Account store directory is required.", "rootDirectory");

            this.rootDirectory = rootDirectory;
            Directory.CreateDirectory(rootDirectory);
        }

        public bool TryLoad(string accountId, out DFMPLocalAccountRecord record)
        {
            record = null;
            string path = GetRecordPath(accountId);
            if (!File.Exists(path))
                return false;

            try
            {
                record = DFMPLocalAccountRecord.FromJson(File.ReadAllText(path), accountId);
                return record != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Auth] Failed to load account record '{path}': {ex.Message}");
                return false;
            }
        }

        public void Save(DFMPLocalAccountRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            string path = GetRecordPath(record.AccountId);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, record.ToJson(), Encoding.UTF8);

            if (File.Exists(path))
                File.Replace(temporaryPath, path, path + ".bak", true);
            else
                File.Move(temporaryPath, path);
        }

        string GetRecordPath(string accountId)
        {
            return Path.Combine(rootDirectory, GetRecordFileName(accountId));
        }

        // Account ids are hashed rather than used directly so no id can escape into a file path.
        public static string GetRecordFileName(string accountId)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(accountId ?? string.Empty));
                return DFMPCredential.ToHex(hash) + ".json";
            }
        }
    }
}

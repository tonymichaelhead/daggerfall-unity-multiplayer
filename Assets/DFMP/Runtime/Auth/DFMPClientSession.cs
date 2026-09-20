using System;
using System.IO;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// The credentials the client authenticates with. Normally handed over by the launcher through a
    /// restricted-permission session file, which is consumed and deleted on read.
    /// </summary>
    public static class DFMPClientSession
    {
        [Serializable]
        class SessionFile
        {
            public string accountId = string.Empty;
            public string credential = string.Empty;
        }

        public static string AccountId { get; private set; } = string.Empty;
        public static string Credential { get; private set; } = string.Empty;

        public static bool HasCredential
        {
            get { return DFMPCredential.IsValidCredentialFormat(Credential); }
        }

        public static bool HasAccountId
        {
            get { return !string.IsNullOrEmpty(AccountId); }
        }

        public static void Set(string accountId, string credential)
        {
            AccountId = accountId ?? string.Empty;
            Credential = credential ?? string.Empty;
        }

        public static void SetFromPassword(string accountId, string password)
        {
            Set(accountId, DFMPCredential.DeriveClientCredential(accountId, password));
        }

        public static void Clear()
        {
            AccountId = string.Empty;
            Credential = string.Empty;
        }

        /// <summary>
        /// Reads a launcher-written session file. The launcher's copy is deleted on read because it is
        /// a one-shot handoff; a development file left in the working directory is kept.
        /// </summary>
        public static bool TryLoadFromFile(string path, bool deleteAfterRead = true)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                var session = JsonUtility.FromJson<SessionFile>(File.ReadAllText(path));
                if (session == null || string.IsNullOrWhiteSpace(session.accountId))
                    return false;

                Set(session.accountId, session.credential);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Auth] Failed to read session file: {ex.Message}");
                return false;
            }
            finally
            {
                if (deleteAfterRead)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DFMP Auth] Failed to delete session file: {ex.Message}");
                    }
                }
            }
        }

        public const string DevelopmentSessionFileName = "dfmp-client-session.json";

        static bool attemptedDevelopmentLoad;

        /// <summary>Development convenience so an editor or terminal client can connect without the launcher.</summary>
        public static bool TryLoadDevelopmentSession()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), DevelopmentSessionFileName);
            return TryLoadFromFile(path, false);
        }

        /// <summary>Falls back to the development session file once, if nothing has supplied an identity.</summary>
        public static void EnsureLoaded()
        {
            if (HasAccountId || attemptedDevelopmentLoad)
                return;

            attemptedDevelopmentLoad = true;
            if (TryLoadDevelopmentSession())
                Debug.Log($"[DFMP Auth] Using development session for account '{AccountId}'.");
        }
    }
}

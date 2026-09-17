using System.IO;
using DFMP.Runtime;
using UnityEditor;
using UnityEngine;

namespace DFMP.EditorTools
{
    /// <summary>
    /// Writes the development session file the client falls back to when it is started from the
    /// editor instead of the launcher.
    /// </summary>
    public class DFMPDevelopmentSessionWindow : EditorWindow
    {
        string accountId = string.Empty;
        string password = string.Empty;
        string status = string.Empty;

        [MenuItem("DFMP/Development Session")]
        public static void ShowWindow()
        {
            GetWindow<DFMPDevelopmentSessionWindow>("DFMP Session").minSize = new Vector2(380, 190);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Writes dfmp-client-session.json in the project root so an editor client can authenticate " +
                "without the launcher. The file is gitignored and holds a derived credential, not your password.",
                MessageType.Info);

            accountId = EditorGUILayout.TextField("Username", accountId);
            password = EditorGUILayout.PasswordField("Password", password);

            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(accountId) || string.IsNullOrEmpty(password));
            if (GUILayout.Button("Write Session File"))
                WriteSessionFile();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Delete Session File"))
                DeleteSessionFile();

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);
        }

        string GetSessionPath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), DFMPClientSession.DevelopmentSessionFileName);
        }

        void WriteSessionFile()
        {
            string normalizedAccountId;
            string reason;
            if (!DFMPAccountPolicy.TryNormalize(accountId, out normalizedAccountId, out reason))
            {
                status = "Invalid username: " + reason;
                return;
            }

            if (!DFMPCredential.IsAcceptablePassword(password, out reason))
            {
                status = "Invalid password: " + reason;
                return;
            }

            string credential = DFMPCredential.DeriveClientCredential(normalizedAccountId, password);
            string json = JsonUtility.ToJson(new SessionFile { accountId = normalizedAccountId, credential = credential }, true);

            File.WriteAllText(GetSessionPath(), json);
            password = string.Empty;
            status = $"Wrote session for '{normalizedAccountId}'.";
        }

        void DeleteSessionFile()
        {
            string path = GetSessionPath();
            if (File.Exists(path))
            {
                File.Delete(path);
                status = "Deleted the development session file.";
            }
            else
            {
                status = "No development session file to delete.";
            }
        }

        [System.Serializable]
        class SessionFile
        {
            public string accountId = string.Empty;
            public string credential = string.Empty;
        }
    }
}

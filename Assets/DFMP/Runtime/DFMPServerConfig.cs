using System;
using System.IO;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPServerConfig
    {
        public const string DefaultConfigFileName = "dfmp-server.json";

        public string ServerName = "Tony's DFU RP";
        public int Port = 7777;
        public int DiscoveryPort = 7778;
        public int MaxConnections = 16;
        public int TickRate = 30;
        public float HeartbeatInterval = 5.0f;
        public bool LanDiscoveryEnabled = true;
        public string Motd = "Welcome to Daggerfall Unity Multiplayer";

        public static string GetConfigFilePath()
        {
            // First check root project/working directory, else persistentDataPath
            string rootPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultConfigFileName);
            return rootPath;
        }

        public static DFMPServerConfig LoadOrCreate(string customPath = null)
        {
            string filePath = !string.IsNullOrEmpty(customPath) ? customPath : GetConfigFilePath();

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var config = JsonUtility.FromJson<DFMPServerConfig>(json);
                    if (config != null)
                        return config;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Config] Error loading config from '{filePath}': {ex.Message}. Falling back to defaults.");
            }

            var defaultConfig = new DFMPServerConfig();
            Save(defaultConfig, filePath);
            return defaultConfig;
        }

        public static void Save(DFMPServerConfig config, string customPath = null)
        {
            if (config == null)
                return;

            string filePath = !string.IsNullOrEmpty(customPath) ? customPath : GetConfigFilePath();

            try
            {
                string json = JsonUtility.ToJson(config, true);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Config] Error saving config to '{filePath}': {ex.Message}");
            }
        }
    }
}

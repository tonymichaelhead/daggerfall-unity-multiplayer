using System;
using System.IO;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public class DFMPServerChatConfig
    {
        public int MaxMessageLength = 256;
        public float MinIntervalSeconds = 1.0f;
    }

    [Serializable]
    public class DFMPServerIdentityConfig
    {
        public string ServerWorldId = "default";
        public bool WhitelistEnabled;
        public string[] AllowedAccountIds = new string[0];
    }

    [Serializable]
    public class DFMPServerGameplayConfig
    {
        public bool EnableBeginnerTutorial;
    }

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
        public DFMPServerChatConfig Chat = new DFMPServerChatConfig();
        public DFMPServerIdentityConfig Identity = new DFMPServerIdentityConfig();
        public DFMPServerGameplayConfig Gameplay = new DFMPServerGameplayConfig();

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ServerName))
                ServerName = "Tony's DFU RP";

            Port = Port > 0 && Port <= 65535 ? Port : 7777;
            DiscoveryPort = DiscoveryPort > 0 && DiscoveryPort <= 65535 ? DiscoveryPort : 7778;
            MaxConnections = MaxConnections > 0 && MaxConnections <= 128 ? MaxConnections : 16;
            TickRate = TickRate >= 10 && TickRate <= 120 ? TickRate : 30;
            HeartbeatInterval = HeartbeatInterval > 0f ? HeartbeatInterval : 5.0f;
            LanDiscoveryEnabled = true;
            if (string.IsNullOrWhiteSpace(Motd))
                Motd = "Welcome to Daggerfall Unity Multiplayer";

            if (Chat == null)
                Chat = new DFMPServerChatConfig();

            Chat.MaxMessageLength = Chat.MaxMessageLength > 0 && Chat.MaxMessageLength <= 2048 ? Chat.MaxMessageLength : 256;
            Chat.MinIntervalSeconds = Chat.MinIntervalSeconds > 0f ? Chat.MinIntervalSeconds : 1.0f;

            if (Identity == null)
                Identity = new DFMPServerIdentityConfig();

            Identity.ServerWorldId = string.IsNullOrWhiteSpace(Identity.ServerWorldId) ? "default" : Identity.ServerWorldId.Trim();
            Identity.AllowedAccountIds = Identity.AllowedAccountIds ?? new string[0];

            if (Gameplay == null)
                Gameplay = new DFMPServerGameplayConfig();
        }

        public static string GetConfigFilePath()
        {
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
                    {
                        config.Normalize();
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Config] Error loading config from '{filePath}': {ex.Message}. Falling back to defaults.");
            }

            var defaultConfig = new DFMPServerConfig();
            defaultConfig.Normalize();
            Save(defaultConfig, filePath);
            return defaultConfig;
        }

        public static void Save(DFMPServerConfig config, string customPath = null)
        {
            if (config == null)
                return;

            config.Normalize();
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

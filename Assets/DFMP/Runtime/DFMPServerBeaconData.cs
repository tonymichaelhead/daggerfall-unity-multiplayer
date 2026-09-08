using System;
using System.Text;
using UnityEngine;

namespace DFMP.Runtime
{
    [Serializable]
    public struct DFMPServerBeaconData
    {
        public const string PingHeader = "DFMP_PING";
        public const string PongHeader = "DFMP_PONG";
        public const int ProtocolVersion = 1;

        public string Header;
        public int Version;
        public string ServerName;
        public int CurrentPlayers;
        public int MaxPlayers;
        public int Port;
        public string Motd;

        public static byte[] CreatePingPacket()
        {
            var beacon = new DFMPServerBeaconData
            {
                Header = PingHeader,
                Version = ProtocolVersion
            };
            string json = JsonUtility.ToJson(beacon);
            return Encoding.UTF8.GetBytes(json);
        }

        public static byte[] CreatePongPacket(string serverName, int currentPlayers, int maxPlayers, int port, string motd)
        {
            var beacon = new DFMPServerBeaconData
            {
                Header = PongHeader,
                Version = ProtocolVersion,
                ServerName = serverName ?? string.Empty,
                CurrentPlayers = currentPlayers,
                MaxPlayers = maxPlayers,
                Port = port,
                Motd = motd ?? string.Empty
            };
            string json = JsonUtility.ToJson(beacon);
            return Encoding.UTF8.GetBytes(json);
        }

        public static bool TryParse(byte[] data, int length, out DFMPServerBeaconData beacon)
        {
            beacon = default(DFMPServerBeaconData);
            if (data == null || length <= 0)
                return false;

            try
            {
                string json = Encoding.UTF8.GetString(data, 0, length);
                if (!json.StartsWith("{", StringComparison.Ordinal) || !json.Contains(PingHeader) && !json.Contains(PongHeader))
                    return false;

                beacon = JsonUtility.FromJson<DFMPServerBeaconData>(json);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

using System;
using UnityEngine;

namespace DFCoop.Runtime
{
    /// <summary>
    /// Pure, decoupled parser and configuration container for dedicated server command-line options.
    /// Can be unit tested in isolation without spinning up Unity scenes or runtimes.
    /// </summary>
    public struct ServerCommandLineArgs
    {
        public bool IsDedicatedServer;
        public bool IsClient;
        public string Address;
        public int Port;
        public int TickRate;
        public int MaxConnections;
        public string Arena2Path;
        public float HeartbeatInterval;

        public static ServerCommandLineArgs Default => new ServerCommandLineArgs
        {
            IsDedicatedServer = false,
            IsClient = false,
            Address = "127.0.0.1",
            Port = 7777,
            TickRate = 30,
            MaxConnections = 16,
            Arena2Path = null,
            HeartbeatInterval = 5.0f
        };

        public static ServerCommandLineArgs Parse(string[] args, bool isBatchMode = false)
        {
            var result = Default;

            if (args == null || args.Length == 0)
            {
                if (isBatchMode)
                    result.IsDedicatedServer = true;

                return result;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.IsNullOrEmpty(args[i]))
                    continue;

                string arg = args[i].Trim().ToLowerInvariant();

                if (arg == "-server" || arg == "-dedicated" || arg == "--server" || arg == "--dedicated")
                {
                    result.IsDedicatedServer = true;
                    result.IsClient = false;
                }
                else if (arg == "-client" || arg == "--client" || arg == "-connect" || arg == "--connect")
                {
                    result.IsClient = true;
                    result.IsDedicatedServer = false;
                }
                else if ((arg == "-address" || arg == "--address" || arg == "-host" || arg == "--host") && i + 1 < args.Length)
                {
                    if (!string.IsNullOrEmpty(args[i + 1]))
                        result.Address = args[i + 1];
                }
                else if ((arg == "-port" || arg == "--port") && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int port) && port > 0 && port <= 65535)
                        result.Port = port;
                }
                else if ((arg == "-tickrate" || arg == "--tickrate") && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int tickrate))
                        result.TickRate = Mathf.Clamp(tickrate, 10, 120);
                }
                else if ((arg == "-maxconnections" || arg == "--maxconnections" || arg == "-maxplayers" || arg == "--maxplayers") && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int maxConnections))
                        result.MaxConnections = Mathf.Clamp(maxConnections, 1, 128);
                }
                else if ((arg == "-arena2" || arg == "--arena2") && i + 1 < args.Length)
                {
                    result.Arena2Path = args[i + 1];
                }
                else if ((arg == "-heartbeat" || arg == "--heartbeat") && i + 1 < args.Length)
                {
                    if (float.TryParse(args[i + 1], out float interval))
                        result.HeartbeatInterval = Mathf.Max(1.0f, interval);
                }
            }

            if (isBatchMode && !result.IsDedicatedServer && !result.IsClient)
                result.IsDedicatedServer = true;

            return result;
        }
    }
}

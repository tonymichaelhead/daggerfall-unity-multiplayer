using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace DFMP.Runtime
{
    public struct DFMPServerInfo
    {
        public string ServerName;
        public string EndPoint;
        public string IpAddress;
        public int Port;
        public int CurrentPlayers;
        public int MaxPlayers;
        public string Motd;
        public int PingMs;
        public DateTime LastSeen;

        public string DisplayString
        {
            get
            {
                string pingStr = PingMs >= 0 ? $"{PingMs}ms" : "---";
                return $"{ServerName}  [{CurrentPlayers}/{MaxPlayers}]  ({IpAddress}:{Port})  {pingStr}";
            }
        }
    }

    public class DFMPServerDiscoveryClient : IDisposable
    {
        private UdpClient udpClient;
        private Thread receiveThread;
        private volatile bool isSearching;
        private readonly int defaultDiscoveryPort;
        private readonly object lockObject = new object();
        private readonly Dictionary<string, DFMPServerInfo> discoveredServers = new Dictionary<string, DFMPServerInfo>();
        private readonly Dictionary<string, long> pingStartTimes = new Dictionary<string, long>();

        public event Action OnServersUpdated;

        public DFMPServerDiscoveryClient(int defaultDiscoveryPort = 7778)
        {
            this.defaultDiscoveryPort = defaultDiscoveryPort;
        }

        public List<DFMPServerInfo> GetServers()
        {
            lock (lockObject)
            {
                return new List<DFMPServerInfo>(discoveredServers.Values);
            }
        }

        public void StartDiscovery()
        {
            if (isSearching)
                return;

            try
            {
                udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;
                udpClient.Client.ReceiveTimeout = 2000;

                isSearching = true;
                receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "DFMP_ServerDiscoveryClient"
                };
                receiveThread.Start();

                // Send initial broadcasts
                SendBroadcastPing();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Discovery] Failed to start discovery client: {ex.Message}");
                StopDiscovery();
            }
        }

        public void SendBroadcastPing()
        {
            if (udpClient == null)
                return;

            try
            {
                byte[] pingBytes = DFMPServerBeaconData.CreatePingPacket();
                long nowTicks = DateTime.UtcNow.Ticks;

                // 1. Broadcast to LAN
                var broadcastEp = new IPEndPoint(IPAddress.Broadcast, defaultDiscoveryPort);
                lock (lockObject)
                {
                    pingStartTimes[broadcastEp.ToString()] = nowTicks;
                }
                udpClient.Send(pingBytes, pingBytes.Length, broadcastEp);

                // 2. Direct ping to localhost (for local dedicated server testing)
                var localEp = new IPEndPoint(IPAddress.Loopback, defaultDiscoveryPort);
                lock (lockObject)
                {
                    pingStartTimes[localEp.ToString()] = nowTicks;
                }
                udpClient.Send(pingBytes, pingBytes.Length, localEp);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Discovery] Error sending discovery ping: {ex.Message}");
            }
        }

        public void PingDirect(string address, int port)
        {
            if (udpClient == null)
                return;

            try
            {
                if (IPAddress.TryParse(address, out IPAddress ip))
                {
                    byte[] pingBytes = DFMPServerBeaconData.CreatePingPacket();
                    var ep = new IPEndPoint(ip, port);
                    lock (lockObject)
                    {
                        pingStartTimes[ep.ToString()] = DateTime.UtcNow.Ticks;
                    }
                    udpClient.Send(pingBytes, pingBytes.Length, ep);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Discovery] Error pinging direct address '{address}:{port}': {ex.Message}");
            }
        }

        public void StopDiscovery()
        {
            isSearching = false;

            if (udpClient != null)
            {
                try
                {
                    udpClient.Close();
                }
                catch { }
                udpClient = null;
            }

            if (receiveThread != null && receiveThread.IsAlive)
            {
                try
                {
                    receiveThread.Abort();
                }
                catch { }
                receiveThread = null;
            }
        }

        public void Clear()
        {
            lock (lockObject)
            {
                discoveredServers.Clear();
                pingStartTimes.Clear();
            }
        }

        private void ReceiveLoop()
        {
            while (isSearching)
            {
                try
                {
                    var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpClient.Receive(ref remoteEp);

                    if (receivedBytes == null || receivedBytes.Length == 0)
                        continue;

                    if (DFMPServerBeaconData.TryParse(receivedBytes, receivedBytes.Length, out var pongBeacon))
                    {
                        if (pongBeacon.Header == DFMPServerBeaconData.PongHeader)
                        {
                            long receiveTicks = DateTime.UtcNow.Ticks;
                            int pingMs = -1;

                            lock (lockObject)
                            {
                                string epKey = remoteEp.ToString();
                                if (pingStartTimes.TryGetValue(epKey, out long startTicks))
                                {
                                    pingMs = (int)((receiveTicks - startTicks) / TimeSpan.TicksPerMillisecond);
                                }
                                else
                                {
                                    // Try matching by IP or fallback
                                    pingMs = 5;
                                }

                                string serverKey = $"{remoteEp.Address}:{pongBeacon.Port}";
                                var info = new DFMPServerInfo
                                {
                                    ServerName = pongBeacon.ServerName,
                                    EndPoint = serverKey,
                                    IpAddress = remoteEp.Address.ToString(),
                                    Port = pongBeacon.Port,
                                    CurrentPlayers = pongBeacon.CurrentPlayers,
                                    MaxPlayers = pongBeacon.MaxPlayers,
                                    Motd = pongBeacon.Motd,
                                    PingMs = pingMs,
                                    LastSeen = DateTime.UtcNow
                                };

                                discoveredServers[serverKey] = info;
                            }

                            OnServersUpdated?.Invoke();
                        }
                    }
                }
                catch (SocketException)
                {
                    // Socket timeout or closed
                    if (!isSearching)
                        break;
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isSearching)
                        Debug.LogWarning($"[DFMP Discovery Client] Receive error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            StopDiscovery();
        }
    }
}

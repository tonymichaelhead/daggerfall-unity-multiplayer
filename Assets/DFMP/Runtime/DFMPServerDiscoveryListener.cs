using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace DFMP.Runtime
{
    /// <summary>
    /// UDP listener that responds to LAN broadcast discovery pings (DFMP_PING)
    /// with server information, player counts, and connection details.
    /// </summary>
    public class DFMPServerDiscoveryListener : IDisposable
    {
        private UdpClient udpClient;
        private Thread listenThread;
        private volatile bool isRunning;

        private readonly int discoveryPort;
        private readonly int gamePort;
        private readonly Func<string> getServerName;
        private readonly Func<int> getCurrentPlayers;
        private readonly Func<int> getMaxPlayers;
        private readonly Func<string> getMotd;

        public bool IsRunning => isRunning;

        public DFMPServerDiscoveryListener(
            int discoveryPort,
            int gamePort,
            Func<string> getServerName,
            Func<int> getCurrentPlayers,
            Func<int> getMaxPlayers,
            Func<string> getMotd = null)
        {
            this.discoveryPort = discoveryPort;
            this.gamePort = gamePort;
            this.getServerName = getServerName;
            this.getCurrentPlayers = getCurrentPlayers;
            this.getMaxPlayers = getMaxPlayers;
            this.getMotd = getMotd;
        }

        public void Start()
        {
            if (isRunning)
                return;

            try
            {
                udpClient = new UdpClient();
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
                udpClient.EnableBroadcast = true;

                isRunning = true;
                listenThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "DFMP_ServerDiscoveryListener"
                };
                listenThread.Start();

                Debug.Log($"[DFMP Discovery] Discovery listener started on UDP port {discoveryPort} (Advertising Game Port {gamePort}).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DFMP Discovery] Failed to bind discovery listener on port {discoveryPort}: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            isRunning = false;

            if (udpClient != null)
            {
                try
                {
                    udpClient.Close();
                }
                catch { }
                udpClient = null;
            }

            if (listenThread != null && listenThread.IsAlive)
            {
                try
                {
                    listenThread.Abort();
                }
                catch { }
                listenThread = null;
            }
        }

        private void ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpClient.Receive(ref remoteEp);

                    if (receivedBytes == null || receivedBytes.Length == 0)
                        continue;

                    if (DFMPServerBeaconData.TryParse(receivedBytes, receivedBytes.Length, out var requestBeacon))
                    {
                        if (requestBeacon.Header == DFMPServerBeaconData.PingHeader)
                        {
                            string sName = getServerName != null ? getServerName() : "DFMP Server";
                            int curPlayers = getCurrentPlayers != null ? getCurrentPlayers() : 0;
                            int maxP = getMaxPlayers != null ? getMaxPlayers() : 16;
                            string motd = getMotd != null ? getMotd() : string.Empty;

                            byte[] responseBytes = DFMPServerBeaconData.CreatePongPacket(sName, curPlayers, maxP, gamePort, motd);
                            udpClient.Send(responseBytes, responseBytes.Length, remoteEp);
                        }
                    }
                }
                catch (SocketException)
                {
                    // Socket closed or interrupted
                    break;
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isRunning)
                        Debug.LogWarning($"[DFMP Discovery] Listen error: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

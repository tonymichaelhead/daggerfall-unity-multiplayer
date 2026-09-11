using NUnit.Framework;
using DFMP.Runtime;
using System.IO;

namespace DFMP.Tests
{
    [TestFixture]
    public class DFMPServerConfigAndDiscoveryTests
    {
        [Test]
        public void ServerConfig_DefaultValues_AreCorrect()
        {
            var config = new DFMPServerConfig();
            Assert.AreEqual("Tony's DFU RP", config.ServerName);
            Assert.AreEqual(7777, config.Port);
            Assert.AreEqual(7778, config.DiscoveryPort);
            Assert.AreEqual(16, config.MaxConnections);
            Assert.AreEqual(30, config.TickRate);
            Assert.AreEqual(5.0f, config.HeartbeatInterval);
            Assert.IsTrue(config.LanDiscoveryEnabled);
            Assert.IsFalse(string.IsNullOrEmpty(config.Motd));
            Assert.NotNull(config.StartingLocation);
            Assert.AreEqual(DFMPStartingLocationModes.LocationCenter, config.StartingLocation.Mode);
            Assert.AreEqual("Daggerfall", config.StartingLocation.RegionName);
            Assert.AreEqual("Daggerfall", config.StartingLocation.LocationName);
            Assert.NotNull(config.Rest);
            Assert.AreEqual(DFMPRestPolicies.Disabled, config.Rest.Policy);
            Assert.NotNull(config.Developer);
            Assert.IsFalse(config.Developer.CommandsEnabled);
        }

        [Test]
        public void ServerConfig_NormalizesRestPolicy()
        {
            var config = new DFMPServerConfig
            {
                Rest = new DFMPServerRestConfig { Policy = "servermanaged" }
            };

            config.Normalize();

            Assert.AreEqual(DFMPRestPolicies.ServerManaged, config.Rest.Policy);

            config.Rest.Policy = "unsupported";
            config.Normalize();

            Assert.AreEqual(DFMPRestPolicies.Disabled, config.Rest.Policy);
        }

        [Test]
        public void ServerConfig_SaveAndLoad_RoundtripsSuccessfully()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"dfmp_test_config_{System.Guid.NewGuid()}.json");

            try
            {
                var original = new DFMPServerConfig
                {
                    ServerName = "Custom Server Test",
                    Port = 8888,
                    DiscoveryPort = 8889,
                    MaxConnections = 24,
                    TickRate = 60,
                    Motd = "Custom MOTD Test"
                };

                DFMPServerConfig.Save(original, tempFile);
                Assert.IsTrue(File.Exists(tempFile));

                var loaded = DFMPServerConfig.LoadOrCreate(tempFile);
                Assert.AreEqual("Custom Server Test", loaded.ServerName);
                Assert.AreEqual(8888, loaded.Port);
                Assert.AreEqual(8889, loaded.DiscoveryPort);
                Assert.AreEqual(24, loaded.MaxConnections);
                Assert.AreEqual(60, loaded.TickRate);
                Assert.AreEqual("Custom MOTD Test", loaded.Motd);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Test]
        public void ServerCommandLineArgs_ParsesServerNameFlag()
        {
            string[] args = new string[] { "-server", "-name", "Tony's DFU RP", "-port", "7777" };
            var result = ServerCommandLineArgs.Parse(args, false);

            Assert.IsTrue(result.IsDedicatedServer);
            Assert.AreEqual("Tony's DFU RP", result.ServerName);
            Assert.AreEqual(7777, result.Port);
        }

        [Test]
        public void BeaconData_PingPacket_SerializesAndParses()
        {
            byte[] pingPacket = DFMPServerBeaconData.CreatePingPacket();
            Assert.IsNotNull(pingPacket);
            Assert.IsTrue(pingPacket.Length > 0);

            bool success = DFMPServerBeaconData.TryParse(pingPacket, pingPacket.Length, out var beacon);
            Assert.IsTrue(success);
            Assert.AreEqual(DFMPServerBeaconData.PingHeader, beacon.Header);
            Assert.AreEqual(DFMPServerBeaconData.ProtocolVersion, beacon.Version);
        }

        [Test]
        public void BeaconData_PongPacket_SerializesAndParsesPayload()
        {
            byte[] pongPacket = DFMPServerBeaconData.CreatePongPacket("Tony's DFU RP", 3, 16, 7777, "Welcome!");
            Assert.IsNotNull(pongPacket);
            Assert.IsTrue(pongPacket.Length > 0);

            bool success = DFMPServerBeaconData.TryParse(pongPacket, pongPacket.Length, out var beacon);
            Assert.IsTrue(success);
            Assert.AreEqual(DFMPServerBeaconData.PongHeader, beacon.Header);
            Assert.AreEqual(DFMPServerBeaconData.ProtocolVersion, beacon.Version);
            Assert.AreEqual("Tony's DFU RP", beacon.ServerName);
            Assert.AreEqual(3, beacon.CurrentPlayers);
            Assert.AreEqual(16, beacon.MaxPlayers);
            Assert.AreEqual(7777, beacon.Port);
            Assert.AreEqual("Welcome!", beacon.Motd);
        }

        [Test]
        public void ServerInfo_DisplayString_FormatsCorrectly()
        {
            var info = new DFMPServerInfo
            {
                ServerName = "Tony's DFU RP",
                IpAddress = "127.0.0.1",
                Port = 7777,
                CurrentPlayers = 3,
                MaxPlayers = 16,
                PingMs = 12
            };

            string display = info.DisplayString;
            Assert.IsTrue(display.Contains("Tony's DFU RP"));
            Assert.IsTrue(display.Contains("[3/16]"));
            Assert.IsTrue(display.Contains("(127.0.0.1:7777)"));
            Assert.IsTrue(display.Contains("12ms"));
        }

        [Test]
        public void ServerConfig_NormalizesStartingLocation()
        {
            var config = new DFMPServerConfig
            {
                StartingLocation = new DFMPServerStartingLocationConfig
                {
                    Mode = "explicitworldcoordinates",
                    RegionName = " Daggerfall ",
                    LocationName = " Castle Daggerfall ",
                    MarkerName = " West Gate "
                }
            };

            config.Normalize();

            Assert.AreEqual(DFMPStartingLocationModes.ExplicitWorldCoordinates, config.StartingLocation.Mode);
            Assert.AreEqual("Daggerfall", config.StartingLocation.RegionName);
            Assert.AreEqual("Castle Daggerfall", config.StartingLocation.LocationName);
            Assert.AreEqual("West Gate", config.StartingLocation.MarkerName);
        }
    }
}

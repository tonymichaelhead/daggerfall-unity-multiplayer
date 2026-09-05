using NUnit.Framework;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DFCoop.Runtime;
using UnityEngine;

namespace DFCoop.Tests
{
    [TestFixture]
    public class ServerCommandLineArgsTests
    {
        [Test]
        public void DefaultValues_AreReasonable()
        {
            var def = ServerCommandLineArgs.Default;

            Assert.IsFalse(def.IsDedicatedServer);
            Assert.IsFalse(def.IsClient);
            Assert.AreEqual("127.0.0.1", def.Address);
            Assert.AreEqual(7777, def.Port);
            Assert.AreEqual(30, def.TickRate);
            Assert.AreEqual(16, def.MaxConnections);
            Assert.IsNull(def.Arena2Path);
            Assert.AreEqual(5.0f, def.HeartbeatInterval);
        }

        [Test]
        public void Parse_NullOrEmptyArgs_ReturnsDefaults()
        {
            var result = ServerCommandLineArgs.Parse(null, false);
            Assert.IsFalse(result.IsDedicatedServer);
            Assert.AreEqual(7777, result.Port);

            result = ServerCommandLineArgs.Parse(new string[0], false);
            Assert.IsFalse(result.IsDedicatedServer);
        }

        [Test]
        public void Parse_MaxConnectionsClamping_Works()
        {
            string[] args = new string[] { "-server", "-maxconnections", "0" };
            var result = ServerCommandLineArgs.Parse(args, false);
            Assert.AreEqual(1, result.MaxConnections);

            args = new string[] { "-server", "--maxplayers", "256" };
            result = ServerCommandLineArgs.Parse(args, false);
            Assert.AreEqual(128, result.MaxConnections);

            args = new string[] { "-server", "-maxconnections", "32" };
            result = ServerCommandLineArgs.Parse(args, false);
            Assert.AreEqual(32, result.MaxConnections);
        }

        [Test]
        public void Parse_BatchMode_SetsDedicatedServer()
        {
            var result = ServerCommandLineArgs.Parse(new string[0], true);
            Assert.IsTrue(result.IsDedicatedServer);
            Assert.IsFalse(result.IsClient);
        }

        [Test]
        public void Parse_ClientFlag_EnablesClientAndOverridesBatchServerDefault()
        {
            string[] args = new string[] { "-client" };
            var result = ServerCommandLineArgs.Parse(args, true);

            Assert.IsTrue(result.IsClient);
            Assert.IsFalse(result.IsDedicatedServer);
            Assert.AreEqual("127.0.0.1", result.Address);
        }

        [Test]
        public void Parse_ClientAddress_ParsedCorrectly()
        {
            string[] args = new string[] { "-client", "-address", "192.168.1.10", "-port", "8888" };
            var result = ServerCommandLineArgs.Parse(args, true);

            Assert.IsTrue(result.IsClient);
            Assert.IsFalse(result.IsDedicatedServer);
            Assert.AreEqual("192.168.1.10", result.Address);
            Assert.AreEqual(8888, result.Port);
        }

        [Test]
        public void Parse_ServerFlag_EnablesDedicatedServer()
        {
            string[] args = new string[] { "-server" };
            var result = ServerCommandLineArgs.Parse(args, false);
            Assert.IsTrue(result.IsDedicatedServer);

            args = new string[] { "--dedicated" };
            result = ServerCommandLineArgs.Parse(args, false);
            Assert.IsTrue(result.IsDedicatedServer);
        }

        [Test]
        public void Parse_CustomPort_ParsedCorrectly()
        {
            string[] args = new string[] { "-server", "-port", "8888" };
            var result = ServerCommandLineArgs.Parse(args, false);

            Assert.IsTrue(result.IsDedicatedServer);
            Assert.AreEqual(8888, result.Port);
        }

        [Test]
        public void Parse_InvalidPort_RetainsDefault()
        {
            string[] args = new string[] { "-server", "-port", "invalid" };
            var result = ServerCommandLineArgs.Parse(args, false);

            Assert.AreEqual(7777, result.Port);
        }

        [Test]
        public void Parse_TickrateClamping_Works()
        {
            // Below min (10)
            string[] args = new string[] { "-server", "-tickrate", "2" };
            var result = ServerCommandLineArgs.Parse(args, false);
            Assert.AreEqual(10, result.TickRate);

            // Above max (120)
            args = new string[] { "-server", "-tickrate", "240" };
            result = ServerCommandLineArgs.Parse(args, false);
            Assert.AreEqual(120, result.TickRate);

            // Valid
            args = new string[] { "-server", "-tickrate", "60" };
            result = ServerCommandLineArgs.Parse(args, false);
            Assert.AreEqual(60, result.TickRate);
        }

        [Test]
        public void Parse_Arena2Path_ParsedProperly()
        {
            string[] args = new string[] { "-server", "-arena2", @"C:\Games\Daggerfall\ARENA2" };
            var result = ServerCommandLineArgs.Parse(args, false);

            Assert.AreEqual(@"C:\Games\Daggerfall\ARENA2", result.Arena2Path);
        }

        [Test]
        public void TimeSnapshot_FromWorldTime_UsesClassicMinutesAndTimeScale()
        {
            GameObject go = new GameObject("DFCoop_TimeSnapshotTest");

            try
            {
                var worldTime = go.AddComponent<WorldTime>();
                worldTime.DaggerfallDateTime = new DaggerfallDateTime();
                worldTime.DaggerfallDateTime.FromClassicDaggerfallTime(523530);
                worldTime.TimeScale = 8f;

                var snapshot = DFCoopTimeSnapshot.FromWorldTime(worldTime);

                Assert.AreEqual(523530, snapshot.ClassicMinutes);
                Assert.AreEqual(8f, snapshot.TimeScale);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TimeSnapshot_ApplyToWorldTime_SetsClassicMinutesAndTimeScale()
        {
            GameObject go = new GameObject("DFCoop_TimeSnapshotApplyTest");

            try
            {
                var worldTime = go.AddComponent<WorldTime>();
                worldTime.DaggerfallDateTime = new DaggerfallDateTime();

                var snapshot = new DFCoopTimeSnapshot
                {
                    ClassicMinutes = 600000,
                    TimeScale = 4f
                };

                snapshot.ApplyToWorldTime(worldTime);

                Assert.AreEqual(600000, worldTime.DaggerfallDateTime.ToClassicDaggerfallTime());
                Assert.AreEqual(4f, worldTime.TimeScale);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

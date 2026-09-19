using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Serialization;
using DFMP.Runtime;
using NUnit.Framework;

namespace DFMP.Tests
{
    [TestFixture]
    public class QuestLocationRevealTests
    {
        [Test]
        public void LocalBuilding_ShouldDiscoverBuilding()
        {
            Assert.IsTrue(DFMPQuestLocationReveal.ShouldDiscoverBuilding(
                197899,
                "The Woodwing Residence"));
        }

        [Test]
        public void RemoteDungeonWithoutBuilding_ShouldNotDiscoverBuilding()
        {
            Assert.IsFalse(DFMPQuestLocationReveal.ShouldDiscoverBuilding(0, null));
            Assert.IsFalse(DFMPQuestLocationReveal.ShouldDiscoverBuilding(0, "The Hole of Balul"));
            Assert.IsFalse(DFMPQuestLocationReveal.ShouldDiscoverBuilding(12, string.Empty));
        }

        [Test]
        public void TryGetBuildingDiscovery_RejectsNullPlace()
        {
            int buildingKey;
            string buildingName;
            Assert.IsFalse(DFMPQuestLocationReveal.TryGetBuildingDiscovery(
                null,
                out buildingKey,
                out buildingName));
            Assert.AreEqual(0, buildingKey);
            Assert.AreEqual(string.Empty, buildingName);
        }

        [Test]
        public void QuestStateEnvelope_RoundTripsNamedDiscoveredBuilding()
        {
            var buildings = new Dictionary<int, PlayerGPS.DiscoveredBuilding>
            {
                {
                    197899,
                    new PlayerGPS.DiscoveredBuilding
                    {
                        buildingKey = 197899,
                        displayName = "The Woodwing Residence",
                        isOverrideName = true
                    }
                }
            };
            var locations = new Dictionary<int, PlayerGPS.DiscoveredLocation>
            {
                {
                    12345,
                    new PlayerGPS.DiscoveredLocation
                    {
                        mapID = 1,
                        mapPixelID = 12345,
                        regionName = "Daggerfall",
                        locationName = "Daggerfall",
                        discoveredBuildings = buildings
                    }
                }
            };

            string discoveryJson = SaveLoadManager.Serialize(
                typeof(Dictionary<int, PlayerGPS.DiscoveredLocation>),
                locations,
                false);

            var envelope = new DFMPQuestStateEnvelope
            {
                Version = DFMPQuestStateEnvelope.CurrentVersion,
                QuestMachineJson = "{\"siteLinks\":[],\"quests\":[]}",
                DiscoveryJson = discoveryJson
            };

            string payload = DFMPQuestStateCodec.Encode(envelope);
            DFMPQuestStateEnvelope decoded;
            string reason;
            Assert.IsTrue(DFMPQuestStateCodec.TryDecode(payload, out decoded, out reason), reason);
            Assert.IsFalse(string.IsNullOrEmpty(decoded.DiscoveryJson));

            var restored = SaveLoadManager.Deserialize(
                typeof(Dictionary<int, PlayerGPS.DiscoveredLocation>),
                decoded.DiscoveryJson) as Dictionary<int, PlayerGPS.DiscoveredLocation>;
            Assert.NotNull(restored);
            Assert.IsTrue(restored.ContainsKey(12345));
            Assert.NotNull(restored[12345].discoveredBuildings);
            Assert.IsTrue(restored[12345].discoveredBuildings.ContainsKey(197899));
            Assert.AreEqual(
                "The Woodwing Residence",
                restored[12345].discoveredBuildings[197899].displayName);
            Assert.IsTrue(restored[12345].discoveredBuildings[197899].isOverrideName);
        }
    }
}

using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPQuestLocationReveal
    {
        public static bool ShouldDiscoverBuilding(int buildingKey, string buildingName)
        {
            return buildingKey != 0 && !string.IsNullOrEmpty(buildingName);
        }

        public static bool TryGetBuildingDiscovery(
            object placeObject,
            out int buildingKey,
            out string buildingName)
        {
            buildingKey = 0;
            buildingName = string.Empty;

            Place place = placeObject as Place;
            if (place == null)
                return false;

            buildingKey = place.SiteDetails.buildingKey;
            buildingName = place.SiteDetails.buildingName;
            return ShouldDiscoverBuilding(buildingKey, buildingName);
        }

        public static void HandleRevealedPlace(object placeObject)
        {
            Place place = placeObject as Place;
            if (place == null)
                return;

            int buildingKey;
            string buildingName;
            if (TryGetBuildingDiscovery(place, out buildingKey, out buildingName) &&
                GameManager.HasInstance &&
                GameManager.Instance.PlayerGPS != null)
            {
                GameManager.Instance.PlayerGPS.DiscoverBuilding(buildingKey, buildingName);
                Debug.Log("[DFMP Quest] Revealed building '" + buildingName + "'.");
            }

            DFMPPositionReporter.RequestQuestSave();
        }
    }
}

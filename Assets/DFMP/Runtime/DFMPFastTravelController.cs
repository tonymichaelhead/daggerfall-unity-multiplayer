using DaggerfallConnect.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DFMP.Hooks;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPFastTravelController
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            DaggerfallHooks.TryHandleFastTravel = TryHandleFastTravel;
        }

        static bool TryHandleFastTravel(object travelPopupObject)
        {
            if (!NetworkClient.isConnected || !NetworkClient.ready)
                return false;

            var travelPopup = travelPopupObject as DaggerfallTravelPopUp;
            if (travelPopup == null)
                return false;

            DFPosition endPos = travelPopup.EndPos;
            int mapPixelX = (int)endPos.X;
            int mapPixelY = (int)endPos.Y;
            if (!DFMPSpawnProtocol.IsValidMapPixel(mapPixelX, mapPixelY))
            {
                Debug.LogWarning($"[DFMP Transition] Rejected local fast travel request with invalid map pixel: mapPixel={mapPixelX}/{mapPixelY}.");
                return true;
            }

            NetworkClient.Send(new DFMPFastTravelRequest
            {
                MapPixelX = mapPixelX,
                MapPixelY = mapPixelY
            });

            DaggerfallUI.Instance.UserInterfaceManager.PopWindow();
            if (travelPopup.TravelWindow != null)
                travelPopup.TravelWindow.CloseTravelWindows(true);
            if (DaggerfallUI.Instance.FadeBehaviour != null)
                DaggerfallUI.Instance.FadeBehaviour.FadeHUDFromBlack();

            Debug.Log($"[DFMP Transition] Client requested server fast travel: mapPixel={mapPixelX}/{mapPixelY}.");
            return true;
        }
    }
}

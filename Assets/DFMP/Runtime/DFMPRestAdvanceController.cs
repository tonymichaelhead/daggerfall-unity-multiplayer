using DaggerfallWorkshop.Game;
using DFMP.Hooks;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPRestAdvanceController
    {
        const string RestManagedMessage = "Rest is server-managed in multiplayer.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            DaggerfallHooks.TryHandleRestAdvance = TryHandleRestAdvance;
        }

        static bool TryHandleRestAdvance(string restModeName)
        {
            DFMPRestAdvanceMode mode;
            if (!DFMPRestAdvancePolicy.TryParseMode(restModeName, out mode))
                return false;

            if (!DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(NetworkClient.isConnected, mode))
                return false;

            DaggerfallUI.MessageBox(RestManagedMessage, true);
            Debug.Log($"[DFMP Rest] Blocked client-side rest/loiter time advancement: mode={mode}.");
            return true;
        }
    }
}

using DFMP.Hooks;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public static class DFMPRestAdvanceController
    {
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

            DFMPNetworkClient.RequestRest(restModeName);
            Debug.Log($"[DFMP Rest] Blocked client-side rest/loiter time advancement: mode={mode}.");
            return true;
        }
    }
}

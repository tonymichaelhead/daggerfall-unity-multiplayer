using DFMP.Hooks;
using DaggerfallWorkshop.Game;
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
            DaggerfallHooks.TryHandleTimeAdvance = TryHandleTimeAdvance;
            DaggerfallHooks.TryHandleRestWorldTimeTick = TryHandleRestWorldTimeTick;
            DaggerfallHooks.OnRestHourElapsed = OnRestHourElapsed;
        }

        static bool TryHandleTimeAdvance(string source, int seconds)
        {
            if (!DFMPTimeAdvancePolicy.ShouldConsumeClientAdvance(NetworkClient.isConnected, source, seconds))
                return false;

            Debug.Log($"[DFMP Time] Blocked client-side time advancement: source={source}, seconds={seconds}.");
            return true;
        }

        static bool TryHandleRestWorldTimeTick()
        {
            if (!DFMPRestAdvancePolicy.ShouldSkipRestWorldTime(NetworkClient.isConnected))
                return false;

            return true;
        }

        static bool TryHandleRestAdvance(string restModeName)
        {
            DFMPRestAdvanceMode mode;
            if (!DFMPRestAdvancePolicy.TryParseMode(restModeName, out mode))
                return false;

            if (!DFMPRestAdvancePolicy.ShouldConsumeRestAdvance(NetworkClient.isConnected, mode))
                return false;

            ShowBlockedMessage(DFMPRestAdvancePolicy.GetBlockedRestMessage(mode));
            Debug.Log($"[DFMP Rest] Blocked client-side rest/loiter: mode={mode}, policy={DFMPWorldSettings.CurrentRestPolicy}.");
            return true;
        }

        static void OnRestHourElapsed()
        {
            DFMPRestSessionController.NotifyHourElapsed();
        }

        static void ShowBlockedMessage(string message)
        {
            if (DaggerfallUI.Instance == null)
                return;

            DaggerfallUI.MessageBox(message);
        }
    }
}

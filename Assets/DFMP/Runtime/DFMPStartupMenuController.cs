using UnityEngine;
using DaggerfallWorkshop.Game.UserInterface;
using DFMP.Hooks;

namespace DFMP.Runtime
{
    /// <summary>
    /// Replaces the DFU launcher "Play" action with the multiplayer server list.
    /// The single-player start sequence (title menu, splash video, save selection) is never reachable.
    /// </summary>
    public static class DFMPStartupMenuController
    {
        public const string LaunchButtonText = "Join Server";

        static readonly Vector2 LaunchButtonSize = new Vector2(72, 12);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer)
                return;

            DaggerfallHooks.ShouldForceStartupMenu = ShouldForceStartupMenu;
            DaggerfallHooks.ConfigureStartupLaunchButton = ConfigureStartupLaunchButton;
            DaggerfallHooks.TryHandleStartupLaunch = TryHandleStartupLaunch;
        }

        static bool ShouldForceStartupMenu()
        {
            // Without this the launcher can skip straight into the single-player game scene.
            return true;
        }

        static void ConfigureStartupLaunchButton(object launchButton)
        {
            Button button = launchButton as Button;
            if (button == null)
                return;

            button.Label.Text = LaunchButtonText;
            button.Size = LaunchButtonSize;
        }

        static bool TryHandleStartupLaunch()
        {
            DFMPServerListInputHandler.OpenServerList();
            return true;
        }
    }
}

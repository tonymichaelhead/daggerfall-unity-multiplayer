using System;
using UnityEngine;

namespace DFMP.Hooks
{
    /// <summary>
    /// Central event dispatcher for lightweight, additive hooks placed into upstream DFU core scripts.
    /// Core DFU scripts (Assets/Scripts/**) only invoke these events; they do not contain multiplayer logic.
    /// </summary>
    public static class DaggerfallHooks
    {
        // Example hook signatures will be added here as needed when instrumenting core systems:
        // public static Action<GameObject, object> OnEnemySpawned;
        // public static Action<ulong> OnWorldTimeChanged;
        // public static Func<bool> IsHeadlessDedicatedServer;
        public static Func<object, bool> TryHandleFastTravel;
        public static Func<string, bool> TryHandleRestAdvance;
        public static Func<string, int, bool> TryHandleTimeAdvance;
        public static Func<bool> TryHandleVampirismTransformation;
        public static Func<object, object, object, bool, bool, bool> TryHandleBuildingInteriorTransition;
        public static Func<object, bool, bool> TryHandleBuildingExteriorTransition;
        public static Func<object, object, object, object, bool, bool> TryHandleDungeonInteriorTransition;
        public static Func<object, bool, bool> TryHandleDungeonExteriorTransition;
        public static Func<bool> ShouldForceStartupMenu;
        public static Action<object> ConfigureStartupLaunchButton;
        public static Func<bool> TryHandleStartupLaunch;
    }
}

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
    }
}

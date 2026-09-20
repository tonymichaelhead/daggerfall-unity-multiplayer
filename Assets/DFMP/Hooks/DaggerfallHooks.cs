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
        public static Func<bool> TryHandleRestWorldTimeTick;
        public static Action OnRestHourElapsed;
        public static Func<string, int, bool> TryHandleTimeAdvance;
        public static Func<bool> TryHandleVampirismTransformation;
        public static Func<object, bool> ShouldSuppressQuestClockExpiry;
        public static Func<object, string> TryFormatQuestClockJournal;
        public static Func<object, object, object, Vector3, int, int> TryHandleQuestTeleport;
        public static Func<object, object, int, int, string, bool> TryHandleQuestFoeCommand;
        public static Func<string, bool> TryHandleUnsupportedQuestSceneAction;
        public static Func<bool> TryHandlePlayerDeath;
        public static Func<object, bool> TryHandlePlayerMissileHit;
        public static Func<object, object, object, bool, bool, bool> TryHandlePlayerWeaponHit;
        public static Func<object, object, object, bool, bool, bool> TryHandleBuildingInteriorTransition;
        public static Func<object, bool, bool> TryHandleBuildingExteriorTransition;
        public static Func<object, object, object, object, bool, bool> TryHandleDungeonInteriorTransition;
        public static Func<object, bool, bool> TryHandleDungeonExteriorTransition;
        public static Func<bool> ShouldForceStartupMenu;
        public static Action<object> ConfigureStartupLaunchButton;
        public static Func<bool> TryHandleStartupLaunch;
        // When bound and false, DFU hides Start In Dungeon and never uses it for new-character startup.
        public static Func<bool> IsStartInDungeonEnabled;
        public static Action<ulong, bool> OnActionDoorToggled;
        public static Action OnGuildMembershipChanged;
        public static Action<object> OnQuestLocationRevealed;
        public static Func<bool> TryHandleExitGame;
        public static Func<object, object, object, object, bool> TryHandlePlacedQuestFoe;
        // Args: action, foe, native placement point, native ground contact under that point, spawn generation, spawn index.
        public static Func<object, object, Vector3, Vector3, int, int, bool> TryHandleDynamicQuestFoePlacement;
    }
}

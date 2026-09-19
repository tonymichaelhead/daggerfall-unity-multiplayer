using DaggerfallConnect;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPRestSessionController : MonoBehaviour
    {
        public const float RestingInterruptDistance = 12f;

        static DFMPRestSessionController instance;
        DaggerfallRestWindow.RestModes lastRestMode = DaggerfallRestWindow.RestModes.Selection;
        DaggerfallRestWindow activeRestWindow;
        int lastSnapshotHealth = -1;
        string lastRestPolicy = DFMPRestPolicies.Disabled;

        public static void EnsureInstance()
        {
            if (instance != null)
                return;

            GameObject controllerGo = new GameObject("DFMP_RestSessionController");
            Object.DontDestroyOnLoad(controllerGo);
            instance = controllerGo.AddComponent<DFMPRestSessionController>();
        }

        public static void Reset()
        {
            if (instance != null)
                Destroy(instance.gameObject);

            instance = null;
        }

        public static void NotifyHourElapsed()
        {
            if (instance == null || !NetworkClient.isConnected)
                return;

            instance.SendRestRequest(instance.ResolveActiveRestModeName(), DFMPRestRequestKind.HourElapsed);
        }

        public static void NotifyRequestRejected(DFMPRestRequestKind kind)
        {
            if (instance == null || kind != DFMPRestRequestKind.Start)
                return;

            instance.CloseActiveRest();
        }

        public static void NotifyVitalSnapshot(int health)
        {
            if (instance == null)
                return;

            instance.HandleVitalSnapshot(health);
        }

        void OnDestroy()
        {
            if (lastRestMode != DaggerfallRestWindow.RestModes.Selection)
                SendRestRequest(RestModeName(lastRestMode), DFMPRestRequestKind.Stop);

            if (instance == this)
                instance = null;
        }

        void Update()
        {
            if (!NetworkClient.isConnected)
            {
                lastRestMode = DaggerfallRestWindow.RestModes.Selection;
                activeRestWindow = null;
                return;
            }

            string restPolicy = DFMPWorldSettings.CurrentRestPolicy;
            if (lastRestPolicy != restPolicy)
            {
                lastRestPolicy = restPolicy;
                if (!DFMPServerRestConfig.IsServerManaged(restPolicy))
                    CloseActiveRest();
            }

            DaggerfallRestWindow.RestModes currentMode = GetCurrentRestMode();
            if (IsActiveRestMode(currentMode) && lastRestMode == DaggerfallRestWindow.RestModes.Selection)
            {
                CacheActiveRestWindow();
                SendRestRequest(RestModeName(currentMode), DFMPRestRequestKind.Start);
            }
            else if (!IsActiveRestMode(currentMode) && IsActiveRestMode(lastRestMode))
            {
                SendRestRequest(RestModeName(lastRestMode), DFMPRestRequestKind.Stop);
                activeRestWindow = null;
            }

            lastRestMode = currentMode;

            if (IsActiveRestMode(currentMode) && HasNearbyHostileEnemy())
                AbortActiveRestForEnemy();
        }

        void HandleVitalSnapshot(int health)
        {
            if (lastSnapshotHealth >= 0 && health < lastSnapshotHealth && IsActiveRestMode(lastRestMode))
                AbortActiveRestForEnemy();

            lastSnapshotHealth = health;
        }

        void SendRestRequest(string restModeName, DFMPRestRequestKind kind)
        {
            if (!NetworkClient.isConnected || string.IsNullOrEmpty(restModeName))
                return;

            NetworkClient.Send(new DFMPRestRequest
            {
                RestModeName = restModeName,
                Kind = kind
            });
        }

        static DaggerfallRestWindow.RestModes GetCurrentRestMode()
        {
            if (!GameManager.HasInstance || GameManager.Instance.PlayerEntity == null)
                return DaggerfallRestWindow.RestModes.Selection;

            return GameManager.Instance.PlayerEntity.CurrentRestMode;
        }

        static bool IsActiveRestMode(DaggerfallRestWindow.RestModes mode)
        {
            return mode == DaggerfallRestWindow.RestModes.TimedRest ||
                   mode == DaggerfallRestWindow.RestModes.FullRest;
        }

        static string RestModeName(DaggerfallRestWindow.RestModes mode)
        {
            if (mode == DaggerfallRestWindow.RestModes.TimedRest)
                return nameof(DaggerfallRestWindow.RestModes.TimedRest);

            if (mode == DaggerfallRestWindow.RestModes.FullRest)
                return nameof(DaggerfallRestWindow.RestModes.FullRest);

            return string.Empty;
        }

        string ResolveActiveRestModeName()
        {
            DaggerfallRestWindow.RestModes mode = GetCurrentRestMode();
            string name = RestModeName(mode);
            if (!string.IsNullOrEmpty(name))
                return name;

            return RestModeName(lastRestMode);
        }

        void CacheActiveRestWindow()
        {
            if (DaggerfallUI.Instance == null || DaggerfallUI.UIManager == null)
                return;

            activeRestWindow = DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;
        }

        void AbortActiveRestForEnemy()
        {
            DaggerfallRestWindow restWindow = activeRestWindow;
            if (restWindow == null && DaggerfallUI.Instance != null && DaggerfallUI.UIManager != null)
                restWindow = DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;

            if (restWindow != null)
                restWindow.AbortRestForEnemySpawn();
        }

        void CloseActiveRest()
        {
            DaggerfallRestWindow restWindow = activeRestWindow;
            if (restWindow == null && DaggerfallUI.Instance != null && DaggerfallUI.UIManager != null)
                restWindow = DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;

            if (restWindow != null)
                restWindow.CloseWindow();

            if (GameManager.HasInstance && GameManager.Instance.PlayerEntity != null)
                GameManager.Instance.PlayerEntity.CurrentRestMode = DaggerfallRestWindow.RestModes.Selection;
        }

        static bool HasNearbyHostileEnemy()
        {
            if (!GameManager.HasInstance || GameObject.FindGameObjectWithTag("Player") == null)
                return false;

            PlayerEnterExit playerEnterExit = GameManager.Instance.PlayerEnterExit;
            if (playerEnterExit == null || !playerEnterExit.IsPlayerInsideDungeon || playerEnterExit.Dungeon == null)
                return false;

            Vector3 playerPosition = GameManager.Instance.PlayerObject != null
                ? GameManager.Instance.PlayerObject.transform.position
                : Vector3.zero;
            DFMPDynamicEnemyState[] states = FindObjectsOfType<DFMPDynamicEnemyState>();
            for (int index = 0; index < states.Length; index++)
            {
                DFMPDynamicEnemyState state = states[index];
                if (state == null || state.LifecycleState != DFMPDynamicEnemyLifecycleState.SpawnedAlive)
                    continue;

                if (state.Reaction == (int)DFBlock.EnemyReactionTypes.Passive)
                    continue;

                Vector3 enemyPosition = DFMPDynamicEnemyPresentation.DungeonLocalToScenePosition(
                    playerEnterExit.Dungeon.transform.position,
                    state.DungeonLocalPosition);
                if (Vector3.Distance(playerPosition, enemyPosition) <= RestingInterruptDistance)
                    return true;
            }

            return false;
        }
    }
}

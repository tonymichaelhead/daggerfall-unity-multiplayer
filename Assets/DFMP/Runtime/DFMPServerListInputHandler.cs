using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DFMP.Runtime
{
    /// <summary>
    /// Persistent input listener for opening the Multiplayer Server List UI.
    /// Monitors hotkeys (F10, RightControl) during game startup/main menu and in-game.
    /// </summary>
    public class DFMPServerListInputHandler : MonoBehaviour
    {
        public static DFMPServerListInputHandler Instance { get; private set; }

        public KeyCode PrimaryHotkey = KeyCode.F10;
        public KeyCode SecondaryHotkey = KeyCode.RightControl;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            // Do not spin up client UI input handler in dedicated server / batchmode
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer)
                return;

            if (Instance != null)
                return;

            GameObject go = new GameObject("DFMP_ServerListInputHandler");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DFMPServerListInputHandler>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (DaggerfallUI.UIManager == null || DFMPChatController.IsTextInputOwned)
                return;

            if (Input.GetKeyDown(PrimaryHotkey) || Input.GetKeyDown(SecondaryHotkey))
            {
                ToggleServerList();
            }
        }

        public static void ToggleServerList()
        {
            var uiManager = DaggerfallUI.UIManager;
            if (uiManager == null)
                return;

            var topWindow = uiManager.TopWindow;
            if (topWindow is DFMPServerListWindow)
            {
                uiManager.PopWindow();
            }
            else
            {
                uiManager.PushWindow(new DFMPServerListWindow(uiManager, topWindow));
            }
        }

        public static void OpenServerList()
        {
            var uiManager = DaggerfallUI.UIManager;
            if (uiManager == null || uiManager.TopWindow is DFMPServerListWindow)
                return;

            uiManager.PushWindow(new DFMPServerListWindow(uiManager, uiManager.TopWindow));
        }
    }
}

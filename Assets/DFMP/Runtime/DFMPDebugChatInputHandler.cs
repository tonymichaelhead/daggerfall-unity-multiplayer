using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DFMP.Runtime
{
    public class DFMPDebugChatInputHandler : MonoBehaviour
    {
        public static DFMPDebugChatInputHandler Instance { get; private set; }

        public KeyCode ToggleHotkey = KeyCode.F9;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer)
                return;

            if (Instance != null)
                return;

            GameObject go = new GameObject("DFMP_DebugChatInputHandler");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DFMPDebugChatInputHandler>();
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

        void Update()
        {
            if (DaggerfallUI.UIManager == null)
                return;

            if (Input.GetKeyDown(ToggleHotkey))
            {
                ToggleChat();
            }
        }

        public static void ToggleChat()
        {
            var uiManager = DaggerfallUI.UIManager;
            if (uiManager == null)
                return;

            var topWindow = uiManager.TopWindow;
            if (topWindow is DFMPDebugChatWindow)
            {
                uiManager.PopWindow();
            }
            else
            {
                var chatWin = new DFMPDebugChatWindow(uiManager, topWindow);
                uiManager.PushWindow(chatWin);
            }
        }
    }
}

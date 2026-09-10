using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPAdminInputHandler : MonoBehaviour
    {
        public static DFMPAdminInputHandler Instance { get; private set; }

        public KeyCode ToggleHotkey = KeyCode.F12;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer || Instance != null)
                return;

            GameObject handlerObject = new GameObject("DFMP_AdminInputHandler");
            DontDestroyOnLoad(handlerObject);
            Instance = handlerObject.AddComponent<DFMPAdminInputHandler>();
        }

        void Awake()
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
            if (DaggerfallUI.UIManager == null || !Input.GetKeyDown(ToggleHotkey))
                return;

            IUserInterfaceWindow topWindow = DaggerfallUI.UIManager.TopWindow;
            if (topWindow is DFMPAdminWindow)
            {
                DaggerfallUI.UIManager.PopWindow();
                return;
            }

            if (DFMPNetworkClient.IsConnected)
                DaggerfallUI.UIManager.PushWindow(new DFMPAdminWindow(DaggerfallUI.UIManager, topWindow));
        }
    }
}
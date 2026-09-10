using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using Mirror;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPChatController : MonoBehaviour
    {
        const int PassiveRowCount = 6;
        const float FadeDurationSeconds = 1f;

        static readonly Color PlayerTextColor = new Color(0.88f, 0.88f, 0.92f, 1f);
        static readonly Color SystemTextColor = new Color(1f, 0.82f, 0.36f, 1f);

        readonly DFMPChatHistory history = new DFMPChatHistory();
        readonly TextLabel[] passiveLabels = new TextLabel[PassiveRowCount];

        DaggerfallHUD attachedHud;
        Panel passivePanel;
        DFMPChatWindow openWindow;
        bool wasConnected;

        public static DFMPChatController Instance { get; private set; }
        public static bool IsTextInputOwned { get; private set; }

        public KeyCode OpenHotkey = KeyCode.F9;

        public DFMPChatHistory History
        {
            get { return history; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            if (Application.isBatchMode || DedicatedServerBootstrap.IsDedicatedServer || Instance != null)
                return;

            GameObject controllerObject = new GameObject("DFMP_ChatController");
            DontDestroyOnLoad(controllerObject);
            Instance = controllerObject.AddComponent<DFMPChatController>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DFMPEventBus.Instance.ChatMessageReceived += OnChatMessageReceived;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            DFMPEventBus.Instance.ChatMessageReceived -= OnChatMessageReceived;
            DetachPassivePanel();
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            bool connected = DFMPNetworkClient.IsConnected;
            if (wasConnected && !connected)
                ResetForConnection();
            wasConnected = connected;

            EnsurePassivePanel();
            UpdatePassivePanel();

            if (Input.GetKeyDown(OpenHotkey))
                TryOpenChat();
        }

        public void TryOpenChat()
        {
            if (IsTextInputOwned || !DFMPNetworkClient.IsConnected || DaggerfallUI.UIManager == null ||
                InputManager.Instance == null || InputManager.Instance.IsPaused)
                return;

            DaggerfallHUD hud = DaggerfallUI.Instance != null ? DaggerfallUI.Instance.DaggerfallHUD : null;
            if (hud == null || DaggerfallUI.UIManager.TopWindow != hud)
                return;

            openWindow = new DFMPChatWindow(DaggerfallUI.UIManager, hud, this);
            IsTextInputOwned = true;
            DaggerfallUI.UIManager.PushWindow(openWindow);
        }

        public void SendPlayerMessage(string text)
        {
            string sanitizedText;
            string reason;
            if (!DFMPChatProtocol.TrySanitize(text, out sanitizedText, out reason))
            {
                AddLocalSystemMessage(string.Format("Message not sent: {0}.", reason));
                return;
            }

            if (!NetworkClient.active || NetworkClient.connection == null)
            {
                AddLocalSystemMessage("Message not sent: not connected to a server.");
                return;
            }

            NetworkClient.Send(new DFMPChatSubmitMessage { Text = sanitizedText });
        }

        public void AddLocalSystemMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            AddMessage(new DFMPChatDeliveryMessage
            {
                Kind = DFMPChatMessageKind.System,
                ConnectionId = -1,
                SenderDisplayName = string.Empty,
                Text = text.Trim()
            });
        }

        public void ResetForConnection()
        {
            if (openWindow != null)
            {
                if (DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow == openWindow)
                    openWindow.CloseWindow();
                else
                {
                    openWindow.ReleaseInputState();
                    NotifyWindowClosed(openWindow);
                }
            }

            history.Clear();
        }

        void OnChatMessageReceived(DFMPChatMessageReceivedEvent chatEvent)
        {
            if (chatEvent == null)
                return;

            AddMessage(chatEvent.Message);
        }

        void AddMessage(DFMPChatDeliveryMessage message)
        {
            history.Add(message, Time.unscaledTime);
            if (openWindow != null)
                openWindow.RefreshHistory(false);
        }

        public void NotifyWindowClosed(DFMPChatWindow window)
        {
            if (openWindow != window)
                return;

            openWindow = null;
            IsTextInputOwned = false;
        }

        void EnsurePassivePanel()
        {
            DaggerfallHUD hud = DaggerfallUI.Instance != null ? DaggerfallUI.Instance.DaggerfallHUD : null;
            if (hud == attachedHud)
                return;

            DetachPassivePanel();
            if (hud == null)
                return;

            attachedHud = hud;
            passivePanel = new Panel
            {
                Position = new Vector2(4, 126),
                Size = new Vector2(212, 48),
                BackgroundColor = new Color(0f, 0f, 0f, 0.32f)
            };
            attachedHud.NativePanel.Components.Add(passivePanel);

            for (int index = 0; index < passiveLabels.Length; index++)
            {
                passiveLabels[index] = new TextLabel
                {
                    Position = new Vector2(4, 3 + index * 7),
                    ShadowPosition = Vector2.zero
                };
                passivePanel.Components.Add(passiveLabels[index]);
            }
        }

        void UpdatePassivePanel()
        {
            if (passivePanel == null)
                return;

            DaggerfallHUD hud = attachedHud;
            bool shouldShow = openWindow == null && DFMPNetworkClient.IsConnected && hud != null &&
                DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow == hud;
            var entries = history.GetPassiveEntries(Time.unscaledTime, PassiveRowCount);
            passivePanel.Enabled = shouldShow && entries.Count > 0;
            if (!passivePanel.Enabled)
                return;

            for (int index = 0; index < passiveLabels.Length; index++)
            {
                TextLabel label = passiveLabels[index];
                if (index >= entries.Count)
                {
                    label.Enabled = false;
                    continue;
                }

                DFMPChatHistoryEntry entry = entries[index];
                float age = Mathf.Max(0f, Time.unscaledTime - entry.ReceivedAt);
                float alpha = age <= DFMPChatHistory.DefaultPassiveLifetimeSeconds - FadeDurationSeconds
                    ? 1f
                    : Mathf.Clamp01((DFMPChatHistory.DefaultPassiveLifetimeSeconds - age) / FadeDurationSeconds);
                Color baseColor = entry.IsPlayerMessage ? PlayerTextColor : SystemTextColor;
                label.Enabled = true;
                label.Text = entry.GetDisplayText(52);
                label.TextColor = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
            }
        }

        void DetachPassivePanel()
        {
            if (attachedHud != null && passivePanel != null)
                attachedHud.NativePanel.Components.Remove(passivePanel);
            attachedHud = null;
            passivePanel = null;
        }
    }
}
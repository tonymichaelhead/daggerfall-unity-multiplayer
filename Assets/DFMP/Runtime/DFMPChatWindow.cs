using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace DFMP.Runtime
{
    public sealed class DFMPChatWindow : DaggerfallPopupWindow
    {
        const int RowsDisplayed = 7;

        static readonly Color PlayerTextColor = new Color(0.88f, 0.88f, 0.92f, 1f);
        static readonly Color SystemTextColor = new Color(1f, 0.82f, 0.36f, 1f);

        readonly DFMPChatController controller;
        readonly Panel mainPanel = new Panel();
        readonly ListBox chatListBox = new ListBox();
        readonly VerticalScrollBar scrollBar = new VerticalScrollBar();
        readonly TextBox inputTextBox = new TextBox();
        readonly TextLabel newMessagesLabel = new TextLabel();

        bool previousInputPaused;
        IMECompositionMode previousImeMode;
        bool previousCursorActive;
        bool previousCursorVisible;
        CursorLockMode previousCursorLockMode;
        bool capturedCursorState;
        bool restoredInput;
        bool toggleCloseArmed;
        long displayedRevision;
        readonly List<long> displayedSequences = new List<long>();

        public DFMPChatWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous, DFMPChatController controller)
            : base(uiManager, previous)
        {
            this.controller = controller;
            PauseWhileOpen = false;
        }

        protected override void Setup()
        {
            base.Setup();

            mainPanel.Position = new Vector2(4, 116);
            mainPanel.Size = new Vector2(230, 80);
            mainPanel.BackgroundColor = new Color(0f, 0f, 0f, 0.72f);
            mainPanel.Outline.Enabled = true;
            NativePanel.Components.Add(mainPanel);

            chatListBox.Position = new Vector2(4, 4);
            chatListBox.Size = new Vector2(211, 54);
            chatListBox.RowsDisplayed = RowsDisplayed;
            chatListBox.WrapTextItems = true;
            chatListBox.WrapWords = true;
            chatListBox.ShadowPosition = Vector2.zero;
            chatListBox.OnScroll += ChatListBox_OnScroll;
            mainPanel.Components.Add(chatListBox);

            scrollBar.Position = new Vector2(217, 4);
            scrollBar.Size = new Vector2(8, 54);
            scrollBar.DisplayUnits = RowsDisplayed;
            scrollBar.OnScroll += ScrollBar_OnScroll;
            mainPanel.Components.Add(scrollBar);

            inputTextBox.Position = new Vector2(4, 62);
            inputTextBox.Size = new Vector2(221, 14);
            inputTextBox.MaxCharacters = DFMPChatProtocol.MaxMessageLength;
            inputTextBox.UseFocus = true;
            inputTextBox.OverridesHotkeySequences = true;
            inputTextBox.Outline.Enabled = true;
            mainPanel.Components.Add(inputTextBox);

            newMessagesLabel.Position = new Vector2(148, 51);
            newMessagesLabel.Text = "New messages";
            newMessagesLabel.TextColor = SystemTextColor;
            newMessagesLabel.ShadowPosition = Vector2.zero;
            newMessagesLabel.Enabled = false;
            mainPanel.Components.Add(newMessagesLabel);

            SetFocus(inputTextBox);
            RefreshHistory(true);
        }

        public override void OnPush()
        {
            base.OnPush();
            previousInputPaused = InputManager.Instance != null && InputManager.Instance.IsPaused;
            if (InputManager.Instance != null)
            {
                previousCursorVisible = InputManager.Instance.CursorVisible;
                InputManager.Instance.ClearAllActions();
                InputManager.Instance.IsPaused = true;
                InputManager.Instance.CursorVisible = true;
            }

            if (GameManager.HasInstance && GameManager.Instance.PlayerMouseLook != null)
            {
                previousCursorActive = GameManager.Instance.PlayerMouseLook.cursorActive;
                previousCursorLockMode = Cursor.lockState;
                capturedCursorState = true;
                GameManager.Instance.PlayerMouseLook.cursorActive = true;
                Cursor.lockState = CursorLockMode.None;
            }

            previousImeMode = Input.imeCompositionMode;
            Input.imeCompositionMode = IMECompositionMode.On;
        }

        public override void Update()
        {
            base.Update();

            if (!toggleCloseArmed && !Input.GetKey(controller.OpenHotkey))
                toggleCloseArmed = true;

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && !inputTextBox.IMECompositionInProgress)
            {
                string text = inputTextBox.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    controller.SendPlayerMessage(text);
                CloseWindow();
            }
            else if (Input.GetKeyDown(KeyCode.Escape) ||
                (toggleCloseArmed && Input.GetKeyDown(controller.OpenHotkey)))
            {
                CloseWindow();
            }
        }

        public override void OnPop()
        {
            ReleaseInputState();
            controller.NotifyWindowClosed(this);
            base.OnPop();
        }

        public void RefreshHistory(bool forceFollow)
        {
            if (chatListBox == null || controller == null)
                return;

            int previousCount = chatListBox.Count;
            int maximumScrollIndex = Math.Max(0, previousCount - RowsDisplayed);
            bool wasFollowing = forceFollow || chatListBox.ScrollIndex >= maximumScrollIndex;
            int previousScrollIndex = chatListBox.ScrollIndex;
            long firstVisibleSequence = previousScrollIndex >= 0 && previousScrollIndex < displayedSequences.Count
                ? displayedSequences[previousScrollIndex]
                : -1;
            bool hasNewMessages = controller.History.Revision > displayedRevision;

            chatListBox.ClearItems();
            displayedSequences.Clear();
            foreach (DFMPChatHistoryEntry entry in controller.History.Entries)
            {
                ListBox.ListItem item;
                chatListBox.AddItem(entry.DisplayText, out item);
                Color color = entry.IsPlayerMessage ? PlayerTextColor : SystemTextColor;
                item.textColor = color;
                item.selectedTextColor = color;
                item.highlightedTextColor = color;
                item.highlightedSelectedTextColor = color;
                displayedSequences.Add(entry.Sequence);
            }
            displayedRevision = controller.History.Revision;

            scrollBar.TotalUnits = chatListBox.Count;
            if (wasFollowing && chatListBox.Count > 0)
            {
                chatListBox.SelectedIndex = chatListBox.Count - 1;
                chatListBox.ScrollToSelected();
                newMessagesLabel.Enabled = false;
            }
            else
            {
                int preservedIndex = displayedSequences.IndexOf(firstVisibleSequence);
                chatListBox.ScrollIndex = preservedIndex >= 0 ? preservedIndex : previousScrollIndex;
                scrollBar.ScrollIndex = chatListBox.ScrollIndex;
                newMessagesLabel.Enabled = hasNewMessages;
            }
        }

        public void ReleaseInputState()
        {
            if (restoredInput)
                return;

            restoredInput = true;
            Input.imeCompositionMode = previousImeMode;
            if (InputManager.Instance != null)
            {
                InputManager.Instance.ClearAllActions();
                InputManager.Instance.IsPaused = previousInputPaused;
                InputManager.Instance.CursorVisible = previousCursorVisible;
            }

            if (capturedCursorState && GameManager.HasInstance && GameManager.Instance.PlayerMouseLook != null)
            {
                GameManager.Instance.PlayerMouseLook.cursorActive = previousCursorActive;
                Cursor.lockState = previousCursorLockMode;
            }
        }

        void ChatListBox_OnScroll()
        {
            scrollBar.ScrollIndex = chatListBox.ScrollIndex;
            if (chatListBox.ScrollIndex >= Math.Max(0, chatListBox.Count - RowsDisplayed))
                newMessagesLabel.Enabled = false;
        }

        void ScrollBar_OnScroll()
        {
            chatListBox.ScrollIndex = scrollBar.ScrollIndex;
            if (chatListBox.ScrollIndex >= Math.Max(0, chatListBox.Count - RowsDisplayed))
                newMessagesLabel.Enabled = false;
        }
    }
}
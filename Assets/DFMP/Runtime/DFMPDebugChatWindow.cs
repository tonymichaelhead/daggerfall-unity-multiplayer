using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DFMP.Runtime
{
    public class DFMPDebugChatWindow : DaggerfallPopupWindow
    {
        readonly List<string> messageHistory = new List<string>();

        Panel mainPanel = new Panel();
        TextLabel titleLabel = new TextLabel();
        ListBox chatListBox = new ListBox();
        TextBox inputTextBox = new TextBox();
        Button sendButton = new Button();
        Button closeButton = new Button();

        public DFMPDebugChatWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous = null)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            base.Setup();

            mainPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.VerticalAlignment = VerticalAlignment.Middle;
            mainPanel.Size = new Vector2(320, 200);
            mainPanel.Outline.Enabled = true;
            mainPanel.BackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            NativePanel.Components.Add(mainPanel);

            titleLabel.Position = new Vector2(8, 6);
            titleLabel.Text = "DFMP DEBUG CHAT";
            titleLabel.ShadowPosition = Vector2.zero;
            titleLabel.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            mainPanel.Components.Add(titleLabel);

            chatListBox.Position = new Vector2(8, 22);
            chatListBox.Size = new Vector2(304, 130);
            chatListBox.RowsDisplayed = 8;
            chatListBox.ShadowPosition = Vector2.zero;
            chatListBox.TextColor = new Color(0.85f, 0.85f, 0.95f, 1f);
            chatListBox.SelectedTextColor = new Color(1f, 0.9f, 0.3f, 1f);
            mainPanel.Components.Add(chatListBox);

            inputTextBox.Position = new Vector2(8, 158);
            inputTextBox.Size = new Vector2(230, 14);
            inputTextBox.MaxCharacters = DFMPChatProtocol.MaxMessageLength;
            inputTextBox.DefaultText = "Type a chat message...";
            mainPanel.Components.Add(inputTextBox);

            sendButton.Position = new Vector2(246, 158);
            sendButton.Size = new Vector2(64, 14);
            sendButton.Label.Text = "Send";
            sendButton.Label.ShadowPosition = Vector2.zero;
            sendButton.Outline.Enabled = true;
            sendButton.OnMouseClick += SendButton_OnMouseClick;
            mainPanel.Components.Add(sendButton);

            closeButton.Position = new Vector2(246, 176);
            closeButton.Size = new Vector2(64, 14);
            closeButton.Label.Text = "Close";
            closeButton.Label.ShadowPosition = Vector2.zero;
            closeButton.Outline.Enabled = true;
            closeButton.BackgroundColor = new Color(0.35f, 0.15f, 0.15f, 0.85f);
            closeButton.OnMouseClick += CloseButton_OnMouseClick;
            mainPanel.Components.Add(closeButton);

            AddMessage("[DFMP] debug chat ready");
            DFMPEventBus.Instance.ChatMessageReceived += OnChatMessageReceived;
        }

        public override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.Return) && inputTextBox != null && inputTextBox.Enabled)
            {
                SubmitCurrentText();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
                CloseWindow();
        }

        public override void OnPop()
        {
            DFMPEventBus.Instance.ChatMessageReceived -= OnChatMessageReceived;
            base.OnPop();
        }

        void AddMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            string timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            if (messageHistory.Count > 50)
                messageHistory.RemoveAt(0);

            messageHistory.Add(timestampedMessage);
            if (chatListBox != null)
            {
                chatListBox.AddItem(timestampedMessage);
                chatListBox.SelectedIndex = chatListBox.Count - 1;
            }
        }

        void OnChatMessageReceived(DFMPChatMessageReceivedEvent evt)
        {
            if (evt == null)
                return;

            AddMessage($"{evt.SenderDisplayName}: {evt.MessageText}");
        }

        void SendButton_OnMouseClick(object sender, Vector2 position)
        {
            SubmitCurrentText();
        }

        void CloseButton_OnMouseClick(object sender, Vector2 position)
        {
            CloseWindow();
        }

        void SubmitCurrentText()
        {
            if (inputTextBox == null)
                return;

            string text = inputTextBox.Text.Trim();
            inputTextBox.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return;

            string reason;
            if (!DFMPChatProtocol.TrySanitize(text, out string sanitizedText, out reason))
            {
                AddMessage($"[DFMP] reject: {reason}");
                return;
            }

            if (!NetworkClient.active || NetworkClient.connection == null)
            {
                AddMessage("[DFMP] reject: not connected to a server");
                return;
            }

            var msg = new DFMPChatMessage
            {
                ConnectionId = 0,
                SenderDisplayName = "Me",
                Text = sanitizedText
            };

            NetworkClient.Send(msg);
        }
    }
}

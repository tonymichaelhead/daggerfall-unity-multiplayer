using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPDiscordAuthorizeWindow : DaggerfallPopupWindow
    {
        readonly Color mainPanelBg = new Color(0.05f, 0.05f, 0.08f, 0.95f);
        readonly Panel mainPanel = new Panel();
        readonly TextLabel statusLabel = new TextLabel();
        readonly TextLabel urlLabel = new TextLabel();

        string authorizeUri = string.Empty;
        string status = "Approve this server in your browser.";
        bool componentsReady;

        public DFMPDiscordAuthorizeWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
            AllowCancel = false;
        }

        public void Apply(string uri, string statusText)
        {
            authorizeUri = uri ?? string.Empty;
            if (!string.IsNullOrEmpty(statusText))
                status = statusText;

            RefreshLabels();
        }

        public void SetStatus(string statusText)
        {
            if (!string.IsNullOrEmpty(statusText))
                status = statusText;

            RefreshLabels();
        }

        protected override void Setup()
        {
            base.Setup();

            mainPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.VerticalAlignment = VerticalAlignment.Middle;
            mainPanel.Size = new Vector2(320, 150);
            mainPanel.Outline.Enabled = true;
            mainPanel.BackgroundColor = mainPanelBg;
            NativePanel.Components.Add(mainPanel);

            var titleLabel = new TextLabel
            {
                Position = new Vector2(10, 8),
                Text = "AUTHORIZE DISCORD",
                ShadowPosition = Vector2.zero,
                TextColor = DaggerfallUI.DaggerfallDefaultTextColor
            };
            mainPanel.Components.Add(titleLabel);

            var instruction = new TextLabel
            {
                Position = new Vector2(10, 26),
                Size = new Vector2(300, 32),
                WrapText = true,
                MaxWidth = 300,
                Text = "Your browser has been opened to approve this server with Discord. " +
                    "If nothing opened, copy the address below.",
                ShadowPosition = Vector2.zero,
                TextColor = new Color(0.85f, 0.85f, 0.95f, 1f)
            };
            mainPanel.Components.Add(instruction);

            urlLabel.Position = new Vector2(10, 66);
            urlLabel.Size = new Vector2(300, 48);
            urlLabel.WrapText = true;
            urlLabel.MaxWidth = 300;
            urlLabel.ShadowPosition = Vector2.zero;
            urlLabel.TextColor = new Color(0.7f, 0.78f, 0.9f, 1f);
            mainPanel.Components.Add(urlLabel);

            statusLabel.Position = new Vector2(10, 118);
            statusLabel.Size = new Vector2(300, 16);
            statusLabel.WrapText = true;
            statusLabel.MaxWidth = 300;
            statusLabel.ShadowPosition = Vector2.zero;
            statusLabel.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            mainPanel.Components.Add(statusLabel);

            var cancelButton = new Button
            {
                Position = new Vector2(220, 132),
                Size = new Vector2(80, 14),
                BackgroundColor = new Color(0.35f, 0.15f, 0.15f, 0.85f)
            };
            cancelButton.Label.Text = "Cancel";
            cancelButton.Label.ShadowPosition = Vector2.zero;
            cancelButton.Outline.Enabled = true;
            cancelButton.OnMouseClick += CancelButton_OnMouseClick;
            mainPanel.Components.Add(cancelButton);

            componentsReady = true;
            RefreshLabels();
        }

        void CancelButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            DFMPNetworkClient.Stop();
        }

        void RefreshLabels()
        {
            if (!componentsReady)
                return;

            urlLabel.Text = authorizeUri;
            statusLabel.Text = status;
        }
    }
}

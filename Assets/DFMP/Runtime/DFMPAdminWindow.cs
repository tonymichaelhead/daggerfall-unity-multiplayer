using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPAdminWindow : DaggerfallPopupWindow
    {
        static readonly Color AdminTextColor = new Color(0.35f, 0.95f, 0.45f, 1f);
        static readonly Color AdminSelectedTextColor = new Color(0.65f, 1f, 0.7f, 1f);

        readonly Panel mainPanel = new Panel();
        readonly TextLabel statusLabel = new TextLabel();
        readonly ListBox playerListBox = new ListBox();
        readonly VerticalScrollBar scrollBar = new VerticalScrollBar();
        readonly List<DFMPAdminPlayer> players = new List<DFMPAdminPlayer>();

        public DFMPAdminWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous = null)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            base.Setup();

            mainPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.VerticalAlignment = VerticalAlignment.Middle;
            mainPanel.Size = new Vector2(300, 185);
            mainPanel.Outline.Enabled = true;
            mainPanel.BackgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            NativePanel.Components.Add(mainPanel);

            var titleLabel = new TextLabel
            {
                Position = new Vector2(8, 6),
                Text = "SERVER ADMINISTRATION",
                ShadowPosition = Vector2.zero,
                TextColor = DaggerfallUI.DaggerfallDefaultTextColor
            };
            mainPanel.Components.Add(titleLabel);

            var playersTab = new Button
            {
                Position = new Vector2(8, 22),
                Size = new Vector2(70, 14),
                BackgroundColor = new Color(0.20f, 0.40f, 0.25f, 0.85f)
            };
            playersTab.Label.Text = "Players";
            playersTab.Label.ShadowPosition = Vector2.zero;
            playersTab.Outline.Enabled = true;
            mainPanel.Components.Add(playersTab);

            var playerListPanel = new Panel
            {
                Position = new Vector2(8, 40),
                Size = new Vector2(284, 100),
                BackgroundColor = new Color(0.02f, 0.02f, 0.04f, 0.90f)
            };
            playerListPanel.Outline.Enabled = true;
            mainPanel.Components.Add(playerListPanel);

            playerListBox.Position = new Vector2(4, 4);
            playerListBox.Size = new Vector2(266, 92);
            playerListBox.RowsDisplayed = 6;
            playerListBox.ShadowPosition = Vector2.zero;
            playerListBox.TextColor = new Color(0.85f, 0.85f, 0.95f, 1f);
            playerListBox.SelectedTextColor = new Color(1f, 0.9f, 0.3f, 1f);
            playerListBox.OnScroll += PlayerListBox_OnScroll;
            playerListPanel.Components.Add(playerListBox);

            scrollBar.Position = new Vector2(272, 4);
            scrollBar.Size = new Vector2(8, 92);
            scrollBar.DisplayUnits = 6;
            scrollBar.OnScroll += ScrollBar_OnScroll;
            playerListPanel.Components.Add(scrollBar);

            AddActionButton("Kick", new Vector2(8, 146), new Color(0.45f, 0.16f, 0.16f, 0.9f), KickButton_OnMouseClick);
            AddActionButton("Refresh", new Vector2(108, 146), new Color(0.15f, 0.20f, 0.30f, 0.85f), RefreshButton_OnMouseClick);
            AddActionButton("Close", new Vector2(212, 146), new Color(0.35f, 0.15f, 0.15f, 0.85f), CloseButton_OnMouseClick);

            statusLabel.Position = new Vector2(8, 170);
            statusLabel.Text = "Requesting player list...";
            statusLabel.ShadowPosition = Vector2.zero;
            statusLabel.TextColor = new Color(0.65f, 0.65f, 0.7f, 1f);
            mainPanel.Components.Add(statusLabel);

            DFMPNetworkClient.AdminRosterReceived += OnAdminRosterReceived;
            DFMPNetworkClient.RequestAdminRoster();
        }

        public override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.Escape) || InputManager.Instance.GetBackButtonUp())
                CloseWindow();
            else if (!DFMPNetworkClient.IsConnected)
                statusLabel.Text = "Disconnected from server.";
        }

        public override void OnPop()
        {
            DFMPNetworkClient.AdminRosterReceived -= OnAdminRosterReceived;
            base.OnPop();
        }

        void AddActionButton(string text, Vector2 position, Color backgroundColor, BaseScreenComponent.OnMouseClickHandler clickHandler)
        {
            var button = new Button
            {
                Position = position,
                Size = new Vector2(80, 18),
                BackgroundColor = backgroundColor
            };
            button.Label.Text = text;
            button.Label.ShadowPosition = Vector2.zero;
            button.Outline.Enabled = true;
            button.OnMouseClick += clickHandler;
            mainPanel.Components.Add(button);
        }

        void OnAdminRosterReceived(DFMPAdminRosterResponse response)
        {
            players.Clear();
            playerListBox.ClearItems();

            if (response.Players != null)
            {
                foreach (DFMPAdminPlayer player in response.Players)
                {
                    players.Add(player);
                    string label = string.Format("{0} ({1}){2}",
                        player.DisplayName,
                        player.AccountId,
                        player.IsAdmin ? " - Admin" : string.Empty);
                    ListBox.ListItem listItem;
                    playerListBox.AddItem(label, out listItem);
                    if (player.IsAdmin)
                    {
                        listItem.textColor = AdminTextColor;
                        listItem.selectedTextColor = AdminSelectedTextColor;
                        listItem.highlightedTextColor = AdminSelectedTextColor;
                        listItem.highlightedSelectedTextColor = AdminSelectedTextColor;
                    }
                }
            }

            scrollBar.TotalUnits = players.Count;
            statusLabel.Text = players.Count == 1
                ? "1 player logged in."
                : string.Format("{0} players logged in.", players.Count);
        }

        void KickButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            int selectedIndex = playerListBox.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= players.Count)
            {
                statusLabel.Text = "Select a player to kick.";
                return;
            }

            DFMPAdminPlayer selectedPlayer = players[selectedIndex];
            var confirmation = new DaggerfallMessageBox(uiManager, uiManager.TopWindow, true);
            confirmation.PauseWhileOpen = true;
            confirmation.SetText(string.Format("Kick {0} from the server?", selectedPlayer.DisplayName));
            confirmation.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
            confirmation.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
            confirmation.OnButtonClick += (messageBox, button) =>
            {
                messageBox.CloseWindow();
                if (button != DaggerfallMessageBox.MessageBoxButtons.Yes)
                    return;

                statusLabel.Text = string.Format("Kicking {0}...", selectedPlayer.DisplayName);
                DFMPNetworkClient.RequestKickPlayer(selectedPlayer.ConnectionId);
            };
            uiManager.PushWindow(confirmation);
        }

        void RefreshButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            statusLabel.Text = "Refreshing player list...";
            DFMPNetworkClient.RequestAdminRoster();
        }

        void CloseButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            CloseWindow();
        }

        void PlayerListBox_OnScroll()
        {
            scrollBar.ScrollIndex = playerListBox.ScrollIndex;
        }

        void ScrollBar_OnScroll()
        {
            playerListBox.ScrollIndex = scrollBar.ScrollIndex;
        }
    }
}
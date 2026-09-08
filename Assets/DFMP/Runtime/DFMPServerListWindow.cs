using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace DFMP.Runtime
{
    /// <summary>
    /// Multiplayer Server List & Browser Window.
    /// Provides LAN discovery, custom direct IP connection, and server status displays.
    /// Accessible via hotkey (e.g. F10 / RightControl) on the main menu.
    /// </summary>
    public class DFMPServerListWindow : DaggerfallPopupWindow
    {
        private const string NativeBackgroundImg = "PICK03I0.IMG";

        private Panel mainPanel = new Panel();
        private TextLabel titleLabel = new TextLabel();
        private TextLabel statusLabel = new TextLabel();
        private ListBox serverListBox = new ListBox();
        private VerticalScrollBar scrollBar = new VerticalScrollBar();

        private Button connectButton = new Button();
        private Button refreshButton = new Button();
        private Button directConnectButton = new Button();
        private Button closeButton = new Button();

        private TextBox directAddressTextBox = new TextBox();
        private Panel directAddressPanel = new Panel();

        private readonly List<DFMPServerInfo> serverList = new List<DFMPServerInfo>();
        private DFMPServerDiscoveryClient discoveryClient;
        private float refreshTimer = 0f;
        private const float AutoRefreshInterval = 3.0f;

        // Visual Colors matching DFU Retro UI
        private readonly Color mainPanelBg = new Color(0.05f, 0.05f, 0.08f, 0.95f);
        private readonly Color listBg = new Color(0.02f, 0.02f, 0.04f, 0.90f);
        private readonly Color buttonBg = new Color(0.15f, 0.20f, 0.30f, 0.85f);
        private readonly Color buttonActiveBg = new Color(0.20f, 0.40f, 0.25f, 0.85f);
        private readonly Color buttonCancelBg = new Color(0.35f, 0.15f, 0.15f, 0.85f);

        public DFMPServerListWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous = null)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            base.Setup();

            discoveryClient = new DFMPServerDiscoveryClient();
            discoveryClient.OnServersUpdated += OnDiscoveryUpdated;
            discoveryClient.StartDiscovery();

            // Main container panel (320x200 standard classic resolution)
            mainPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.VerticalAlignment = VerticalAlignment.Middle;
            mainPanel.Size = new Vector2(300, 185);
            mainPanel.Outline.Enabled = true;
            mainPanel.BackgroundColor = mainPanelBg;
            NativePanel.Components.Add(mainPanel);

            // Title Label
            titleLabel.Position = new Vector2(8, 6);
            titleLabel.Text = "DAGGERFALL MULTIPLAYER - SERVER LIST";
            titleLabel.ShadowPosition = Vector2.zero;
            titleLabel.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            mainPanel.Components.Add(titleLabel);

            // Server List Panel
            Panel serverListPanel = new Panel();
            serverListPanel.Position = new Vector2(8, 22);
            serverListPanel.Size = new Vector2(284, 110);
            serverListPanel.Outline.Enabled = true;
            serverListPanel.BackgroundColor = listBg;
            mainPanel.Components.Add(serverListPanel);

            // Server ListBox
            serverListBox.Position = new Vector2(4, 4);
            serverListBox.Size = new Vector2(266, 102);
            serverListBox.RowsDisplayed = 7;
            serverListBox.ShadowPosition = Vector2.zero;
            serverListBox.TextColor = new Color(0.85f, 0.85f, 0.95f, 1f);
            serverListBox.SelectedTextColor = new Color(1f, 0.9f, 0.3f, 1f);
            serverListBox.OnSelectItem += ServerListBox_OnSelectItem;
            serverListBox.OnMouseDoubleClick += ServerListBox_OnDoubleClick;
            serverListBox.OnScroll += ServerListBox_OnScroll;
            serverListPanel.Components.Add(serverListBox);

            // Scroll bar
            scrollBar.Position = new Vector2(272, 4);
            scrollBar.Size = new Vector2(8, 102);
            scrollBar.DisplayUnits = 7;
            scrollBar.OnScroll += ScrollBar_OnScroll;
            serverListPanel.Components.Add(scrollBar);

            // Direct Address Input Panel
            directAddressPanel.Position = new Vector2(8, 136);
            directAddressPanel.Size = new Vector2(180, 14);
            directAddressPanel.Outline.Enabled = true;
            directAddressPanel.BackgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.9f);
            mainPanel.Components.Add(directAddressPanel);

            directAddressTextBox.Position = new Vector2(2, 2);
            directAddressTextBox.Size = new Vector2(176, 10);
            directAddressTextBox.MaxCharacters = 32;
            directAddressTextBox.Text = "127.0.0.1:7777";
            directAddressTextBox.DefaultText = "IP:Port (e.g. 127.0.0.1:7777)";
            directAddressPanel.Components.Add(directAddressTextBox);

            // Direct Connect Button
            directConnectButton.Position = new Vector2(192, 136);
            directConnectButton.Size = new Vector2(100, 14);
            directConnectButton.Label.Text = "Direct Connect";
            directConnectButton.Label.ShadowPosition = Vector2.zero;
            directConnectButton.BackgroundColor = buttonBg;
            directConnectButton.Outline.Enabled = true;
            directConnectButton.OnMouseClick += DirectConnectButton_OnMouseClick;
            mainPanel.Components.Add(directConnectButton);

            // Action Buttons
            connectButton.Position = new Vector2(8, 156);
            connectButton.Size = new Vector2(80, 18);
            connectButton.Label.Text = "Connect";
            connectButton.Label.ShadowPosition = Vector2.zero;
            connectButton.BackgroundColor = buttonActiveBg;
            connectButton.Outline.Enabled = true;
            connectButton.OnMouseClick += ConnectButton_OnMouseClick;
            mainPanel.Components.Add(connectButton);

            refreshButton.Position = new Vector2(108, 156);
            refreshButton.Size = new Vector2(80, 18);
            refreshButton.Label.Text = "Refresh";
            refreshButton.Label.ShadowPosition = Vector2.zero;
            refreshButton.BackgroundColor = buttonBg;
            refreshButton.Outline.Enabled = true;
            refreshButton.OnMouseClick += RefreshButton_OnMouseClick;
            mainPanel.Components.Add(refreshButton);

            closeButton.Position = new Vector2(212, 156);
            closeButton.Size = new Vector2(80, 18);
            closeButton.Label.Text = "Close";
            closeButton.Label.ShadowPosition = Vector2.zero;
            closeButton.BackgroundColor = buttonCancelBg;
            closeButton.Outline.Enabled = true;
            closeButton.OnMouseClick += CloseButton_OnMouseClick;
            mainPanel.Components.Add(closeButton);

            // Status label
            statusLabel.Position = new Vector2(8, 176);
            statusLabel.Text = "Searching for LAN / local servers...";
            statusLabel.ShadowPosition = Vector2.zero;
            statusLabel.TextColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            mainPanel.Components.Add(statusLabel);

            PopulateServerList();
        }

        public override void Update()
        {
            base.Update();

            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer >= AutoRefreshInterval)
            {
                refreshTimer = 0f;
                discoveryClient?.SendBroadcastPing();
            }

            // ESC or Back closes the window
            if (Input.GetKeyDown(KeyCode.Escape) || InputManager.Instance.GetBackButtonUp())
            {
                CloseWindow();
            }
        }

        public override void OnPop()
        {
            base.OnPop();
            if (discoveryClient != null)
            {
                discoveryClient.StopDiscovery();
                discoveryClient.Dispose();
                discoveryClient = null;
            }
        }

        private void OnDiscoveryUpdated()
        {
            PopulateServerList();
        }

        private void PopulateServerList()
        {
            if (discoveryClient == null)
                return;

            serverList.Clear();
            serverListBox.ClearItems();

            var list = discoveryClient.GetServers();
            foreach (var server in list)
            {
                serverList.Add(server);
                serverListBox.AddItem(server.DisplayString);
            }

            scrollBar.TotalUnits = serverList.Count;

            if (serverList.Count == 0)
            {
                statusLabel.Text = "No LAN servers found. Enter Direct IP or click Refresh.";
            }
            else
            {
                statusLabel.Text = $"Found {serverList.Count} server(s). Select and click Connect.";
            }
        }

        private void ServerListBox_OnSelectItem()
        {
            int index = serverListBox.SelectedIndex;
            if (index >= 0 && index < serverList.Count)
            {
                var server = serverList[index];
                directAddressTextBox.Text = $"{server.IpAddress}:{server.Port}";
            }
        }

        private void ServerListBox_OnDoubleClick(BaseScreenComponent sender, Vector2 position)
        {
            ConnectSelectedServer();
        }

        private void ServerListBox_OnScroll()
        {
            scrollBar.ScrollIndex = serverListBox.ScrollIndex;
        }

        private void ScrollBar_OnScroll()
        {
            serverListBox.ScrollIndex = scrollBar.ScrollIndex;
        }

        private void ConnectButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            ConnectSelectedServer();
        }

        private void DirectConnectButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            string addressText = directAddressTextBox.Text.Trim();
            if (string.IsNullOrEmpty(addressText))
                return;

            string host = "127.0.0.1";
            ushort port = 7777;

            if (addressText.Contains(":"))
            {
                string[] parts = addressText.Split(':');
                host = parts[0];
                if (parts.Length > 1 && ushort.TryParse(parts[1], out ushort parsedPort))
                    port = parsedPort;
            }
            else
            {
                host = addressText;
            }

            InitiateConnection(host, port);
        }

        private void ConnectSelectedServer()
        {
            int index = serverListBox.SelectedIndex;
            if (index >= 0 && index < serverList.Count)
            {
                var server = serverList[index];
                InitiateConnection(server.IpAddress, (ushort)server.Port);
            }
            else
            {
                // Fallback to direct address input
                DirectConnectButton_OnMouseClick(null, Vector2.zero);
            }
        }

        private void InitiateConnection(string host, ushort port)
        {
            Debug.Log($"[DFMP UI] Connecting to server at {host}:{port}...");

            // Close server list window
            CloseWindow();

            // Start client networking
            DFMPNetworkClient.Start(host, port, 30);
        }

        private void RefreshButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            discoveryClient?.Clear();
            discoveryClient?.SendBroadcastPing();
            PopulateServerList();
        }

        private void CloseButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            CloseWindow();
        }
    }
}

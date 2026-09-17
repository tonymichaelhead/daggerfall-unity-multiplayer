using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace DFMP.Runtime
{
    public class DFMPCharacterSelectWindow : DaggerfallPopupWindow
    {
        readonly Color mainPanelBg = new Color(0.05f, 0.05f, 0.08f, 0.95f);
        readonly Color listBg = new Color(0.02f, 0.02f, 0.04f, 0.90f);
        readonly Color buttonBg = new Color(0.15f, 0.20f, 0.30f, 0.85f);
        readonly Color buttonActiveBg = new Color(0.20f, 0.40f, 0.25f, 0.85f);
        readonly Color buttonCancelBg = new Color(0.35f, 0.15f, 0.15f, 0.85f);
        readonly Color buttonDisabledBg = new Color(0.16f, 0.16f, 0.18f, 0.85f);
        readonly Color enabledTextColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        readonly Color disabledTextColor = new Color(0.45f, 0.45f, 0.48f, 1f);

        readonly Panel mainPanel = new Panel();
        readonly TextLabel statusLabel = new TextLabel();
        readonly ListBox characterListBox = new ListBox();
        readonly VerticalScrollBar scrollBar = new VerticalScrollBar();
        readonly List<DFMPCharacterSummary> characters = new List<DFMPCharacterSummary>();

        Button enterWorldButton;
        Button newCharacterButton;
        Button deleteButton;
        Button backButton;

        DFMPCharacterRosterMessage roster;
        bool allowDelete = true;
        bool keepConnectionOnClose;
        bool closing;
        // IsSetup only becomes true after Setup() returns, so it cannot gate roster application here.
        bool componentsReady;

        public DFMPCharacterSelectWindow(
            IUserInterfaceManager uiManager,
            IUserInterfaceWindow previous,
            DFMPCharacterRosterMessage initialRoster)
            : base(uiManager, previous)
        {
            AllowCancel = false;
            roster = initialRoster;
            allowDelete = initialRoster.AllowCharacterDelete;
        }

        protected override void Setup()
        {
            base.Setup();

            mainPanel.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.VerticalAlignment = VerticalAlignment.Middle;
            mainPanel.Size = new Vector2(300, 185);
            mainPanel.Outline.Enabled = true;
            mainPanel.BackgroundColor = mainPanelBg;
            NativePanel.Components.Add(mainPanel);

            var titleLabel = new TextLabel
            {
                Position = new Vector2(8, 6),
                Text = "SELECT CHARACTER",
                ShadowPosition = Vector2.zero,
                TextColor = DaggerfallUI.DaggerfallDefaultTextColor
            };
            mainPanel.Components.Add(titleLabel);

            var listPanel = new Panel
            {
                Position = new Vector2(8, 22),
                Size = new Vector2(284, 108),
                BackgroundColor = listBg
            };
            listPanel.Outline.Enabled = true;
            mainPanel.Components.Add(listPanel);

            characterListBox.Position = new Vector2(4, 4);
            characterListBox.Size = new Vector2(266, 100);
            characterListBox.RowsDisplayed = 7;
            characterListBox.ShadowPosition = Vector2.zero;
            characterListBox.TextColor = new Color(0.85f, 0.85f, 0.95f, 1f);
            characterListBox.SelectedTextColor = new Color(1f, 0.9f, 0.3f, 1f);
            characterListBox.OnSelectItem += CharacterListBox_OnSelectItem;
            characterListBox.OnMouseDoubleClick += CharacterListBox_OnDoubleClick;
            characterListBox.OnScroll += CharacterListBox_OnScroll;
            listPanel.Components.Add(characterListBox);

            scrollBar.Position = new Vector2(272, 4);
            scrollBar.Size = new Vector2(8, 100);
            scrollBar.DisplayUnits = 7;
            scrollBar.OnScroll += ScrollBar_OnScroll;
            listPanel.Components.Add(scrollBar);

            enterWorldButton = AddActionButton("Enter World", new Vector2(8, 136), buttonActiveBg, EnterWorldButton_OnMouseClick);
            newCharacterButton = AddActionButton("New Character", new Vector2(108, 136), buttonBg, NewCharacterButton_OnMouseClick);
            deleteButton = AddActionButton("Delete", new Vector2(8, 156), buttonCancelBg, DeleteButton_OnMouseClick);
            backButton = AddActionButton("Back", new Vector2(212, 156), buttonCancelBg, BackButton_OnMouseClick);

            statusLabel.Position = new Vector2(8, 176);
            statusLabel.Text = "Loading characters...";
            statusLabel.ShadowPosition = Vector2.zero;
            statusLabel.TextColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            mainPanel.Components.Add(statusLabel);

            DFMPNetworkClient.CharacterRosterReceived += OnRosterReceived;
            DFMPNetworkClient.CharacterActionResultReceived += OnActionResultReceived;

            componentsReady = true;
            RebuildFromRoster();
        }

        public void ApplyRoster(DFMPCharacterRosterMessage message)
        {
            roster = message;
            allowDelete = message.AllowCharacterDelete;
            if (!componentsReady)
                return;

            RebuildFromRoster();
        }

        void RebuildFromRoster()
        {
            characters.Clear();
            characterListBox.ClearItems();

            if (roster.Characters != null)
            {
                for (int i = 0; i < roster.Characters.Length; i++)
                {
                    DFMPCharacterSummary summary = roster.Characters[i];
                    characters.Add(summary);
                    characterListBox.AddItem(FormatCharacterRow(summary));
                }
            }

            scrollBar.TotalUnits = characters.Count;
            HideOrShowDeleteButton();
            RefreshActionState();
            Debug.Log($"[DFMP Join] Character select listed {characters.Count} character(s).");
        }

        void HideOrShowDeleteButton()
        {
            if (deleteButton == null)
                return;

            if (allowDelete)
            {
                deleteButton.Position = new Vector2(8, 156);
                deleteButton.Size = new Vector2(80, 18);
                deleteButton.Enabled = true;
            }
            else
            {
                deleteButton.Position = new Vector2(-400, -400);
                deleteButton.Size = Vector2.zero;
                deleteButton.Enabled = false;
            }
        }

        public void CloseForGameplay(bool keepConnection)
        {
            if (closing)
                return;

            keepConnectionOnClose = keepConnection;
            closing = true;
            CloseWindow();
        }

        public override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.Escape) || InputManager.Instance.GetBackButtonUp())
                RequestLeave();
        }

        public override void OnPop()
        {
            DFMPNetworkClient.CharacterRosterReceived -= OnRosterReceived;
            DFMPNetworkClient.CharacterActionResultReceived -= OnActionResultReceived;
            base.OnPop();

            if (keepConnectionOnClose)
                return;

            DFMPNetworkClient.Stop();
            DFMPServerListInputHandler.OpenServerList();
        }

        Button AddActionButton(string text, Vector2 position, Color backgroundColor, BaseScreenComponent.OnMouseClickHandler clickHandler)
        {
            var button = new Button
            {
                Position = position,
                Size = new Vector2(80, 18),
                BackgroundColor = backgroundColor
            };
            button.Label.Text = text;
            button.Label.ShadowPosition = Vector2.zero;
            button.Label.TextColor = enabledTextColor;
            button.Outline.Enabled = true;
            button.OnMouseClick += clickHandler;
            mainPanel.Components.Add(button);
            return button;
        }

        void RefreshActionState()
        {
            bool hasSelection = HasSelection();
            bool atCap = characters.Count >= roster.MaxCharacters && roster.MaxCharacters > 0;

            SetButtonEnabled(enterWorldButton, hasSelection, buttonActiveBg);
            SetButtonEnabled(newCharacterButton, !atCap, buttonBg);
            if (allowDelete)
                SetButtonEnabled(deleteButton, hasSelection, buttonCancelBg);

            if (characters.Count == 0)
                statusLabel.Text = "No characters on this account.";
            else if (atCap)
                statusLabel.Text = DFMPCharacterSelectPolicy.FormatCharacterLimitStatus(characters.Count, roster.MaxCharacters);
            else
                statusLabel.Text = string.Format("{0}/{1} characters", characters.Count, roster.MaxCharacters);
        }

        void SetButtonEnabled(Button button, bool enabled, Color enabledBackground)
        {
            if (button == null)
                return;

            button.BackgroundColor = enabled ? enabledBackground : buttonDisabledBg;
            button.Label.TextColor = enabled ? enabledTextColor : disabledTextColor;
        }

        bool HasSelection()
        {
            return characterListBox.SelectedIndex >= 0 && characterListBox.SelectedIndex < characters.Count;
        }

        DFMPCharacterSummary GetSelectedCharacter()
        {
            int index = characterListBox.SelectedIndex;
            return characters[index];
        }

        static string FormatCharacterRow(DFMPCharacterSummary summary)
        {
            string className = string.IsNullOrWhiteSpace(summary.ClassName) ? "Adventurer" : summary.ClassName;
            return string.Format("{0}    Lv {1}    {2}", summary.CharacterName, summary.Level, className);
        }

        void CharacterListBox_OnSelectItem()
        {
            RefreshActionState();
        }

        void CharacterListBox_OnDoubleClick(BaseScreenComponent sender, Vector2 position)
        {
            RequestEnterWorld();
        }

        void CharacterListBox_OnScroll()
        {
            scrollBar.ScrollIndex = characterListBox.ScrollIndex;
        }

        void ScrollBar_OnScroll()
        {
            characterListBox.ScrollIndex = scrollBar.ScrollIndex;
        }

        void EnterWorldButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            RequestEnterWorld();
        }

        void NewCharacterButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            if (characters.Count >= roster.MaxCharacters && roster.MaxCharacters > 0)
            {
                statusLabel.Text = DFMPCharacterSelectPolicy.FormatCharacterLimitStatus(characters.Count, roster.MaxCharacters);
                return;
            }

            statusLabel.Text = "Starting new character...";
            DFMPNetworkClient.RequestCreateCharacter();
        }

        void DeleteButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            if (!allowDelete || !HasSelection())
                return;

            DFMPCharacterSummary selected = GetSelectedCharacter();
            var confirmation = new DaggerfallMessageBox(uiManager, uiManager.TopWindow, true);
            confirmation.PauseWhileOpen = true;
            confirmation.SetText(string.Format("Delete {0}? This cannot be undone.", selected.CharacterName));
            confirmation.AddButton(DaggerfallMessageBox.MessageBoxButtons.Yes);
            confirmation.AddButton(DaggerfallMessageBox.MessageBoxButtons.No, true);
            confirmation.OnButtonClick += (messageBox, button) =>
            {
                messageBox.CloseWindow();
                if (button != DaggerfallMessageBox.MessageBoxButtons.Yes)
                    return;

                statusLabel.Text = string.Format("Deleting {0}...", selected.CharacterName);
                DFMPNetworkClient.RequestDeleteCharacter(selected.CharacterId);
            };
            uiManager.PushWindow(confirmation);
        }

        void BackButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            RequestLeave();
        }

        void RequestEnterWorld()
        {
            if (!HasSelection())
            {
                statusLabel.Text = "Select a character to enter the world.";
                return;
            }

            DFMPCharacterSummary selected = GetSelectedCharacter();
            statusLabel.Text = string.Format("Entering world as {0}...", selected.CharacterName);
            DFMPNetworkClient.RequestSelectCharacter(selected.CharacterId);
        }

        void RequestLeave()
        {
            CloseForGameplay(false);
        }

        void OnRosterReceived(DFMPCharacterRosterMessage message)
        {
            ApplyRoster(message);
        }

        void OnActionResultReceived(DFMPCharacterActionResultMessage message)
        {
            if (message.Accepted)
                return;

            statusLabel.Text = string.IsNullOrWhiteSpace(message.Reason)
                ? "That character action was refused."
                : message.Reason;
            RefreshActionState();
        }
    }
}

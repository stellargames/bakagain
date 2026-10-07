namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Controller for the Restore Game screen (REQ_LOAD.DAT over OPTIONS2.SCX),
    /// the Unity counterpart of <c>dialog_LoadGame @ 0x6e29c</c>. Drives two
    /// file pickers — left pane = save directories, right pane = saves within
    /// the selected directory — and the Restore/Cancel buttons.
    /// </summary>
    public class LoadGameMenu : SaveLoadMenuBase, IActionHandler, IScreenInput {
        // REQ_LOAD action ids — verified against generated/REQ/REQ_LOAD.json.
        private const int ButtonRestore = 193;
        private const int ButtonCancel = 192;

        private ILogger _logger;
        private IDialogManager _dialogManager;
        private BakAgain.Core.Services.IGameFlow _flow;

        protected override void Awake() {
            base.Awake();
            _logger = LogManager.LoggerFactory.CreateLogger<LoadGameMenu>();
        }

        [Inject]
        public void Construct(ISaveGameDirectoryService saves, IDialogManager dialogManager,
            BakAgain.Core.Services.IGameFlow flow,
            BakAgain.UI.Navigation.IScreenNavigator navigator) {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        }

        // Populate the pickers once the loader is awake (activation kicked its async REQ build).
        protected override void OnAfterShow() => _ = LoadDirectoriesAsync();

        // --- IActionHandler ---

        public void PrimaryAction(int actionId) {
            switch (actionId) {
                case ButtonRestore:
                    DoRestore();
                    break;
                case ButtonCancel:
                    Close();
                    break;
            }
        }

        public async Awaitable SecondaryAction(int actionId) {
            // Right-click help, per dialog_LoadGame (@ 0x6e6d1 / 0x6e716):
            // Restore -> DDX 136, Cancel -> DDX 137.
            switch (actionId) {
                case ButtonRestore:
                    await _dialogManager.ShowById(136).AsTask();
                    break;
                case ButtonCancel:
                    await _dialogManager.ShowById(137).AsTask();
                    break;
            }
        }

        protected override void Activate() => DoRestore();

        private async void DoRestore() {
            if (_selectedSlot < 0 || _selectedSlot >= _slots.Count) {
                // "no game selected" — MAINMENU.C:529-531 (TASK-804).
                await _dialogManager.ShowById(0x8b).AsTask();
                return;
            }
            SaveSlotInfo slot = _slots[_selectedSlot];
            if (!slot.IsValid) {
                _logger.LogWarning("Cannot restore {Path}: invalid header / wrong version.", slot.FullPath);
                return;
            }
            // The flow hydrates, then (on success) leaves the current configuration — clearing this
            // screen off the stack — and presents the fullmap + chapter dialog before entering the
            // world (faithful to StartGameOrLoadSave @ 0x20835, mode 3). On failure we simply stay.
            bool ok = await _flow.LoadSave(slot.FullPath);
            if (!ok) {
                _logger.LogError("Restore failed for '{Name}' ({Path}). Staying on screen.",
                    slot.DisplayName, slot.FullPath);
                return;
            }
            _logger.LogInformation("Restored '{Name}' (chapter {Chapter}).",
                slot.DisplayName, slot.ChapterNumber);
        }

        // --- IScreenInput ---
        // dialog_LoadGame is a type-2 ("InteractiveScreen") REQ, same as SAVE — arrows/Enter/Esc are
        // owned outright by this screen (MenuLayerHost gives it a ScreenInputLayer instead of a
        // focus-warp NavigableLayer, see IScreenInput.cs). Unlike SAVE, LOAD has no IN_SAVE.DAT text
        // boxes (there is nothing to type — a restore is picked, not named), so WantsText is false and
        // Tab/OnText/OnEdit are all no-ops; only the two pickers + Restore/Cancel need arrow/Enter/Esc.

        public bool WantsText => false;

        public bool OnTab(bool shift) => false;

        public void OnText(char c) {
        }

        public bool OnEdit(EditKey key) => false;

        // Up/Down move the Games picker selection; Ctrl+Up/Down move the Directories picker instead,
        // matching SaveGameMenu's OnDirection. Left/Right have nothing to do here (no caret, no boxes).
        public bool OnDirection(NavDirection dir, bool ctrl) => dir switch {
            NavDirection.Up => ctrl ? MoveDirectorySelection(-1) : MoveGamesSelection(-1),
            NavDirection.Down => ctrl ? MoveDirectorySelection(1) : MoveGamesSelection(1),
            _ => false,
        };

        public void OnSubmit() => DoRestore();

        public void OnCancel() => Close();
    }
}

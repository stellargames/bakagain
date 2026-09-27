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
    [RequireComponent(typeof(UserInterfaceLoader))]
    public class LoadGameMenu : BakAgain.UI.Navigation.ScreenBase, IActionHandler, IFilePickerSource, IScreenInput {
        // REQ_LOAD action ids — verified against generated/REQ/REQ_LOAD.json.
        private const int DirectoryPickerAction = 197;
        private const int FilePickerAction = 196;
        private const int ButtonRestore = 193;
        private const int ButtonCancel = 192;

        // Picker geometry decoded from filePicker_create (sub_ovr142_0) args at
        // the two call sites in dialog_LoadGame @ 0x6e2fb / 0x6e318:
        //   arg_0 = xOffset, arg_2 = yOffset (VGA px), arg_4 = total_width
        //   (list + a 16px scrollbar the engine reserves on the right),
        //   arg_6 = visible rows. Raw VGA args are Directories (33,49,85) and
        //   Games (125,49,159); the widget height is (fontHeight+5)*rows + 2
        //   ≈ 67 VGA. Scaled here into the canonical 1600×1200 stage (×5 / ×6,
        //   Canonical.VgaScale*) so they land in the same square-pixel space as
        //   the REQ buttons — the loader draws purely in canonical px and never
        //   sees VGA. The 16px scrollbar reservation is applied loader-side.
        private static readonly Rect DirectoryPickerRect = new(165, 294, 425, 462);
        private static readonly Rect FilePickerRect = new(625, 294, 795, 462);
        private const int VisibleRows = 5;

        private ILogger _logger;
        private ISaveGameDirectoryService _saves;
        private IDialogManager _dialogManager;
        private BakAgain.Core.Services.IGameFlow _flow;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private UserInterfaceLoader _loader;

        // Snapshots populated as the user navigates. Both lists are empty
        // until the directory picker fires a selection in the left pane.
        private IReadOnlyList<SaveDirectoryInfo> _directories = Array.Empty<SaveDirectoryInfo>();
        private IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();
        private int _selectedDirectory = -1;
        private int _selectedSlot = -1;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<LoadGameMenu>();
            _loader = GetComponent<UserInterfaceLoader>();
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

        private void OnDisable() {
            _selectedDirectory = -1;
            _selectedSlot = -1;
            _directories = Array.Empty<SaveDirectoryInfo>();
            _slots = Array.Empty<SaveSlotInfo>();
        }

        private async UniTask LoadDirectoriesAsync() {
            _directories = await _saves.ListDirectoriesAsync();
            _slots = Array.Empty<SaveSlotInfo>();
            _selectedSlot = -1;
            // Open with the first directory selected (which in turn selects its
            // first save), matching the engine's initial highlight.
            _selectedDirectory = _directories.Count > 0 ? 0 : -1;
            _loader.RefreshFilePicker(DirectoryPickerAction);
            if (_selectedDirectory >= 0) {
                await LoadSlotsAsync(_directories[0].Name);
            } else {
                _loader.RefreshFilePicker(FilePickerAction);
            }
        }

        private async UniTask LoadSlotsAsync(string directoryName) {
            _slots = await _saves.ListSlotsAsync(directoryName);
            // Auto-select the first save of the (newly) selected directory.
            _selectedSlot = _slots.Count > 0 ? 0 : -1;
            _loader.RefreshFilePicker(FilePickerAction);
        }

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

        private async void DoRestore() {
            if (_selectedSlot < 0 || _selectedSlot >= _slots.Count) {
                _logger.LogInformation("Restore pressed with no save selected — ignoring.");
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

        private void Close() => _navigator.Pop().Forget();

        // --- IFilePickerSource ---

        public Rect GetPickerRect(int actionId) => actionId switch {
            DirectoryPickerAction => DirectoryPickerRect,
            FilePickerAction => FilePickerRect,
            _ => Rect.zero,
        };

        public int GetVisibleRows(int actionId) => VisibleRows;

        public int GetItemCount(int actionId) => actionId switch {
            DirectoryPickerAction => _directories.Count,
            FilePickerAction => _slots.Count,
            _ => 0,
        };

        public string GetItemLabel(int actionId, int index) => actionId switch {
            DirectoryPickerAction when index >= 0 && index < _directories.Count =>
                _directories[index].DisplayName,
            FilePickerAction when index >= 0 && index < _slots.Count =>
                _slots[index].DisplayName,
            _ => string.Empty,
        };

        public int GetSelectedIndex(int actionId) => actionId switch {
            DirectoryPickerAction => _selectedDirectory,
            FilePickerAction => _selectedSlot,
            _ => -1,
        };

        public void OnItemSelected(int actionId, int index) {
            switch (actionId) {
                case DirectoryPickerAction:
                    _selectedDirectory = index;
                    if (index >= 0 && index < _directories.Count) {
                        _ = LoadSlotsAsync(_directories[index].Name);
                    }
                    break;
                case FilePickerAction:
                    _selectedSlot = index;
                    break;
            }
        }

        public void OnItemActivated(int actionId, int index) {
            // Double-click: select the row, then (for a save) load it — the engine
            // synthesises Restore on a double-click of a Games row (dialog_LoadGame
            // @ 0x6e61d). Double-clicking a directory just selects it.
            OnItemSelected(actionId, index);
            if (actionId == FilePickerAction) {
                DoRestore();
            }
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

        // Moves the Games picker selection by delta (∓1), reusing the same
        // OnItemSelected(FilePickerAction, ...) path the picker's own mouse click/double-click handling
        // uses (updates _selectedSlot, kicks off the slot's selection side effects). Unlike a mouse
        // click, nothing else redraws the picker afterward, so this also asks the loader to redraw the
        // highlighted row. Clamps at the ends rather than wrapping; when nothing is selected yet
        // (_selectedSlot == -1) this lands on index 0 from either direction (Clamp(-1±1, 0, count-1)),
        // rather than SaveGameMenu's fallback-then-add-delta approach which double-applies delta and
        // skips index 0 on the first Down press from an unselected state.
        private bool MoveGamesSelection(int delta) {
            if (_slots.Count == 0) {
                return false;
            }
            int newIndex = Math.Clamp(_selectedSlot + delta, 0, _slots.Count - 1);
            OnItemSelected(FilePickerAction, newIndex);
            _loader.RefreshFilePicker(FilePickerAction);
            return true;
        }

        // Moves the Directories picker selection by delta (∓1) the same way — OnItemSelected also
        // kicks off LoadSlotsAsync for the newly-selected directory's Games list, same as a mouse click
        // on a directory row would.
        private bool MoveDirectorySelection(int delta) {
            if (_directories.Count == 0) {
                return false;
            }
            int newIndex = Math.Clamp(_selectedDirectory + delta, 0, _directories.Count - 1);
            OnItemSelected(DirectoryPickerAction, newIndex);
            _loader.RefreshFilePicker(DirectoryPickerAction);
            return true;
        }
    }
}

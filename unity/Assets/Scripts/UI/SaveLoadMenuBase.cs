namespace BakAgain.UI {
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement.Loaders;
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// The two file pickers the Save and Restore screens share — left pane = save directories,
    /// right pane = saves within the selected directory. The original's two dialogs drive the
    /// same list widget (canassa LISTWDG.C, used by both mainmenu_save_load_game_dialog and
    /// mainmenu_save_save_game_dialog in MAINMENU.C); they differ only in what a selection
    /// fills in (Save's text boxes) and what activating a game does (restore vs save).
    /// </summary>
    [RequireComponent(typeof(UserInterfaceLoader))]
    public abstract class SaveLoadMenuBase : BakAgain.UI.Navigation.ScreenBase, IFilePickerSource {
        // REQ_LOAD / REQ_SAVE action ids — verified against generated/REQ/REQ_LOAD.json and REQ_SAVE.json.
        protected const int DirectoryPickerAction = 197;
        protected const int FilePickerAction = 196;

        // Picker geometry decoded from filePicker_create (sub_ovr142_0) args at
        // the two call sites in dialog_LoadGame @ 0x6e2fb / 0x6e318 — dialog_SaveGame
        // shares the same layout (MAINMENU.C:358-359 and 631-632 pass the same args):
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

        protected ISaveGameDirectoryService _saves;
        protected BakAgain.UI.Navigation.IScreenNavigator _navigator;
        protected UserInterfaceLoader _loader;

        // Snapshots populated as the user navigates. Both lists are empty
        // until the directory picker fires a selection in the left pane.
        protected IReadOnlyList<SaveDirectoryInfo> _directories = Array.Empty<SaveDirectoryInfo>();
        protected IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();
        protected int _selectedDirectory = -1;
        protected int _selectedSlot = -1;

        protected virtual void Awake() {
            _loader = GetComponent<UserInterfaceLoader>();
        }

        protected virtual void OnDisable() {
            _selectedDirectory = -1;
            _selectedSlot = -1;
            _directories = Array.Empty<SaveDirectoryInfo>();
            _slots = Array.Empty<SaveSlotInfo>();
        }

        /// <summary>A directory row became the selection (Save fills its directory box).</summary>
        protected virtual void OnDirectorySelected(SaveDirectoryInfo directory) {
        }

        /// <summary>A game row became the selection (Save fills its game box).</summary>
        protected virtual void OnSlotSelected(SaveSlotInfo slot) {
        }

        /// <summary>Enter, or a double-click on a game row: restore or save.</summary>
        protected abstract void Activate();

        // *** THE LAST-USED DIRECTORY AND GAME ARE ONE PAIR SHARED BY BOTH DIALOGS. *** The original's
        // g_szSaveSlotDirName + g_abSaveFileHeader (MAINMENU.C:49-59), each with a valid flag: memory
        // only, never written to a file. Every exit of either dialog records the current pair
        // (load: :551-561; save cancel: :1015-1033), a save records the directory and name it wrote
        // (:959, :998-1007), and a bookmark forgets the game (:1480-1492). On open each dialog looks
        // the pair up by its labels — directory case-insensitively (stricmp, :376), game exactly
        // (strcmp, :390) — and a name no longer listed leaves row 0 (TASK-849).
        private static string s_lastDirectory;
        private static string s_lastGame;

        protected static void RememberLast(string directory, string game) {
            s_lastDirectory = directory;
            s_lastGame = game;
        }

        /// <summary>A bookmark overwrote the header the save dialogs would look for (MAINMENU.C:1492).</summary>
        internal static void ForgetLastGame() => s_lastGame = null;

        protected void RememberSelection() => RememberLast(
            _selectedDirectory >= 0 && _selectedDirectory < _directories.Count ? _directories[_selectedDirectory].DisplayName : null,
            _selectedSlot >= 0 && _selectedSlot < _slots.Count ? _slots[_selectedSlot].DisplayName : null);

        protected void Close() {
            RememberSelection();
            _navigator.Pop().Forget();
        }

        // restoreLast: the open, which reselects the remembered pair (the original's need_slot_sel /
        // need_file_sel, cleared after the first listing); a re-list after Remove Directory does not.
        protected virtual async UniTask LoadDirectoriesAsync(bool restoreLast = false) {
            _directories = await _saves.ListDirectoriesAsync();
            _slots = Array.Empty<SaveSlotInfo>();
            _selectedSlot = -1;
            _selectedDirectory = _directories.Count > 0 ? 0 : -1;
            if (restoreLast && s_lastDirectory != null) {
                for (int i = 0; i < _directories.Count; i++) {
                    if (string.Equals(_directories[i].DisplayName, s_lastDirectory, StringComparison.OrdinalIgnoreCase)) {
                        _selectedDirectory = i;
                        break;
                    }
                }
            }
            _loader.RefreshFilePicker(DirectoryPickerAction);
            if (_selectedDirectory >= 0) {
                OnDirectorySelected(_directories[_selectedDirectory]);
                await RefreshSlotsAsync(_directories[_selectedDirectory].Name, restoreLast ? s_lastGame : null);
            } else {
                _loader.RefreshFilePicker(FilePickerAction);
            }
        }

        // Re-lists the slots of one directory and refreshes the right-pane
        // picker; Save/Remove Game call it again after their write/delete completes.
        protected async UniTask RefreshSlotsAsync(string directoryName, string selectGame = null) {
            _slots = await _saves.ListSlotsAsync(directoryName);
            // Auto-select the first save of the (newly) selected directory, or the named one.
            _selectedSlot = _slots.Count > 0 ? 0 : -1;
            for (int i = 0; selectGame != null && i < _slots.Count; i++) {
                if (string.Equals(_slots[i].DisplayName, selectGame, StringComparison.Ordinal)) {
                    _selectedSlot = i;
                    break;
                }
            }
            _loader.RefreshFilePicker(FilePickerAction);
            if (_selectedSlot >= 0) {
                OnSlotSelected(_slots[_selectedSlot]);
            }
        }

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
                        OnDirectorySelected(_directories[index]);
                        _ = RefreshSlotsAsync(_directories[index].Name);
                    }
                    break;
                case FilePickerAction:
                    _selectedSlot = index;
                    if (index >= 0 && index < _slots.Count) {
                        OnSlotSelected(_slots[index]);
                    }
                    break;
            }
        }

        public void OnItemActivated(int actionId, int index) {
            // Double-click: select the row, then (for a game) activate it — the engine
            // synthesises Restore/Save on a double-click of a Games row (MAINMENU.C:485
            // and 805). Double-clicking a directory just selects it.
            OnItemSelected(actionId, index);
            if (actionId == FilePickerAction) {
                Activate();
            }
        }

        // Up/Down (Ctrl: the Directories picker) move the selection by delta (∓1) through the
        // same OnItemSelected path a click takes, then redraw the picker (nothing else does after
        // a key). Clamps at the ends rather than wrapping — the original's widget_list_scroll
        // (canassa UI/WIDGET.C:195) stops at row 0 and at count-1, for both dialogs alike. The
        // original has no "nothing selected" row: a non-empty list always has its wSub_state row
        // current (LISTWDG.C:174), starting at 0 and reset to 0 by every remove (LISTWDG.C:163). The
        // port reaches -1 only for an empty list, which returns early; a -1 would land on row 0.
        protected bool MoveGamesSelection(int delta) => MoveSelection(FilePickerAction, _slots.Count, _selectedSlot, delta);

        protected bool MoveDirectorySelection(int delta) =>
            MoveSelection(DirectoryPickerAction, _directories.Count, _selectedDirectory, delta);

        private bool MoveSelection(int actionId, int count, int selected, int delta) {
            if (count == 0) {
                return false;
            }
            OnItemSelected(actionId, Math.Clamp(selected + delta, 0, count - 1));
            _loader.RefreshFilePicker(actionId);
            return true;
        }
    }
}

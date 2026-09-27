namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Controller for the Save Game screen (REQ_SAVE.DAT over OPTIONS2.SCX),
    /// the Unity counterpart of <c>dialog_SaveGame @ 0x6e7e8</c>. Drives two
    /// file pickers — left pane = save directories, right pane = saves within
    /// the selected directory — the two IN_SAVE.DAT text fields below them
    /// (a per-field text/selection model this screen owns and pushes to the
    /// loader's non-editable <c>InputFormRenderer</c>, built from Task 2) that
    /// the pickers fill in on selection, and the Save/Cancel/Remove Game/Remove
    /// Directory buttons. Mirrors <see cref="LoadGameMenu"/>; the two pickers,
    /// Open/Close, and the <see cref="IFilePickerSource"/> implementation are
    /// identical.
    /// </summary>
    [RequireComponent(typeof(UserInterfaceLoader))]
    public class SaveGameMenu : BakAgain.UI.Navigation.ScreenBase, IActionHandler, IFilePickerSource, IScreenInput {
        // REQ_SAVE action ids — verified against generated/REQ/REQ_SAVE.json.
        private const int DirectoryPickerAction = 197;
        private const int FilePickerAction = 196;
        private const int ButtonSave = 193;
        private const int ButtonCancel = 192;
        // *** THE REMOVE BUTTONS ARE NEVER GREYED, and there is nothing to update. *** The original
        // leaves them live and REFUSES WITH A DIALOG when there is nothing selected — ddx 135 for a
        // game (0x87) and 137 for a directory (0x7e), after asking for confirmation. Greying them
        // instead would swallow those lines and leave the player with a dead button and no reason.
        // An UpdateRemoveButtonStates() stub lived here and was called from two places; it was empty,
        // and the behaviour it implied is the wrong one.
        private const int ButtonRemoveGame = 194;
        private const int ButtonRemoveDirectory = 195;

        // Per-field max length. The IN_SAVE.DAT fields carry no max-length of their own
        // (InputField has no such property — see GameData.Resources.Menu.InputField); the
        // original's DOS 8.3 directory name is 8 chars, and the "Games" field's ItemCount
        // (90, a NUL-inclusive buffer size per the prior TextField.maxLength wiring) gives
        // 89 usable chars. Hardcoded here since the model doesn't expose ItemCount.
        private const int DirFieldMaxLength = 8;
        private const int GameFieldMaxLength = 89;

        // Picker geometry decoded from filePicker_create (sub_ovr142_0) args at
        // the two call sites in dialog_LoadGame @ 0x6e2fb / 0x6e318 — dialog_SaveGame
        // shares the same layout (REQ_SAVE's FilePicker entries carry no geometry
        // of their own, same as REQ_LOAD's):
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
        private ISaveGameService _saveGameService;
        private IDialogManager _dialogManager;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private UserInterfaceLoader _loader;

        // Snapshots populated as the user navigates. Both lists are empty
        // until the directory picker fires a selection in the left pane.
        private IReadOnlyList<SaveDirectoryInfo> _directories = Array.Empty<SaveDirectoryInfo>();
        private IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();
        private int _selectedDirectory = -1;
        private int _selectedSlot = -1;

        // The per-field text/caret/selection model this screen owns and drives (the fields
        // are now drawn non-editable by InputFormRenderer — see Task 2 in
        // docs/superpowers/sdd/... — so there's no native TextField state to read anymore).
        // Index 0 = dir box, 1 = game box, matching IN_SAVE.DAT field order.
        private readonly string[] _text = {"", ""};
        private readonly int[] _selStart = {0, 0};
        private readonly int[] _selEnd = {0, 0};

        // Which of the two IN_SAVE.DAT boxes is the active text-edit target for
        // OnText/OnEdit/caret movement (0 = dir box, 1 = game box). Per the faithful
        // SAVE type-2 model the game box starts focused (Open sets this to 1); Tab/
        // Shift+Tab toggle it (there are only two fields, so shift doesn't change
        // which way it goes).
        private int _activeField = 1;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<SaveGameMenu>();
            _loader = GetComponent<UserInterfaceLoader>();
            // The loader (re)builds its boxes asynchronously every time this screen is
            // (re)enabled; reconcile the initial focus once they exist (mirrors
            // MenuLayerHost's own Built-event + IsBuilt pull-reconcile pattern for the
            // same "async build can finish after/around our own Open/Awake" race).
            _loader.Built += OnLoaderBuilt;
        }

        private void OnDestroy() {
            _loader.Built -= OnLoaderBuilt;
        }

        // The loader's async build can (re)create the IN-form fields after our own Open/Awake
        // reconcile ran (same race MenuLayerHost guards against) — re-push the full model for
        // both fields once they exist so the newly-built renderer picks up our current state.
        private void OnLoaderBuilt(IReadOnlyList<NavWidget> widgets) {
            PushField(0);
            PushField(1);
        }

        // *** A PHONE HAS NO KEYBOARD TO TYPE A SAVE NAME WITH. *** The boxes are drawn, not
        // edited, and take their text from key events — so on a touch-only device no name could
        // be entered and every save was refused ("You must enter a file name"). A press on a box
        // makes it the active one, as Tab does, and on a device with an on-screen keyboard opens
        // it; Update copies what is typed there into the same model the keys write.
        private TouchScreenKeyboard _osk;
        private int _oskField = -1;

        // Registered on the document root, so it survives the loader rebuilding its boxes on every
        // show; a press anywhere inside a box bubbles up here.
        private UnityEngine.UIElements.VisualElement _pressRoot;

        private void OnEnable() {
            _pressRoot = GetComponent<UnityEngine.UIElements.UIDocument>()?.rootVisualElement;
            _pressRoot?.RegisterCallback<UnityEngine.UIElements.PointerDownEvent>(OnRootPointerDown);
        }

        private void OnRootPointerDown(UnityEngine.UIElements.PointerDownEvent evt) {
            for (var e = evt.target as UnityEngine.UIElements.VisualElement; e != null; e = e.parent) {
                if (e.name == "in-field-0" || e.name == "in-field-1") {
                    OnFieldPressed(e.name[^1] - '0');
                    return;
                }
            }
        }

        private void OnFieldPressed(int field) {
            _logger?.LogDebug($"SaveGameMenu: box {field} pressed; on-screen keyboard {(TouchScreenKeyboard.isSupported ? "opens" : "not supported")}.");
            _activeField = field;
            _selStart[field] = 0;
            _selEnd[field] = _text[field].Length;
            PushField(0);
            PushField(1);
            if (TouchScreenKeyboard.isSupported) {
                _osk = TouchScreenKeyboard.Open(_text[field], TouchScreenKeyboardType.Default,
                    autocorrection: false, multiline: false);
                _oskField = field;
            }
        }

        private void Update() {
            if (_osk == null || _oskField < 0) {
                return;
            }
            int cap = _oskField == 0 ? DirFieldMaxLength : GameFieldMaxLength;
            string typed = _osk.text ?? string.Empty;
            if (typed.Length > cap) {
                typed = typed[..cap];
            }
            if (typed != _text[_oskField]) {
                _text[_oskField] = typed;
                _selStart[_oskField] = _selEnd[_oskField] = typed.Length;
                PushField(_oskField);
            }
            if (_osk.status != TouchScreenKeyboard.Status.Visible) {
                _osk = null;
                _oskField = -1;
            }
        }

        // Pushes field i's current text/selection/active state to the loader's IN-form
        // renderer. Call whenever that field's model changes, and for BOTH fields whenever
        // the active field changes (so the old active field's highlight/caret clears).
        private void PushField(int i) {
            _loader.SetInputText(i, _text[i]);
            _loader.SetInputSelection(i, _selStart[i], _selEnd[i]);
            _loader.SetInputActive(i, i == _activeField);
        }

        // Sets field i's text and select-alls it (used when a picker fills a box), then
        // pushes the model to the renderer. Caps to the field's max length — the same clamp
        // OnText enforces — so a picker name can never exceed the header field either.
        private void SetFieldText(int i, string t) {
            t ??= string.Empty;
            int cap = i == 0 ? DirFieldMaxLength : GameFieldMaxLength;
            _text[i] = t.Length > cap ? t[..cap] : t;
            _selStart[i] = 0;
            _selEnd[i] = _text[i].Length;
            PushField(i);
        }

        [Inject]
        public void Construct(ISaveGameDirectoryService saves, ISaveGameService saveGameService,
            IDialogManager dialogManager, BakAgain.UI.Navigation.IScreenNavigator navigator) {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _saveGameService = saveGameService ?? throw new ArgumentNullException(nameof(saveGameService));
            _dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));
            _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        }

        // Post-activation setup. Faithful SAVE type-2 model: the game box (field 1) starts
        // focused with its text select-all'd, the dir box (field 0) inactive. The fields are
        // (re)built asynchronously by the loader on this same enable, so this push is
        // best-effort — if they don't exist yet, OnLoaderBuilt's reconcile pushes the same
        // model once they do.
        protected override void OnAfterShow() {
            _activeField = 1;
            _selStart[1] = 0;
            _selEnd[1] = _text[1].Length;
            PushField(0);
            PushField(1);
            _ = LoadDirectoriesAsync();
        }

        private void OnDisable() {
            _pressRoot?.UnregisterCallback<UnityEngine.UIElements.PointerDownEvent>(OnRootPointerDown);
            _pressRoot = null;
            if (_osk != null) {
                _osk.active = false;
                _osk = null;
                _oskField = -1;
            }
            _selectedDirectory = -1;
            _selectedSlot = -1;
            _directories = Array.Empty<SaveDirectoryInfo>();
            _slots = Array.Empty<SaveSlotInfo>();
        }

        private async UniTask LoadDirectoriesAsync() {
            // The original's MkSaveGameDir always seeds GAMES\SAVES.G01 when the
            // save dialog opens, so a fresh install (no save-set dirs yet) still
            // has a directory to select.
            await _saves.EnsureDefaultDirectoryAsync();
            _directories = await _saves.ListDirectoriesAsync();
            _slots = Array.Empty<SaveSlotInfo>();
            _selectedSlot = -1;
            // Open with the first directory selected (which in turn selects its
            // first save), matching the engine's initial highlight.
            _selectedDirectory = _directories.Count > 0 ? 0 : -1;
            _loader.RefreshFilePicker(DirectoryPickerAction);
            if (_selectedDirectory >= 0) {
                // Pre-fill the dir box for the auto-selected directory. The push is a no-op
                // until the loader has built the fields; OnLoaderBuilt's reconcile re-pushes
                // the model once they exist.
                SetFieldText(0, _directories[0].DisplayName);
                await RefreshSlotsAsync(_directories[0].Name);
            } else {
                _loader.RefreshFilePicker(FilePickerAction);
            }
        }

        // Re-lists the slots of one directory and refreshes the right-pane
        // picker. Factored out of the initial directory load so Save/Remove
        // Game can call it again after their write/delete completes.
        private async UniTask RefreshSlotsAsync(string directoryName) {
            _slots = await _saves.ListSlotsAsync(directoryName);
            // Auto-select the first save of the (newly) selected directory.
            _selectedSlot = _slots.Count > 0 ? 0 : -1;
            _loader.RefreshFilePicker(FilePickerAction);
            if (_selectedSlot >= 0) {
                SetFieldText(1, _slots[_selectedSlot].DisplayName);
            }
        }

        // --- IActionHandler ---

        public void PrimaryAction(int actionId) {
            switch (actionId) {
                case ButtonSave:
                    DoSave();
                    break;
                case ButtonCancel:
                    Close();
                    break;
                case ButtonRemoveGame:
                    DoRemoveGame();
                    break;
                case ButtonRemoveDirectory:
                    DoRemoveDirectory();
                    break;
            }
        }

        public async Awaitable SecondaryAction(int actionId) {
            // Right-click help, per dialog_SaveGame's 9-entry dispatch table
            // (RE §Q5, 0x6f31b): only the four buttons show distinct help text —
            // the pickers and the two text boxes have none at this dialog's level.
            switch (actionId) {
                case ButtonSave:
                    await _dialogManager.ShowById(124).AsTask();
                    break;
                case ButtonCancel:
                    await _dialogManager.ShowById(125).AsTask();
                    break;
                case ButtonRemoveGame:
                    await _dialogManager.ShowById(123).AsTask();
                    break;
                case ButtonRemoveDirectory:
                    await _dialogManager.ShowById(122).AsTask();
                    break;
            }
        }

        // Box-authoritative Save, per dialog_SaveGame's Save handler (RE §Q3,
        // 0x6ef1b). The typed box text — not the picker highlight — drives
        // everything: an existing directory/save name (case rules below) is
        // reused (overwrite), a name with no match creates a new directory or
        // allocates a new slot.
        private async void DoSave() {
            string dirName = _text[0].Trim();
            string gameName = _text[1];

            // 1. Directory-name FORMAT check first — only when a dir name was actually entered.
            if (!string.IsNullOrEmpty(dirName) && !_saves.IsValidDirectoryName(dirName)) {
                await _dialogManager.ShowById(146); // "directory name...not valid...letters and numbers only..."
                return;
            }

            // 2. Missing-name checks.
            bool dirEmpty = string.IsNullOrEmpty(dirName);
            bool gameEmpty = string.IsNullOrEmpty(gameName);
            if (dirEmpty && gameEmpty) {
                await _dialogManager.ShowById(153); // "...must enter a directory name, and a file name..."
                return;
            }
            if (dirEmpty) {
                await _dialogManager.ShowById(152); // "...must enter a directory name..."
                return;
            }
            if (gameEmpty) {
                await _dialogManager.ShowById(151); // "...must enter a file name for your saved game..."
                return;
            }

            // Resolve the directory: an existing row matches case-insensitively
            // (DOS 8.3 names aren't case-sensitive); no match creates a new one.
            SaveDirectoryInfo existingDir = _directories.FirstOrDefault(d =>
                string.Equals(d.DisplayName, dirName, StringComparison.OrdinalIgnoreCase));
            string dirNameOnDisk;
            if (existingDir != null) {
                dirNameOnDisk = existingDir.Name;
            } else {
                string created = await _saves.CreateDirectoryAsync(dirName);
                if (created == null) {
                    // CreateDirectoryAsync doesn't distinguish "all 21 slots taken" (DDX 147)
                    // from "mkdir failed / disk full" (DDX 148) — the original shows whichever
                    // applies; 147 covers both since the service reports one failure signal.
                    await _dialogManager.ShowById(147);
                    return;
                }
                _directories = await _saves.ListDirectoriesAsync();
                _loader.RefreshFilePicker(DirectoryPickerAction);
                dirNameOnDisk = created;
            }

            // Resolve the slot: an existing save with this exact name (case-sensitive —
            // save names are free text, unlike directory names) is overwritten; no match
            // allocates the lowest free manual slot (1-20; slot 0 is the Bookmark).
            IReadOnlyList<SaveSlotInfo> slots = await _saves.ListSlotsAsync(dirNameOnDisk);
            SaveSlotInfo existingSlot = slots.FirstOrDefault(s =>
                string.Equals(s.DisplayName, gameName, StringComparison.Ordinal));
            int slotIndex;
            if (existingSlot != null) {
                slotIndex = existingSlot.SlotIndex; // overwrite
            } else {
                slotIndex = _saves.LowestFreeSlotIndex(dirNameOnDisk);
                if (slotIndex < 0) {
                    await _dialogManager.ShowById(149); // "...directory is full...remove a game...or create a new one."
                    return;
                }
            }

            bool ok = await _saveGameService.SaveAsync(dirNameOnDisk, slotIndex, gameName);
            if (!ok) {
                _logger.LogError("Save failed for '{Name}' (dir '{Dir}', slot {Slot}).",
                    gameName, dirNameOnDisk, slotIndex);
                await _dialogManager.ShowById(150); // "...may not have enough free disk space."
                return;
            }

            _logger.LogInformation("Saved '{Name}' (dir '{Dir}', slot {Slot}).",
                gameName, dirNameOnDisk, slotIndex);
            // The original's Save exits the dialog on success (RE §Q3).
            Close();
        }

        private async void DoRemoveGame() {
            if (_selectedSlot < 0 || _selectedSlot >= _slots.Count) {
                await _dialogManager.ShowById(135); // "There are no selected games that can be removed."
                return;
            }
            if (!await _dialogManager.ShowConfirmById(113)) {
                // "Are you sure you would like to remove the game currently highlighted...?" -> No
                return;
            }
            SaveSlotInfo slot = _slots[_selectedSlot];
            await _saves.DeleteSlotAsync(slot.FullPath);
            if (_selectedDirectory >= 0 && _selectedDirectory < _directories.Count) {
                await RefreshSlotsAsync(_directories[_selectedDirectory].Name);   // re-lists the slots
            }
        }

        private async void DoRemoveDirectory() {
            if (_selectedDirectory < 0 || _selectedDirectory >= _directories.Count) {
                await _dialogManager.ShowById(126); // "There is no selected directory that can be removed."
                return;
            }
            if (!await _dialogManager.ShowConfirmById(112)) {
                // "Are you sure you would like to remove the directory...including all of the games it contains?" -> No
                return;
            }
            SaveDirectoryInfo dir = _directories[_selectedDirectory];
            await _saves.DeleteDirectoryAsync(dir.Name);
            await LoadDirectoriesAsync(); // re-lists directories (and re-selects/loads their slots) + button states
        }

        private void Close() => _navigator.Pop().Forget();

        // Grey out Remove Game / Remove Directory when there's nothing selected to remove.
        // UserInterfaceLoader doesn't currently expose a per-element enable/disable hook (no
        // SetElementEnabled/SetDisabled/GetElement-style API — checked; enabled state is baked
        // in at build time from the REQ's Disabled flag only). Until one exists, this is a no-op;
        // DoRemoveGame/DoRemoveDirectory already guard on _selectedSlot/_selectedDirectory < 0,
        // so an unguarded button press is safe, just not visually greyed.
        // TODO: no per-element enable hook on UserInterfaceLoader yet.

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
                        SetFieldText(0, _directories[index].DisplayName);
                        // Repopulates the Games picker and (via RefreshSlotsAsync) sets the
                        // game-box field for the newly-selected directory's auto-selected slot.
                        _ = RefreshSlotsAsync(_directories[index].Name);
                    }
                            break;
                case FilePickerAction:
                    _selectedSlot = index;
                    if (index >= 0 && index < _slots.Count) {
                        // SetFieldText select-alls the filled name so it reads as
                        // selected/overwritable, matching the original's screenshot.
                        SetFieldText(1, _slots[index].DisplayName);
                    }
                            break;
            }
        }

        public void OnItemActivated(int actionId, int index) {
            // Double-click: select the row, then (for a save slot) save into it —
            // mirrors dialog_LoadGame's double-click-to-restore, adapted to Save's
            // double-click-to-save. Double-clicking a directory just selects it.
            OnItemSelected(actionId, index);
            if (actionId == FilePickerAction) {
                DoSave();
            }
        }

        // --- IScreenInput ---
        // dialog_SaveGame is a type-2 ("InteractiveScreen") REQ: arrows/Tab/Enter/Esc are
        // owned outright by this screen (MenuLayerHost gives it a ScreenInputLayer instead
        // of a focus-warp NavigableLayer, see IScreenInput.cs). This screen WantsText, so
        // InputAdapter forwards every character/edit key to us; the fields themselves are
        // drawn non-editable (InputFormRenderer, Task 2) — this screen owns the field
        // text/caret/selection model outright and pushes it to the renderer.

        public bool WantsText => true;

        public bool OnTab(bool shift) {
            // Only two fields, so shift doesn't change which way the toggle goes — Tab and
            // Shift+Tab both just swap dir <-> game, matching the original's two-box cycle.
            _activeField = _activeField == 0 ? 1 : 0;
            int a = _activeField;
            _selStart[a] = 0;
            _selEnd[a] = _text[a].Length;
            // Push both — the old active field must lose its caret/highlight (goes plain),
            // the new one gains the select-all highlight.
            PushField(0);
            PushField(1);
            return true;
        }

        public void OnText(char c) {
            int a = _activeField;
            string value = _text[a];
            int s = Math.Clamp(Math.Min(_selStart[a], _selEnd[a]), 0, value.Length);
            int e = Math.Clamp(Math.Max(_selStart[a], _selEnd[a]), 0, value.Length);
            string replaced = value[..s] + c + value[e..];
            int maxLength = a == 0 ? DirFieldMaxLength : GameFieldMaxLength;
            if (replaced.Length > maxLength) {
                return; // would exceed the field's max length — drop the keystroke
            }
            _text[a] = replaced;
            _selStart[a] = _selEnd[a] = s + 1;
            PushField(a);
        }

        public bool OnEdit(EditKey key) {
            int a = _activeField;
            string value = _text[a];
            int s = Math.Clamp(Math.Min(_selStart[a], _selEnd[a]), 0, value.Length);
            int e = Math.Clamp(Math.Max(_selStart[a], _selEnd[a]), 0, value.Length);
            bool hasSelection = e > s;

            switch (key) {
                case EditKey.Backspace:
                    if (hasSelection) {
                        _text[a] = value[..s] + value[e..];
                        _selStart[a] = _selEnd[a] = s;
                    } else if (s > 0) {
                        _text[a] = value[..(s - 1)] + value[s..];
                        _selStart[a] = _selEnd[a] = s - 1;
                    }
                    PushField(a);
                    return true;
                case EditKey.Delete:
                    if (hasSelection) {
                        _text[a] = value[..s] + value[e..];
                        _selStart[a] = _selEnd[a] = s;
                    } else if (s < value.Length) {
                        _text[a] = value[..s] + value[(s + 1)..];
                        _selStart[a] = _selEnd[a] = s;
                    }
                    PushField(a);
                    return true;
                case EditKey.Left: {
                    int pos = Math.Clamp(Math.Min(_selStart[a], _selEnd[a]) - (hasSelection ? 0 : 1), 0, value.Length);
                    _selStart[a] = _selEnd[a] = pos;
                    PushField(a);
                    return true;
                }
                case EditKey.Right: {
                    int pos = Math.Clamp(Math.Max(_selStart[a], _selEnd[a]) + (hasSelection ? 0 : 1), 0, value.Length);
                    _selStart[a] = _selEnd[a] = pos;
                    PushField(a);
                    return true;
                }
                case EditKey.Home:
                    _selStart[a] = _selEnd[a] = 0;
                    PushField(a);
                    return true;
                case EditKey.End:
                    _selStart[a] = _selEnd[a] = value.Length;
                    PushField(a);
                    return true;
                default:
                    return false;
            }
        }

        // Real arrow keys arrive here (not through OnEdit — see ScreenInputLayer/InputAdapter:
        // OnEdit(Left/Right) is a numpad-4/6-only path). Left/Right mirror OnEdit's caret move;
        // Up/Down move the Games picker selection, or (ctrl held, i.e. Ctrl+Up/Down) the
        // Directories picker instead — matching the faithful SAVE type-2 model.
        public bool OnDirection(NavDirection dir, bool ctrl) => dir switch {
            NavDirection.Left => OnEdit(EditKey.Left),
            NavDirection.Right => OnEdit(EditKey.Right),
            NavDirection.Up => ctrl ? MoveDirectorySelection(-1) : MoveGamesSelection(-1),
            NavDirection.Down => ctrl ? MoveDirectorySelection(1) : MoveGamesSelection(1),
            _ => false,
        };

        public void OnSubmit() => DoSave();

        public void OnCancel() => Close();

        // Moves the Games picker selection by delta (∓1), reusing the same
        // OnItemSelected(FilePickerAction, ...) path the picker's own mouse click/double-click
        // handling uses (updates _selectedSlot, fills the game-box field, select-alls it).
        // Unlike a mouse click, nothing else redraws the picker afterward, so this also asks
        // the loader to redraw the highlighted row. Clamps at the ends rather than wrapping.
        private bool MoveGamesSelection(int delta) {
            if (_slots.Count == 0) {
                return false;
            }
            int newIndex = Math.Clamp((_selectedSlot < 0 ? 0 : _selectedSlot) + delta, 0, _slots.Count - 1);
            OnItemSelected(FilePickerAction, newIndex);
            _loader.RefreshFilePicker(FilePickerAction);
            return true;
        }

        // Moves the Directories picker selection by delta (∓1) the same way — OnItemSelected
        // also kicks off RefreshSlotsAsync for the newly-selected directory's Games list, same
        // as a mouse click on a directory row would.
        private bool MoveDirectorySelection(int delta) {
            if (_directories.Count == 0) {
                return false;
            }
            int newIndex = Math.Clamp((_selectedDirectory < 0 ? 0 : _selectedDirectory) + delta, 0,
                _directories.Count - 1);
            OnItemSelected(DirectoryPickerAction, newIndex);
            _loader.RefreshFilePicker(DirectoryPickerAction);
            return true;
        }
    }
}

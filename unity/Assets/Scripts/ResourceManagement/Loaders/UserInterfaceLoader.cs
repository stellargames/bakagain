namespace BakAgain.ResourceManagement.Loaders {
    using BakAgain.Core;
    using BakAgain.UI;
    using BakAgain.UI.Cursor;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Menu;
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Builds a UI Toolkit screen from an extracted REQ menu definition. Owns the REQ
    /// load + build loop, the element builders, and the widget set handed to the input
    /// layer (<see cref="Built"/>/<see cref="Cleared"/>). Text buttons fire
    /// <see cref="IActionHandler"/>; Toggle elements reflect state from an
    /// <see cref="IMenuStateProvider"/>. Three concerns are delegated to focused
    /// helpers: file-picker widgets to <see cref="FilePickerRenderer"/>, BICONS icon
    /// loading + Toggle icon state to <see cref="MenuIconLoader"/>, and the companion
    /// LBL labels to <see cref="MenuLabelRenderer"/>.
    /// </summary>
    public class UserInterfaceLoader : MonoBehaviour {
        [SerializeField]
        private string userInterfaceAddress = "REQ_OPT0.DAT";

        // Optional companion LBL resource (e.g. LBL_PREF.DAT) drawn over the menu. The
        // original loads these separately from the REQ (resourceLoadLabels + drawAllLabels).
        // Colour is a theme concern now (.req-label / .req-label--title), so no palette
        // address is needed here.
        [SerializeField]
        private string labelAddress = "";

        // Optional companion IN resource (e.g. IN_SAVE.DAT) — the game's text-input-box
        // resource (labelled editable text boxes, e.g. the Save dialog's "Directories"/
        // "Games" fields). Only screens that actually have one set it; every other REQ
        // screen leaves it empty and InputFormRenderer no-ops.
        [SerializeField]
        private string inputFormAddress = "";

        // Which cursor set this screen's hover indices address. The REQ menus use POINTER; the
        // world, combat and location screens use POINTERG — the same element data means a different
        // shape depending on the screen, so it cannot be inferred from the widget.
        [SerializeField]
        private string cursorSet = "POINTER";

        private UIDocument _document;
        private IActionHandler _actionHandler;
        private IMenuStateProvider _stateProvider;
        private IFilePickerSource _filePickerSource;
        private ILogger _logger;

        // Software cursor (data-driven, mirrors sub_seg030_97F: hovering a widget shows
        // SetPointerImage(uiElement.cursor), leaving restores the arrow). Null-safe: a
        // NullCursorManager is injected until the CursorOverlay prefab is assigned.
        private ICursorManager _cursorManager;
        private BakAgain.Audio.MenuSoundService _menuSound;

        [Inject]
        public void Construct(ICursorManager cursorManager, BakAgain.Audio.MenuSoundService menuSound) {
            _cursorManager = cursorManager;
            _menuSound = menuSound;
        }

        private AsyncOperationHandle<UserInterface> _uiHandle;
        private bool _uiHandleValid;
        private UserInterface _userInterface;

        // BICONS icon loading + Toggle icon state (MenuIconLoader) and the LBL label
        // drawing (MenuLabelRenderer) live in their own components; the loader creates
        // them in Awake and forwards build / refresh / teardown.
        private MenuIconLoader _icons;
        private MenuLabelRenderer _labels;
        private InputFormRenderer _inputForm;

        // The REQ file-picker widgets (Restore/Save Game lists) and all their state
        // are owned by FilePickerRenderer; the loader just builds them and forwards
        // Refresh / keyboard-selection / teardown. Null on screens with no picker
        // (i.e. no IFilePickerSource component on the GameObject).
        private FilePickerRenderer _filePickerRenderer;

        // Built widgets handed to the input-ownership layer (MenuLayerHost wraps them in a
        // NavigableLayer). Replaces the old in-loader keyboard Update()/_navEntries nav — keyboard /
        // gamepad / accelerator focus now lives in NavigableLayer, driven by the one InputAdapter.
        public event Action<IReadOnlyList<BakAgain.UI.InputCore.NavWidget>> Built;
        public event Action Cleared;
        private readonly List<BakAgain.UI.InputCore.NavWidget> _navWidgets = new();
        // Every built entry in build order, and any runtime navigability overrides applied
        // over the data's Disabled flag. Both are cleared with the panel.
        private readonly List<(VisualElement Element, UiElement Entry)> _builtEntries = new();
        private readonly Dictionary<int, bool> _navigableOverrides = new();
        // The loader's async build can fire Built before a sibling host has subscribed (VContainer
        // prefab-instantiation lifecycle ordering). So expose the current state for the host to
        // reconcile against (pull), not only react to the event (push).
        public bool IsBuilt { get; private set; }
        public IReadOnlyList<BakAgain.UI.InputCore.NavWidget> CurrentNavWidgets => _navWidgets;

        /// <summary>
        /// Every built entry's action id, <b>in the order the REQ authored them</b>.
        /// </summary>
        /// <remarks>
        /// <b>For screens where an entry's CELL matters, not its id.</b> SHOOT.DAT ships eight
        /// quarrel buttons over four positions and the original repacks which id sits in which cell
        /// per actor, so the shoot menu has to know the authored cell order to move them —
        /// <c>combat_arena_menu_find_item_page</c> reads the page straight off the entry's index.
        /// Deriving that order from the ids instead (sorting them, or assuming a run) is exactly the
        /// mistake <see cref="GameData.Resources.Combat.CombatMenuSlots.PageOfSlot"/> documents.
        ///
        /// <para>Includes non-navigable entries, like <see cref="_builtEntries"/> itself: an entry
        /// the data ships hidden still occupies its cell.</para>
        /// </remarks>
        public IReadOnlyList<int> AuthoredActionIds {
            get {
                var ids = new List<int>(_builtEntries.Count);
                foreach ((VisualElement _, UiElement entry) in _builtEntries) {
                    ids.Add(entry.ActionId);
                }
                return ids;
            }
        }

        /// <summary>The REQ-defined canonical rect for an ActionId (from the loaded UserInterface),
        /// or false if not loaded / not found. Lets a screen controller place data-driven content
        /// (world viewport, compass slot) off the REQ layout instead of hardcoded coordinates.</summary>
        public bool TryGetElementRect(int actionId, out UnityEngine.Rect canonicalRect) =>
            TryGetRect(_userInterface, actionId, out canonicalRect);

        /// <summary>
        /// The REQ's own canonical rect — where the screen's panel sits inside the
        /// <see cref="Frame"/>, from the file's XPosition/YPosition/Width/Height.
        /// </summary>
        /// <remarks>
        /// This is the panel, not the whole frame: REQ_CAMP's is (65,66,1470,606), the same rect as
        /// the 3D viewport, because the camp panel is drawn where the world normally is. A screen
        /// whose backdrop must be confined to its panel (rather than covering the frame) reads it
        /// from here instead of restating the numbers. Empty before the async load completes.
        /// </remarks>
        public UnityEngine.Rect PanelRect => _userInterface == null
            ? UnityEngine.Rect.zero
            : new UnityEngine.Rect(_userInterface.XPosition, _userInterface.YPosition,
                _userInterface.Width, _userInterface.Height);

        /// <summary>The loaded REQ's <see cref="GameData.Resources.Menu.UserInterfaceType"/>
        /// (Menu/Overlay/InteractiveScreen) — lets <c>MenuLayerHost</c> pick the right input layer.
        /// Defaults to <c>Menu</c> before the async load completes, so a not-yet-loaded screen falls
        /// back to the ordinary NavigableLayer path rather than guessing InteractiveScreen.</summary>
        public UserInterfaceType UserInterfaceType =>
            _userInterface?.UserInterfaceType ?? UserInterfaceType.Menu;

        /// <summary>The loaded REQ's <see cref="GameData.Resources.Layout.DesignFrame"/> — the
        /// coordinate space the menu's absolute rects resolve against. Other components on this
        /// screen (chapter overlay, compass, inventory grid) that build into the same
        /// <see cref="CanonicalStage"/> read this rather than re-deriving their own frame. Null
        /// before the async load completes — every reader is guarded (<c>IsBuilt</c> / <c>Built</c>)
        /// so they never actually observe null in practice, but pass it straight to
        /// <see cref="CanonicalStage.GetOrCreate"/>, which treats null as "no resource" and applies
        /// its own canonical fallback rather than each caller reproducing that logic.</summary>
        public GameData.Resources.Layout.DesignFrame Frame => _userInterface?.Frame;

        /// <summary>The loaded REQ's item-grid geometry
        /// (<see cref="GameData.Resources.Inventory.InventoryLayout"/>) — cell size, column/row
        /// counts, the member shift and the loot centering box, all in this screen's
        /// <see cref="Frame"/>. Non-null only for <c>REQ_INV</c>, the one screen with a fixed-cell
        /// item grid; null everywhere else and before the async load completes. Read by
        /// <see cref="BakAgain.UI.Inventory.ItemGridRenderer"/>, which treats null as "use the
        /// model's faithful defaults".</summary>
        public GameData.Resources.Inventory.InventoryLayout Inventory => _userInterface?.Inventory;

        /// <summary>Number of IN-form fields built by the companion <see cref="InputFormRenderer"/>
        /// (0 when no <c>inputFormAddress</c> is set / nothing built yet).</summary>
        public int InputFieldCount => _inputForm?.FieldCount ?? 0;

        /// <summary>Pushes the displayed text for IN-form field <paramref name="i"/>
        /// (see <see cref="InputFormRenderer.SetText"/>). No-op if nothing built yet.</summary>
        public void SetInputText(int i, string text) => _inputForm?.SetText(i, text);

        /// <summary>Pushes the caret/selection range for IN-form field <paramref name="i"/>
        /// (see <see cref="InputFormRenderer.SetSelection"/>). No-op if nothing built yet.</summary>
        public void SetInputSelection(int i, int selStart, int selEnd) => _inputForm?.SetSelection(i, selStart, selEnd);

        /// <summary>Marks IN-form field <paramref name="i"/> active/inactive
        /// (see <see cref="InputFormRenderer.SetActive"/>). No-op if nothing built yet.</summary>
        public void SetInputActive(int i, bool active) => _inputForm?.SetActive(i, active);

        public static bool TryGetRect(GameData.Resources.Menu.UserInterface ui, int actionId,
            out UnityEngine.Rect canonicalRect) {
            canonicalRect = default;
            if (ui?.MenuEntries == null) {
                return false;
            }
            foreach (GameData.Resources.Menu.UiElement e in ui.MenuEntries) {
                if (e.ActionId == actionId) {
                    canonicalRect = new UnityEngine.Rect(e.XPosition, e.YPosition, e.Width, e.Height);
                    return true;
                }
            }
            return false;
        }

        private void Awake() {
            _document = GetComponent<UIDocument>();
            _actionHandler = GetComponent<IActionHandler>();
            _stateProvider = _actionHandler as IMenuStateProvider;
            _filePickerSource = _actionHandler as IFilePickerSource ?? GetComponent<IFilePickerSource>();
            if (_filePickerSource != null) {
                _filePickerRenderer = new FilePickerRenderer(_filePickerSource);
            }
            _logger = LogManager.LoggerFactory.CreateLogger<UserInterfaceLoader>();
            _icons = new MenuIconLoader(_stateProvider, _logger, _gated.Contains);
            _labels = new MenuLabelRenderer(labelAddress, _logger);
            _inputForm = new InputFormRenderer(inputFormAddress, _logger);

            // VContainer's RegisterComponentInNewPrefab injects the registered controller component
            // (MainMenu, PreferencesMenu, …) but NOT this sibling loader on the same GameObject, so
            // Construct(ICursorManager) never runs and the keyboard-focus cursor warp silently
            // no-ops. Fall back to the scene's cursor singleton (eagerly created at container build,
            // so present before any menu shows); stays null only when no CursorOverlay prefab is
            // wired, which the ?. call sites already tolerate.
            if (_cursorManager == null) {
                _cursorManager = FindAnyObjectByType<CursorManager>();
            }

            // Same sibling-injection gap for the select-sound service. MenuSoundService is a plain DI
            // singleton (not a scene object), so the FindFirstObjectByType trick can't reach it.
            // Read the static composition seam RootLifetimeScope publishes at container build instead
            // of reaching into the container at runtime. Stays null (and the ?. call sites no-op)
            // only before the container is built / if no registration is present.
            _menuSound ??= BakAgain.Audio.MenuSoundService.Instance;
        }

        /// <summary>
        /// The REQ this screen builds from, for a screen that has more than one.
        /// </summary>
        /// <remarks>
        /// <b>Set it while the screen is hidden.</b> <see cref="OnEnable"/> reads the address once
        /// and everything downstream — the widget set, the nav layer, the USS class — is built from
        /// that read, so a write to a screen already up would leave the two disagreeing. Setting it
        /// before <c>Push</c> is the supported order, which is also when the caller knows which
        /// layout it wants.
        /// </remarks>
        public string Address {
            get => userInterfaceAddress;
            set {
                if (string.IsNullOrWhiteSpace(value) || value == userInterfaceAddress) {
                    return;
                }
                if (isActiveAndEnabled) {
                    _logger?.LogError("Refusing to swap {Old} for {New} on a screen that is already "
                        + "showing; set Address before the screen is pushed.",
                        userInterfaceAddress, value);
                    return;
                }

                userInterfaceAddress = value;
            }
        }

        private void OnEnable() {
            _ = OnEnableAsync();
        }

        private void OnDisable() {
            // The screen can be re-enabled (Preferences opens/closes repeatedly),
            // so tear down what OnEnable built and release every handle to avoid
            // duplicate elements and leaked Addressables references.
            if (_document != null && _document.rootVisualElement != null) {
                _document.rootVisualElement.Clear();
            }
            _icons?.ClearToggles();
            _filePickerRenderer?.Clear();
            _navWidgets.Clear();
            _builtEntries.Clear();
            _navigableOverrides.Clear();
            IsBuilt = false;
            Cleared?.Invoke(); // tell MenuLayerHost to pop its NavigableLayer
            ReleaseHandles();
        }

        // The one select seam for a REQ widget, shared by the mouse click and the keyboard activate:
        // play the gated select cue (Task 2), then dispatch the action and resync toggle icons (the
        // keyboard path already did the latter; doing it for every select is harmless + consistent).
        private void Select(UiElement entry) {
            // Touch aids (spec 2026-09-29-android-touch-aids-design.md): the release of a finger whose
            // long-press already fired the right-click, or of a held compass arrow that already
            // repeated, is eaten. Null on desktop and in tests: a plain primary.
            if ((BakAgain.UI.InputCore.TouchInputState.Instance?.TakeSelectRoute(entry.ActionId)
                    ?? BakAgain.UI.InputCore.SelectRoute.Primary) == BakAgain.UI.InputCore.SelectRoute.Swallow) {
                return;
            }
            int? sound = ResolveSelectSound(entry);
            if (sound.HasValue) {
                _menuSound?.Play(sound.Value);
            }
            _actionHandler?.PrimaryAction(entry.ActionId);
            RefreshToggles();
        }

        // Collapses the original's TWO cues into the single "select" cue this UI plays (one sound on
        // the completed click / toggle). Which sound each edge would play — and which SoundFlags bit
        // gates it — is decoded upstream on UiElement.PressSound / ReleaseSound; the COLLAPSE is the
        // port decision and is the only part that belongs here.
        //
        // Taking either cue means the element is audible whenever the original made ANY sound, i.e.
        // silent only when both are suppressed: inventory slots, the world-viewport hotspot, GDS
        // zones, file-picker rows. MainMenu (release suppressed) still pounds and REQ_PUZL (press
        // suppressed) still plays its 18=swoosh, because both cues resolve to the same sound.
        // See docs/superpowers/specs/2026-06-24-ui-select-sounds-design.md.
        public static int? ResolveSelectSound(UiElement entry) =>
            entry.PressSound ?? entry.ReleaseSound;

        /// <summary>
        /// Whether an entry takes part in keyboard/gamepad navigation. menupage_navigate (canassa
        /// MENUPAGE.C:222-311) gates on <c>wEnable_gate</c> — which the extractor emits as
        /// <see cref="UiElement.Disabled"/> — and never on the draw flag.
        ///
        /// <para><see cref="UiElement.Visible"/> IS that draw flag ("0 = skipped by menu_drawEntry,
        /// hit-test still runs"), so a hit-only zone backed by SCX art is a perfectly navigable
        /// entry: REQ_INV's portraits, container window and gold readout, and CONTENTS' nine
        /// chapter rows, are all Visible:false + Disabled:0 and the original walks every one of
        /// them. The one screen whose invisible entries must stay unfocusable — REQ_OPT0's two
        /// label-less buttons — marks them Disabled:1, so this gate already covers it.</para>
        ///
        /// <para>The action-id half excludes the zero-size <c>ActionId -1</c> padding every REQ
        /// carries (28 of them in REQ_INV), which the draw-flag skip used to mask. It keys on the
        /// id rather than the rect deliberately: LOAD's and SAVE's FilePickers are zero-size in the
        /// DAT and are sized by the renderer at build time, so a size-based exclusion would make
        /// those screens un-navigable.</para>
        /// </summary>
        internal static bool IsNavigable(UiElement entry) =>
            entry.Disabled == 0 && entry.ActionId >= 0;

        // Register a built widget as a keyboard/gamepad-navigable target (NavigableLayer drives focus
        // and activation over these). See IsNavigable for the gate logic — it deliberately includes
        // faceless ClickArea hotspots backed by SCX art. The element is set focusable so
        // element.Focus() works (device nav is detached from the UI module). Primary / Secondary
        // are the same delegates the mouse manipulators invoke.
        private void RegisterNav(VisualElement element, UiElement entry) {
            // Record every entry, navigable or not: SetEntryState needs to reach the ones the data
            // ships disabled (a REQ that swaps which buttons are live at runtime, like REQ_CAMP).
            _builtEntries.Add((element, entry));
            if (!IsNavigable(entry)) {
                return;
            }
            element.focusable = true;
            _navWidgets.Add(NavWidgetFor(element, entry));
        }

        private BakAgain.UI.InputCore.NavWidget NavWidgetFor(VisualElement element, UiElement entry) =>
            new BakAgain.UI.InputCore.NavWidget(
                element,
                entry.Label,
                _positionOverrides.TryGetValue(entry.ActionId, out Vector2 moved)
                    ? new Rect(moved.x, moved.y, entry.Width, entry.Height)
                    : new Rect(PanelOriginX + entry.XPosition, PanelOriginY + entry.YPosition,
                        entry.Width, entry.Height),
                () => Select(entry),
                () => { _ = _actionHandler?.SecondaryAction(entry.ActionId); },
                entry.ActionId);

        /// <summary>
        /// Shows or hides a built entry and sets whether it takes part in navigation — the runtime
        /// half of the original's <c>bActive_flag</c> / <c>wEnable_gate</c> writes.
        ///
        /// <para>A REQ can ship an entry the data marks invisible and disabled and still expect it
        /// live later: REQ_CAMP's Stop button replaces Camp and Exit for the duration of a rest.
        /// Nothing could reach those entries before this, because only navigable ones were kept.</para>
        ///
        /// <para>Re-raises <see cref="Built"/> so the input layer is rebuilt over the new widget set
        /// — <c>MenuLayerHost.PushLayer</c> pops first, so this swaps the layer rather than stacking
        /// a second one.</para>
        /// </summary>
        /// <returns>False when this screen has no entry with that action id.</returns>
        /// <summary>
        /// Give an ImageButton a different face, or restore its authored one with null.
        /// </summary>
        /// <remarks>
        /// <b>An element can mean two different things on one screen.</b> The inventory REQ's
        /// button 22 is "Use" everywhere except a shop with more than a shelf's worth of stock,
        /// where <c>UI_DrawInventory</c> @0x56801 rewrites it into the next-page control — same
        /// rect, new action and new icon. Rather than author a second element that the data does
        /// not have, the icon is overridden here the way the original overwrites the field.
        ///
        /// <para>Deliberately does NOT re-raise <c>Built</c>: this repaints one button, and the
        /// rebuild is what makes <see cref="SetEntryState"/> re-entrant.</para>
        /// </summary>
        /// <returns>False when no ImageButton carries that action.</returns>
        public bool SetEntryIcon(int actionId, int? iconBase) {
            if (iconBase.HasValue) {
                _iconOverrides[actionId] = iconBase.Value;
            } else {
                _iconOverrides.Remove(actionId);
            }

            var found = false;
            foreach ((VisualElement element, UiElement entry) in _builtEntries) {
                if (entry.ActionId != actionId) {
                    continue;
                }
                found = true;
                _icons?.LoadAndApplyIcon(element, iconBase ?? entry.IconBase);
            }

            return found;
        }

        /// <summary>The offset from an entry's own face to its highlighted one.</summary>
        /// <remarks>
        /// <c>widget_menu_draw</c> blits <c>sprite_base + hover</c>, so the second frame of the
        /// pair is the highlight — <c>menu_type_3_4</c> @0x2b898's four states are +0 on, +1
        /// on-hovered, +2 off, +3 off-hovered. <see cref="AddImageButton"/>'s pointer handlers
        /// already use it for hover; <see cref="SetEntryHeld"/> uses it for the other thing that
        /// raises the same flag.
        /// </remarks>
        public const int HeldIconOffset = 1;

        /// <summary>
        /// Draw an entry in its HELD face while the screen it raised is up, or back in its own.
        /// </summary>
        /// <remarks>
        /// <b>The original marks the button you are inside.</b> Measured on the travel HUD against
        /// the camp screen, 2026-09-12: with the panel open, REQ_MAIN's encamp button (action 18,
        /// IconBase 14) matches BICONS2#7 to a mean per-channel difference of 10.2 and its own
        /// BICONS1#7 only to 29.5; on the travel screen the two swap, 7.5 against 29.0. The other
        /// five buttons are byte-identical between the two captures, and the frame goes back on
        /// exit.
        ///
        /// <para><b>It is not a shade, and it is not hover.</b> TASK-344 spent three sessions on
        /// five other mechanisms — the toggle frame, the disabled-sprite swap, the sundial's
        /// palette remap per-widget and geometric, and a screen palette that does not exist — and
        /// parking the cursor on the button with nothing open leaves it in its rest face (216
        /// pixels change, all of them the cursor sprite). It is the +1 frame, held.</para>
        ///
        /// <para>A gated or authored-disabled entry keeps the blank stone: one gate, and it returns
        /// before the highlight is computed.</para>
        /// </remarks>
        /// <returns>False when no entry carries that action, or the entry is switched off.</returns>
        /// <summary>
        /// The sprite base the entry carrying <paramref name="actionId"/> currently shows -- its
        /// IconBase, or the override a screen has put on it (<c>wSprite_base</c> as the original
        /// reads it for a right-click's help text).
        /// </summary>
        public bool TryGetIconBase(int actionId, out int iconBase) {
            foreach ((VisualElement _, UiElement entry) in _builtEntries) {
                if (entry.ActionId == actionId) {
                    iconBase = _iconOverrides.TryGetValue(actionId, out int over) ? over : entry.IconBase;
                    return true;
                }
            }
            iconBase = 0;
            return false;
        }

        public bool SetEntryHeld(int actionId, bool held) {
            foreach ((VisualElement _, UiElement entry) in _builtEntries) {
                if (entry.ActionId != actionId) {
                    continue;
                }
                if (entry.Disabled != 0 || _gated.Contains(actionId)) {
                    return false;
                }

                return SetEntryIcon(actionId, held ? entry.IconBase + HeldIconOffset : (int?)null);
            }

            return false;
        }

        private readonly System.Collections.Generic.Dictionary<int, int> _iconOverrides = new();

        /// <summary>Action ids the screen has switched off at runtime.</summary>
        /// <remarks>
        /// Same lifetime as <see cref="_iconOverrides"/> — it survives a hide/show, because the
        /// screen re-asserts it from its own <c>Built</c> handler and a gate that forgot itself on
        /// teardown would come back lit.
        /// </remarks>
        private readonly System.Collections.Generic.HashSet<int> _gated = new();

        /// <summary>
        /// Switch an entry off, or back on — the runtime half of the enable gate.
        /// </summary>
        /// <remarks>
        /// <b>A gated widget does not dim; it wears a different sprite.</b> <c>widget_menu_draw</c>
        /// (canassa <c>UI/WIDGET.C:253</c>) is
        /// <c>if (wEnable_gate != 0) { blit(0x32); return; }</c> — the entry's own IconBase is never
        /// reached, and neither is the hover frame. <see cref="DisabledButtonIcon"/> is that sprite,
        /// BICONS1#25, the bare stone disc.
        ///
        /// <para><b>This is deliberately NOT <see cref="SetEntryState"/>'s <c>navigable</c>.</b>
        /// That flag also describes an entry which is drawn and simply never clickable — the combat
        /// menu's capability label is exactly that and must keep its own face. A screen that means
        /// "unavailable" says so here, and normally says both: the original's one gate stops the
        /// mouse hit-test as well, which <c>navigable</c> already models.</para>
        ///
        /// <para>Repaints in place rather than re-raising <c>Built</c>, like
        /// <see cref="SetEntryIcon"/>: nothing about the widget set changes.</para>
        /// </remarks>
        /// <returns>False when this screen has no entry with that action id.</returns>
        public bool SetEntryGate(int actionId, bool gated) {
            if (gated) {
                _gated.Add(actionId);
            } else {
                _gated.Remove(actionId);
            }

            var found = false;
            var anyToggle = false;
            foreach ((VisualElement element, UiElement entry) in _builtEntries) {
                if (entry.ActionId != actionId) {
                    continue;
                }
                found = true;
                if (entry.ElementType == ElementType.Toggle) {
                    // A Toggle's face is a child element the icon loader owns; RefreshToggles is the
                    // only thing that can reach it.
                    anyToggle = true;
                } else if (entry.ElementType == ElementType.ImageButton
                    && PaintsFace(entry, hitTestOnly)) {
                    _icons?.LoadAndApplyIcon(element, LiveFaceIcon(entry,
                        _iconOverrides.TryGetValue(actionId, out int over) ? over : entry.IconBase));
                }
            }

            if (anyToggle) {
                RefreshToggles();
            }

            return found;
        }

        /// <summary>
        /// The face a button wears while the pointer is on it — its own, one brighter.
        /// </summary>
        /// <remarks>
        /// <b>A BARE BUTTON STAYS BARE.</b> The highlight is base + 1 (the original's <c>di</c>
        /// flag), and <b>-1 is the sentinel for "this entry has no icon"</b> — so -1 + 1 landed on
        /// icon 0, which is a real sprite. A screen that had deliberately cleared a button got a
        /// face back the moment the pointer crossed it: the picklock screen's Use stone, which
        /// INVENTOR.C leaves bare because neither of its two meanings applies to a lock, grew a
        /// small arrow on hover (TASK-585).
        ///
        /// <para>The leave path never had this problem — <see cref="LiveFaceIcon"/> feeds
        /// <c>LoadAndApplyIcon</c>, which clears on a negative. Only the arithmetic needed the
        /// guard.</para>
        /// </remarks>
        internal static int HoverIcon(int baseIcon) => baseIcon < 0 ? baseIcon : baseIcon + 1;

        /// <summary>The face an entry wears right now, gate included.</summary>
        /// <remarks>
        /// <see cref="FaceIcon"/> answers the AUTHORED question — the <c>Disabled</c> field the REQ
        /// ships — and cannot see a gate the screen set afterwards. Both end at the same sprite.
        /// </remarks>
        private int LiveFaceIcon(UiElement entry, int baseIcon) =>
            FaceIcon(entry, baseIcon, entry != null && _gated.Contains(entry.ActionId));

        /// <summary>The canonical rect a built entry was authored at, before any override.</summary>
        public bool TryGetEntryRect(int actionId, out Rect rect) {
            foreach ((VisualElement _, UiElement entry) in _builtEntries) {
                if (entry.ActionId == actionId) {
                    rect = new Rect(PanelOriginX + entry.XPosition, PanelOriginY + entry.YPosition,
                        entry.Width, entry.Height);
                    return true;
                }
            }
            rect = default;
            return false;
        }

        /// <summary>
        /// Move a built entry, in canonical units — the runtime half of the original's
        /// <c>pEntries[n].rect.x/y</c> writes.
        /// </summary>
        /// <remarks>
        /// <b>Recorded as an override rather than written back onto the entry.</b> The
        /// <see cref="UiElement"/> comes from the cached REQ resource, so mutating it would move the
        /// button for every later load of that screen — the same hazard the icon and navigability
        /// overrides above exist to avoid.
        /// </remarks>
        public bool SetEntryPosition(int actionId, float canonicalX, float canonicalY) {
            var found = false;
            foreach ((VisualElement element, UiElement entry) in _builtEntries) {
                if (entry.ActionId != actionId) {
                    continue;
                }
                found = true;
                element.style.left = canonicalX;
                element.style.top = canonicalY;
            }
            if (found) {
                _positionOverrides[actionId] = new Vector2(canonicalX, canonicalY);
            }
            return found;
        }

        /// <summary>Drop a position override, putting the entry back where the REQ authored it.</summary>
        public bool ClearEntryPosition(int actionId) {
            if (!_positionOverrides.Remove(actionId)) {
                return false;
            }
            foreach ((VisualElement element, UiElement entry) in _builtEntries) {
                if (entry.ActionId == actionId) {
                    // Back to the AUTHORED spot, which is panel-relative like every other
                    // placement here — not to the raw number.
                    element.style.left = PanelOriginX + entry.XPosition;
                    element.style.top = PanelOriginY + entry.YPosition;
                }
            }
            return true;
        }

        private readonly System.Collections.Generic.Dictionary<int, Vector2> _positionOverrides = new();

        /// <summary>
        /// When set, this panel supplies CLICK AREAS ONLY and paints no chrome of its own.
        /// </summary>
        /// <remarks>
        /// <b>Some screens hit-test a REQ without ever drawing it.</b> The cipher puzzle is the
        /// clear case: <c>UI_RunCipherPuzzle</c> @0x78c60 blits PUZZLE.SCX, repositions the click
        /// areas and renders its own text, and never calls a menu draw at all — so REQ_PUZL's one
        /// visible entry, the exit ImageButton at (1280,1008), is never painted by the original
        /// even though the data marks it visible. Painting it generically put a button on the chest
        /// plate that the real game leaves bare.
        ///
        /// <para>This reuses the draw/hit split the loader already honours for <c>Visible == false</c>
        /// entries — no face, click wiring intact — and applies it to every entry rather than to the
        /// ones the data happens to flag.</para>
        ///
        /// <para><b>Authored on the prefab, not set from code.</b> Setting it from a sibling
        /// component's <c>Awake</c> looks early enough and is not: the panel built with it still
        /// false, and the exit button still painted. Serialized, it is true before any lifecycle
        /// method runs, so there is no ordering question left to get wrong. The setter stays public
        /// for tests and for a screen that decides this at runtime.</para>
        /// </remarks>
        [SerializeField]
        [Tooltip("Supply click areas only and paint no chrome (the cipher puzzle draws its own).")]
        private bool hitTestOnly;

        /// <inheritdoc cref="hitTestOnly" />
        public bool HitTestOnly {
            get => hitTestOnly;
            set => hitTestOnly = value;
        }

        /// <summary>
        /// The panel's own origin — every authored widget coordinate is RELATIVE to it.
        /// </summary>
        /// <remarks>
        /// *** AUTHORED WIDGET POSITIONS ARE PANEL-RELATIVE, NOT ABSOLUTE. *** A REQ declares its own
        /// rect and then places its widgets inside it, so a button at y=462 in a panel whose origin
        /// is y=66 belongs at 528 on screen. Treating 462 as absolute puts every widget on such a
        /// panel <b>66 units too high</b>.
        ///
        /// <para><b>Only two shipped REQs have a non-zero origin</b> — REQ_CAMP at (65,66) and
        /// REQ_INV2 at (530,210) — and **only the camp screen came through here**, which is why this
        /// survived. `QuantityPickerView` reads REQ_INV2 itself and never touches this loader: it
        /// positions a panel element at the REQ's origin and adds its buttons as CHILDREN with the
        /// authored coordinates, so it composes origin + authored correctly and never had the bug.
        /// Checked, because the first write-up of this fix claimed the picker was fixed by it too.</para>
        ///
        /// <para>On the camp screen the missing offset put "Camp until Healed" and "Exit" on top of
        /// the third party row — measured live: the row spans y 414-474 and the buttons started at
        /// 462, where the original leaves a clear gap (its buttons sit at 66 + 462 = 528, VGA 88,
        /// which a capture of the running game confirms).</para>
        ///
        /// <para>Applied per WIDGET rather than by moving the stage: the screens draw their own
        /// content (the camp backdrop, its dial, the party table) onto the same stage in absolute
        /// coordinates and are already correct, so shifting the stage would break them to fix the
        /// widgets.</para>
        /// </remarks>
        private int PanelOriginX => _userInterface?.XPosition ?? 0;

        /// <inheritdoc cref="PanelOriginX" />
        private int PanelOriginY => _userInterface?.YPosition ?? 0;

        /// <summary>Whether an entry's face is drawn. Its click area is unaffected either way.</summary>
        /// <remarks>
        /// *** THIS GATES THE PAINT ONLY, NEVER THE <c>req-hidden</c> CLASS. *** That class is
        /// <c>visibility: hidden</c> in ClassicTheme.tss, and UI Toolkit does not pick a hidden
        /// element — so suppressing chrome by adding it takes the click area down with the face,
        /// which is the opposite of the draw/hit split this is for. Measured: the puzzle's exit
        /// region stopped answering <c>panel.Pick</c> entirely until this was split apart.
        ///
        /// <para>ponytail: only the ImageButton's icon is gated, because that is the only chrome a
        /// hit-test-only screen currently paints (REQ_PUZL's sole visible entry). A TextButton's
        /// caption and a Toggle's icon would still draw; gate them here when a second screen needs
        /// it, rather than pre-emptively.</para>
        /// </remarks>
        public static bool PaintsFace(UiElement entry, bool hitTestOnly) =>
            entry.Visible && !hitTestOnly;

        /// <summary>The blank stone face every disabled ImageButton wears.</summary>
        /// <remarks>
        /// <c>widget_menu_draw</c> blits this sprite and returns as soon as the entry's enable gate
        /// is set, so it replaces the icon rather than overlaying it.
        /// </remarks>
        public const int DisabledButtonIcon = 0x32;

        /// <summary>The icon an ImageButton actually paints, which is not always its own.</summary>
        /// <remarks>
        /// A disabled entry wears <see cref="DisabledButtonIcon"/> and keeps its IconBase unread —
        /// see the note at the call site for the five shipped REQ files this changes.
        /// </remarks>
        public static int FaceIcon(UiElement entry, int baseIcon) =>
            FaceIcon(entry, baseIcon, gated: false);

        /// <inheritdoc cref="FaceIcon(UiElement,int)"/>
        /// <param name="gated">
        /// Whether the SCREEN has switched this entry off since the panel was built — see
        /// <see cref="SetEntryGate"/>. The authored <c>Disabled</c> field and a runtime gate are the
        /// same gate to <c>widget_menu_draw</c>; they differ only in who set it.
        /// </param>
        public static int FaceIcon(UiElement entry, int baseIcon, bool gated) =>
            gated || (entry != null && entry.Disabled != 0) ? DisabledButtonIcon : baseIcon;

        public bool SetEntryState(int actionId, bool visible, bool navigable) {
            var found = false;
            foreach ((VisualElement element, UiElement entry) in _builtEntries) {
                if (entry.ActionId != actionId) {
                    continue;
                }
                found = true;
                element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

                // *** THE CLASS IS PART OF THE STATE, NOT JUST THE DISPLAY BOX. *** `req-hidden` is
                // `visibility: hidden` (ClassicTheme.tss), which survives an inline `display: Flex`
                // and takes picking down with it — so an entry the DATA ships as Visible:false could
                // never be shown by a screen asking for it, no matter what this method set.
                //
                // REQ_CAMP's Stop button is the casualty that found this: it is Visible:false in the
                // file and switched on for the duration of a rest, so abandoning a rest was
                // impossible — the button was invisible AND unpickable while the screen believed it
                // had shown it. Measured before the fix: visibility=Hidden, Pick() -> nothing.
                element.EnableInClassList("req-hidden", !visible);

                // An entry the data ships invisible never had its face loaded (see PaintsFace), so
                // showing it now has to fetch the icon or it comes up as an empty click zone.
                // ponytail: the hover pair is not re-registered, so a button revealed this way does
                // not light on rollover. Worth doing when a screen needs it; none does today.
                if (visible && entry.ElementType == ElementType.ImageButton && !entry.Visible) {
                    _icons?.LoadAndApplyIcon(element,
                        _iconOverrides.TryGetValue(actionId, out int revealed)
                            ? revealed
                            : entry.IconBase);
                }

                // *** THE SAME HOLE, ON THE OTHER ELEMENT TYPE. *** A TextButton the data ships
                // invisible is built as a bare `req-hitbox` with NO caption child at all (see the
                // build, which only calls GameFontText.Caption under `if (menuEntry.Visible)`), so
                // a screen revealing it got a correctly-placed, correctly-picking, completely blank
                // button. Clearing `req-hidden` above can never paint a face that was never made.
                //
                // REQ_INFO's "Spells" is the case that found it: the character sheet offers the
                // spellbook per member (the original hides it per member too, 0x58457), so it ships
                // Visible:false and is switched on for a caster. Owyn is a caster and the button was
                // there and pickable — just invisible, with nothing to read.
                //
                // Only entries a screen explicitly reveals are promoted, which is why this lives
                // here and not in the build: REQ_TELE's twelve destinations and CONTENTS' nine
                // chapter rows are ALSO hidden TextButtons carrying labels, and they must stay
                // faceless — their screens draw their own art over the top and never call this.
                if (visible && entry.ElementType == ElementType.TextButton && !entry.Visible
                    && element.childCount == 0) {
                    element.RemoveFromClassList("req-hitbox");
                    element.AddToClassList("text-button");
                    // Absolute placement, the same pair the visible path adds.
                    element.AddToClassList("req-element");
                    BakAgain.UI.GameFontText.Caption(element, entry.Label);
                    // The REQ's widgets are built before the screen draws its own panels, so a
                    // button revealed later sits UNDER them: REQ_INFO's Spells came up with only a
                    // sliver showing below the ratings panel it shares an edge with. An entry a
                    // screen has just asked to show is meant to be seen.
                    element.BringToFront();
                }

                element.focusable = navigable;
                // The original has ONE gate, not two: menupage checks wEnable_gate in its mouse
                // hit-test loop as well as in navigation, so a disabled entry cannot be clicked
                // either. Leaving picking on would give a visible-but-disabled button a working
                // mouse path — which is exactly the case a travel-HUD button that greys out hits.
                element.pickingMode = navigable ? PickingMode.Position : PickingMode.Ignore;
                _navigableOverrides[actionId] = navigable;
            }
            if (!found) {
                return false;
            }

            _navWidgets.Clear();
            foreach ((VisualElement element, UiElement entry) in _builtEntries) {
                bool live = _navigableOverrides.TryGetValue(entry.ActionId, out bool over)
                    ? over
                    : IsNavigable(entry);
                if (live && entry.ActionId >= 0) {
                    _navWidgets.Add(NavWidgetFor(element, entry));
                }
            }
            Built?.Invoke(_navWidgets);
            return true;
        }

        private async UniTask OnEnableAsync() {
            if (string.IsNullOrWhiteSpace(userInterfaceAddress)) {
                _logger.LogError("User interface address is not set");
                return;
            }
            // *** GUARD THE DOCUMENT, NOT JUST THE ROOT. *** _document is GetComponent<UIDocument>()
            // and is null whenever this component is enabled without one — and because
            // OnEnableAsync is fire-and-forget, the NullReferenceException that followed sat
            // UNOBSERVED until the GC finalised its result source, at which point UniTask logged it
            // into whatever was running at that moment. That is TASK-382: it looked like an
            // intermittent failure of the pit-swing tests, which are merely the ones that yield
            // enough frames to be running when the finalizer fires. The other two reads of
            // _document in this file already null-check it (lines 270 and 832); this one did not.
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) {
                return;
            }

            _uiHandle = Addressables.LoadAssetAsync<UserInterface>(userInterfaceAddress);
            _uiHandleValid = true;
            _userInterface = await _uiHandle;
            if (_userInterface == null) {
                _logger.LogError("Failed to load user interface from address {Address}", userInterfaceAddress);
                return;
            }

            // The GameObject may have been disabled again before the async load
            // finished; bail rather than build into a hidden document.
            if (!isActiveAndEnabled) {
                return;
            }

            await BuildInto(root);
        }

        // The build proper, factored out of the address path so a caller-supplied element set can
        // reuse it verbatim (see SetMenuEntries). Everything below is unchanged.
        private async UniTask BuildInto(VisualElement root) {
            // Build into the centered canonical stage, not the raw root — the
            // panel is wider than 1600 on non-4:3 windows and absolute coords
            // would land left-aligned. Clear only the stage's children so the
            // background style BackgroundImageLoader put on it survives, and so
            // we never touch the panel-shared parent container (the old
            // root.parent.alignItems hack restyled the container every OTHER
            // UIDocument on the panel lives in).
            VisualElement stage = CanonicalStage.GetOrCreate(root, _userInterface.Frame);
            stage.Clear();
            // The hovered hotspot went with the stage and no PointerLeave will say so: let go of its
            // cursor, or a location keeps the last scene's word ("Tavern") over the next one.
            _cursorManager?.SetByIndex(-1);
            _navWidgets.Clear();
            _builtEntries.Clear();
            _navigableOverrides.Clear();
            IsBuilt = false;
            root.AddToClassList(AddressToUssClassName(userInterfaceAddress));
            // Selects the REQ's pen palette range; the theme (global tokens + .colorset-*
            // scope classes) maps each Colorset to actual colours (Task 3 of the UI-colour
            // refactor — pens are no longer resolved here).
            root.AddToClassList("colorset-" + (int)_userInterface.Colorset);
            int canvasWidth = _userInterface.Width;

            // The menus use the default POINTER set; the world/combat/location screens swap to
            // POINTERG. Set it once per build so hover SetByIndex calls index the right set.
            _cursorManager?.SelectSet(string.IsNullOrWhiteSpace(cursorSet) ? "POINTER" : cursorSet);

            foreach (UiElement menuEntry in _userInterface.MenuEntries) {
                switch (menuEntry.ElementType) {
                    case ElementType.TextButton:
                        AddTextButton(menuEntry, stage);
                        break;
                    case ElementType.Toggle:
                        AddToggle(menuEntry, stage);
                        break;
                    case ElementType.FilePicker:
                        AddFilePicker(menuEntry, stage);
                        break;
                    case ElementType.ImageButton:
                        AddImageButton(menuEntry, stage);
                        break;
                    case ElementType.ClickArea:
                        AddHotspot(menuEntry, stage);
                        break;
                    case ElementType.InputField:
                    case ElementType.TextLink:
                    case ElementType.StatefulIcon:   // engine type-7 widget; unused by shipped REQ
                    case ElementType.CompassWindow:  // synthetic non-rendered marker (travel-HUD compass rect)
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            RefreshToggles();
            await _labels.BuildLabels(stage, canvasWidth, () => isActiveAndEnabled);
            await _inputForm.BuildFields(stage, canvasWidth, () => isActiveAndEnabled);

            // Hand the built nav widgets to the input-ownership layer (MenuLayerHost, on the in-scope
            // menu prefabs, wraps them in a Passive NavigableLayer; screens without a host — the
            // in-game travel REQ — simply build with no layer pushed).
            IsBuilt = true;
            _menuSound?.Warmup(); // best-effort preload of sound_pound to avoid a first-select load hitch
            Built?.Invoke(_navWidgets);
        }

        /// <summary>
        /// Replaces this screen's element set with one built at runtime and rebuilds the widgets.
        /// </summary>
        /// <returns>False when the layout has not loaded yet, or the screen is not showing.</returns>
        /// <remarks>
        /// <para>For screens whose clickable regions are not in the REQ file. An interactive location
        /// is the case this exists for: <c>REQ_GDS.DAT</c> ships 21 element slots whose contents are
        /// placeholders, and <c>gds_loadSceneFile</c> zeroes the count and writes the scene's hotspots
        /// over them from index 0 — so the shipped entries are scratch and are meant to be discarded,
        /// not merged with. The largest scene has 11 hotspots, so the slot count is headroom rather
        /// than a limit we need to enforce.</para>
        ///
        /// <para>The layout still loads by address like every other screen; only the element array is
        /// runtime data. That keeps locations on this renderer — the same click, hover-cursor and nav
        /// wiring every REQ widget gets — instead of a parallel hotspot layer.</para>
        /// </remarks>
        public async UniTask<bool> SetMenuEntries(UiElement[] entries) {
            if (_userInterface == null) {
                _logger.LogError("SetMenuEntries called before {Address} finished loading.",
                    userInterfaceAddress);
                return false;
            }
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null || !isActiveAndEnabled) {
                return false;
            }

            _userInterface.MenuEntries = entries ?? System.Array.Empty<UiElement>();
            await BuildInto(root);
            return IsBuilt;
        }

        private void OnDestroy() {
            ReleaseHandles();
        }

        private void ReleaseHandles() {
            _icons?.ReleaseHandles();
            _labels?.ReleaseHandles();
            _inputForm?.ReleaseHandles();
            if (_uiHandleValid && _uiHandle.IsValid()) {
                Addressables.Release(_uiHandle);
            }
            _uiHandleValid = false;
        }

        /// <summary>Re-query every Toggle's state from the <see cref="IMenuStateProvider"/>
        /// and update its icon. Call after a click or a bulk model change
        /// (e.g. the Preferences "Defaults" button).</summary>
        public void RefreshToggles() => _icons?.RefreshToggles();

        private void AddTextButton(UiElement menuEntry, VisualElement root) {
            var button = new Button(Action) {
                // Named by ActionId like the other element builders (imagebutton_/hotspot_/toggle_),
                // so a screen can look up a specific text button by name.
                name = $"button_{menuEntry.ActionId}",
                // Data-driven placement only; all visual styling is in .text-button.
                style = {
                    left = PanelOriginX + menuEntry.XPosition,
                    top = PanelOriginY + menuEntry.YPosition,
                    width = menuEntry.Width,
                    height = menuEntry.Height,
                }
            };
            button.AddManipulator(new Clickable(RightClick) {
                activators = {new ManipulatorActivationFilter {button = MouseButton.RightMouse}}
            });

            // *** AN INVISIBLE TEXT BUTTON IS A BARE HIT BOX, NOT A HIDDEN BUTTON. *** `.text-button`
            // paints a surface and a bevel and the caption paints the label, so suppressing them by
            // adding `req-hidden` was doing real visual work here — at the cost of the hit area,
            // since that class is `visibility: hidden` and UI Toolkit will not pick it. Skipping the
            // chrome instead paints exactly as little and keeps the element live, which is what these
            // entries are FOR.
            //
            // The shipped cases are all faceless zones over SCX art whose clicks the screen wants:
            // REQ_TELE's twelve destinations (TeleportScreen draws its own pins with
            // PickingMode.Ignore and says outright that the "hit-box survives only because the REQ
            // owns it" — so hidden, no destination could be hovered or clicked) and CONTENTS' nine
            // chapter rows, which RegisterNavGateTests already calls faceless hit zones.
            //
            // A screen hiding a button it normally shows still goes through SetEntryState, which owns
            // the class; InventoryMenu's "More Info" is that case and is unaffected.
            if (menuEntry.Visible) {
                button.AddToClassList("text-button");
                button.AddToClassList("req-element"); // absolute placement (dialog buttons stay flex)
                // The caption goes in a child so it can carry the vertical stretch without the chrome
                // going with it — see GameFontText.Caption.
                BakAgain.UI.GameFontText.Caption(button, menuEntry.Label);
            } else {
                button.AddToClassList("req-hitbox");
            }
            RegisterCursorHover(button, menuEntry);
            root.Add(button);
            RegisterNav(button, menuEntry);
            return;

            void Action() {
                Select(menuEntry);
            }

            void RightClick() {
                _ = _actionHandler?.SecondaryAction(menuEntry.ActionId);
            }
        }

        // Data-driven hover cursor (mirrors sub_seg030_97F): show the element's cursor
        // image on pointer-enter, restore the default arrow on leave. No-op until the
        // CursorOverlay prefab is wired (a NullCursorManager is injected meanwhile).
        private void RegisterCursorHover(VisualElement element, UiElement menuEntry) {
            if (_cursorManager == null) {
                return;
            }
            int cursor = menuEntry.Cursor;
            element.RegisterCallback<PointerEnterEvent>(_ => _cursorManager.SetByIndex(cursor));
            element.RegisterCallback<PointerLeaveEvent>(_ => _cursorManager.SetByIndex(-1));
        }

        // Render an ImageButton: its BICONS icon plus click wiring. In the original an
        // ImageButton shares the Toggle renderer (menu_type_3_4 @ 0x2b898) — the icon is
        // drawn at IconBase, swapping to IconBase+1 while the entry is highlighted. The
        // button face IS the icon (not part of the SCX), so it is loaded here.
        private void AddImageButton(UiElement menuEntry, VisualElement root) {
            // Read through the override every time rather than capturing the authored value: an
            // entry's icon can be swapped after the panel is built (see SetEntryIcon), and the
            // hover pair has to follow it or the button flickers back to its old face.
            int BaseIcon() => _iconOverrides.TryGetValue(menuEntry.ActionId, out int over)
                ? over
                : menuEntry.IconBase;

            int baseIcon = BaseIcon();
            var button = new VisualElement {
                name = $"imagebutton_{menuEntry.ActionId}",
                pickingMode = PickingMode.Position,
                style = {
                    left = PanelOriginX + menuEntry.XPosition,
                    top = PanelOriginY + menuEntry.YPosition,
                    width = menuEntry.Width,
                    height = menuEntry.Height,
                },
            };
            button.AddToClassList("req-hitbox");

            // *** NO `req-hidden` HERE, EVER. *** For this element type the face IS the icon, and
            // the block below already declines to load it — so the class would buy nothing visually
            // and cost the hit area, being `visibility: hidden` (ClassicTheme.tss), which UI Toolkit
            // will not pick and NavigableLayer.CanFocus refuses.
            //
            // That is what made COMBAT and SHOOT action 22 dead: the 250x270 character panel on the
            // left, which the original opens the acting character's pack from. Every other link in
            // that chain was already built and wired -- CombatCommands.For(22) -> CharacterScreen ->
            // HotspotService.OpenCombatInventory -- and the class alone kept the click from ever
            // arriving. It also contradicted IsNavigable, which returns TRUE for exactly this shape
            // and has a test saying so.
            //
            // A screen that wants such an entry genuinely hidden asks for it through SetEntryState,
            // which owns the class.

            // *** An invisible ImageButton must not paint its face. *** Visible=0 means "skipped by
            // menu_drawEntry, hit-test still runs", and for this element type the face IS the icon
            // rather than part of the SCX — so loading it anyway is the one case where the draw flag
            // has a visible consequence. Three shipped entries are affected and all carry a real
            // IconBase, not a -1 placeholder: COMBAT and SHOOT action 22 (icon 34, the 250x270
            // character-screen zone) and REQ_CAMP action 194 (icon 117).
            //
            // The click wiring below stays: the entry remains a live hit zone, which is exactly what
            // the original's draw/hit split gives it.
            if (PaintsFace(menuEntry, hitTestOnly)) {
                // *** A DISABLED IMAGEBUTTON WEARS THE BLANK STONE, NOT ITS OWN ICON. ***
                // widget_menu_draw (canassa UI/WIDGET.C:253) is `if (enable_gate) { blit(0x32);
                // return; }` — the entry's IconBase is never reached, and there is no hover frame
                // because the hit-test is skipped too.
                //
                // Drawing IconBase anyway is why the cast screen wore the travel HUD's faces:
                // REQ_CAST and REQ_MAIN share a frame, and REQ_CAST's four disabled school buttons
                // still carry REQ_MAIN's encamp/journal/map icons (10, 16, 14) in the shipped data.
                // CastScreen worked around it by overwriting all six buttons with INVSPELL icons,
                // which also lit four buttons the original ships switched off. Thirteen entries
                // across five REQ files are affected — REQ_CMAP alone has five.
                // LiveFaceIcon, not FaceIcon: a rebuild has to keep a gate the screen set at
                // runtime, or SetEntryState's re-raise repaints the lit face over it.
                _icons.LoadAndApplyIcon(button, LiveFaceIcon(menuEntry, baseIcon));
                if (menuEntry.Disabled == 0) {
                    // Hover highlight (the original's `di` flag): IconBase+1 on enter, back on
                    // leave — but never while gated, because the gate returns before the hover
                    // frame is computed AND the hit-test that would raise these is off anyway.
                    button.RegisterCallback<PointerEnterEvent>(_ => _icons.LoadAndApplyIcon(button,
                        _gated.Contains(menuEntry.ActionId)
                            ? DisabledButtonIcon
                            : HoverIcon(BaseIcon())));
                    button.RegisterCallback<PointerLeaveEvent>(_ => _icons.LoadAndApplyIcon(button,
                        LiveFaceIcon(menuEntry, BaseIcon())));
                }
            }
            RegisterCursorHover(button, menuEntry);
            button.AddManipulator(new Clickable(() => Select(menuEntry)));
            button.AddManipulator(new Clickable(() => {
                _ = _actionHandler?.SecondaryAction(menuEntry.ActionId);
            }) {
                activators = {new ManipulatorActivationFilter {button = MouseButton.RightMouse}}
            });
            root.Add(button);
            RegisterNav(button, menuEntry);
        }

        // Render a ClickArea: an invisible-but-pickable hotspot (e.g. REQ_MAIN's party
        // portraits and 3D viewport). No artwork — just a click region. ClickAreas are
        // invisible by data (Visible == false) but must still take clicks, so picking
        // stays enabled regardless of the Visible flag.
        private void AddHotspot(UiElement menuEntry, VisualElement root) {
            var hotspot = new VisualElement {
                name = $"hotspot_{menuEntry.ActionId}",
                pickingMode = PickingMode.Position,
                style = {
                    left = PanelOriginX + menuEntry.XPosition,
                    top = PanelOriginY + menuEntry.YPosition,
                    width = menuEntry.Width,
                    height = menuEntry.Height,
                },
            };
            hotspot.AddToClassList("req-hitbox");
            hotspot.AddManipulator(new Clickable(() => Select(menuEntry)));
            hotspot.AddManipulator(new Clickable(() => {
                _ = _actionHandler?.SecondaryAction(menuEntry.ActionId);
            }) {
                activators = {new ManipulatorActivationFilter {button = MouseButton.RightMouse}}
            });
            RegisterCursorHover(hotspot, menuEntry);
            root.Add(hotspot);
            RegisterNav(hotspot, menuEntry);
        }

        private void AddToggle(UiElement menuEntry, VisualElement root) {
            var icon = new VisualElement {
                name = $"toggle_{menuEntry.ActionId}",
                style = {
                    left = PanelOriginX + menuEntry.XPosition,
                    top = PanelOriginY + menuEntry.YPosition,
                    width = menuEntry.Width,
                    height = menuEntry.Height,
                }
            };
            icon.AddToClassList("req-hitbox");
            if (!menuEntry.Visible) {
                icon.AddToClassList("req-hidden");
            }
            // Left click toggles/selects; right click asks the handler for help.
            icon.AddManipulator(new Clickable(() => Select(menuEntry)));
            icon.AddManipulator(new Clickable(() => {
                _ = _actionHandler?.SecondaryAction(menuEntry.ActionId);
            }) {
                activators = {new ManipulatorActivationFilter {button = MouseButton.RightMouse}}
            });

            // Hover highlight (the original's `di` flag in menu_type_3_4): swap to the +1 icon frame
            // while hovered, re-querying the on/off state so the right frame shows. Disabled entries
            // aren't highlighted in the original (their hit-test is skipped), so skip the swap there.
            if (menuEntry.Disabled == 0) {
                icon.RegisterCallback<PointerEnterEvent>(_ =>
                    _icons.SetToggleHovered(menuEntry.ActionId, menuEntry, hovered: true));
                icon.RegisterCallback<PointerLeaveEvent>(_ =>
                    _icons.SetToggleHovered(menuEntry.ActionId, menuEntry, hovered: false));
            }

            RegisterCursorHover(icon, menuEntry);
            root.Add(icon);
            _icons.RegisterToggle(menuEntry.ActionId, menuEntry, icon);
            RegisterNav(icon, menuEntry);
        }

        // The REQ file pickers (Restore/Save Game lists) are built and driven by
        // FilePickerRenderer; these three seams forward to it. AddFilePicker runs
        // from the REQ build loop; RefreshFilePicker is called by the screen
        // controller after its data changes; TryMovePickerSelection is the
        // NavigableLayer's keyboard cursor-key hook (routed via MenuLayerHost).
        private void AddFilePicker(UiElement menuEntry, VisualElement root) {
            if (_filePickerRenderer == null) {
                _logger.LogWarning(
                    "Skipping FilePicker {ActionId}: no IFilePickerSource component on the screen.",
                    menuEntry.ActionId);
                return;
            }
            _filePickerRenderer.Add(menuEntry, root);
        }

        /// <summary>Re-render a file picker after its underlying data changed (e.g.
        /// selecting a directory in the left pane re-populates the right pane).</summary>
        public void RefreshFilePicker(int actionId) => _filePickerRenderer?.Refresh(actionId);

        /// <summary>Keyboard cursor-key hook: move the active picker's selection.
        /// Returns true when consumed (Up/Down over a live picker) so the caller —
        /// the NavigableLayer — does not also move button focus.</summary>
        public bool TryMovePickerSelection(BakAgain.UI.InputCore.NavDirection dir) =>
            _filePickerRenderer?.TryMoveSelection(dir) ?? false;

        private static string AddressToUssClassName(string address) {
            string className = address.Replace("/", "__")
                .Replace("[", "__")
                .Replace("]", "")
                .ToLowerInvariant();

            return Regex.Replace(className, "[^a-zA-Z0-9_-]+", "");
        }
    }
}

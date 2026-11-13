namespace BakAgain.UI.InputCore {
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using T = GameData.Resources.Text.UiTemplates;

    /// <summary>
    /// TASK-1080 (owner: "let players remap all actions"): every key the game reads is an action here, with
    /// the original's key as its default, so the Controls page can move any of them. The menus' keys
    /// (Enter, Escape, the arrows and keypad, Tab, Backspace, the page keys, the letter and digit hotkeys) are
    /// read by <see cref="InputAdapter"/>; click and inspect (any mouse button) and the keypad's stand-ins
    /// for them by <see cref="IPointer"/>, and by UI Toolkit through <see cref="UiPointerBindings"/>; the
    /// Shifts and Ctrl by the gameplay seams, as the rest.
    /// </summary>
    /// <remarks>
    /// Fixed: pointer motion and the wheel, typing in a text field, Alt (only the cheat chord reads it), and
    /// the Skip — which is every key nothing else claims, so it follows the others.
    /// </remarks>
    public sealed partial class SystemInputSource {
        // Up, Down, Left, Right.
        private static readonly string[] Directions = { "up", "down", "left", "right" };

        internal InputAction Activate { get; private set; }
        internal InputAction CancelKey { get; private set; }
        internal InputAction NextFocus { get; private set; }
        internal InputAction PreviousFocus { get; private set; }
        internal InputAction PageUp { get; private set; }
        internal InputAction PageDown { get; private set; }
        internal InputAction First { get; private set; }
        internal InputAction Last { get; private set; }

        /// <summary>The arrows (and gamepad), Up, Down, Left, Right.</summary>
        internal InputAction[] NavArrows { get; } = new InputAction[4];

        /// <summary>Keypad 8, 2, 4, 6: focus moves, and the caret in a text field.</summary>
        internal InputAction[] NavPad { get; } = new InputAction[4];

        /// <summary>'a'..'z'.</summary>
        internal InputAction[] Letters { get; } = new InputAction[26];

        /// <summary>'1'..'9', '0': the REQ scancodes 2..11.</summary>
        internal InputAction[] Digits { get; } = new InputAction[10];

        private InputAction _selectKeys, _inspectKeys, _pan, _northUp;
        private InputAction _selectButton, _inspectButton, _leftShift, _rightShift, _ctrl;

        /// <summary>The click's and the inspect's mouse buttons, as moved on the page.</summary>
        internal string SelectButtonPath => _selectButton.bindings[0].effectivePath;
        internal string InspectButtonPath => _inspectButton.bindings[0].effectivePath;
        private readonly List<InputAction> _keyActions = new();

        private InputAction Key(string name, params string[] paths) {
            var action = new InputAction(name, InputActionType.Button);
            foreach (string path in paths) {
                action.AddBinding(path);
            }
            action.Enable();
            _keyActions.Add(action);
            return action;
        }

        // A hotkey is the key that PRINTS its letter (a QWERTZ 'Z' is 'z', an AZERTY 'A' is 'a'), as the
        // adapter always read it. A layout with no such key (Cyrillic) keeps the US position.
        // ponytail: decided once, when the source starts; a layout switched mid-game keeps the old choice.
        // With no graphics device (-nographics: the CI test run) there is no display to ask for the layout,
        // and on Linux the query segfaults in GDK, so the US position stands.
        private static string LetterPath(char c) =>
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null
            && Keyboard.current?.FindKeyOnCurrentKeyboardLayout(c.ToString()) != null ? $"<Keyboard>/#({c})" : $"<Keyboard>/{c}";

        private void InitKeys() {
            Activate = Key("Activate", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Gamepad>/buttonSouth");
            CancelKey = Key("Cancel", "<Keyboard>/escape", "<Gamepad>/buttonEast");
            string[] arrows = { "upArrow", "downArrow", "leftArrow", "rightArrow" };
            string[] pad = { "numpad8", "numpad2", "numpad4", "numpad6" };
            for (int i = 0; i < 4; i++) {
                NavArrows[i] = Key("Navigate " + Directions[i], "<Keyboard>/" + arrows[i],
                    "<Gamepad>/dpad/" + Directions[i], "<Gamepad>/leftStick/" + Directions[i]);
                NavPad[i] = Key("Keypad " + Directions[i], "<Keyboard>/" + pad[i]);
            }
            NextFocus = Key("Next", "<Keyboard>/tab");
            PreviousFocus = Key("Previous", "<Keyboard>/backspace");
            PageUp = Key("PageUp", "<Keyboard>/pageUp");
            PageDown = Key("PageDown", "<Keyboard>/pageDown");
            First = Key("First", "<Keyboard>/home");
            Last = Key("Last", "<Keyboard>/end");
            for (int i = 0; i < 26; i++) {
                char c = (char)('a' + i);
                Letters[i] = Key("Hotkey " + c, LetterPath(c));
            }
            for (int i = 0; i < 10; i++) {
                Digits[i] = Key("Hotkey " + DigitChar(i), "<Keyboard>/" + DigitChar(i));
            }
            // SCREEN.C:114 polls keypad 5 and 0 with the left button and keypad + with the right.
            _selectKeys = Key("SelectKeys", "<Keyboard>/numpad5", "<Keyboard>/numpad0");
            _inspectKeys = Key("InspectKeys", "<Keyboard>/numpadPlus");
            _northUp = Key("MapNorthUp", "<Keyboard>/n");
            _selectButton = Key("Select", "<Mouse>/leftButton");
            _inspectButton = Key("Inspect", "<Mouse>/rightButton");
            // The original reads the two Shifts apart (shop paging back vs to the front, combat's arms).
            _leftShift = Key("LeftShift", "<Keyboard>/leftShift");
            _rightShift = Key("RightShift", "<Keyboard>/rightShift");
            _ctrl = Key("Ctrl", "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl");

            _pan = new InputAction("PanArena", InputActionType.Value);
            _pan.AddCompositeBinding("2DVector(mode=2)") // Digital: each axis -1, 0 or 1, as the keys were read
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _pan.Enable();
            _keyActions.Add(_pan);
        }

        /// <summary>'1'..'9' for 0..8, '0' for 9.</summary>
        internal static char DigitChar(int i) => i == 9 ? '0' : (char)('1' + i);

        private static readonly string[] WalkIds = { "forward", "back", "turn_left", "turn_right" };
        private static readonly string[] WalkLabels = { T.ControlsForward, T.ControlsBack, T.ControlsTurnLeft, T.ControlsTurnRight };
        private static readonly string[] NavLabels = { T.ControlsUp, T.ControlsDown, T.ControlsLeft, T.ControlsRight };
        private static readonly string[] PanLabels = { T.ControlsPanUp, T.ControlsPanDown, T.ControlsPanLeft, T.ControlsPanRight };

        // In the page's order: walking, the fight, the menus, the hotkeys.
        private void BuildSlots() {
            const string W = InputBindings.Walk, C = InputBindings.Combat, M = InputBindings.Menus;
            var slots = new List<RebindSlot>();
            // The _move composites: WASD is binding 0 with parts 1-4, the arrows binding 5 with parts 6-9.
            for (int i = 0; i < 4; i++) slots.Add(new RebindSlot(WalkIds[i], W, WalkLabels[i], _move, 1 + i));
            for (int i = 0; i < 4; i++) slots.Add(new RebindSlot(WalkIds[i] + "_2", W, WalkLabels[i], _move, 6 + i, second: true));
            slots.Add(new RebindSlot("swing", C, T.ControlsSwing, _swing, 0));
            slots.Add(new RebindSlot("combat_overlay", C, T.ControlsCombatOverlay, _toggleOverlay, 0));
            for (int i = 0; i < 4; i++) slots.Add(new RebindSlot("pan_" + Directions[i], C, PanLabels[i], _pan, 1 + i));
            slots.Add(new RebindSlot("activate", M, T.ControlsActivate, Activate, 0));
            slots.Add(new RebindSlot("activate_2", M, T.ControlsActivate, Activate, 1, second: true));
            slots.Add(new RebindSlot("cancel", M, T.ControlsCancel, CancelKey, 0));
            for (int i = 0; i < 4; i++) slots.Add(new RebindSlot("nav_" + Directions[i], M, NavLabels[i], NavArrows[i], 0));
            for (int i = 0; i < 4; i++) slots.Add(new RebindSlot("nav_" + Directions[i] + "_2", M, NavLabels[i], NavPad[i], 0, second: true));
            slots.Add(new RebindSlot("next", M, T.ControlsNext, NextFocus, 0));
            slots.Add(new RebindSlot("previous", M, T.ControlsPrevious, PreviousFocus, 0));
            slots.Add(new RebindSlot("page_up", M, T.ControlsPageUp, PageUp, 0));
            slots.Add(new RebindSlot("page_down", M, T.ControlsPageDown, PageDown, 0));
            slots.Add(new RebindSlot("first", M, T.ControlsFirst, First, 0));
            slots.Add(new RebindSlot("last", M, T.ControlsLast, Last, 0));
            slots.Add(new RebindSlot("select", M, T.ControlsSelect, _selectButton, 0, mouseButton: true));
            slots.Add(new RebindSlot("inspect", M, T.ControlsInspect, _inspectButton, 0, mouseButton: true));
            slots.Add(new RebindSlot("select_key", M, T.ControlsSelectKey, _selectKeys, 0));
            slots.Add(new RebindSlot("select_key_2", M, T.ControlsSelectKey, _selectKeys, 1, second: true));
            slots.Add(new RebindSlot("inspect_key", M, T.ControlsInspectKey, _inspectKeys, 0));
            slots.Add(new RebindSlot("map_north_up", M, T.ControlsNorthUp, _northUp, 0));
            for (int i = 0; i < 26; i++) {
                char c = (char)('a' + i);
                slots.Add(new RebindSlot("hotkey_" + c, M, T.ControlsHotkey, Letters[i], 0,
                    letter: char.ToUpperInvariant(c).ToString(), section: T.ControlsGroupHotkeys));
            }
            for (int i = 0; i < 10; i++) {
                slots.Add(new RebindSlot("hotkey_" + DigitChar(i), M, T.ControlsHotkey, Digits[i], 0,
                    letter: DigitChar(i).ToString(), section: T.ControlsGroupHotkeys));
            }
            const string Mod = T.ControlsGroupModifiers;
            slots.Add(new RebindSlot("left_shift", M, T.ControlsShiftLeft, _leftShift, 0, section: Mod));
            slots.Add(new RebindSlot("right_shift", M, T.ControlsShiftRight, _rightShift, 0, section: Mod));
            slots.Add(new RebindSlot("ctrl", M, T.ControlsCtrl, _ctrl, 0, section: Mod));
            slots.Add(new RebindSlot("ctrl_2", M, T.ControlsCtrl, _ctrl, 1, second: true, section: Mod));
            RebindSlots = slots;
            InputBindings.Load(RebindSlots);
        }

        /// <summary>Whether a modifier action holds <paramref name="control"/>: not a key press of its own.</summary>
        internal bool IsModifier(InputControl control) =>
            Binds(_leftShift, control) || Binds(_rightShift, control) || Binds(_ctrl, control);

        private void DisposeKeys() {

            foreach (InputAction action in _keyActions) {
                Release(action);
            }
            _keyActions.Clear();
        }

        /// <summary>Whether <paramref name="control"/> is one of <paramref name="action"/>'s keys now.</summary>
        internal static bool Binds(InputAction action, InputControl control) {
            foreach (InputControl c in action.controls) {
                if (c == control) {
                    return true;
                }
            }
            return false;
        }
    }

    public sealed partial class SystemInputSource : IMapOptionInput {
        public bool ToggleNorthUpMap => _northUp.WasPressedThisFrame();
    }
}

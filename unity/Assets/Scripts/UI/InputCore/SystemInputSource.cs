namespace BakAgain.UI.InputCore {
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;

    /// <summary>The ONE class that reads Unity Input System devices for positional/analog input.
    /// Implements IPointer (thin proxy over Pointer.current) and IGameplayInput (Task 5). Everything
    /// else injects the interfaces. Kept on the input-leak-gate allowlist.</summary>
    public sealed partial class SystemInputSource : IPointer, System.IDisposable {
        /// <summary>
        /// One pointer button, read from any of several controls — <b>the keypad substitutes for
        /// the mouse.</b>
        /// </summary>
        /// <remarks>
        /// <c>screen_input_poll_confirm_cancel</c> (SCREEN.C:114) polls keypad 5 and keypad 0 in
        /// the same expression as the left mouse button, and keypad + as the right, rather than
        /// translating them somewhere earlier. So a keyboard-only player has BOTH buttons, and
        /// reading only real mouse buttons quietly removes that — see
        /// <c>MenuClickButton.PrimaryScanCodes</c>.
        ///
        /// <para><b>Release needs all of them up, not any one of them.</b> Holding keypad 5 and
        /// releasing the mouse is not a release: the original polls a combined expression each
        /// frame, so the button is down while ANY of its controls is. Reporting the release on the
        /// first control to come up would end a drag the player is still making.</para>
        /// </remarks>
        private sealed class DeviceButton : IPointerButton {
            private readonly System.Func<ButtonControl>[] _get;
            public DeviceButton(params System.Func<ButtonControl>[] get) { _get = get; }

            public bool IsDown {
                get {
                    foreach (System.Func<ButtonControl> g in _get) {
                        if (g()?.isPressed ?? false) { return true; }
                    }
                    return false;
                }
            }

            public bool PressedThisFrame {
                get {
                    foreach (System.Func<ButtonControl> g in _get) {
                        if (g()?.wasPressedThisFrame ?? false) { return true; }
                    }
                    return false;
                }
            }

            public bool ReleasedThisFrame {
                get {
                    var any = false;
                    foreach (System.Func<ButtonControl> g in _get) {
                        ButtonControl c = g();
                        if (c == null) { continue; }
                        if (c.isPressed) { return false; }   // still held on another control
                        any |= c.wasReleasedThisFrame;
                    }
                    return any;
                }
            }
        }

        private static Mouse Mouse => Mouse.current;
        private static Pointer Pointer => UnityEngine.InputSystem.Pointer.current;

        private readonly DeviceButton _primary;
        private readonly DeviceButton _secondary;

        public SystemInputSource() {
            // Every device that can press, not "the mouse, else the pointer": Android can report a
            // Mouse alongside the touchscreen, and then a finger was never read as a press — the
            // cast screen saw hover but no click (TASK-789). DeviceButton ORs its controls.
            _primary = new DeviceButton(
                () => Mouse?.leftButton,
                () => Touchscreen.current?.press,
                () => Pointer?.press,
                () => Keyboard.current?.numpad5Key,
                () => Keyboard.current?.numpad0Key);
            _secondary = new DeviceButton(
                () => Mouse?.rightButton,
                () => Keyboard.current?.numpadPlusKey);
            InitGameplay(); // Task 5
#if UNITY_EDITOR
            InitModelDebug(); // Task 7 — editor-only debug harness reads
#endif
        }

        /// <summary>
        /// Disables and releases every action this class created.
        /// </summary>
        /// <remarks>
        /// <b>An enabled InputAction keeps a state-change monitor registered on its controls, and
        /// that outlives the object.</b> Without this, each container build left thirteen enabled
        /// actions behind whose owner was gone; the Input System kept notifying them until the
        /// notification hit a dead InputActionState and threw from inside the monitor, once per
        /// input event, for the rest of the session.
        ///
        /// <para>VContainer calls this because the container CREATES this singleton — see
        /// <c>InputCoreInstaller</c>, where the same distinction bit the shared action asset.</para>
        /// </remarks>
        public void Dispose() {
            DisposeGameplay();
#if UNITY_EDITOR
            DisposeModelDebug();
#endif
        }

        public bool IsPresent => Pointer != null && !(Pointer is Touchscreen); // touch: no software cursor

        // A touchscreen reports a position with every press, so a press is answerable even though
        // there is no hover to draw a cursor at.
        public bool CanPoint => Pointer != null;
        public Vector2 ScreenPosition => Pointer?.position.ReadValue() ?? Vector2.zero;
        public Vector2 Delta => Pointer?.delta.ReadValue() ?? Vector2.zero;
        public Vector2 Scroll => Mouse?.scroll.ReadValue() ?? Vector2.zero;
        IPointerButton IPointer.Primary => _primary;
        IPointerButton IPointer.Secondary => _secondary;
    }

    public sealed partial class SystemInputSource : IGameplayInput {
        private InputAction _move, _look, _run, _ascend, _descend, _zoom;

        private void InitGameplay() {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");
            _look = new InputAction("Look", InputActionType.Value, binding: "<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick");
            _run = new InputAction("Run", InputActionType.Button, binding: "<Keyboard>/leftShift");
            _run.AddBinding("<Gamepad>/leftShoulder");
            _ascend = new InputAction("Ascend", InputActionType.Button, binding: "<Keyboard>/space");
            _descend = new InputAction("Descend", InputActionType.Button, binding: "<Keyboard>/leftCtrl");
            _zoom = new InputAction("Zoom", InputActionType.Value, binding: "<Mouse>/scroll/y");
            _move.Enable(); _look.Enable(); _run.Enable(); _ascend.Enable(); _descend.Enable(); _zoom.Enable();

            _revealCredits = new InputAction("RevealRareCredits", InputActionType.Button, binding: "<Keyboard>/n");
            _revealCredits.Enable();

            _toggleOverlay = new InputAction("ToggleCombatOverlay", InputActionType.Button, binding: "<Keyboard>/g");
            _toggleOverlay.Enable();

            _cheatKey = new InputAction("CheatCentral", InputActionType.Button, binding: "<Keyboard>/backquote");
            _cheatKey.Enable();
        }

        private void DisposeGameplay() {
            foreach (InputAction action in new[] {
                    _move, _look, _run, _ascend, _descend, _zoom, _revealCredits,
                    _toggleOverlay, _cheatKey }) {
                Release(action);
            }
        }

        // Disable before Dispose: Dispose alone leaves the monitor registered until the finalizer,
        // which is exactly the window the storm happened in.
        private static void Release(InputAction action) {
            if (action == null) {
                return;
            }
            action.Disable();
            action.Dispose();
        }

        public Vector2 Move => _move.ReadValue<Vector2>();
        public Vector2 Look => _look.ReadValue<Vector2>();
        public bool Run => _run.IsPressed();
        public float Vertical => (_ascend.IsPressed() ? 1f : 0f) - (_descend.IsPressed() ? 1f : 0f);
        public float Zoom => _zoom.ReadValue<float>();
        public bool LeftShift => Keyboard.current?.leftShiftKey.isPressed ?? false;
        public bool RightShift => Keyboard.current?.rightShiftKey.isPressed ?? false;
        public bool Ctrl => Keyboard.current?.ctrlKey.isPressed ?? false;
    }

    // Task 8: CreditsView's "hold N to reveal rare credits" Easter egg. Its own InputAction (created
    // in InitGameplay alongside the other self-owned actions) rather than IGameplayInput — a niche
    // cheat hold isn't gameplay movement, so it gets its own tiny seam (interface segregation).
    // The combat tactical overlay's G toggle. Its own action alongside the other self-owned ones,
    // and read as an EDGE: a held key must not strobe the overlay at frame rate.
    public sealed partial class SystemInputSource : ICombatOverlayInput {
        private InputAction _toggleOverlay;

        public bool ToggleOverlayPressed => _toggleOverlay?.WasPressedThisFrame() ?? false;
    }

    public sealed partial class SystemInputSource : ICheatInput {
        private InputAction _revealCredits;

        public bool RevealRareCredits => _revealCredits.IsPressed();

        private InputAction _cheatKey;

        public bool CheatKeyPressed => _cheatKey?.WasPressedThisFrame() ?? false;
        public bool CheatKeyHeld => _cheatKey?.IsPressed() ?? false;
        public bool SkipChapterKeyPressed => _revealCredits?.WasPressedThisFrame() ?? false;

        public bool CheatChordHeld {
            get {
                Keyboard kb = Keyboard.current;
                return kb != null && GameData.Resources.World.CheatCentral.ModifiersMatch(
                    kb.rightShiftKey.isPressed, kb.leftAltKey.isPressed || kb.rightAltKey.isPressed,
                    kb.leftShiftKey.isPressed, kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            }
        }
    }

    // The overhead map's north-up toggle. Shares the 'N' key with the credits Easter egg above and
    // deliberately NOT its InputAction: that one is a hold and this is a press, and the two screens
    // are never up at once. Reading the hold here would flip the option once per frame the key was
    // down; reading this there would light the reveal for a single frame.
    public sealed partial class SystemInputSource : IMapOptionInput {
        public bool ToggleNorthUpMap => _revealCredits.WasPressedThisFrame();
    }

#if UNITY_EDITOR
    // Editor-only: backs IModelDebugInput for the ModelDebugState viewer harness (Task 7). Never
    // registered/allocated in player builds — see InputCoreInstaller + RootLifetimeScope, which
    // exclude ModelDebugState itself from player builds too.
    public sealed partial class SystemInputSource : IModelDebugInput {
        private InputAction _dbgPrevModel, _dbgNextModel, _dbgPrevZone, _dbgNextZone, _dbgRotL, _dbgRotR;

        private void InitModelDebug() {
            _dbgPrevModel = new InputAction("DbgPrevModel", InputActionType.Button);
            _dbgPrevModel.AddBinding("<Keyboard>/leftBracket");
            _dbgPrevModel.AddBinding("<Keyboard>/comma");

            _dbgNextModel = new InputAction("DbgNextModel", InputActionType.Button);
            _dbgNextModel.AddBinding("<Keyboard>/rightBracket");
            _dbgNextModel.AddBinding("<Keyboard>/period");

            _dbgPrevZone = new InputAction("DbgPrevZone", InputActionType.Button);
            _dbgPrevZone.AddBinding("<Keyboard>/minus");
            _dbgPrevZone.AddBinding("<Keyboard>/numpadMinus");

            _dbgNextZone = new InputAction("DbgNextZone", InputActionType.Button);
            _dbgNextZone.AddBinding("<Keyboard>/equals");
            // numpadPlus was here and is now the SECONDARY pointer button, which is a real game
            // binding (SCREEN.C:114) rather than a development convenience. Bound to both, one
            // press would right-click the world and change zone at the same time; `equals` still
            // reaches this action.

            _dbgRotL = new InputAction("DbgRotateLeft", InputActionType.Button, binding: "<Keyboard>/leftArrow");
            _dbgRotR = new InputAction("DbgRotateRight", InputActionType.Button, binding: "<Keyboard>/rightArrow");

            _dbgPrevModel.Enable(); _dbgNextModel.Enable();
            _dbgPrevZone.Enable(); _dbgNextZone.Enable();
            _dbgRotL.Enable(); _dbgRotR.Enable();
        }

        private void DisposeModelDebug() {
            foreach (InputAction action in new[] {
                    _dbgPrevModel, _dbgNextModel, _dbgPrevZone, _dbgNextZone, _dbgRotL, _dbgRotR }) {
                Release(action);
            }
        }

        public bool PrevModel => _dbgPrevModel.WasPressedThisFrame();
        public bool NextModel => _dbgNextModel.WasPressedThisFrame();
        public bool PrevZone => _dbgPrevZone.WasPressedThisFrame();
        public bool NextZone => _dbgNextZone.WasPressedThisFrame();
        public bool RotateLeft => _dbgRotL.IsPressed();
        public bool RotateRight => _dbgRotR.IsPressed();
    }
#endif
}

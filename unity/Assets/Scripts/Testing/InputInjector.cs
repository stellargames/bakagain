#if UNITY_EDITOR
namespace BakAgain.Testing {
    using System.Collections;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.LowLevel;

    /// <summary>
    /// Editor/automation-only synthetic input injector for driving the running game from the
    /// Unity MCP (<c>execute_code</c>). It queues real events into the Input System, so polling
    /// code observes them exactly as it would physical input — e.g. <c>UserInterfaceLoader</c>'s
    /// Tab/arrow navigation reading <c>ButtonControl.wasPressedThisFrame</c>, or the software
    /// cursor reacting to pointer movement.
    ///
    /// <para>Timing: a queued event is processed at the start of the NEXT Input System update
    /// (the dynamic update, before MonoBehaviour.Update). The tap routines therefore queue the
    /// press, yield a frame so the press is observed, then queue the release — replicating a
    /// real key/mouse tap across frames. Coroutines resume in the same Update phase as game
    /// <c>Update()</c>, so what this sees is what the game sees.</para>
    ///
    /// <para>Wrapped in <c>#if UNITY_EDITOR</c> so it never ships in a player build.</para>
    ///
    /// <para><b>Task 9 (input-abstraction):</b> for pointer position/click driving, prefer setting
    /// <c>BakAgain.UI.InputCore.InputDriver.Pointer</c> to a <c>FakePointer</c> (or
    /// <c>InputDriver.Gameplay</c> to a <c>FakeGameplayInput</c>) — it drives seam consumers directly,
    /// with no device queueing/frame-delay and no dependence on Game View focus routing. This class's
    /// device-event path (<c>InputSystem.QueueStateEvent</c>-based <see cref="TapKey"/>/<see cref="TapKeys"/>
    /// and the mouse routines) remains the way to reach <c>InputAction</c>-bound behaviour the fakes
    /// can't: submit/cancel bindings and <c>Keyboard.onTextInput</c> typing, which read real Input
    /// System device state rather than the seam interfaces.</para>
    /// </summary>
    public sealed class InputInjector : MonoBehaviour {
        private static InputInjector _instance;

        /// <summary>Lazily-created, scene-independent driver. Safe to call from execute_code.</summary>
        public static InputInjector Instance {
            get {
                if (_instance == null) {
                    var go = new GameObject("~InputInjector") { hideFlags = HideFlags.DontSave };
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<InputInjector>();
                }
                return _instance;
            }
        }

        /// <summary>Status of the most recent action, readable from a later execute_code call.</summary>
        public static string Last = "(idle)";

        // ---- Keyboard ------------------------------------------------------------------

        /// <summary>Tap one key (down ~2 frames, then up).</summary>
        public static void TapKey(Key key) => TapKeys(2, key);

        /// <summary>Tap a chord (all keys down together for <paramref name="holdFrames"/>, then up).
        /// Use for Shift+Tab etc.: <c>TapKeys(2, Key.LeftShift, Key.Tab)</c>.</summary>
        public static void TapKeys(int holdFrames, params Key[] keys) =>
            Instance.StartCoroutine(Instance.TapRoutine(holdFrames, keys));

        /// <remarks>
        /// *** THE STATE IS RE-QUEUED EVERY FRAME, NOT ONCE. *** On this setup the real X keyboard
        /// is routed into the game view by <c>editorInputBehaviorInPlayMode</c> and pushes its own
        /// "nothing pressed" state each frame, which overwrites a single injected event almost
        /// immediately — the same overwrite the mouse notes below describe for position. Queued
        /// once, a hold reads as pressed for a frame or two at most: measured
        /// <c>upArrowKey.isPressed == false</c> in the middle of what was supposed to be a
        /// 600-frame hold, with the party never moving.
        ///
        /// <para>That matters beyond the harness: it makes "I injected a key and nothing happened"
        /// worthless as evidence, which is exactly how an unverifiable input report stayed
        /// unverifiable. Re-queueing each frame wins against the overwrite, so a hold is a hold.</para>
        /// </remarks>
        private IEnumerator TapRoutine(int holdFrames, Key[] keys) {
            Keyboard kb = Keyboard.current;
            if (kb == null) { Last = "FAIL: no Keyboard.current"; yield break; }
            Last = "tapping " + string.Join("+", keys);
            for (int i = 0; i < Mathf.Max(1, holdFrames); i++) {
                InputSystem.QueueStateEvent(kb, new KeyboardState(keys));
                yield return null;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Last = "tapped " + string.Join("+", keys);
        }

        // ---- Mouse ---------------------------------------------------------------------
        // Synthetic mouse position is fragile on the headless box: the real X mouse (parked at the
        // origin) is routed to the game view by editorInputBehaviorInPlayMode and overwrites the
        // injected position back to (0,0) every frame. So reading the "current" position is
        // unreliable — every event we queue bakes its target position in.
        //
        // *** THE ANSWER IS TO RE-QUEUE PER FRAME, AS THE KEYBOARD DOES. *** This used to say the
        // single baked-in event was enough because clicks "rely on UI Toolkit's pointer capture
        // (taken on button-down)". They do — but the capture is only taken if the press survives to
        // be sampled, and it did not: no PointerDown ever reached any element. See ClickRoutine.

        /// <summary>Move the mouse to a screen position (bottom-left origin), with a non-zero delta
        /// so movement-reactive code (cursor reclaim, keyboard-focus reset) fires. The position may
        /// be reset by the real device on a later frame; for clicks use <see cref="ClickAt"/>, which
        /// bakes the position into the button events.</summary>
        public static void MoveMouse(Vector2 screenPos) =>
            Instance.StartCoroutine(Instance.MoveRoutine(screenPos));

        private IEnumerator MoveRoutine(Vector2 pos) {
            Mouse m = Mouse.current;
            if (m == null) { Last = "FAIL: no Mouse.current"; yield break; }
            Vector2 prev = m.position.ReadValue();
            // Re-queued per frame for the same reason ClickRoutine is — one event is overwritten
            // by the routed real device before anything downstream samples it.
            for (int i = 0; i < HoverFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = pos, delta = i == 0 ? pos - prev : Vector2.zero });
                yield return null;
            }
            Last = "moved mouse to " + pos;
        }

        /// <summary>Click at an explicit screen position (bottom-left origin). The button-down event
        /// carries the target position (move + press atomically), so the press lands on the widget
        /// under it and UI Toolkit captures the pointer; the release then fires that widget's click
        /// even if the real device reset the position to (0,0) in between. This is the reliable
        /// click on the headless setup — prefer it over the position-reading <see cref="Click"/>.
        /// To convert a UI Toolkit panel point to a screen point, invert RuntimePanelUtils
        /// .ScreenToPanel and flip Y (Screen.height - y): ScreenToPanel uses a top-left origin while
        /// the Mouse device uses bottom-left.</summary>
        public static void ClickAt(Vector2 screenPos, bool rightButton = false) =>
            Instance.StartCoroutine(Instance.ClickRoutine(screenPos, rightButton));

        /// <summary>Click at the mouse's current position. Unreliable on the headless box (the
        /// position is usually (0,0) from real-device overwrite); use <see cref="ClickAt"/> when you
        /// know the target coordinates.</summary>
        public static void Click(bool rightButton = false) =>
            Instance.StartCoroutine(Instance.ClickRoutine(
                Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero, rightButton));

        /// <summary>Frames to hold the pointer at the target before pressing, so the UI module has
        /// placed its pointer there and the raycast resolves to the widget under it.</summary>
        private const int HoverFrames = 3;

        /// <summary>Frames to hold the button down, and to re-assert the position after release.</summary>
        private const int PressFrames = 4;
        private const int ReleaseFrames = 3;

        /// <remarks>
        /// *** RE-QUEUED EVERY FRAME, FOR THE SAME REASON THE KEYBOARD IS. *** See TapRoutine: the
        /// real X device is routed into the game view by <c>editorInputBehaviorInPlayMode</c> and
        /// pushes its own state — pointer at the origin, no buttons — every frame. The keyboard was
        /// given a per-frame re-queue and the mouse was not; it queued its press once and leaned on
        /// UI Toolkit taking a pointer capture on button-down. That capture never happened, because
        /// the press was overwritten before <c>InputSystemUIInputModule</c> sampled the action.
        ///
        /// <para>Measured 2026-09-06 (TASK-348): a single queued click delivered a PointerMove and
        /// NO PointerDown to any element — verified against a dialog button, a full-screen modal
        /// scrim, and a known-good HUD button, with the device reading (0,0)/not-pressed moments
        /// later. That made "I clicked it and nothing happened" worthless as evidence about the
        /// game, which is how a real picking bug and a harness artefact looked identical.</para>
        ///
        /// <para>The three phases are separate because the module needs a frame with the pointer at
        /// rest on the target BEFORE the press to raycast it, and a frame with the button released
        /// while still over the target to raise the click.</para>
        /// </remarks>
        private IEnumerator ClickRoutine(Vector2 pos, bool right) {
            Mouse m = Mouse.current;
            if (m == null) { Last = "FAIL: no Mouse.current"; yield break; }
            Vector2 prev = m.position.ReadValue();
            MouseButton btn = right ? MouseButton.Right : MouseButton.Left;

            Last = (right ? "right" : "left") + "-clicking at " + pos;
            for (int i = 0; i < HoverFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = pos, delta = i == 0 ? pos - prev : Vector2.zero });
                yield return null;
            }
            for (int i = 0; i < PressFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = pos }.WithButton(btn, true));
                yield return null;
            }
            for (int i = 0; i < ReleaseFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = pos });
                yield return null;
            }
            Last = (right ? "right" : "left") + "-clicked at " + pos;
        }

        /// <summary>Press the left button at <paramref name="from"/>, carry it to <paramref name="to"/>
        /// with the button held, and release there: a real device drag (bottom-left screen origin).</summary>
        /// <remarks>
        /// <b>Needed because a drag cannot be sent as separate down/move/up events.</b> The routed real
        /// device pushes "no buttons" every frame, so a press queued once is gone before the next
        /// move arrives: DragGestureManipulator saw a press and a release and never promoted a drag,
        /// and the item never left the container. Every frame of this is re-queued with the button
        /// down, as ClickRoutine does for its press.
        /// </remarks>
        public static void DragAt(Vector2 from, Vector2 to, bool rightButton = false) =>
            Instance.StartCoroutine(Instance.DragRoutine(from, to, rightButton ? MouseButton.Right : MouseButton.Left));

        private const int DragSteps = 12;

        private IEnumerator DragRoutine(Vector2 from, Vector2 to, MouseButton button) {
            Mouse m = Mouse.current;
            if (m == null) { Last = "FAIL: no Mouse.current"; yield break; }
            Vector2 prev = m.position.ReadValue();
            Last = "dragging " + from + " -> " + to;
            for (int i = 0; i < HoverFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = from, delta = i == 0 ? from - prev : Vector2.zero });
                yield return null;
            }
            for (int i = 0; i < PressFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = from }.WithButton(button, true));
                yield return null;
            }
            Vector2 last = from;
            for (int i = 1; i <= DragSteps; i++) {
                Vector2 at = Vector2.Lerp(from, to, (float)i / DragSteps);
                InputSystem.QueueStateEvent(m, new MouseState { position = at, delta = at - last }.WithButton(button, true));
                last = at;
                yield return null;
            }
            for (int i = 0; i < PressFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = to }.WithButton(button, true));
                yield return null;
            }
            for (int i = 0; i < ReleaseFrames; i++) {
                InputSystem.QueueStateEvent(m, new MouseState { position = to });
                yield return null;
            }
            Last = "dragged " + from + " -> " + to;
        }

        // ---- Keep the player loop running ----------------------------------------------

        /// <summary>Clear the editor pause so the player loop ticks and queued taps complete.
        /// The MCP screenshot tool (and entering play mode) leaves the editor Paused, which freezes
        /// frame progression — call this after every screenshot and before relying on injected input.
        /// </summary>
        public static void EnsureRunning() {
            UnityEditor.EditorApplication.isPaused = false;
            Last = "ensured running (unpaused)";
        }

        // ---- Game-view focus fix -------------------------------------------------------

        /// <summary>Route all injected input to the Game View even when it isn't focused.
        /// Without this, the Editor (default <c>RespectGameViewFocus</c>) updates device state —
        /// so <c>wasPressedThisFrame</c> polling sees the press — but processes events in the
        /// editor update, so dynamic-update <c>InputAction</c> callbacks (<c>performed</c>) never
        /// fire. That polling-works-but-actions-don't split was the path-A blocker. Persisted on
        /// the InputSystem settings asset, so a single call survives play-mode entry.</summary>
        public static void EnsureGameViewInput() {
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Last = "game-view input routing = AllDeviceInputAlwaysGoesToGameView";
        }

        // ---- Self-test -----------------------------------------------------------------

        /// <summary>Inject a Tab press and confirm polling code observes wasPressedThisFrame —
        /// proves the injector reaches the same input path the game polls. Read <see cref="Last"/>.</summary>
        public static void SelfTest() => Instance.StartCoroutine(Instance.SelfTestRoutine());

        private IEnumerator SelfTestRoutine() {
            Last = "selftest running";
            Keyboard kb = Keyboard.current;
            if (kb == null) { Last = "selftest FAIL: no Keyboard.current"; yield break; }
            bool observed = false;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Tab));
            for (int i = 0; i < 6 && !observed; i++) {
                yield return null;
                if (kb.tabKey.wasPressedThisFrame) {
                    observed = true;
                }
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return null;
            Last = observed
                ? "selftest PASS: wasPressedThisFrame observed by polling code"
                : "selftest FAIL: injected press not observed";
        }
    }
}
#endif

namespace BakAgain.UI.InGame {
    using BakAgain.UI.InputCore;
    using BakAgain.World;
    using UnityEngine;
    using UnityEngine.UIElements;

    // Classic step/turn movement: a held movement axis (keyboard/gamepad, via IGameplayInput) OR a
    // held pointer (mouse/touch/pen, via IPointer) on a nav-arrow button steps once and then, after
    // a dead time, repeats at a fixed wall-clock rate — see RepeatDelaySeconds and RepeatStepsPerSecond. Relocated
    // verbatim from the old InGameScreen.Update poller; now reads the input seams (not
    // Keyboard.current/Pointer.current directly) so the driver is testable and device-agnostic — a
    // held finger on an on-screen nav-arrow repeats movement on Android exactly like held-mouse on
    // desktop, and a gamepad left-stick drives the same axis as WASD/arrows. Gated by `active`:
    // movement can only run when the travel surface owns input.
    public sealed class ClassicMovementDriver : IMovementDriver {
        /// <summary>
        /// Seconds a held direction does NOTHING before the repeat starts — the original's
        /// <c>g_nFrameTickCountdown = 0x5a</c>, which is 90 ticks of its own 236.7 Hz timer.
        /// </summary>
        /// <remarks>
        /// <b>The countdown is a TIMER, not a frame counter, and the difference is ~4x.</b>
        /// `int8timerInterrupt` (@0x198e2) decrements it once per interrupt and clamps it at zero,
        /// and `InitializeTimers` is called with 13 from both of its call sites — PIT divisor
        /// 65535/13 = 5041, so 1193182/5041 = <b>236.7 Hz</b> and ninety of them is <b>0.38 s</b>.
        /// Measured live to confirm: the word at 0x3cad5, seeded with 1000, fell 761 counts in
        /// 3.35 s of wall clock. This used to wait 90 Unity FRAMES — 1.5 s at 60 fps, four times
        /// too long, and different again on a 144 Hz monitor.
        ///
        /// <para><b>Once it expires, the original moves on every LOOP ITERATION.</b>
        /// <c>world3d_main_loop</c> (WORLDLP.C:224-242) arms the countdown when the focused REQ
        /// entry is a compass arrow and no input arrived that frame, sets it ONCE, and afterwards
        /// only tests it against zero. A fresh direction press cancels a pending repeat rather than
        /// restarting it.</para>
        ///
        /// <para><b>The RATE past the dead time is not the original's to match.</b> An iteration
        /// pays for a full <c>world_render_scene_dispatch</c> and a present, so the repeat runs as
        /// fast as the machine renders: measured at <b>8.84 steps a second</b> (22 steps in 2.489 s,
        /// from one patched save, follow-road off) and <b>~9.4 turns a second</b> on an emulator
        /// clocked at 20.4M cycles/s — the same number, because both are one action per iteration.
        /// A faster machine repeats faster, so there is no single rate to port — see
        /// <see cref="RepeatStepsPerSecond"/> for the one this driver uses.</para>
        /// </remarks>
        private const int RepeatDelayTicks = 0x5a;
        private const float RepeatDelaySeconds =
            (float)(RepeatDelayTicks / GameData.Resources.Config.DialogTextSpeed.TicksPerSecond);

        /// <summary>
        /// How fast a held direction repeats once the dead time is over, in actions per second.
        /// </summary>
        /// <remarks>
        /// <b>Paced by wall clock, by the owner's decision (TASK-431, 2026-09-14).</b> The original
        /// repeats once per uncapped world-loop iteration, so its rate is the host machine's; the
        /// port used to repeat once per rendered frame, which strode ~57 steps a second at 57 fps and
        /// faster on a faster monitor. The value is the original's measured cadence on the
        /// emulator — 22 steps in 2.489 s from one patched save — so it matches what that session
        /// saw and is the same on every display. One constant: change it to change the feel.
        /// </remarks>
        private const float RepeatStepsPerSecond = 8.84f;
        private const float RepeatIntervalSeconds = 1f / RepeatStepsPerSecond;
        // REQ_MAIN nav ActionIds = DOS arrow scancodes.
        private const int MoveForward = 72;   // 0x48 Up
        private const int MoveBackward = 80;  // 0x50 Down
        private const int TurnLeft = 75;      // 0x4B Left
        private const int TurnRight = 77;     // 0x4D Right

        private readonly UIDocument _document;
        private readonly PartyMovement _movement;
        private readonly IGameplayInput _gameplay;
        private readonly IPointer _pointer;
        private readonly System.Func<float> _deltaSeconds;
        private int _heldAction = -1;
        private float _secondsUntilRepeat;

        /// <param name="deltaSeconds">Seconds since the previous tick. Defaults to
        /// <see cref="Time.deltaTime"/>; tests pass their own so the dead time can be driven
        /// without a frame rate.</param>
        public ClassicMovementDriver(UIDocument document, PartyMovement movement,
            IGameplayInput gameplay, IPointer pointer, System.Func<float> deltaSeconds = null) {
            _document = document;
            _movement = movement;
            _gameplay = gameplay;
            _pointer = pointer;
            _deltaSeconds = deltaSeconds ?? (() => Time.deltaTime);
        }

        public void Tick(bool active) {
            if (!active) {
                _heldAction = -1;
                TouchInputState.Instance?.TakeTouchAction();   // a tap under a menu is not a step later
                return;
            }
            int action = ResolveHeldMovementAction();
            if (action < 0) {
                _heldAction = -1;
                // *** THIS IS THE WORLD LOOP'S "nothing moved" ARM. *** A dialog sub-action can ask
                // for the hotspot pass to run where the party stands; the original consumes that
                // request on an iteration where the party did not move, and an idle tick here is
                // exactly that. Not on the !active path above: that is a menu being up, not the
                // world running.
                _movement?.RunRequestedHotspotPass();
                return;
            }
            if (action != _heldAction) {
                // A new direction steps at once and arms the dead time. The original gets the same
                // shape from a different place: the press moves the party, and the arrow it leaves
                // FOCUSED is what the loop then sees on the following input-less frames.
                _heldAction = action;
                _secondsUntilRepeat = RepeatDelaySeconds;
                // A held REQ arrow's first step is its own click, on release — stepping here too
                // would make every tap two steps.
                if (!IsHeldReqArrow(action)) {
                    Apply(action);
                }
                return;
            }
            _secondsUntilRepeat -= _deltaSeconds();
            if (_secondsUntilRepeat > 0f) {
                return;
            }
            Apply(action);
            if (IsHeldReqArrow(action)) {
                TouchInputState.Instance.SwallowNextArrowClick = true;   // the release must not add a step
            }
            // One action per tick at most, and a long frame does not queue the steps it missed —
            // a hitch should cost a step, not replay several at once.
            _secondsUntilRepeat = System.Math.Max(0f, _secondsUntilRepeat + RepeatIntervalSeconds);
        }

        private void Apply(int action) {
            switch (action) {
                case MoveForward: _movement?.MoveForward(); break;
                case MoveBackward: _movement?.MoveBackward(); break;
                case TurnLeft: _movement?.TurnLeft(); break;
                case TurnRight: _movement?.TurnRight(); break;
            }
        }

        // The movement action currently held: a held arrow key, else the movement button the pointer
        // is held over (REQ_MAIN ImageButtons are named "imagebutton_{ActionId}"). -1 if none.
        private int ResolveHeldMovementAction() {
            Vector2 move = _gameplay.Move;
            const float dead = 0.5f;
            if (move.y > dead) return MoveForward;
            if (move.y < -dead) return MoveBackward;
            if (move.x < -dead) return TurnLeft;
            if (move.x > dead) return TurnRight;

            // A finger held on a touch pad or compass arrow, as UI Toolkit's pointer events saw it.
            int touchHeld = TouchInputState.Instance?.TakeTouchAction() ?? -1;
            if (IsMovementAction(touchHeld)) {
                return touchHeld;
            }

            IPanel panel = _document?.rootVisualElement?.panel;
            // CanPoint, not IsPresent: this is a press on the compass, and a finger presses
            // without ever hovering (TASK-67).
            if (_pointer.CanPoint && _pointer.Primary.IsDown && panel != null) {
                // The pointer device reports a bottom-left origin, but ScreenToPanel expects a
                // top-left one, so flip Y (Screen.height - y) before converting — otherwise Pick
                // probes the vertically-mirrored point and never lands on the arrow button.
                Vector2 screen = _pointer.ScreenPosition;
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                    panel, new Vector2(screen.x, Screen.height - screen.y));
                // REQ_MAIN's own arrows, and the touch aids' pads (TouchControlsView), which reuse this
                // pick — and so the original's hold-to-repeat — by carrying the same action id.
                for (VisualElement el = panel.Pick(panelPos); el != null; el = el.parent) {
                    if (TryMovementAction(el.name, "imagebutton_", out int aid)
                        || TryMovementAction(el.name, "touchpad_", out aid)) {
                        return aid;
                    }
                }
            }
            return -1;
        }

        private static bool IsHeldReqArrow(int action) =>
            TouchInputState.Instance is TouchInputState t && t.HeldIsReqArrow && t.HeldTouchAction == action;

        private static bool TryMovementAction(string name, string prefix, out int actionId) {
            actionId = -1;
            return !string.IsNullOrEmpty(name) && name.StartsWith(prefix)
                && int.TryParse(name.Substring(prefix.Length), out actionId) && IsMovementAction(actionId);
        }

        private static bool IsMovementAction(int id) =>
            id == MoveForward || id == MoveBackward || id == TurnLeft || id == TurnRight;
    }
}

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
    public sealed class ClassicMovementDriver {
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
        internal const float RepeatStepsPerSecond = 8.84f;
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
        private readonly System.Func<bool> _continuous;
        private int _heldAction = -1;
        // Enhanced continuous walking: a press commits to one preference step's worth of glide steps,
        // so a short tap covers the ground a Classic tap does.
        private int _tapDirection;
        private int _tapSteps;
        // The held action came from a REQ compass arrow (touch or mouse): that arrow's own click
        // takes a tap's step on release, so the press must not take one too.
        private bool _heldIsReqArrow;
        private float _secondsUntilRepeat;

        /// <param name="deltaSeconds">Seconds since the previous tick. Defaults to
        /// <see cref="Time.deltaTime"/>; tests pass their own so the dead time can be driven
        /// without a frame rate.</param>
        /// <param name="continuous">True while Enhanced continuous walking is on (TASK-966): forward and
        /// back glide while held instead of stepping. Null is the faithful game.</param>
        public ClassicMovementDriver(UIDocument document, PartyMovement movement,
            IGameplayInput gameplay, IPointer pointer, System.Func<float> deltaSeconds = null,
            System.Func<bool> continuous = null) {
            _document = document;
            _movement = movement;
            _gameplay = gameplay;
            _pointer = pointer;
            _deltaSeconds = deltaSeconds ?? (() => Time.deltaTime);
            _continuous = continuous;
        }

        /// <summary>
        /// A forward/back click that reached the travel screen without a press the driver saw (a
        /// sub-frame tap on a compass arrow): under continuous walking it glides one step's ground
        /// instead of jumping a step. False when continuous walking is off — the click steps as before.
        /// </summary>
        public bool TapGlide(int action) {
            int direction = DirectionOf(action);
            if (direction == 0 || _movement == null || _continuous?.Invoke() != true) {
                return false;
            }
            _tapDirection = direction;
            _tapSteps = _movement.GlideStepsPerStep;
            return true;
        }

        private static int DirectionOf(int action) =>
            action == MoveForward ? 1 : action == MoveBackward ? -1 : 0;

        // Continuous walking's share of a tick. True when it handled the tick.
        private bool TickGlide(int action) {
            int direction = DirectionOf(action);
            float dt = _deltaSeconds();
            if (direction != 0) {
                if (action != _heldAction) {
                    _heldAction = action;
                    _tapDirection = direction;
                    _tapSteps = _movement.GlideStepsPerStep;
                }
                if (_heldIsReqArrow && TouchInputState.Instance is TouchInputState state) {
                    state.SwallowNextArrowClick = true;   // the glide is the step; the release adds none
                }
            } else if (action < 0 && _tapSteps > 0) {
                direction = _tapDirection;   // a tap shorter than a step still walks one
            } else {
                _tapSteps = 0;
                if (!_movement.IsGliding) {
                    return false;
                }
                _movement.Glide(0, dt, RepeatStepsPerSecond);   // the camera reaches the point paid for
                return action < 0;
            }
            int taken = _movement.Glide(direction, dt, RepeatStepsPerSecond);
            _tapSteps = direction == _tapDirection ? System.Math.Max(0, _tapSteps - taken) : 0;
            if (taken == 0 && !_movement.IsGliding) {
                _tapSteps = 0;   // refused: a tap does not keep pushing at a wall
            }
            return true;
        }

        // Called once per frame. `active` == the travel surface is the stack's resolved input target
        // (no menu/modal above). When false the driver must produce NO movement and release any
        // device capture it holds.
        public void Tick(bool active) {
            if (!active) {
                _heldAction = -1;
                _tapSteps = 0;
                TouchInputState.Instance?.TakeTouchAction();   // a tap under a menu is not a step later
                return;
            }
            int action = ResolveHeldMovementAction();
            if (_movement != null && _continuous?.Invoke() == true && TickGlide(action)) {
                return;
            }
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
                if (!_heldIsReqArrow) {
                    Apply(action);
                }
                return;
            }
            _secondsUntilRepeat -= _deltaSeconds();
            if (_secondsUntilRepeat > 0f) {
                return;
            }
            Apply(action);
            if (_heldIsReqArrow && TouchInputState.Instance is TouchInputState state) {
                state.SwallowNextArrowClick = true;   // the release must not add a step
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
            _heldIsReqArrow = false;
            Vector2 move = _gameplay.Move;
            const float dead = 0.5f;
            if (move.y > dead) return MoveForward;
            if (move.y < -dead) return MoveBackward;
            if (move.x < -dead) return TurnLeft;
            if (move.x > dead) return TurnRight;

            // A finger held on a touch pad or compass arrow, as UI Toolkit's pointer events saw it.
            int touchHeld = TouchInputState.Instance?.TakeTouchAction() ?? -1;
            if (IsMovementAction(touchHeld)) {
                _heldIsReqArrow = IsHeldReqArrow(touchHeld);
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
                    if (TryMovementAction(el.name, "imagebutton_", out int aid)) {
                        _heldIsReqArrow = true;   // a mouse on the REQ arrow: its click steps (TASK-739)
                        return aid;
                    }
                    if (TryMovementAction(el.name, "touchpad_", out aid)) {
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

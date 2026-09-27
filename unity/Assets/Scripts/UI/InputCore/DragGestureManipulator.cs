namespace BakAgain.UI.InputCore {
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A press → drag → release recogniser, as a UI Toolkit <see cref="PointerManipulator"/>.
    ///
    /// <para>This is the event-driven home for the parts of a drag gesture that every screen would
    /// otherwise hand-roll: press/release edges, a movement threshold before a press becomes a drag,
    /// and pointer capture so the drag keeps being delivered after the pointer leaves the element it
    /// started on. Capture is the load-bearing part and is pinned by
    /// <c>PointerCaptureContractTests</c>.</para>
    ///
    /// <para>Attach it to the element that owns the whole interactive area (a screen's stage), not to
    /// each draggable item: the recogniser then also sees presses that land on nothing, which is what
    /// a "click empty space to deselect" rule needs. It never calls <c>StopPropagation</c>, so
    /// buttons and other manipulators beneath keep working normally.</para>
    ///
    /// <para>Positions reported to subscribers are <b>local to the target</b> (i.e. stage-local for a
    /// stage-mounted recogniser), because that is the space screens lay out in.</para>
    ///
    /// <para>Deliberately knows nothing about items, slots or drop targets — the host decides what a
    /// press means. Continuous input (movement, look, cursor position) does not belong here; Unity's
    /// own guidance is to poll that, and it stays on <c>IGameplayInput</c>/<c>IPointer</c>.</para>
    /// </summary>
    public sealed class DragGestureManipulator : PointerManipulator {
        private const int PrimaryButton = 0;
        private const int SecondaryButton = 1;

        // Pseudo-pointer id for non-pointer (keyboard/gamepad) activations, so they form their own
        // click run: a keyboard Enter never continues a mouse run, or vice versa.
        private const int KeyboardPointerId = int.MinValue;

        private readonly float _doubleClickSeconds;
        private readonly float _longPressSeconds;
        private Vector2 _pressLocal;
        private int _capturedPointer = PointerId.invalidPointerId;
        private bool _dragging;

        // *** A FINGER HAS NO SECOND BUTTON, SO THE HOLD IS THE SECOND BUTTON. ***
        // Right-click opens the examine/describe path throughout this port, so without this a touch
        // player loses a whole verb: `e.button == 1` is a thing a finger never synthesises. Armed on
        // a touch press, disarmed the moment the gesture becomes a drag or ends. TASK-67 §3.
        private IVisualElementScheduledItem _longPress;

        // Click-count state. UI Toolkit's own IPointerEvent.clickCount is NOT usable here: measured
        // 2026-07-30 against the runtime InputSystemUIInputModule, a second press 1.5 s after the
        // first still reported clickCount == 2 (and the matching PointerUp reported 1). Unity's own
        // documentation for the field ships a `ClickCountMonitor` example that counts by hand for
        // the same reason, so that is what this does.
        private int _clickCount;
        private int _lastPointerId = PointerId.invalidPointerId;
        private int _lastButton = -1;
        private float _lastPressTime;
        private Vector2 _lastPressLocal;

        /// <summary>
        /// Primary press: target-local position, and the click count (1, 2, … for single/double/…).
        /// The count is computed here — see the note on the click-count fields for why UI Toolkit's
        /// own <c>clickCount</c> can't be used.
        /// </summary>
        public event Action<Vector2, int> Pressed;

        /// <summary>Secondary (right / long-press) press. Argument is target-local.</summary>
        public event Action<Vector2> SecondaryPressed;

        /// <summary>The press has moved past the threshold and is now a drag.</summary>
        public event Action<Vector2> DragStarted;

        /// <summary>Pointer moved while dragging.</summary>
        public event Action<Vector2> Dragged;

        /// <summary>Release. The bool is true when the gesture became a drag, false for a click.</summary>
        public event Action<Vector2, bool> Released;

        /// <param name="target">The element the gesture is observed on — normally a screen's stage.</param>
        /// <param name="dragThreshold">Movement in target-space before a press counts as a drag.</param>
        /// <param name="doubleClickSeconds">Window in which a second press counts as a double-click.
        /// Defaults to 0.5 s, the value Unity's own <c>ClickCountMonitor</c> documentation example
        /// uses. Injectable so tests can drive the reset deterministically.</param>
        /// <param name="longPressSeconds">How long a FINGER must rest before the press becomes a
        /// secondary. Defaults to 0.5 s — Android's own
        /// <c>ViewConfiguration.getLongPressTimeout()</c>, taken rather than invented so the gesture
        /// matches every other app on the device. Injectable so tests need not wait half a second.
        /// </param>
        public DragGestureManipulator(VisualElement target, float dragThreshold,
            float doubleClickSeconds = 0.5f, float longPressSeconds = 0.5f) {
            this.target = target;
            DragThreshold = dragThreshold;
            _doubleClickSeconds = doubleClickSeconds;
            _longPressSeconds = longPressSeconds;
        }

        /// <summary>
        /// Movement in target-space before a press counts as a drag — and, because a double-click's
        /// second press must land near its first, the position tolerance for the click count too.
        ///
        /// <para>Settable, not a constructor-only value: a host whose threshold arrives with
        /// asynchronously-loaded data (the inventory screen's REQ resource) would otherwise have to
        /// replace the recogniser, which throws away any pointer capture the old one was holding
        /// mid-gesture. Read on each event, so a change takes effect immediately and harmlessly.</para>
        /// </summary>
        public float DragThreshold { get; set; }

        /// <summary>
        /// Optional replacement for the drag-promotion test, given the movement since the press.
        /// </summary>
        /// <remarks>
        /// <b>Because the inventory's rule is not a radius.</b> <c>invui_handle_item_drag</c>
        /// promotes on <c>abs(dx) + abs(dy) &gt; 4</c> in the original's own pixels — a diamond, and
        /// an anisotropic one once converted, since canonical x is x5 and y is x6. No value of
        /// <see cref="DragThreshold"/> reproduces that, so a host with a real rule supplies it here.
        ///
        /// <para><b>It replaces the drag test only.</b> <see cref="DragThreshold"/> still serves as
        /// the double-click position tolerance, which is a different question with its own answer —
        /// collapsing the two is what made one number carry both jobs in the first place.</para>
        /// </remarks>
        public System.Func<Vector2, bool> StartsDrag { get; set; }

        /// <summary>True between a primary press and its release.</summary>
        public bool IsPressed => _capturedPointer != PointerId.invalidPointerId;

        protected override void RegisterCallbacksOnTarget() {
            // TrickleDown: the recogniser is mounted on an ancestor of everything it watches, so it
            // has to see the press on the way DOWN to the item that was hit, not on the way back up
            // (a child manipulator — e.g. Clickable on a REQ button — may stop the bubbling phase).
            target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            // CaptureOut is delivered to the element LOSING the capture — that is how this target
            // learns a child stole the pointer mid-press (see OnCaptureOut).
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget() {
            target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            Cancel();
        }

        // A child manipulator can take over the capture during the SAME press this recogniser
        // captured on: every REQ button/hotspot carries a Clickable, and Clickable captures on
        // pointer-down — last capturer wins. From then on the captured events (including the
        // release) go exclusively to the child, so OnUp never arrives here. Without this reset the
        // recogniser stayed "pressed" and OnDown swallowed the NEXT press — the dud click after
        // switching members in the inventory (JvE, 2026-08-02). No Released is raised: the press
        // was consumed by the child (its Clickable fired), there is nothing for the host to end.
        private void OnCaptureOut(PointerCaptureOutEvent e) {
            if (e.pointerId == _capturedPointer) {
                DisarmLongPress();
                _capturedPointer = PointerId.invalidPointerId;
                _dragging = false;
            }
        }

        /// <summary>Abandon any in-flight gesture and drop the capture. For a host tearing down.</summary>
        public void Cancel() {
            DisarmLongPress();
            if (_capturedPointer != PointerId.invalidPointerId) {
                if (target.HasPointerCapture(_capturedPointer)) {
                    target.ReleasePointer(_capturedPointer);
                }
                _capturedPointer = PointerId.invalidPointerId;
            }
            _dragging = false;
        }

        private Vector2 Local(IPointerEvent e) =>
            target.WorldToLocal(new Vector2(e.position.x, e.position.y));

        private void OnDown(PointerDownEvent e) {
            if (e.button == SecondaryButton) {
                SecondaryPressed?.Invoke(Local(e));
                return;
            }
            // Self-heal: "pressed" without actually holding the capture is a stale gesture (some
            // capture-loss path OnCaptureOut didn't cover). Reset and treat this press normally —
            // the alternative is swallowing it, which reads as a dead click.
            if (IsPressed && !target.HasPointerCapture(_capturedPointer)) {
                Cancel();
            }
            if (e.button != PrimaryButton || IsPressed) {
                return;
            }
            _pressLocal = Local(e);
            _dragging = false;
            UpdateClickCount(e.pointerId, e.button, _pressLocal);
            // Capture so move/up keep arriving once the pointer leaves whatever was pressed. Without
            // this a drag would stop dead at the item cell's edge.
            _capturedPointer = e.pointerId;
            target.CapturePointer(e.pointerId);
            Pressed?.Invoke(_pressLocal, _clickCount);
            ArmLongPress(e);
        }

        /// <summary>
        /// Start the hold that turns a resting finger into a secondary press.
        /// </summary>
        /// <remarks>
        /// <b>Only for touch.</b> A mouse has a right button, and arming this for it would fire
        /// examine on any slow click or on a press held before dragging.
        ///
        /// <para><b>There is deliberately no movement tolerance of its own.</b> The hold is
        /// cancelled when the gesture promotes to a drag, which reuses the host's real rule —
        /// the inventory's is <c>abs(dx) + abs(dy) &gt; 4</c> in the original's own pixels, a
        /// diamond no radius reproduces (see <see cref="StartsDrag"/>). A separate slop constant
        /// here would be a second, wrong answer to a question already answered.</para>
        /// </remarks>
        private void ArmLongPress(PointerDownEvent e) {
            if (_longPressSeconds <= 0f || e.pointerType != UnityEngine.UIElements.PointerType.touch) {
                return;
            }
            int pointer = e.pointerId;
            _longPress = target.schedule
                .Execute(() => FireLongPress(pointer))
                .StartingIn((long)(_longPressSeconds * 1000f));
        }

        private void DisarmLongPress() {
            _longPress?.Pause();
            _longPress = null;
        }

        /// <remarks>
        /// Ends the gesture WITHOUT raising <see cref="Released"/>, which is what makes the hold a
        /// secondary rather than a slow click: the mouse's own secondary path (<c>OnDown</c>, button
        /// 1) raises <see cref="SecondaryPressed"/> alone and never captures, so a host that sees
        /// press-then-nothing is already the contract here — it is what <c>OnCaptureOut</c> leaves
        /// behind when a child steals the press.
        /// </remarks>
        private void FireLongPress(int pointer) {
            _longPress = null;
            if (_capturedPointer != pointer || _dragging) {
                return;
            }
            Vector2 at = _pressLocal;
            Cancel();
            SecondaryPressed?.Invoke(at);
        }

        // A press continues the previous click run only when it is the same pointer and button,
        // lands within the drag threshold of the last press, and arrives inside the window.
        // Position is part of it because this recogniser is mounted on the stage, so the event target
        // is always the stage — it cannot use "same target" the way Unity's example does.
        private void UpdateClickCount(int pointerId, int button, Vector2 local) {
            bool continues = pointerId == _lastPointerId
                && button == _lastButton
                && (local - _lastPressLocal).magnitude <= DragThreshold
                && Time.unscaledTime < _lastPressTime + _doubleClickSeconds;
            _clickCount = continues ? _clickCount + 1 : 1;
            _lastPointerId = pointerId;
            _lastButton = button;
            _lastPressLocal = local;
            _lastPressTime = Time.unscaledTime;
        }

        /// <summary>
        /// Count a non-pointer activation — a keyboard/gamepad Enter on a focused widget — into the
        /// SAME double-click run the pointer path uses, and return the resulting count.
        ///
        /// <para>Faithful because in the original Enter IS a click (canassa MENUPAGE.C:444-445 —
        /// key_is_down(0x1c) feeds the same value the left mouse button does; line 348 is a comment
        /// over an empty stub, not the support): two quick Enters on one item reach the same
        /// deadline that makes two mouse clicks a double-click (invui_handle_item_drag,
        /// INVENTOR.C:719-725, with <c>old_sel</c> captured at L634). Sharing this counter is also
        /// what keeps a second reclick timer out of the codebase — the one this task deleted.</para>
        ///
        /// <param name="at">Where the activation "happened" (the focused widget's centre, in the
        /// same space presses report). Position participates for the same reason it does for the
        /// pointer: activating a different cell starts a fresh run, mirroring the original's
        /// <c>*p_selected_slot == old_sel</c> guard.</param>
        /// </summary>
        public int CountActivation(Vector2 at) {
            UpdateClickCount(KeyboardPointerId, PrimaryButton, at);
            return _clickCount;
        }

        private void OnMove(PointerMoveEvent e) {
            if (!IsPressed || e.pointerId != _capturedPointer) {
                return;
            }
            Vector2 local = Local(e);
            if (!_dragging) {
                Vector2 moved = local - _pressLocal;
                bool promote = StartsDrag != null
                    ? StartsDrag(moved)
                    : moved.magnitude > DragThreshold;
                if (!promote) {
                    return; // still a press, not yet a drag
                }
                _dragging = true;
                DisarmLongPress(); // a finger that is dragging is not resting
                DragStarted?.Invoke(local);
            }
            Dragged?.Invoke(local);
        }

        private void OnUp(PointerUpEvent e) {
            if (!IsPressed || e.pointerId != _capturedPointer) {
                return;
            }
            Vector2 local = Local(e);
            bool wasDrag = _dragging;
            Cancel();
            Released?.Invoke(local, wasDrag);
        }
    }
}

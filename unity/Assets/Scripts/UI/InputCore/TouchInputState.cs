namespace BakAgain.UI.InputCore {
    using UnityEngine;

    /// <summary>C3: what the cell under the combat cursor holds, which picks the side bar's buttons.</summary>
    public enum CursorContext { None, Target, Ground }
    public enum SelectRoute { Primary, Swallow }

    /// <summary>
    /// The Android touch aids' shared state (spec 2026-09-29-android-touch-aids-design.md): the
    /// held pad, the combat cursor's context, and the one-shot flags that turn the next REQ select
    /// into something else. The owner chose split pads for travel and the cursor for combat
    /// (2026-10-01); the other variants are gone.
    /// </summary>
    /// <remarks>
    /// Published as <see cref="Instance"/> because UserInterfaceLoader is a prefab sibling VContainer
    /// never injects (the same pattern as MenuSoundService.Instance).
    /// </remarks>
    public sealed class TouchInputState {
        public static TouchInputState Instance { get; set; }

        /// <summary>
        /// The action a long-press fired the right-click on: that element's release click is eaten.
        /// Keyed to the ACTION, not "the next select": the right-click may open a screen on its own
        /// panel before the finger lifts, so the release click never comes, and a plain flag ate the
        /// next screen's first tap (the character sheet's Exit, twice on the emulator). Any other
        /// select, or a new press seen on this panel, drops it.
        /// </summary>
        public int? SuppressSelectFor { get; set; }

        /// <summary>A new finger press, seen through UI Toolkit's own pointer events, panel-wide.</summary>
        public void OnTouchPressStarted() => SuppressSelectFor = null;

        private bool _gridToggleRequested;

        /// <summary>The side bar's grid button: one press, one toggle — the G key's edge.</summary>
        public void RequestGridToggle() => _gridToggleRequested = true;

        public bool TakeGridToggle() {
            bool asked = _gridToggleRequested;
            _gridToggleRequested = false;
            return asked;
        }

        /// <summary>
        /// The touch pad or compass arrow a finger is holding, from UI Toolkit pointer events (-1 when
        /// none). ClassicMovementDriver reads it before its polled pointer, which never reported a
        /// held finger on the owner's phone (2026-09-30) while UI Toolkit's own events did.
        /// </summary>
        public int HeldTouchAction { get; private set; } = -1;

        /// <summary>The held element is a REQ compass arrow, whose own click takes a tap's step.</summary>
        public bool HeldIsReqArrow { get; private set; }

        /// <summary>A held compass arrow has repeated: its release click must not add one more step.</summary>
        public bool SwallowNextArrowClick { get; set; }

        public void PressHold(int actionId, bool reqArrow) {
            HeldTouchAction = actionId;
            HeldIsReqArrow = reqArrow;
            SwallowNextArrowClick = false;
            _pendingTap = reqArrow ? -1 : actionId;   // a REQ arrow's click takes a tap's step itself
        }

        private int _pendingTap = -1;

        /// <summary>
        /// The pad to act on this frame: the one held, else a pad tapped and released since the last
        /// frame, once. The driver reads once a frame, so a tap that began and ended in between was
        /// never seen and took no step (emulator, 2026-10-02).
        /// </summary>
        public int TakeTouchAction() {
            int action = HeldTouchAction >= 0 ? HeldTouchAction : _pendingTap;
            _pendingTap = -1;
            return action;
        }

        public void ReleaseHold(int actionId) {
            if (HeldTouchAction == actionId) {
                HeldTouchAction = -1;
                HeldIsReqArrow = false;
            }
        }

        /// <summary>C3: what the cell under the combat cursor holds.</summary>
        public CursorContext CursorContext { get; set; }

        /// <summary>C3: a spell or item is waiting for a target, so the buttons cast rather than fight.</summary>
        public bool AwaitingTarget { get; set; }

        /// <summary>While <see cref="AwaitingTarget"/>: the cell under the cursor would take the cast (TASK-823).</summary>
        public bool CastAccepted { get; set; }

        /// <summary>The acting character can walk to the cell under the cursor this turn (TASK-819).</summary>
        public bool MoveAccepted { get; set; }

        /// <summary>Combat: the screen point (Input System coords, bottom-left) the hover pick uses on touch.</summary>
        public Vector2? CombatHoverScreenPoint { get; set; }

        /// <summary>
        /// A fight starting or ending: a preview from another fight would ring an arbitrary cell.
        /// </summary>
        public void ForgetCombatPreview() {
            CombatHoverScreenPoint = null;
            _pendingTap = -1;   // a cursor tap is not a travel step after the fight, nor the reverse
            // Nor is a held pad: the finger that walked into the fight stepped the cursor up the
            // arena on every repeat (TASK-822). A new press is needed on the other side.
            HeldTouchAction = -1;
            HeldIsReqArrow = false;
            SwallowNextArrowClick = false;
        }

        /// <summary>
        /// Whether a long-press on this REQ action is its right-click. Not for the four compass
        /// arrows: holding one is hold-to-walk, and its right-click is only the arrow's help text.
        /// </summary>
        public static bool LongPressApplies(int actionId) =>
            actionId != 72 && actionId != 75 && actionId != 77 && actionId != 80;

        public SelectRoute TakeSelectRoute(int actionId = -1) {
            if (SwallowNextArrowClick && actionId >= 0 && !LongPressApplies(actionId)) {
                SwallowNextArrowClick = false;
                return SelectRoute.Swallow;
            }
            if (SuppressSelectFor.HasValue) {
                bool sameElement = SuppressSelectFor == actionId;
                SuppressSelectFor = null;
                if (sameElement) {
                    return SelectRoute.Swallow;
                }
            }
            return SelectRoute.Primary;
        }
    }
}

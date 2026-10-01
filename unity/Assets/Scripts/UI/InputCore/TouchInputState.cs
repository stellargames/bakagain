namespace BakAgain.UI.InputCore {
    using UnityEngine;

    public enum TouchTravelVariant { ThumbPad = 0, SplitPads = 1, Minimal = 2 }
    public enum TouchCombatVariant { SelectThenConfirm = 0, FingerHover = 1 }
    public enum SelectRoute { Primary, Swallow }

    public interface IPrefsStore {
        int GetInt(string key, int fallback);
        void SetInt(string key, int value);
    }

    public sealed class PlayerPrefsStore : IPrefsStore {
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// The Android touch aids' shared state (spec 2026-09-29-android-touch-aids-design.md): which
    /// variant the owner is trying, and the one-shot flags that turn the next REQ select into
    /// something else.
    /// </summary>
    /// <remarks>
    /// Published as <see cref="Instance"/> because UserInterfaceLoader is a prefab sibling VContainer
    /// never injects (the same pattern as MenuSoundService.Instance).
    /// </remarks>
    public sealed class TouchInputState {
        private const string TravelKey = "touch travel variant";
        private const string CombatKey = "touch combat variant";
        private readonly IPrefsStore _prefs;

        public static TouchInputState Instance { get; set; }

        public TouchInputState(IPrefsStore prefs) {
            _prefs = prefs;
            Travel = Read<TouchTravelVariant>(TravelKey);
            Combat = Read<TouchCombatVariant>(CombatKey);
        }

        public event System.Action Changed;
        public TouchTravelVariant Travel { get; private set; }
        public TouchCombatVariant Combat { get; private set; }

        /// <summary>
        /// Set after a long-press fired a secondary: that finger's release select is eaten. Any new
        /// press drops it (<see cref="OnTouchPressStarted"/>), because the secondary may open a screen
        /// before the finger lifts — the release then never reaches a select, and a leftover flag ate
        /// the next screen's first tap (found on the emulator: the character sheet's Exit).
        /// </summary>
        public bool SuppressNextSelect { get; set; }

        /// <summary>A new finger press, seen through UI Toolkit's own pointer events, panel-wide.</summary>
        public void OnTouchPressStarted() => SuppressNextSelect = false;

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
        }

        public void ReleaseHold(int actionId) {
            if (HeldTouchAction == actionId) {
                HeldTouchAction = -1;
                HeldIsReqArrow = false;
            }
        }

        /// <summary>Combat: the screen point (Input System coords, bottom-left) the hover pick uses on touch.</summary>
        public Vector2? CombatHoverScreenPoint { get; set; }

        public void CycleTravel() {
            Travel = (TouchTravelVariant)(((int)Travel + 1) % 3);
            _prefs.SetInt(TravelKey, (int)Travel);
            Changed?.Invoke();
        }

        public void CycleCombat() {
            Combat = (TouchCombatVariant)(((int)Combat + 1) % 2);
            _prefs.SetInt(CombatKey, (int)Combat);
            Changed?.Invoke();
        }

        /// <summary>
        /// A fight starting: a selection from an earlier fight would ring an arbitrary cell.
        /// </summary>
        public void OnFightStarted() {
            CombatHoverScreenPoint = null;
            Changed?.Invoke();
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
            if (SuppressNextSelect) {
                SuppressNextSelect = false;
                return SelectRoute.Swallow;
            }
            return SelectRoute.Primary;
        }

        private T Read<T>(string key) where T : struct, System.Enum {
            int v = _prefs.GetInt(key, 0);
            return System.Enum.IsDefined(typeof(T), v) ? (T)(object)v : default;
        }
    }
}

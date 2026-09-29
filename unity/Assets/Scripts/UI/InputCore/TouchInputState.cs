namespace BakAgain.UI.InputCore {
    using UnityEngine;

    public enum TouchTravelVariant { ThumbPad = 0, SplitPads = 1, Minimal = 2 }
    public enum TouchCombatVariant { SelectThenConfirm = 0, FingerHover = 1 }
    public enum SelectRoute { Primary, Secondary, Swallow }

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

        /// <summary>While set, the next REQ select is delivered as its SecondaryAction, then clears.</summary>
        public bool ExamineArmed { get; set; }

        /// <summary>
        /// Set after a long-press fired a secondary, to the touch that did it: that finger's release
        /// select is eaten. Keyed to the TOUCH, not "the next select", because the secondary may open
        /// a screen before the finger lifts — the release then never reaches a select, and a plain
        /// flag ate the next screen's first tap instead (found on the emulator: the sheet's Exit).
        /// </summary>
        public int? SuppressSelectForTouchId { get; set; }

        /// <summary>The primary touch's id right now (Input System), or null without a touchscreen.</summary>
        public static int? CurrentTouchId() =>
            UnityEngine.InputSystem.Touchscreen.current?.primaryTouch.touchId.ReadValue();

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

        public SelectRoute TakeSelectRoute(int? currentTouchId = null) {
            if (SuppressSelectForTouchId.HasValue) {
                bool sameTouch = SuppressSelectForTouchId == currentTouchId;
                SuppressSelectForTouchId = null;
                if (sameTouch) {
                    return SelectRoute.Swallow;
                }
            }
            if (ExamineArmed) {
                ExamineArmed = false;
                Changed?.Invoke();
                return SelectRoute.Secondary;
            }
            return SelectRoute.Primary;
        }

        private T Read<T>(string key) where T : struct, System.Enum {
            int v = _prefs.GetInt(key, 0);
            return System.Enum.IsDefined(typeof(T), v) ? (T)(object)v : default;
        }
    }
}

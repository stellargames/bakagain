namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    /// <summary>The Android touch aids' shared state (spec 2026-09-29-android-touch-aids-design.md).</summary>
    public class TouchInputStateTests {
        private sealed class MemPrefs : IPrefsStore {
            public readonly Dictionary<string, int> Values = new Dictionary<string, int>();
            public int GetInt(string key, int fallback) => Values.TryGetValue(key, out int v) ? v : fallback;
            public void SetInt(string key, int value) => Values[key] = value;
        }

        [Test]
        public void DefaultsAreThumbPadAndSelectThenConfirm() {
            var s = new TouchInputState(new MemPrefs());
            Assert.AreEqual(TouchTravelVariant.ThumbPad, s.Travel);
            Assert.AreEqual(TouchCombatVariant.SelectThenConfirm, s.Combat);
        }

        [Test]
        public void CyclingWrapsAndPersists() {
            var prefs = new MemPrefs();
            var s = new TouchInputState(prefs);
            int changes = 0; s.Changed += () => changes++;
            s.CycleTravel(); s.CycleTravel(); s.CycleTravel();
            Assert.AreEqual(TouchTravelVariant.ThumbPad, s.Travel, "three steps wrap back");
            s.CycleCombat();
            Assert.AreEqual(TouchCombatVariant.FingerHover, s.Combat);
            Assert.AreEqual(4, changes);
            Assert.AreEqual(TouchCombatVariant.FingerHover, new TouchInputState(prefs).Combat, "persisted");
        }

        [Test]
        public void ExamineIsOneShot() {
            var s = new TouchInputState(new MemPrefs()) { ExamineArmed = true };
            Assert.AreEqual(SelectRoute.Secondary, s.TakeSelectRoute());
            Assert.IsFalse(s.ExamineArmed, "spent by the select it redirected");
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute());
        }

        [Test]
        public void SuppressSwallowsTheReleaseOfThatTouchOnly() {
            var s = new TouchInputState(new MemPrefs()) { SuppressSelectForTouchId = 7, ExamineArmed = true };
            Assert.AreEqual(SelectRoute.Swallow, s.TakeSelectRoute(currentTouchId: 7), "the long-press release is eaten");
            Assert.AreEqual(SelectRoute.Secondary, s.TakeSelectRoute(currentTouchId: 8), "examine still waits for a real tap");
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute(currentTouchId: 9));
        }

        /// <summary>
        /// Found on the emulator: a long-press on a portrait opened the character sheet before the
        /// finger lifted, so its release never became a select — and the leftover suppression ate the
        /// sheet's Exit. A select from a LATER touch must never be swallowed.
        /// </summary>
        [Test]
        public void AStaleSuppressionDoesNotEatALaterTouch() {
            var s = new TouchInputState(new MemPrefs()) { SuppressSelectForTouchId = 7 };
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute(currentTouchId: 8));
            Assert.IsNull(s.SuppressSelectForTouchId, "and it is cleared, not left armed");
        }

        /// <summary>
        /// Final review #2: an Examine armed on the travel screen turned the first battlefield tap of
        /// the next fight into an instant Swing (its button is hidden in a fight, the flag was not).
        /// </summary>
        [Test]
        public void AFightStartingDisarmsExamineAndForgetsAnOldSelection() {
            var s = new TouchInputState(new MemPrefs()) {
                ExamineArmed = true, CombatHoverScreenPoint = new UnityEngine.Vector2(1, 2),
            };
            s.OnFightStarted();
            Assert.IsFalse(s.ExamineArmed);
            Assert.IsNull(s.CombatHoverScreenPoint);
            Assert.AreEqual(SelectRoute.Primary, s.TakeSelectRoute());
        }

        /// <summary>Final review #4: a held compass arrow is hold-to-walk; its long-press must not open its help text.</summary>
        [Test]
        public void ALongPressNeverAppliesToTheMovementArrows() {
            foreach (int id in new[] { 72, 75, 77, 80 }) {
                Assert.IsFalse(TouchInputState.LongPressApplies(id), $"arrow {id}");
            }
            Assert.IsTrue(TouchInputState.LongPressApplies(2), "a portrait");
            Assert.IsTrue(TouchInputState.LongPressApplies(192), "the world view");
        }

        [Test]
        public void CorruptPrefValueFallsBackToDefault() {
            var prefs = new MemPrefs(); prefs.Values["touch travel variant"] = 99;
            Assert.AreEqual(TouchTravelVariant.ThumbPad, new TouchInputState(prefs).Travel);
        }
    }
}

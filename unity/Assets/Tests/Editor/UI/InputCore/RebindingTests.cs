namespace BakAgain.Tests.Editor.UI.InputCore {
    using System.Linq;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// TASK-1080 (owner: "let players remap all actions"): the remappable slots, their saved
    /// bindings, the swap when a key is already taken, and the reset.
    /// </summary>
    public class RebindingTests : InputTestFixture {
        private SystemInputSource _source;

        [TearDown]
        public void ClearSaved() {
            _source?.Dispose();
            _source = null;
            foreach (string id in new[] { "forward", "back", "turn_left", "turn_right", "swing", "combat_overlay" }) {
                PlayerPrefs.DeleteKey(InputBindings.PrefsPrefix + id);
            }
        }

        private RebindSlot Slot(string id) => _source.RebindSlots.Single(s => s.Id == id);

        [Test]
        public void ASavedBindingIsUsedFromTheStart_AndReplacesTheDefault() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            PlayerPrefs.SetString(InputBindings.PrefsPrefix + "forward", "<Keyboard>/i");
            _source = new SystemInputSource();

            Press(keyboard.iKey);
            Assert.Greater(_source.Move.y, 0.5f, "I walks forward");
            Release(keyboard.iKey);
            Press(keyboard.wKey);
            Assert.AreEqual(0f, _source.Move.y, 1e-4f, "W no longer does");
            Release(keyboard.wKey);
            Press(keyboard.upArrowKey);
            Assert.Greater(_source.Move.y, 0.5f, "the original's arrow still does");
        }

        [Test]
        public void AKeyAnotherSlotHolds_SwapsWithIt_AndBothAreSaved() {
            InputSystem.AddDevice<Keyboard>();
            _source = new SystemInputSource();

            InputBindings.Set(_source.RebindSlots, Slot("forward"), "<Keyboard>/s");

            Assert.AreEqual("<Keyboard>/s", Slot("forward").Path);
            Assert.AreEqual("<Keyboard>/w", Slot("back").Path, "back takes forward's old key");
            Assert.AreEqual("<Keyboard>/s", PlayerPrefs.GetString(InputBindings.PrefsPrefix + "forward"));
            Assert.AreEqual("<Keyboard>/w", PlayerPrefs.GetString(InputBindings.PrefsPrefix + "back"));
        }

        [Test]
        public void SwingIsTheMiddleButton_UntilItIsMovedToAKey() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            _source = new SystemInputSource();

            Press(mouse.middleButton);
            Assert.IsTrue(_source.SwingPressed, "the middle button swings");
            Release(mouse.middleButton);
            InputSystem.Update();

            InputBindings.Set(_source.RebindSlots, Slot("swing"), "<Keyboard>/f");
            Press(keyboard.fKey);
            Assert.IsTrue(_source.SwingPressed, "F swings");
            Release(keyboard.fKey);
            Press(mouse.middleButton);
            Assert.IsFalse(_source.SwingPressed, "the middle button no longer does");
        }

        [Test]
        public void ResetPutsBackTheDefaults_AndForgetsTheSavedOnes() {
            InputSystem.AddDevice<Keyboard>();
            _source = new SystemInputSource();
            InputBindings.Set(_source.RebindSlots, Slot("combat_overlay"), "<Keyboard>/k");

            InputBindings.Reset(_source.RebindSlots);

            Assert.AreEqual("<Keyboard>/g", Slot("combat_overlay").Path);
            Assert.IsFalse(PlayerPrefs.HasKey(InputBindings.PrefsPrefix + "combat_overlay"));
        }

        [Test]
        public void TheControlsRowNamesTheActionAndItsKey_AndTheSwingHintFollowsAMovedSwing() {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _source = new SystemInputSource();
            Assert.AreEqual("Walk forward: W", BakAgain.UI.EnhancedSettingsScreen.ControlText(Slot("forward"), waiting: false));
            StringAssert.Contains("middle-click", BakAgain.UI.Combat.Enhanced.DesktopCombatHud.SwingHint());

            InputBindings.Set(_source.RebindSlots, Slot("swing"), "<Keyboard>/f");

            StringAssert.Contains("or F to swing", BakAgain.UI.Combat.Enhanced.DesktopCombatHud.SwingHint());
        }
    }
}

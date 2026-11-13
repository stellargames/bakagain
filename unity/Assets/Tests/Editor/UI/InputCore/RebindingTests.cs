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
            if (_source != null) {
                InputBindings.Reset(_source.RebindSlots);
            }
            _source?.Dispose();
            _source = null;
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
        public void EveryUiAndGameKeyHasASlot_OnceEach() {
            InputSystem.AddDevice<Keyboard>();
            _source = new SystemInputSource();
            var ids = _source.RebindSlots.Select(s => s.Id).ToList();
            CollectionAssert.IsSubsetOf(new[] {
                "activate", "activate_2", "cancel", "nav_up", "nav_down", "nav_left", "nav_right",
                "nav_up_2", "nav_down_2", "nav_left_2", "nav_right_2", "next", "previous",
                "page_up", "page_down", "first", "last", "select_key", "select_key_2", "inspect_key",
                "forward_2", "back_2", "turn_left_2", "turn_right_2", "pan_up", "pan_down", "pan_left", "pan_right",
                "map_north_up", "hotkey_a", "hotkey_z", "hotkey_1", "hotkey_0",
                "select", "inspect", "left_shift", "right_shift", "ctrl", "ctrl_2",
            }, ids);
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "no id twice");
            Assert.AreEqual(26 + 10, ids.Count(i => i.StartsWith("hotkey_")));
        }

        [Test]
        public void TheSelectKeyMoved_ClicksOnItsNewKey_AndTheOtherKeypadKeyStillClicks() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            _source = new SystemInputSource();
            IPointer pointer = _source;
            InputBindings.Set(_source.RebindSlots, Slot("select_key"), "<Keyboard>/space");

            Press(keyboard.spaceKey);
            Assert.IsTrue(pointer.Primary.IsDown, "Space clicks");
            Release(keyboard.spaceKey);
            Press(keyboard.numpad5Key);
            Assert.IsFalse(pointer.Primary.IsDown, "keypad 5 no longer does");
            Release(keyboard.numpad5Key);
            Press(keyboard.numpad0Key);
            Assert.IsTrue(pointer.Primary.IsDown, "keypad 0 still does");
            Release(keyboard.numpad0Key);
            InputBindings.Set(_source.RebindSlots, Slot("inspect_key"), "<Keyboard>/q");
            Press(keyboard.qKey);
            Assert.IsTrue(pointer.Secondary.IsDown, "Q inspects");
        }

        [Test]
        public void TheArenaPanAndTheMapToggle_FollowTheirNewKeys() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            _source = new SystemInputSource();
            ICombatOverlayInput combat = _source;
            IMapOptionInput map = _source;
            Press(keyboard.upArrowKey);
            Assert.Greater(combat.Pan.y, 0.5f, "the arrow pans by default");
            Release(keyboard.upArrowKey);

            InputBindings.Set(_source.RebindSlots, Slot("pan_up"), "<Keyboard>/i");
            InputBindings.Set(_source.RebindSlots, Slot("map_north_up"), "<Keyboard>/f6");
            Press(keyboard.iKey);
            Assert.Greater(combat.Pan.y, 0.5f, "I pans");
            Release(keyboard.iKey);
            Press(keyboard.f6Key);
            Assert.IsTrue(map.ToggleNorthUpMap, "F6 turns the map");
        }

        [Test]
        public void ClickAndInspectSwapped_TheRightButtonClicks_AndTheLeftInspects() {
            InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            _source = new SystemInputSource();
            IPointer pointer = _source;

            InputBindings.Set(_source.RebindSlots, Slot("select"), "<Mouse>/rightButton");

            Assert.AreEqual("<Mouse>/leftButton", Slot("inspect").Path, "the swap gave inspect the left button");
            Press(mouse.rightButton);
            Assert.IsTrue(pointer.Primary.IsDown, "the right button clicks");
            Assert.IsFalse(pointer.Secondary.IsDown, "and does not also inspect");
            Release(mouse.rightButton);
            Press(mouse.leftButton);
            Assert.IsTrue(pointer.Secondary.IsDown, "the left button inspects");
            Assert.IsFalse(pointer.Primary.IsDown, "and no longer clicks");
        }

        [Test]
        public void TheModifiersFollowTheirKeys_AndTheSwingHintNamesAMovedShift() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _source = new SystemInputSource();
            IGameplayInput keys = _source;

            InputBindings.Set(_source.RebindSlots, Slot("left_shift"), "<Keyboard>/f7");
            InputBindings.Set(_source.RebindSlots, Slot("ctrl"), "<Keyboard>/f8");

            Press(keyboard.f7Key);
            Assert.IsTrue(keys.LeftShift, "F7 is the left Shift");
            Release(keyboard.f7Key);
            InputSystem.Update();
            Press(keyboard.leftShiftKey);
            Assert.IsFalse(keys.LeftShift, "the Shift key no longer is");
            Release(keyboard.leftShiftKey);
            Press(keyboard.f8Key);
            Assert.IsTrue(keys.Ctrl, "F8 is Ctrl");
            Release(keyboard.f8Key);
            Press(keyboard.rightCtrlKey);
            Assert.IsTrue(keys.Ctrl, "the right Ctrl, the second key, still is");
            StringAssert.Contains("F7+click", BakAgain.UI.Combat.Enhanced.DesktopCombatHud.SwingHint());
        }

        [Test]
        public void ASwapStaysInsideItsGroup() {
            InputSystem.AddDevice<Keyboard>();
            _source = new SystemInputSource();
            RebindSlot hotkeyM = Slot("hotkey_m");
            string before = hotkeyM.Path;

            InputBindings.Set(_source.RebindSlots, Slot("forward"), "<Keyboard>/m");

            Assert.AreEqual("<Keyboard>/m", Slot("forward").Path);
            Assert.AreEqual(before, hotkeyM.Path, "M still presses the M button: walking and the hotkeys are not one set");
        }

        [Test]
        public void TheControlsRowNamesTheActionAndItsKey_AndTheSwingHintFollowsAMovedSwing() {
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _source = new SystemInputSource();
            Assert.AreEqual("Walk forward: W", BakAgain.UI.EnhancedSettingsScreen.ControlText(Slot("forward"), waiting: false));
            Assert.AreEqual("Walk forward (2nd key): Up Arrow", BakAgain.UI.EnhancedSettingsScreen.ControlText(Slot("forward_2"), waiting: false));
            Assert.AreEqual("Hotkey M: M", BakAgain.UI.EnhancedSettingsScreen.ControlText(Slot("hotkey_m"), waiting: false));
            StringAssert.Contains("middle-click", BakAgain.UI.Combat.Enhanced.DesktopCombatHud.SwingHint());

            InputBindings.Set(_source.RebindSlots, Slot("swing"), "<Keyboard>/f");

            StringAssert.Contains("or F to swing", BakAgain.UI.Combat.Enhanced.DesktopCombatHud.SwingHint());
        }
    }
}

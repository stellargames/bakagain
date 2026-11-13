namespace BakAgain.Tests.Editor.UI.InputCore {
    using System.Collections.Generic;
    using BakAgain.UI.InputCore;
    using NUnit.Framework;
    using UnityEngine.InputSystem;

    public class InputAdapterTests : InputTestFixture {
        private sealed class SpyCommands : IUiCommands {
            public readonly List<string> Calls = new List<string>();
            public void MoveFocus(NavDirection d) => Calls.Add("Move:" + d);
            public void Activate() => Calls.Add("Activate");
            public void Cancel() => Calls.Add("Cancel");
            public void Accelerator(char c) => Calls.Add("Accel:" + c);
            public void Skip(char key = '\0') => Calls.Add("Skip");
            public IInputLayer TopLayer { get; set; }
            public bool IsModal => false;
        }

        // ONE action asset for the whole fixture, disabled rather than destroyed at the end.
        //
        // Neither half is load-bearing for the mouse-move storm — that came from the PROJECT-WIDE
        // actions asset, which InputTestFixture copies per test and destroys at teardown, stranding
        // the copy's <Mouse>/position monitor (six tests, six strays). Unsetting the project-wide
        // reference fixed it; measured at 6 strays with it set and 0 without, this file unchanged.
        //
        // Kept anyway because it is the better shape: one asset per run instead of one per test, and
        // Disable() rather than Dispose(). Disable unregisters the monitors, which is the part that
        // matters; the generated Dispose() is Object.Destroy(asset) with no Disable in front of it,
        // and destroying an asset whose maps are still enabled is exactly how a stray is made.
        private SharedInputActions _actions;

        [OneTimeSetUp]
        public void CreateActions() => _actions = new SharedInputActions();

        [OneTimeTearDown]
        public void ReleaseActions() {
            _actions?.Actions?.Disable();
            _actions = null;
        }

        // The keys the adapter reads (TASK-1080): one source per test, released with the test.
        private SystemInputSource _keys;

        [TearDown]
        public void ReleaseKeys() {
            if (_keys != null) {
                InputBindings.Reset(_keys.RebindSlots);
                _keys.Dispose();
                _keys = null;
            }
        }

        // Started here so each test body just drives input.
        private InputAdapter StartedAdapter(IUiCommands spy) {
            _keys ??= new SystemInputSource();
            var adapter = new InputAdapter(spy, _actions, _keys);
            adapter.Start();
            return adapter;
        }

        private RebindSlot Slot(string id) => System.Linq.Enumerable.Single(_keys.RebindSlots, s => s.Id == id);

        // TASK-1080 (owner: "remap all actions"): the UI keys are actions of their own now.
        [Test]
        public void ActivateMovedToSpace_ActivatesOnSpace_AndEnterBecomesASkip() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                InputBindings.Set(_keys.RebindSlots, Slot("activate"), "<Keyboard>/space");
                Press(keyboard.spaceKey);
                adapter.Tick();
                CollectionAssert.AreEqual(new[] { "Activate" }, spy.Calls, "Space activates, and is not also a Skip");
                Release(keyboard.spaceKey);
                spy.Calls.Clear();
                Press(keyboard.enterKey);
                adapter.Tick();
                CollectionAssert.AreEqual(new[] { "Skip" }, spy.Calls, "Enter is just another key now");
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void CancelAndNextMoved_FollowTheirNewKeys() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                InputBindings.Set(_keys.RebindSlots, Slot("cancel"), "<Keyboard>/f2");
                InputBindings.Set(_keys.RebindSlots, Slot("next"), "<Keyboard>/f1");
                Press(keyboard.f2Key);
                adapter.Tick();
                Release(keyboard.f2Key);
                Press(keyboard.f1Key);
                adapter.Tick();
                CollectionAssert.AreEqual(new[] { "Cancel", "Move:Next" }, spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void NavigationMovedToALetter_MovesFocus_AndTheLetterTakesTheArrow() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                InputBindings.Set(_keys.RebindSlots, Slot("nav_up"), "<Keyboard>/i");
                Press(keyboard.iKey);
                adapter.Tick();
                CollectionAssert.AreEqual(new[] { "Move:Up" }, spy.Calls, "I moves up and is no longer hotkey I");
                Release(keyboard.iKey);
                spy.Calls.Clear();
                Press(keyboard.upArrowKey);
                adapter.Tick();
                CollectionAssert.AreEqual(new[] { "Accel:i" }, spy.Calls, "the swap gave hotkey I the up arrow");
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void AHotkeyMovedToAnotherKey_SendsItsLetterThere() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                InputBindings.Set(_keys.RebindSlots, Slot("hotkey_m"), "<Keyboard>/k");
                Press(keyboard.kKey);
                adapter.Tick();
                Release(keyboard.kKey);
                Press(keyboard.mKey);
                adapter.Tick();
                CollectionAssert.AreEqual(new[] { "Accel:m", "Accel:k" }, spy.Calls, "K and M swapped");
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void TheKeypadPageAndEndKeys_FollowTheirNewKeys() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands {
                TopLayer = new ScreenInputLayer("screen", new NoTextScreen()),
            };
            InputAdapter adapter = StartedAdapter(spy);
            try {
                InputBindings.Set(_keys.RebindSlots, Slot("nav_down_2"), "<Keyboard>/f3");
                InputBindings.Set(_keys.RebindSlots, Slot("page_up"), "<Keyboard>/f4");
                InputBindings.Set(_keys.RebindSlots, Slot("last"), "<Keyboard>/f5");
                foreach (UnityEngine.InputSystem.Controls.KeyControl key in new[] { keyboard.f3Key, keyboard.f4Key, keyboard.f5Key }) {
                    Press(key);
                    adapter.Tick();
                    Release(key);
                }
                CollectionAssert.AreEqual(new[] { "Move:Down", "Move:PageUp", "Move:Last" }, spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void Tab_PollsToMoveFocusNext() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                // Press() already runs one InputSystem.Update(); calling another would advance a frame
                // and clear wasPressedThisFrame before Tick() polls it. So poll right after the press.
                Press(keyboard.tabKey);
                adapter.Tick();
                Assert.Contains("Move:Next", spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void Letter_PollsToAccelerator() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                Press(keyboard.nKey);
                adapter.Tick();
                Assert.Contains("Accel:n", spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        // A REQ menu's '1'..'0' are scancodes 2..11 like any letter (TASK-796), so over a menu layer a
        // digit is an Accelerator. Elsewhere it stays the Skip that pages a dialog on.
        [Test]
        public void Digit_OverAMenuLayer_IsAnAccelerator_ElsewhereASkip() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands {
                TopLayer = new NavigableLayer("menu", CaptureMode.Passive, new List<NavWidget>(), null, null),
            };
            InputAdapter adapter = StartedAdapter(spy);
            try {
                Press(keyboard.digit1Key);
                adapter.Tick();
                Assert.Contains("Accel:1", spy.Calls);

                spy.Calls.Clear();
                spy.TopLayer = new TravelLayer("travel", new List<NavWidget>(), null);
                Release(keyboard.digit1Key);
                Press(keyboard.digit1Key);
                adapter.Tick();
                Assert.Contains("Accel:1", spy.Calls, "the travel HUD's 1 is the first portrait (TASK-798)");

                spy.Calls.Clear();
                spy.TopLayer = null;
                Release(keyboard.digit1Key);
                Press(keyboard.digit1Key);
                adapter.Tick();
                Assert.Contains("Skip", spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void SubmitAction_RaisesActivate() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                // DefaultInputActions binds UI/Submit to Enter.
                Press(keyboard.enterKey);
                InputSystem.Update();
                Assert.Contains("Activate", spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void CancelAction_RaisesCancel() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                // DefaultInputActions binds UI/Cancel to Escape.
                Press(keyboard.escapeKey);
                InputSystem.Update();
                Assert.Contains("Cancel", spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        // The original's dialog poll returns every scancode it reads except the four arrows, so a key
        // with no other UI meaning has to reach full-frame layers as Skip — Space is the one a player
        // actually presses to page a dialog, and before this it did nothing at all.
        [Test]
        public void UnclaimedKey_PollsToSkip() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                Press(keyboard.spaceKey);
                adapter.Tick();
                Assert.Contains("Skip", spy.Calls);
            } finally {
                adapter.Dispose();
            }
        }

        // The four arrows are the exception the original names (DIALOG.C:161-171 discards them), and
        // here they are already the Navigate map's MoveFocus — a second Skip for the same press would
        // page a dialog the arrow is supposed to leave alone.
        [Test]
        public void Arrow_DoesNotAlsoSkip() {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                Press(keyboard.upArrowKey);
                adapter.Tick();
                Assert.IsFalse(spy.Calls.Contains("Skip"), "an arrow is MoveFocus, never Skip");
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void Click_FiresSkipOnRelease_NotOnPress() {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            var spy = new SpyCommands(); // TopLayer stays null (unchanged) across press+release
            InputAdapter adapter = StartedAdapter(spy);
            try {
                Press(mouse.leftButton);
                InputSystem.Update();
                Assert.IsFalse(spy.Calls.Contains("Skip"), "no Skip on press");
                Release(mouse.leftButton);
                InputSystem.Update();
                Assert.Contains("Skip", spy.Calls, "Skip on release when the top layer is unchanged");
            } finally {
                adapter.Dispose();
            }
        }

        [Test]
        public void Click_DoesNotSkip_WhenTopLayerChangedBetweenPressAndRelease() {
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            var spy = new SpyCommands();
            var layerA = new FakeInputLayer("a", CaptureMode.Exclusive);
            var layerB = new FakeInputLayer("b", CaptureMode.Exclusive);
            InputAdapter adapter = StartedAdapter(spy);
            try {
                // Press while layer A is on top, then the top changes to B (e.g. the prior screen closed
                // and a new one opened) before the release — a carried-over release must NOT skip.
                spy.TopLayer = layerA;
                Press(mouse.leftButton);
                InputSystem.Update();
                spy.TopLayer = layerB;
                Release(mouse.leftButton);
                InputSystem.Update();
                Assert.IsFalse(spy.Calls.Contains("Skip"), "carried-over release must not skip");
            } finally {
                adapter.Dispose();
            }
        }
    
        private sealed class NoTextScreen : IScreenInput {
            public bool WantsText => false;
            public bool OnDirection(NavDirection dir, bool ctrl) => false;
            public bool OnTab(bool shift) => false;
            public void OnText(char c) { }
            public bool OnEdit(EditKey key) => false;
            public void OnSubmit() { }
            public void OnCancel() { }
        }

        private sealed class TextScreen : IScreenInput {
            public bool WantsText => true;
            public bool OnDirection(NavDirection dir, bool ctrl) => false;
            public bool OnTab(bool shift) => false;
            public void OnText(char c) { }
            public bool OnEdit(EditKey key) => false;
            public void OnSubmit() { }
            public void OnCancel() { }
        }

        // The Linux player drops every typed character until the legacy imeCompositionMode is On:
        // its SDL layer gates text on it, and Keyboard.SetIMEEnabled does not reach it (TASK-1058).
        [Test]
        public void ATextScreenSwitchesTextInputOnAndLeavingItSwitchesItBack() {
            InputSystem.AddDevice<Keyboard>();
            var spy = new SpyCommands();
            InputAdapter adapter = StartedAdapter(spy);
            try {
                adapter.Tick();
                Assert.AreEqual(UnityEngine.IMECompositionMode.Auto, UnityEngine.Input.imeCompositionMode);
                spy.TopLayer = new ScreenInputLayer("save", new TextScreen());
                adapter.Tick();
                Assert.AreEqual(UnityEngine.IMECompositionMode.On, UnityEngine.Input.imeCompositionMode);
                spy.TopLayer = null;
                adapter.Tick();
                Assert.AreEqual(UnityEngine.IMECompositionMode.Auto, UnityEngine.Input.imeCompositionMode);
            } finally {
                adapter.Dispose();
                UnityEngine.Input.imeCompositionMode = UnityEngine.IMECompositionMode.Auto;
            }
        }
    }
}

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
            public void Skip() => Calls.Add("Skip");
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

        // Started here so each test body just drives input.
        private InputAdapter StartedAdapter(IUiCommands spy) {
            var adapter = new InputAdapter(spy, _actions);
            adapter.Start();
            return adapter;
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
    }
}

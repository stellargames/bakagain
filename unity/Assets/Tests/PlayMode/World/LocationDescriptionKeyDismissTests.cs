namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.UI.InputCore;
    using BakAgain.World.Scenes;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A town description closes on a key as well as on a click or mouse drift: townscene_cursor_wait_for_drag
    /// polls dialog_poll_arrow_or_button, which returns every scancode but the arrows and NumLock/ScrollLock
    /// (TOWNSCN.C:221-244, DIALOG.C:155-181). The port read only the pointer (TASK-1113 row 12).
    /// </summary>
    public class LocationDescriptionKeyDismissTests {
        private GameObject _go;
        private InputLayerStack _savedStack;

        [SetUp]
        public void SetUp() => _savedStack = UiDriver.Stack;

        [TearDown]
        public void TearDown() {
            UiDriver.Stack = _savedStack;
            if (_go != null) { Object.DestroyImmediate(_go); }
        }

        private (LocationScreen Screen, InputLayerStack Stack) Make() {
            var stack = new InputLayerStack();
            UiDriver.Stack = stack;
            _go = new GameObject("LocationUnderTest");
            var screen = _go.AddComponent<LocationScreen>();
            screen.Construct(null, new FakePointer(), null, null, null, null, null, null, null, null);
            return (screen, stack);
        }

        [UnityTest]
        public IEnumerator AKeyClosesTheDescription() {
            var (screen, stack) = Make();
            bool done = false;
            screen.DismissAsync().ContinueWith(() => done = true).Forget();
            yield return null;
            yield return null;
            Assert.IsFalse(done, "control: nothing pressed yet");

            stack.DispatchIntent(UiIntent.Skip(' '));
            for (int i = 0; i < 4 && !done; i++) { yield return null; }
            Assert.IsTrue(done, "a space closes it");
        }

        [UnityTest]
        public IEnumerator AnArrowDoesNot() {
            var (screen, stack) = Make();
            bool done = false;
            screen.DismissAsync().ContinueWith(() => done = true).Forget();
            yield return null;
            stack.DispatchIntent(UiIntent.Move(NavDirection.Left));
            for (int i = 0; i < 4; i++) { yield return null; }
            Assert.IsFalse(done, "the arrows are dropped (DIALOG.C:169)");
            stack.DispatchIntent(UiIntent.Cancel());
            for (int i = 0; i < 4 && !done; i++) { yield return null; }
            Assert.IsTrue(done, "Esc is a scancode like any other");
        }
    }
}

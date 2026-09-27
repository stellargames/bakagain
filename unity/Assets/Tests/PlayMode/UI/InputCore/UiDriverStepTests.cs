namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using System.Collections;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    /// <summary>
    /// One call is one step — TASK-299 item (b).
    /// </summary>
    /// <remarks>
    /// Intents are consumed at most once per frame, so a batch issued inside one <c>eval</c>
    /// advances a screen ONCE and drops the rest. That cost a session an hour, reading as "the
    /// screen is stuck" rather than as a mistake in the driving.
    /// </remarks>
    public class UiDriverStepTests {
        private sealed class CountingLayer : IInputLayer {
            public CountingLayer(bool handles) => _handles = handles;

            private readonly bool _handles;
            public int Seen { get; private set; }

            public string Id => "counter";
            public CaptureMode CaptureMode => CaptureMode.Exclusive;
            public bool WantsFocus => true;

            public bool HandleIntent(UiIntent intent) {
                Seen++;
                return _handles;
            }

            public void OnPushed() { }
            public void OnPopped() { }
            public void OnActiveChanged(bool isActive) { }
        }

        [TearDown]
        public void Reset() => UiDriver.Stack = null;

        [UnityTest]
        public IEnumerator OneStepDeliversExactlyOneIntent() => UniTask.ToCoroutine(async () => {
            var stack = new InputLayerStack();
            var layer = new CountingLayer(handles: true);
            stack.Push(layer);
            UiDriver.Stack = stack;

            bool taken = await UiDriver.ActivateAsync();

            Assert.IsTrue(taken);
            Assert.AreEqual(1, layer.Seen, "one call must deliver one intent, not a batch");
        });

        [UnityTest]
        public IEnumerator ALayerThatDeclinesReportsFalse() => UniTask.ToCoroutine(async () => {
            // "Nothing was listening" is a different problem from "it was taken and changed
            // nothing", and a void command seam cannot tell them apart. That is why StepAsync
            // returns the stack's answer instead of the interface's.
            var stack = new InputLayerStack();
            var layer = new CountingLayer(handles: false);
            stack.Push(layer);
            UiDriver.Stack = stack;

            Assert.IsFalse(await UiDriver.ActivateAsync());
            Assert.AreEqual(1, layer.Seen, "declined is still delivered");
        });

        [UnityTest]
        public IEnumerator WithNoStackItReportsFalseRatherThanThrowing() =>
            UniTask.ToCoroutine(async () => {
                // A probe run before the container is built is normal, not an error.
                UiDriver.Stack = null;
                Assert.IsFalse(await UiDriver.ActivateAsync());
            });

        [UnityTest]
        public IEnumerator EachCallIsCountedSeparately() => UniTask.ToCoroutine(async () => {
            var stack = new InputLayerStack();
            var layer = new CountingLayer(handles: true);
            stack.Push(layer);
            UiDriver.Stack = stack;

            await UiDriver.ActivateAsync();
            await UiDriver.MoveFocusAsync(NavDirection.Down);
            await UiDriver.CancelAsync();

            Assert.AreEqual(3, layer.Seen);
        });
    }
}

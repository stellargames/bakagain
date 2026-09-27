namespace BakAgain.Tests.PlayMode.UI.InputCore {
    using BakAgain.UI.InputCore;
    using NUnit.Framework;

    /// <summary>
    /// Covers the input half of <see cref="UiDiagnostics" />, which is the half that can be asserted
    /// without a scene. The DRAWING half enumerates live UIDocuments and Canvases, so it is exercised
    /// by using it rather than by a unit test.
    /// </summary>
    public class UiDiagnosticsTests {
        private sealed class Layer : IInputLayer {
            public Layer(string id, CaptureMode mode) {
                Id = id;
                CaptureMode = mode;
            }

            public string Id { get; }
            public CaptureMode CaptureMode { get; }
            public bool WantsFocus => true;
            public bool HandleIntent(UiIntent intent) => true;
            public void OnPushed() { }
            public void OnPopped() { }
            public void OnActiveChanged(bool isActive) { }
        }

        [Test]
        public void AnEmptyStackSaysSo_RatherThanReadingAsHealthy() {
            string text = UiDiagnostics.DescribeStack(new InputLayerStack());

            // "no layers" and "a layer nobody can reach" look identical if the report is silent.
            StringAssert.Contains("stack empty", text);
        }

        [Test]
        public void ANullStackIsReported_NotNullReferenced() {
            Assert.DoesNotThrow(() => UiDiagnostics.DescribeStack(null));
            StringAssert.Contains("no stack", UiDiagnostics.DescribeStack(null));
        }

        [Test]
        public void EveryLayerIsListedInPushOrder() {
            var stack = new InputLayerStack();
            stack.Push(new Layer("world", CaptureMode.Passive));
            stack.Push(new Layer("dialog", CaptureMode.Exclusive));

            string text = UiDiagnostics.DescribeStack(stack);

            // Assert on the INDEXED LINES, not on where the names first appear in the whole string:
            // the header names the target ("target: dialog") before the list starts, so a raw
            // IndexOf comparison measures the header and fails on correct output. It did.
            string[] lines = text.Split('\n');
            string zero = System.Array.Find(lines, line => line.Contains("[0]"));
            string one = System.Array.Find(lines, line => line.Contains("[1]"));

            Assert.IsNotNull(zero, "expected an indexed line for the bottom layer");
            Assert.IsNotNull(one, "expected an indexed line for the top layer");
            StringAssert.Contains("world", zero);
            StringAssert.Contains("dialog", one);
        }

        [Test]
        public void TheResolvedTargetIsMarked_AndIsNotMerelyTheTopLayer() {
            var stack = new InputLayerStack();
            var world = new Layer("world", CaptureMode.Passive);
            var modal = new Layer("dialog", CaptureMode.Exclusive);
            stack.Push(world);
            stack.Push(modal);

            string text = UiDiagnostics.DescribeStack(stack);

            // The whole point of the report: name the layer intents actually reach.
            StringAssert.Contains("target: dialog", text);
            Assert.AreSame(modal, stack.ResolveInputTarget());
        }

        [Test]
        public void ALayerUnderAnExclusiveOneReadsAsBlocked() {
            var stack = new InputLayerStack();
            stack.Push(new Layer("world", CaptureMode.Passive));
            stack.Push(new Layer("dialog", CaptureMode.Exclusive));

            string text = UiDiagnostics.DescribeStack(stack);
            string worldLine = System.Array.Find(
                text.Split('\n'), line => line.Contains("world"));

            Assert.IsNotNull(worldLine);
            StringAssert.Contains("blocked", worldLine);
            StringAssert.Contains("modal: True", text);
        }

        [Test]
        public void WithNoExclusiveLayer_NothingIsBlockedAndItIsNotModal() {
            var stack = new InputLayerStack();
            stack.Push(new Layer("world", CaptureMode.Passive));

            string text = UiDiagnostics.DescribeStack(stack);

            StringAssert.Contains("interactable", text);
            StringAssert.Contains("modal: False", text);
        }
    }
}

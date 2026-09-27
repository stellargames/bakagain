namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The stack-quantity picker's view half (task-51; <c>quantityPickerDialog @0x59EA1</c>,
    /// REQ_INV2.DAT — spec docs/specs/inventory-item-handling.md §14). The value rules themselves
    /// are pinned engine-side by <c>QuantityPickerModelTests</c>; what these cover is what only the
    /// view can get wrong: which elements it builds, the no-Share shrink, and that every route out
    /// — button, Enter, Esc, G, S — resolves the awaited task with the right number.
    ///
    /// <para>Input arrives the way production delivers it: through an <see cref="InputLayerStack"/>
    /// the picker pushes its Exclusive layer onto, so these also prove the layer is on top and
    /// consuming.</para>
    /// </summary>
    public class QuantityPickerViewTests {
        // Big enough to contain the picker's shipped rect (page 530,210 + 530x408) with room to
        // spare, so every button has a real on-panel position to be clicked at.
        private const int PanelWidth = 1600, PanelHeight = 1200;

        private GameObject _go;
        private PanelSettings _panelSettings;
        private RenderTexture _panelTexture;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
            if (_panelTexture != null) { Object.DestroyImmediate(_panelTexture); }
        }

        // A real UIDocument panel, so worldBound/SendEvent behave as in production. No resource
        // provider is passed to ShowAsync, so the picker uses PickerLayout.Shipped — the compiled
        // fallback that mirrors generated/REQ/REQ_INV2.json.
        //
        // The panel is pinned to a fixed size via a target RenderTexture rather than left to derive
        // one from the screen. Batch-mode runs (`make unity-test`, the CI gate) are headless, so a
        // screen-derived panel has no usable dimensions: worldBound stays degenerate, Clickable's
        // ContainsPoint hit test misses, and the click tests below silently do nothing — they
        // passed in the Editor and failed in batch until this was pinned. ConstantPixelSize keeps
        // panel coordinates equal to the canonical px the picker is authored in.
        private VisualElement BuildStage() {
            _go = new GameObject("QuantityPickerUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelTexture = new RenderTexture(PanelWidth, PanelHeight, 0);
            _panelSettings.targetTexture = _panelTexture;
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.referenceResolution = new Vector2Int(PanelWidth, PanelHeight);
            document.panelSettings = _panelSettings;
            return document.rootVisualElement;
        }

        // A [UnityTest] that resolves the picker in the same frame never sees the player loop's
        // layout tick, so worldBound stays degenerate and Clickable's hit test would miss. Same
        // reflection hop ItemGridRendererTests.ForceLayout uses.
        private static void ForceLayout(VisualElement stage) {
            System.Reflection.MethodInfo validateLayout = stage.panel.GetType().GetMethod(
                "ValidateLayout",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            validateLayout?.Invoke(stage.panel, null);
        }

        private static VisualElement Button(VisualElement stage, int actionId) =>
            stage.Q<VisualElement>($"qty_btn_{actionId}");

        // The caption lives in a child now, not on the chrome — GameFontText.Caption puts it there
        // so the vertical stretch does not take the bevel with it.
        private static string GiveText(VisualElement stage) =>
            Button(stage, QuantityPickerView.ActionGive).Q<Label>("caption").text;

        private static void Click(VisualElement target) {
            Vector2 center = target.worldBound.center;
            using (PointerDownEvent down = PointerDownEvent.GetPooled(
                       new Event { type = EventType.MouseDown, mousePosition = center, button = 0 })) {
                down.target = target;
                target.SendEvent(down);
            }
            using (PointerUpEvent up = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, mousePosition = center, button = 0 })) {
                up.target = target;
                target.SendEvent(up);
            }
        }

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator OpensWithEveryEntry_AndTheValueAtAll() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();

            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, max: 6, allowShare: true);

            Assert.IsNotNull(stage.Q<VisualElement>("quantity_picker"), "panel not built");
            Assert.IsNotNull(stage.Q<VisualElement>("qty_picker_scrim"), "pointer scrim not built");
            Assert.AreEqual("Select amount:", stage.Q<Label>("qty_title").text);
            foreach (int action in new[] {
                QuantityPickerView.ActionGive, QuantityPickerView.ActionDown5,
                QuantityPickerView.ActionDown1, QuantityPickerView.ActionUp1,
                QuantityPickerView.ActionUp5, QuantityPickerView.ActionShare,
            }) {
                Assert.IsNotNull(Button(stage, action), $"missing REQ_INV2 entry {action:X}");
            }
            Assert.AreEqual("Give: 6 (All)", GiveText(stage), "the picker opens at the full stack");
            Assert.AreSame(stack.Top, stack.ResolveInputTarget(), "the picker's layer must be on top");
            Assert.IsTrue(stack.IsModal, "the picker's layer must be Exclusive");

            stack.DispatchIntent(UiIntent.Cancel());
            Assert.AreEqual(0, await pick);
            Assert.IsNull(stage.Q<VisualElement>("quantity_picker"), "panel must be torn down");
            Assert.IsNull(stage.Q<VisualElement>("qty_picker_scrim"), "scrim must be torn down");
            Assert.IsNull(stack.Top, "the picker's layer must be popped");
        });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator SteppingDown_ThenAccepting_ReturnsTheSteppedValue() =>
            UniTask.ToCoroutine(async () => {
                VisualElement stage = BuildStage();
                var stack = new InputLayerStack();
                UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, max: 6, allowShare: true);
                ForceLayout(stage);

                Click(Button(stage, QuantityPickerView.ActionDown1));
                Click(Button(stage, QuantityPickerView.ActionDown1));
                Assert.AreEqual("Give: 4", GiveText(stage), "two single steps down from 6");

                Click(Button(stage, QuantityPickerView.ActionGive));
                Assert.AreEqual(4, await pick);
            });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator AcceptingAtNone_Cancels() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();
            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, max: 3, allowShare: false);
            ForceLayout(stage);

            // +1 from the maximum wraps to 0, which reads as the cancel affordance (INVINSP.C:90-98).
            Click(Button(stage, QuantityPickerView.ActionUp1));
            Assert.AreEqual("None: (Cancel)", GiveText(stage));

            Click(Button(stage, QuantityPickerView.ActionGive));
            Assert.AreEqual(0, await pick, "accepting at None is the cancel");
        });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator ShareButton_IsDroppedAndThePanelShrinks_WhenShareIsNotOffered() =>
            UniTask.ToCoroutine(async () => {
                VisualElement stage = BuildStage();
                var stack = new InputLayerStack();

                UniTask<int> withShare = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: true);
                VisualElement panel = stage.Q<VisualElement>("quantity_picker");
                float sharedTop = panel.style.top.value.value;
                float sharedHeight = panel.style.height.value.value;
                stack.DispatchIntent(UiIntent.Cancel());
                await withShare;

                UniTask<int> noShare = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: false);
                panel = stage.Q<VisualElement>("quantity_picker");
                Assert.IsNull(Button(stage, QuantityPickerView.ActionShare),
                    "Share must not be built when the destination can't take a distribution");
                // INVINSP.C:74-76 — rect.y += 7, rect.height -= 14 (VGA px, x6 canonical).
                Assert.AreEqual(sharedTop + 42f, panel.style.top.value.value, 0.01f);
                Assert.AreEqual(sharedHeight - 84f, panel.style.height.value.value, 0.01f);

                stack.DispatchIntent(UiIntent.Cancel());
                await noShare;
            });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator ShareButton_ReturnsMinusOne() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();
            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: true);
            ForceLayout(stage);

            Click(Button(stage, QuantityPickerView.ActionShare));
            Assert.AreEqual(-1, await pick, "Share is the caller's cue to run Distribute");
        });

        // --- keyboard (INVINSP.C:112-166) -------------------------------------------------

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator PageUpPageDown_StepByFive() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();
            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, max: 12, allowShare: false);

            stack.DispatchIntent(UiIntent.Move(NavDirection.PageDown));
            Assert.AreEqual("Give: 7", GiveText(stage), "PgDn is -5");
            stack.DispatchIntent(UiIntent.Move(NavDirection.PageUp));
            Assert.AreEqual("Give: 12 (All)", GiveText(stage), "PgUp is +5");

            stack.DispatchIntent(UiIntent.Cancel());
            await pick;
        });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator ArrowKeys_StepByOne() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();
            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, max: 6, allowShare: false);

            stack.DispatchIntent(UiIntent.Move(NavDirection.Left));
            stack.DispatchIntent(UiIntent.Move(NavDirection.Down));
            Assert.AreEqual("Give: 4", GiveText(stage), "left/down are -1");
            stack.DispatchIntent(UiIntent.Move(NavDirection.Right));
            Assert.AreEqual("Give: 5", GiveText(stage), "right/up are +1");

            stack.DispatchIntent(UiIntent.Cancel());
            await pick;
        });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator GKey_Accepts() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();
            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, max: 6, allowShare: false);

            stack.DispatchIntent(UiIntent.Move(NavDirection.PageDown)); // 6 -> 1
            stack.DispatchIntent(UiIntent.Accelerator('g'));
            Assert.AreEqual(1, await pick);
        });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator SKey_Shares_OnlyWhenShareIsOffered() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();

            UniTask<int> noShare = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: false);
            stack.DispatchIntent(UiIntent.Accelerator('s'));
            Assert.AreEqual(UniTaskStatus.Pending, noShare.Status,
                "S must do nothing when the panel has no Share entry (INVINSP.C:166 gate)");
            stack.DispatchIntent(UiIntent.Cancel());
            await noShare;

            UniTask<int> withShare = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: true);
            stack.DispatchIntent(UiIntent.Accelerator('s'));
            Assert.AreEqual(-1, await withShare);
        });

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator AStrayLetter_DoesNotResolveThePicker() => UniTask.ToCoroutine(async () => {
            VisualElement stage = BuildStage();
            var stack = new InputLayerStack();
            UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: true);

            stack.DispatchIntent(UiIntent.Accelerator('q'));
            stack.DispatchIntent(UiIntent.Skip()); // every pointer release synthesises one of these
            Assert.AreEqual(UniTaskStatus.Pending, pick.Status,
                "only G/S/Enter/Esc and the buttons resolve — a stray key or click must not");

            stack.DispatchIntent(UiIntent.Cancel());
            await pick;
        });
        /// <summary>
        /// AN ABANDONED PICKER MUST FINISH, OR IT DEADENS THE SCREEN FOR THE SESSION. (TASK-568)
        /// </summary>
        /// <remarks>
        /// Until the token existed, the ONLY things that resolved the wait were this panel's own
        /// buttons and its input layer. A screen torn down while the picker was up parked the
        /// continuation for ever — so neither this method's <c>finally</c> nor the caller's ran, the
        /// scrim and panel stayed on the stage, the layer stayed pushed, and the caller's
        /// <c>_pickerOpen</c> stayed true on a component that OUTLIVES the screen. The next open
        /// rebuilt the stage, so it all looked normal while every gesture handler early-returned.
        ///
        /// <para>The assertions are the three things that leak, not just the task status: a
        /// completed task with the layer still on the stack would be the same bug one step along.
        /// </para>
        /// </remarks>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator AbandoningThePicker_ResolvesItAsNone_AndTakesDownEverythingItBuilt() =>
            UniTask.ToCoroutine(async () => {
                VisualElement stage = BuildStage();
                var stack = new InputLayerStack();
                var abandoned = new System.Threading.CancellationTokenSource();

                UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: true,
                    resources: null, abandoned: abandoned.Token);

                // The control: it really is up, so what follows is an abandonment and not a picker
                // that never opened.
                Assert.IsNotNull(stage.Q<VisualElement>("quantity_picker"), "panel not built");
                Assert.IsNotNull(stage.Q<VisualElement>("qty_picker_scrim"), "scrim not built");
                Assert.AreEqual("quantity-picker", stack.Top?.Id, "layer not pushed");
                Assert.AreEqual(UniTaskStatus.Pending, pick.Status);

                abandoned.Cancel();

                // ZERO, the same answer Esc and accepting at "None" give — every caller already
                // reads <= 0 as "no transfer", so an abandoned picker moves nothing.
                Assert.AreEqual(0, await pick,
                    "an abandoned picker must resolve as None, not hang and not transfer");
                Assert.IsNull(stage.Q<VisualElement>("quantity_picker"),
                    "the panel must come down on abandonment, not only when a button answers");
                Assert.IsNull(stage.Q<VisualElement>("qty_picker_scrim"),
                    "a scrim left behind swallows every click on the screen underneath");
                Assert.AreNotEqual("quantity-picker", stack.Top?.Id,
                    "the input layer must be popped — an Exclusive layer with no panel behind it "
                    + "consumes every intent and nothing pops it");
            });

        /// <summary>
        /// The ordinary path must not notice the token. (The control for the fixture above.)
        /// </summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator AnUncancelledToken_LeavesTheNormalAnswerAlone() =>
            UniTask.ToCoroutine(async () => {
                VisualElement stage = BuildStage();
                var stack = new InputLayerStack();
                var abandoned = new System.Threading.CancellationTokenSource();

                UniTask<int> pick = QuantityPickerView.ShowAsync(stage, stack, 6, allowShare: true,
                    resources: null, abandoned: abandoned.Token);
                ForceLayout(stage);
                stack.DispatchIntent(UiIntent.Activate());

                Assert.AreEqual(6, await pick, "Enter still accepts the model's value");
            });
    }
}

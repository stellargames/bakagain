namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Fences <see cref="DialogManager.WaitForResolvedRect"/> — the wait the open-wipe added in
    /// commit 630d12e to read a panel's RESOLVED rect instead of its pre-layout
    /// <see cref="LayoutHint"/>. Before this fixture, nothing exercised its timeout branch at all:
    /// every dialog PlayMode test passes <c>DialogEntryFlags.SkipOpenWipe</c>, which routes around
    /// the wipe (and this method) entirely.
    ///
    /// <para>The property under test is a safety one: a panel that never resolves a layout must
    /// not hang the dialog show. <see cref="WipeGeometryTimeoutFrames_IsFive"/> pins the bound;
    /// <see cref="APanelThatNeverLaysOut_ResolvesToRectZero_WithinTheFrameBudget_AndDoesNotThrow"/>
    /// drives the method directly against a panel that structurally can never lay out (never added
    /// to any panel); <see cref="ShowEntryCore_SkipsTheWipe_WhenThePanelNeverResolves_AndTheDialogStillAppears"/>
    /// drives the real caller (<c>DialogManager.ShowEntryCore</c>, via <c>DisplayEntry</c>) end to
    /// end and proves the consequence: the wipe's mask is never created, and the dialog still
    /// appears on the stage.</para>
    /// </summary>
    public class DialogManagerWaitForResolvedRectTests {
        [SetUp]
        public void RequireShippedGameData() {
            ShippedGameData.RequireOrIgnore();
        }

        [Test]
        public void WipeGeometryTimeoutFrames_IsFive() {
            // Not a tautology: this is the number the two UnityTests below budget their own frame
            // counts against, so a future change to the constant can't silently desync them from
            // what the production code actually waits.
            Assert.AreEqual(5, DialogManager.WipeGeometryTimeoutFrames);
        }

        /// <summary>
        /// THE FENCE for the timeout branch. A freestanding <see cref="VisualElement"/> that is
        /// never added to any panel structurally never receives a Yoga layout pass — UI Toolkit
        /// only lays out elements that belong to a <c>Panel</c> (owned by a <c>UIDocument</c>), so
        /// <c>panel.layout</c> stays unresolved and <see cref="GeometryChangedEvent"/> can never
        /// fire for it. That is exactly the "detached, or a bug upstream" case
        /// <see cref="DialogManager.WaitForResolvedRect"/>'s own remarks call out, and the only way
        /// to reach its timeout path in isolation from the immediate-resolve and
        /// event-driven-resolve paths (both of which need a real panel to exercise).
        /// </summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator APanelThatNeverLaysOut_ResolvesToRectZero_WithinTheFrameBudget_AndDoesNotThrow() =>
            UniTask.ToCoroutine(async () => {
                var orphan = new VisualElement(); // deliberately never added to any panel/hierarchy
                var cts = new CancellationTokenSource();

                int frameBefore = Time.frameCount;
                // If this throws, the UniTask coroutine surfaces the exception and the test fails —
                // that IS the "does not throw" assertion; no try/catch needed to prove it.
                Rect result = await DialogManager.WaitForResolvedRect(orphan, cts.Token);
                int framesElapsed = Time.frameCount - frameBefore;

                Assert.AreEqual(Rect.zero, result,
                    "a panel that never lays out must resolve to Rect.zero, not hang or throw");
                // Generous slack (+3) for the coroutine/UniTask.ToCoroutine plumbing itself, which
                // yields at least once outside the method's own loop; the point is "small bounded
                // number", not an exact frame-for-frame match.
                Assert.LessOrEqual(framesElapsed, DialogManager.WipeGeometryTimeoutFrames + 3,
                    "WaitForResolvedRect must give up within its bounded frame budget, not hang "
                    + "indefinitely waiting for a GeometryChangedEvent that can never fire");
            });

        // A ResizeDialogAction with only Width/Height stated: Left/Top stay Auto, so
        // LayoutApplier's TopLeft anchor pins the panel at (0,0) and these two explicit zeros make
        // it a genuine, deterministic 0x0 box — not merely "not yet laid out". Whether
        // WaitForResolvedRect resolves this via its immediate check or via a GeometryChangedEvent
        // that fires with a zero rect, IsResolved's width>0/height>0 guard can never be satisfied,
        // so ShowEntryCore's own zero-size guard (DialogManager.cs, the wipe call site) must skip
        // PlayAsync either way — which is the behaviour this test exists to pin, independent of
        // which of WaitForResolvedRect's internal branches produced the zero.
        private static DialogEntry ZeroSizeEntry() => new DialogEntry {
            Flags = DialogEntryFlags.None, // NOT SkipOpenWipe — the wipe must be eligible to play
            Actions = new List<DialogActionBase> {
                new ResizeDialogAction { Width = LayoutLength.Px(0f), Height = LayoutLength.Px(0f) },
            },
        };

        /// <summary>
        /// THE FENCE for the caller side. Drives the real production path
        /// (<c>DialogManager.DisplayEntry</c> → <c>ShowEntryCore</c>) with a dialog area that can
        /// never resolve to a non-zero rect, and proves the wipe's mask is never created at all —
        /// not merely torn down by the time the call returns (which would be true whether the wipe
        /// played and finished, or never played), so the assertion polls FRAME BY FRAME while the
        /// call is still in flight rather than only inspecting the end state.
        /// </summary>
        [UnityTest]
        [Timeout(30000)]
        public IEnumerator ShowEntryCore_SkipsTheWipe_WhenThePanelNeverResolves_AndTheDialogStillAppears() =>
            UniTask.ToCoroutine(async () => {
                var host = new GameObject("DialogOverlayUnderTest", typeof(UIDocument));
                try {
                    DialogManager manager = host.AddComponent<DialogManager>();
                    manager.SetActivePalette(new Color[256]); // palette content is not under test

                    VisualElement root = host.GetComponent<UIDocument>().rootVisualElement;
                    UniTask task = manager.DisplayEntry(ZeroSizeEntry());

                    bool maskEverAppeared = false;
                    int guardFrames = 0;
                    while (task.Status == UniTaskStatus.Pending && guardFrames < 300) {
                        VisualElement stage = CanonicalStage.Find(root);
                        if (stage != null && stage.Q<VisualElement>("DialogOpenWipeMask") != null) {
                            maskEverAppeared = true;
                        }
                        await UniTask.Yield();
                        guardFrames++;
                    }
                    Assert.Less(guardFrames, 300, "DisplayEntry never completed — the timeout guard failed to bound the wait");

                    await task; // already complete; re-awaiting surfaces any exception

                    Assert.IsFalse(maskEverAppeared,
                        "the open-wipe mask must never be created when the panel can't resolve a "
                        + "non-zero rect — ShowEntryCore's zero-size guard exists precisely to skip "
                        + "DialogOpenWipe.PlayAsync in this case");

                    VisualElement finalStage = CanonicalStage.Find(root);
                    Assert.IsNotNull(finalStage, "DialogManager must still have built a stage");
                    VisualElement panel = finalStage.Q<VisualElement>("BakDialogPanel");
                    Assert.IsNotNull(panel, "the dialog panel must still appear even though its "
                        + "resolved rect never became non-zero — a skipped wipe must not mean a "
                        + "skipped dialog");
                    Assert.AreSame(finalStage, panel.parent,
                        "the panel must be attached directly to the stage, not left orphaned "
                        + "inside a mask that was never cleaned up");
                } finally {
                    Object.DestroyImmediate(host);
                }
            });
    }
}

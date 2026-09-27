namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Fences <see cref="DialogOpenWipe.PlayAsync"/>'s cancellation path END TO END — the gap the
    /// task report itself flagged: <c>DialogOpenWipeRestoreTests</c> calls
    /// <see cref="DialogOpenWipe.RestorePanel"/> directly, which proves <c>RestorePanel</c>'s own
    /// logic but nothing about <c>PlayAsync</c>'s mask lifecycle or its <c>finally</c> wiring. This
    /// fixture drives the real <c>PlayAsync</c> loop, lets it genuinely start animating (mask
    /// created, panel reparented into it, at least one step taken), cancels mid-flight, and only
    /// then inspects the aftermath.
    ///
    /// <para>The percent area's values (10/20/60/30) are the same distinctive fixture
    /// <c>DialogOpenWipeRestoreTests</c> uses — chosen there specifically because no default,
    /// fallback or px-authored area could produce them by accident.</para>
    /// </summary>
    public class DialogOpenWipeCancellationTests {
        private static LayoutHint PercentArea() {
            var hint = new LayoutHint();
            hint.Left = LayoutLength.Percent(10f);
            hint.Top = LayoutLength.Percent(20f);
            hint.Width = LayoutLength.Percent(60f);
            hint.Height = LayoutLength.Percent(30f);
            return hint;
        }

        /// <summary>
        /// THE FENCE. Starts the real wipe, waits until it has genuinely entered its animation loop
        /// (the mask exists and the panel has been reparented inside it — i.e. this is not just
        /// cancelling before anything happened), cancels, and asserts the aftermath: the panel is
        /// back on the stage wearing its ORIGINAL PERCENT hint (not a pixel snapshot of wherever the
        /// mask had animated to), and the mask is gone from the hierarchy.
        ///
        /// <para>A long duration (10s of unscaled time) is deliberate: it guarantees the loop is
        /// still running — nowhere near its own completion — when the test cancels after a couple
        /// of yielded frames, regardless of how fast or slow those frames render in this environment.
        /// </para>
        /// </summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator CancellingMidAnimation_RestoresThePercentAreaThroughTheRealPath_AndTearsDownTheMask() =>
            UniTask.ToCoroutine(async () => {
                var stage = new VisualElement { name = "Stage" };
                var panel = new VisualElement { name = "Panel" };
                stage.Add(panel);
                LayoutHint area = PercentArea();
                var resolvedRect = new Rect(65f, 66f, 1470f, 606f); // an arbitrary real-looking rect; only `area` matters for restore
                var cts = new CancellationTokenSource();

                UniTask playTask = DialogOpenWipe.PlayAsync(stage, panel, area, resolvedRect,
                    duration: 10f, cts.Token);

                // Wait until the animation has genuinely started: mask created and the panel moved
                // inside it. Bounded so a regression that never starts the loop fails loudly
                // instead of spinning for the whole [Timeout] budget.
                VisualElement mask = null;
                int guardFrames = 0;
                while (guardFrames < 300) {
                    mask = stage.Q<VisualElement>("DialogOpenWipeMask");
                    if (mask != null && panel.parent == mask) {
                        break;
                    }
                    await UniTask.Yield();
                    guardFrames++;
                }
                Assert.IsNotNull(mask, "the wipe never created its mask — cannot be mid-animation");
                Assert.AreSame(mask, panel.parent,
                    "the panel must be reparented into the mask while the wipe is running — this "
                    + "confirms we are genuinely mid-animation, not cancelling before anything started");

                cts.Cancel();
                await playTask; // PlayAsync never throws on cancellation (checked via the token's
                                 // IsCancellationRequested, not passed to UniTask.Yield); this proves that.

                Assert.IsNull(stage.Q<VisualElement>("DialogOpenWipeMask"),
                    "the mask must be torn down after cancellation reaches PlayAsync's finally block");
                Assert.AreSame(stage, panel.parent,
                    "the panel must land back on the stage, not be left inside the removed mask");

                Assert.AreEqual(LengthUnit.Percent, panel.style.left.value.unit,
                    "restoring after cancellation must re-apply the authored LayoutHint (percent), "
                    + "not write back the resolved pixel rect the mask was animating through — a "
                    + "pixel write here would freeze a percentage-authored dialog at whatever size "
                    + "the window happened to be when the wipe was interrupted");
                Assert.AreEqual(10f, panel.style.left.value.value, 0.001f);
                Assert.AreEqual(LengthUnit.Percent, panel.style.top.value.unit);
                Assert.AreEqual(20f, panel.style.top.value.value, 0.001f);
                Assert.AreEqual(LengthUnit.Percent, panel.style.width.value.unit);
                Assert.AreEqual(60f, panel.style.width.value.value, 0.001f);
                Assert.AreEqual(LengthUnit.Percent, panel.style.height.value.unit);
                Assert.AreEqual(30f, panel.style.height.value.value, 0.001f);
            });
    }
}

namespace BakAgain.Tests.PlayMode.UI {
    using System.Threading;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Fences <see cref="DialogOpenWipe.RestorePanel"/>: the wipe's teardown must re-apply the
    /// authored <see cref="LayoutHint"/> through <see cref="BakAgain.UI.Layout.LayoutApplier"/>,
    /// never write the resolved pixel rect back. Writing pixels back would freeze a
    /// percentage-authored dialog at whatever size the window happened to be when the wipe ran —
    /// silently undoing the reflow <c>Frame.Fit = Fill</c> exists to enable.
    /// </summary>
    public class DialogOpenWipeRestoreTests {
        // A percentage-authored area — the exact case Fill exists to serve, and the case
        // that used to lose the wipe entirely.
        private static LayoutHint PercentArea() {
            var hint = new LayoutHint();
            hint.Left = LayoutLength.Percent(10f);
            hint.Top = LayoutLength.Percent(20f);
            hint.Width = LayoutLength.Percent(60f);
            hint.Height = LayoutLength.Percent(30f);
            return hint;
        }

        [Test]
        public void Restore_ReAppliesTheAuthoredHint_AndDoesNotWritePixelsBack() {
            var stage = new VisualElement();
            var panel = new VisualElement();
            stage.Add(panel);
            LayoutHint area = PercentArea();

            // Drive the wipe to completion synchronously via its teardown path.
            DialogOpenWipe.RestorePanel(stage, panel, area);

            Assert.AreEqual(LengthUnit.Percent, panel.style.left.value.unit,
                "Restoring with px would clobber a percentage-authored dialog — the exact "
                + "layout this phase exists to enable.");
            Assert.AreEqual(10f, panel.style.left.value.value, 0.001f);
            Assert.AreEqual(LengthUnit.Percent, panel.style.width.value.unit);
            Assert.AreEqual(60f, panel.style.width.value.value, 0.001f);
        }

        [Test]
        public void Restore_KeepsALayerThatWasABOVEThePanel_ABOVEIt() {
            // *** THIS IS A PICKING BUG THAT LOOKS LIKE NOTHING. *** The ask-about topic grid is
            // added to the stage AFTER the panel, on purpose — "or the panel paints over the
            // buttons". Re-appending the panel on teardown put it back on top, and because the
            // panel's own background is transparent over the parchment backdrop the topics stayed
            // perfectly readable and simply stopped taking clicks. Keyboard activation kept working
            // (it goes through the input stack, not through picking), so the symptom was "the
            // buttons are there and the mouse does nothing" with no visual clue at all.
            var stage = new VisualElement();
            var backdrop = new VisualElement { name = "backdrop" };
            var panel = new VisualElement { name = "panel" };
            var grid = new VisualElement { name = "grid" };
            stage.Add(backdrop);
            stage.Add(panel);
            stage.Add(grid);

            DialogOpenWipe.RestorePanel(stage, panel, LayoutHint.PxRect(0, 0, 10, 10),
                stage.IndexOf(panel));

            Assert.Less(stage.IndexOf(panel), stage.IndexOf(grid),
                "a layer the caller put above the panel must still be above it, or it stops "
                + "receiving pointer events while still painting");
            Assert.Less(stage.IndexOf(backdrop), stage.IndexOf(panel),
                "and one below must stay below");
        }

        [Test]
        public void Restore_WithoutAnIndex_StillAppends_SoExistingCallersAreUnchanged() {
            var stage = new VisualElement();
            var panel = new VisualElement();
            var other = new VisualElement();
            stage.Add(panel);
            stage.Add(other);

            DialogOpenWipe.RestorePanel(stage, panel, LayoutHint.PxRect(0, 0, 10, 10));

            Assert.AreEqual(stage.childCount - 1, stage.IndexOf(panel));
        }

        [Test]
        public void Restore_ReAppliesAPxHintAsPx_SoShippedDialogsAreUnchanged() {
            var stage = new VisualElement();
            var panel = new VisualElement();
            stage.Add(panel);
            LayoutHint area = LayoutHint.PxRect(65, 66, 1470, 606);

            DialogOpenWipe.RestorePanel(stage, panel, area);

            Assert.AreEqual(LengthUnit.Pixel, panel.style.left.value.unit);
            Assert.AreEqual(65f, panel.style.left.value.value, 0.001f);
            Assert.AreEqual(1470f, panel.style.width.value.value, 0.001f);
        }

        // ---- the panel must not scale with the mask ------------------------------------------

        /// <summary>
        /// The fence for TASK-64 item 2: while the mask grows, the panel holds its resolved size.
        /// </summary>
        /// <remarks>
        /// The panel is reparented INTO the growing mask, so a percentage width re-resolves against
        /// the mask every frame unless it is pinned — the box grew with the reveal and re-wrapped
        /// its text for the whole animation. The original reveals the finished box un-scaled.
        /// </remarks>
        [UnityTest]
        public System.Collections.IEnumerator Wipe_PinsThePanelSize_SoItDoesNotGrowWithTheMask() {
            var stage = new VisualElement { style = { width = 1000f, height = 800f } };
            var panel = new VisualElement();
            stage.Add(panel);
            LayoutHint area = PercentArea();
            BakAgain.UI.Layout.LayoutApplier.Apply(panel, area);
            var resolved = new Rect(100f, 160f, 600f, 240f);

            UniTask play = DialogOpenWipe.PlayAsync(
                stage, panel, area, resolved, duration: 0.05f, CancellationToken.None);

            // Mid-wipe: the panel carries a PX size equal to the resolved rect, not a percentage
            // that would track the mask. Checked BEFORE yielding a frame (TASK-671): PlayAsync pins
            // the panel synchronously, before its first await, and the wipe advances by
            // Time.unscaledDeltaTime, so any frame at all may finish it. A CI runner's 3 s frame
            // ended even a 1 s wipe before a one-frame-later check could see the pin.
            Assert.AreEqual(LengthUnit.Pixel, panel.style.width.value.unit,
                "the panel must be pinned in px while it lives inside the growing mask");
            Assert.AreEqual(resolved.width, panel.style.width.value.value, 0.001f);
            Assert.AreEqual(resolved.height, panel.style.height.value.value, 0.001f);

            while (!play.Status.IsCompleted()) {
                yield return null;
            }

            // And the pin is undone: the authored percentage decides again, or the panel would be
            // frozen at whatever size the window happened to be when the wipe ran.
            Assert.AreEqual(LengthUnit.Percent, panel.style.width.value.unit,
                "the px pin must not survive the wipe");
            Assert.AreEqual(60f, panel.style.width.value.value, 0.001f);
        }

        /// <summary>
        /// The other door onto the same failure: a hint that sizes itself with percentage INSETS
        /// rather than an explicit width. LayoutApplier.Apply only writes lengths the hint states,
        /// so without an explicit clear the wipe's px width would survive and freeze the panel.
        /// </summary>
        [Test]
        public void Restore_ClearsThePin_EvenWhenTheHintStatesNoWidth() {
            var stage = new VisualElement();
            var panel = new VisualElement();
            stage.Add(panel);

            // What PlayAsync leaves on the panel mid-wipe.
            panel.style.width = 600f;
            panel.style.height = 240f;

            var insetOnly = new LayoutHint {
                Left = LayoutLength.Percent(10f),
                Top = LayoutLength.Percent(20f),
                Right = LayoutLength.Percent(10f),
                Bottom = LayoutLength.Percent(20f),
            };

            DialogOpenWipe.RestorePanel(stage, panel, insetOnly);

            Assert.AreEqual(StyleKeyword.Null, panel.style.width.keyword,
                "a px width left behind by the wipe freezes an inset-sized dialog");
            Assert.AreEqual(StyleKeyword.Null, panel.style.height.keyword);
            Assert.AreEqual(LengthUnit.Percent, panel.style.right.value.unit);
        }
    }
}

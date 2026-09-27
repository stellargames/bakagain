namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI;
    using BakAgain.UI.Layout;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="DialogManager.ResolveArea"/> is the C# port of <c>dialog_getDialogArea</c>
    /// (0x485bc), where an entry's <c>ResizeDialog</c> action is used <i>in place of</i> the
    /// style's own area rather than merged into it. These tests pin that replacement — including
    /// the deliberate, faithful consequence that an override author's anchor is thrown away by
    /// any entry carrying a resize.
    /// </summary>
    public class DialogAreaResolutionTests {
        // Asymmetric, non-round, and sharing no number with the style area below, so a resolver
        // that returned the wrong one of the two cannot coincidentally satisfy either assertion.
        private static ResizeDialogAction Resize() =>
            new ResizeDialogAction {
                Left = LayoutLength.Px(315f), Top = LayoutLength.Px(738f),
                Width = LayoutLength.Px(1129f), Height = LayoutLength.Px(402f)
            };

        // A percent-valued resize — the shape an override author would actually write. Distinctive,
        // asymmetric, no shared numbers with Resize() or OverriddenStyleArea() so a resolver that
        // mixed the two up cannot coincidentally pass.
        private static ResizeDialogAction PercentResize() =>
            new ResizeDialogAction {
                Left = LayoutLength.Percent(3.5f), Top = LayoutLength.Percent(88.25f),
                Width = LayoutLength.Percent(46.75f), Height = LayoutLength.Percent(17f)
            };

        // A style area an override author might plausibly have authored: percentages AND a
        // non-default anchor, i.e. everything a merge would have to reconcile.
        private static LayoutHint OverriddenStyleArea() =>
            new LayoutHint {
                Anchor = LayoutAnchor.Center,
                Left = LayoutLength.Percent(7.5f),
                Top = LayoutLength.Percent(11.25f),
                Width = LayoutLength.Percent(63.75f),
                Height = LayoutLength.Percent(29.5f),
            };

        /// <summary>
        /// <paramref name="style"/> here stands in for a <c>DialogStyleTable</c> row — a shared
        /// instance (the table is a cached resource) every dialog of that style resolves to. The caller stores the
        /// returned area as its own live state (<c>DialogManager._activeArea</c>), so the
        /// resolver must hand back a copy: if it returned the style's own <see cref="LayoutHint"/>
        /// instance, a later mutation of that live state (e.g. clamping/nudging the placed panel)
        /// would permanently rewrite the shared row, and every subsequent dialog of that style
        /// would render at the mutated position.
        /// </summary>
        [Test]
        public void ResolveArea_WithoutAResize_ReturnsACopyThatCannotMutateTheStyle() {
            var entry = new DialogEntry();
            LayoutHint style = OverriddenStyleArea();

            LayoutHint area = DialogManager.ResolveArea(entry, style);

            Assert.AreNotSame(style, area, "must be a copy — the caller treats the result as its own mutable state");
            Assert.AreEqual(style.Anchor, area.Anchor);
            Assert.AreEqual(style.Left, area.Left);
            Assert.AreEqual(style.Top, area.Top);
            Assert.AreEqual(style.Width, area.Width);
            Assert.AreEqual(style.Height, area.Height);

            // The contract that matters: mutating what the caller got back must not reach the
            // style it came from.
            area.Left = LayoutLength.Px(999f);
            Assert.AreEqual(LayoutLength.Percent(7.5f), style.Left,
                "mutating the resolved area must not reach the shared style");
        }

        [Test]
        public void ResolveArea_WithAResize_UsesTheResizesPxInsets() {
            var entry = new DialogEntry();
            entry.Actions.Add(Resize());

            LayoutHint area = DialogManager.ResolveArea(entry, OverriddenStyleArea());

            // Positions AND units: a bare-number assertion would pass just as happily against
            // "315%", which is the defect class this project has repeatedly shipped.
            Assert.AreEqual(LayoutLength.Px(315f), area.Left);
            Assert.AreEqual(LayoutLength.Px(738f), area.Top);
            Assert.AreEqual(LayoutLength.Px(1129f), area.Width);
            Assert.AreEqual(LayoutLength.Px(402f), area.Height);
        }

        /// <summary>
        /// <b>Fences the 2026-08-05 decision.</b> A percent-valued resize is the whole point of
        /// moving <see cref="ResizeDialogAction"/> off bare ints: an override author states the
        /// panel's area as a percentage of the canvas, and it must reach <see cref="DialogManager.ResolveArea"/>'s
        /// output as percent — not silently coerced to px along the way. If <c>ToLayoutHint</c> (or
        /// anything upstream) ever hardcodes <see cref="LayoutLength.Px(float)"/>, this goes red
        /// while <see cref="ResolveArea_WithAResize_UsesTheResizesPxInsets"/> stays green.
        /// </summary>
        [Test]
        public void ResolveArea_WithAPercentResize_ProducesAPercentValuedHint() {
            var entry = new DialogEntry();
            entry.Actions.Add(PercentResize());

            LayoutHint area = DialogManager.ResolveArea(entry, OverriddenStyleArea());

            Assert.AreEqual(LayoutLength.Percent(3.5f), area.Left);
            Assert.AreEqual(LayoutLength.Percent(88.25f), area.Top);
            Assert.AreEqual(LayoutLength.Percent(46.75f), area.Width);
            Assert.AreEqual(LayoutLength.Percent(17f), area.Height);
            Assert.AreEqual(LayoutLengthUnit.Percent, area.Left.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, area.Top.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, area.Width.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, area.Height.Unit);
        }

        /// <summary>
        /// The load-bearing case. A merge (or a partial replacement of only the four insets) would
        /// leave the style's Center anchor in place, and the resize's px insets would then be
        /// measured from a centred origin — the panel would land in the wrong half of the screen.
        /// The original replaced the whole rect, so the port replaces the whole hint.
        /// </summary>
        [Test]
        public void ResolveArea_WithAResize_DiscardsTheStylesAnchorAndPercentages() {
            var entry = new DialogEntry();
            entry.Actions.Add(Resize());
            LayoutHint style = OverriddenStyleArea();

            LayoutHint area = DialogManager.ResolveArea(entry, style);

            Assert.AreNotSame(style, area, "the resize must produce its own hint, not mutate the style's");
            Assert.AreEqual(LayoutAnchor.TopLeft, area.Anchor, "the style's Center anchor must not survive a resize");
            Assert.AreEqual(LayoutPosition.Absolute, area.Position);
            Assert.AreEqual(LayoutLengthUnit.Px, area.Left.Unit, "no percentage from the style may leak through");
            Assert.AreEqual(LayoutLengthUnit.Px, area.Top.Unit);
            Assert.AreEqual(LayoutLengthUnit.Px, area.Width.Unit);
            Assert.AreEqual(LayoutLengthUnit.Px, area.Height.Unit);
            // The style itself must be untouched — it is the shared table row every later dialog
            // of that style will render with.
            Assert.AreEqual(LayoutAnchor.Center, style.Anchor);
            Assert.AreEqual(LayoutLength.Percent(7.5f), style.Left);
        }

        /// <summary>
        /// End-to-end through the one translator: a resolved area must reach UI Toolkit as the
        /// same numbers AND the same units, on the same edges. Asserting the resolved style rather
        /// than the hint is what catches a translator that dropped an edge or flipped a unit.
        /// </summary>
        [Test]
        public void ResolvedArea_AppliesToAnElementAsAbsolutePxOnTheNearEdges() {
            var entry = new DialogEntry();
            entry.Actions.Add(Resize());
            var element = new VisualElement { name = "dialog_panel" };

            LayoutApplier.Apply(element, DialogManager.ResolveArea(entry, OverriddenStyleArea()));

            Assert.AreEqual(Position.Absolute, element.style.position.value);
            Assert.AreEqual(new Length(315f, LengthUnit.Pixel), element.style.left.value);
            Assert.AreEqual(new Length(738f, LengthUnit.Pixel), element.style.top.value);
            Assert.AreEqual(new Length(1129f, LengthUnit.Pixel), element.style.width.value);
            Assert.AreEqual(new Length(402f, LengthUnit.Pixel), element.style.height.value);
            // Top-left anchoring leaves the far edges unpinned; pinning them as well would fight
            // the explicit width/height.
            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);
        }

    }
}

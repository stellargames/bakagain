namespace BakAgain.Tests.Editor.UI {
    using System.Text.RegularExpressions;
    using BakAgain.UI.Layout;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public class LayoutApplierTests {
        [Test]
        public void ToStyleLength_PxBecomesPixelUnit() {
            StyleLength style = LayoutApplier.ToStyleLength(LayoutLength.Px(200f));
            Assert.AreEqual(LengthUnit.Pixel, style.value.unit);
            Assert.AreEqual(200f, style.value.value);
        }

        [Test]
        public void ToStyleLength_PercentBecomesPercentUnit() {
            StyleLength style = LayoutApplier.ToStyleLength(LayoutLength.Percent(12.5f));
            Assert.AreEqual(LengthUnit.Percent, style.value.unit);
            Assert.AreEqual(12.5f, style.value.value);
        }

        [Test]
        public void ToStyleLength_AutoBecomesStyleKeywordAuto() {
            StyleLength style = LayoutApplier.ToStyleLength(LayoutLength.Auto);
            Assert.AreEqual(StyleKeyword.Auto, style.keyword);
        }

        [Test]
        public void Apply_SetsWidthAndHeightFromTheHint() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Width = LayoutLength.Px(1400f),
                Height = LayoutLength.Percent(60f)
            });

            Assert.AreEqual(1400f, element.style.width.value.value);
            Assert.AreEqual(LengthUnit.Pixel, element.style.width.value.unit);
            Assert.AreEqual(60f, element.style.height.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.height.value.unit);
        }

        [Test]
        public void Apply_WithAutoSize_LeavesExistingSizeAlone() {
            // Auto means "no opinion", not "reset to auto" — Apply must be safely layerable
            // onto an element another system already sized (CreditsView, the REQ loader).
            var element = new VisualElement();
            element.style.width = 640f;
            LayoutApplier.Apply(element, new LayoutHint());
            Assert.AreEqual(640f, element.style.width.value.value);
        }

        [Test]
        public void Apply_WithoutFlow_LeavesTheElementAbsolute() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint());
            Assert.AreEqual(Position.Absolute, element.style.position.value);
        }

        // Position and Flow are independent: LayoutHint.Position defaults to Absolute, so a
        // Flow with no explicit Position must leave the element absolutely positioned — it is
        // still a flex container for its children, but that says nothing about how the element
        // itself sits in ITS parent. (Before the Position/Flow split, Flow != null forced
        // Position.Relative — see Apply_AbsolutePositionWithInsetsAndFlow_IsBothPositioned...
        // below for the case this change exists to make expressible.)
        [Test]
        public void Apply_WithFlowAndDefaultPosition_StaysAbsoluteButBecomesAFlexContainer() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Flow = new LayoutFlow {
                    Direction = LayoutFlowDirection.Row,
                    Wrap = true,
                    Justify = LayoutFlowJustify.Center,
                    Align = LayoutFlowAlign.Stretch
                }
            });

            Assert.AreEqual(Position.Absolute, element.style.position.value,
                "Position defaults to Absolute — Flow alone must not force Relative any more");
            Assert.AreEqual(FlexDirection.Row, element.style.flexDirection.value);
            Assert.AreEqual(Wrap.Wrap, element.style.flexWrap.value);
            Assert.AreEqual(Justify.Center, element.style.justifyContent.value);
            Assert.AreEqual(Align.Stretch, element.style.alignItems.value);
        }

        [Test]
        public void Apply_PositionInFlowWithFlow_IsRelativeAndAFlexContainer() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Position = LayoutPosition.InFlow,
                Flow = new LayoutFlow {
                    Direction = LayoutFlowDirection.Row,
                    Wrap = true,
                    Justify = LayoutFlowJustify.Center,
                    Align = LayoutFlowAlign.Stretch
                }
            });

            Assert.AreEqual(Position.Relative, element.style.position.value);
            Assert.AreEqual(FlexDirection.Row, element.style.flexDirection.value);
            Assert.AreEqual(Wrap.Wrap, element.style.flexWrap.value);
            Assert.AreEqual(Justify.Center, element.style.justifyContent.value);
            Assert.AreEqual(Align.Stretch, element.style.alignItems.value);
        }

        [Test]
        public void Apply_PositionInFlowWithoutFlow_IsJustRelative() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Position = LayoutPosition.InFlow });

            Assert.AreEqual(Position.Relative, element.style.position.value);
        }

        // THE test that proves the fix: before the Position/Flow split, this combination was
        // inexpressible (Apply's if/else forced a choice between "absolute with insets" and
        // "relative flex container"). The credits row needs both at once — pinned at fixed
        // insets in the scroller, AND a flex row for its role/leader/name children — so
        // CreditsView had to call Apply for the flex props, then hand-write left/right and
        // restore Position.Absolute itself. This asserts the single Apply call now does the
        // whole job: absolutely positioned at the given insets AND a flex container.
        [Test]
        public void Apply_AbsolutePositionWithInsetsAndFlow_IsBothPositionedAndAFlexContainer() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Position = LayoutPosition.Absolute,
                Left = LayoutLength.Px(210f),
                Right = LayoutLength.Px(215f),
                Height = LayoutLength.Px(66f),
                Flow = new LayoutFlow {
                    Direction = LayoutFlowDirection.Row,
                    Wrap = false,
                    Justify = LayoutFlowJustify.SpaceBetween,
                    Align = LayoutFlowAlign.Start
                }
            });

            // Absolutely positioned at the given insets...
            Assert.AreEqual(Position.Absolute, element.style.position.value);
            Assert.AreEqual(210f, element.style.left.value.value);
            Assert.AreEqual(215f, element.style.right.value.value);
            Assert.AreEqual(66f, element.style.height.value.value);

            // ...AND a flex container for its own children.
            Assert.AreEqual(FlexDirection.Row, element.style.flexDirection.value);
            Assert.AreEqual(Wrap.NoWrap, element.style.flexWrap.value);
            Assert.AreEqual(Justify.SpaceBetween, element.style.justifyContent.value);
            Assert.AreEqual(Align.FlexStart, element.style.alignItems.value);
        }

        [Test]
        public void Apply_PositionInFlowWithExplicitInsets_DoesNotWriteInsetsAndWarns() {
            var element = new VisualElement();
            LogAssert.Expect(LogType.Warning, new Regex("InFlow.*ignoring|ignoring.*Anchor", RegexOptions.IgnoreCase));

            LayoutApplier.Apply(element, new LayoutHint {
                Position = LayoutPosition.InFlow,
                Left = LayoutLength.Px(210f),
                Right = LayoutLength.Px(215f)
            });

            Assert.AreEqual(Position.Relative, element.style.position.value);
            Assert.AreEqual(StyleKeyword.Null, element.style.left.keyword,
                "InFlow must not write insets — the parent's layout places this element");
            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
        }

        [Test]
        public void Apply_PositionInFlowWithNonDefaultAnchor_Warns() {
            var element = new VisualElement();
            LogAssert.Expect(LogType.Warning, new Regex("InFlow.*ignoring|ignoring.*Anchor", RegexOptions.IgnoreCase));

            LayoutApplier.Apply(element, new LayoutHint {
                Position = LayoutPosition.InFlow,
                Anchor = LayoutAnchor.BottomRight
            });

            Assert.AreEqual(Position.Relative, element.style.position.value);
            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword,
                "InFlow must not apply the anchor either");
        }

        [Test]
        public void Apply_PositionInFlowWithNoInsetsOrAnchor_DoesNotWarn() {
            var element = new VisualElement();
            // Default Anchor (TopLeft) + no insets is the ordinary, non-contradictory case for
            // an InFlow element — must not warn.
            LayoutApplier.Apply(element, new LayoutHint { Position = LayoutPosition.InFlow });
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Apply_WithAspectRatio_SetsTheNativeAspectRatioStyle() {
            // UI Toolkit's style.aspectRatio (verified against the live Unity 6 API, not
            // assumed) is a real translation target, same as Width/Height.
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Width = LayoutLength.Percent(50f),
                AspectRatio = new LayoutAspectRatio(10f, 9f)
            });

            Assert.AreEqual(StyleKeyword.Undefined, element.style.aspectRatio.keyword);
            Assert.AreEqual(10f / 9f, element.style.aspectRatio.value.value, 0.0001f);
        }

        [Test]
        public void Apply_WithAspectRatioAndBothExplicitSizes_IgnoresAspectRatioAndWarns() {
            // Width and Height both explicit over-constrains the element — the spec's error
            // table says ignore the aspect and warn once, rather than let one axis fight UI
            // Toolkit's own aspect-ratio solver.
            var element = new VisualElement();
            LogAssert.Expect(LogType.Warning, new Regex("AspectRatio.*ignoring|ignoring.*AspectRatio", RegexOptions.IgnoreCase));

            LayoutApplier.Apply(element, new LayoutHint {
                Width = LayoutLength.Px(1400f),
                Height = LayoutLength.Percent(60f),
                AspectRatio = new LayoutAspectRatio(10f, 9f)
            });

            Assert.AreEqual(StyleKeyword.Null, element.style.aspectRatio.keyword);
        }

        [Test]
        public void Apply_AnchorBottomRight_PinsToTheOppositeEdges() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.BottomRight });

            // Pinned edges must be EXPLICITLY set, not merely reading 0 because the element is
            // fresh — otherwise this test passes against a no-op ApplyAnchor.
            Assert.AreNotEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreNotEqual(StyleKeyword.Null, element.style.bottom.keyword);
            Assert.AreEqual(0f, element.style.right.value.value);
            Assert.AreEqual(0f, element.style.bottom.value.value);

            // Unpinned edges must be left unset so a position set elsewhere still wins.
            Assert.AreEqual(StyleKeyword.Null, element.style.left.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.top.keyword);
        }

        [Test]
        public void Apply_AnchorTopLeft_PinsTheOppositeEdgesFromBottomRight() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.TopLeft });

            Assert.AreNotEqual(StyleKeyword.Null, element.style.left.keyword);
            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);
        }

        // Decorrelates the TopLeft/BottomRight diagonal pair above: both of those have
        // left==top and right==bottom, so a formula that transposed left/top or right/bottom
        // would still pass both tests. TopRight (left/top swapped relative to TopLeft) and
        // BottomLeft (right/bottom swapped relative to BottomRight) catch that.
        [Test]
        public void Apply_AnchorTopRight_PinsTopAndRightOnly() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.TopRight });

            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword);
            Assert.AreNotEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(0f, element.style.top.value.value);
            Assert.AreEqual(0f, element.style.right.value.value);
            Assert.AreEqual(StyleKeyword.Null, element.style.left.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);
        }

        [Test]
        public void Apply_AnchorBottomLeft_PinsBottomAndLeftOnly() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.BottomLeft });

            Assert.AreNotEqual(StyleKeyword.Null, element.style.bottom.keyword);
            Assert.AreNotEqual(StyleKeyword.Null, element.style.left.keyword);
            Assert.AreEqual(0f, element.style.bottom.value.value);
            Assert.AreEqual(0f, element.style.left.value.value);
            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.top.keyword);
        }

        // FINDING 2 regression coverage: Center/TopCenter/MiddleLeft were previously silently
        // inert on their centred axis/axes (ApplyAnchor only ever wrote 0f to pinned edges, and
        // UI Toolkit's absolute positioning has no implicit centering). The fix uses a 50% inset
        // plus a -50% translate on the centred axis (verified live against Unity 6's
        // IStyle.translate/StyleTranslate API, which accepts percentage Length values).
        [Test]
        public void Apply_AnchorCenter_CentersBothAxesViaInsetAndTranslate() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.Center });

            Assert.AreNotEqual(StyleKeyword.Null, element.style.left.keyword, "Center must set left=50%");
            Assert.AreEqual(50f, element.style.left.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.left.value.unit);

            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword, "Center must set top=50%");
            Assert.AreEqual(50f, element.style.top.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.top.value.unit);

            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);

            Assert.AreNotEqual(StyleKeyword.Null, element.style.translate.keyword,
                "Center must set a translate to pull the element back by half its own size");
            Assert.AreEqual(-50f, element.style.translate.value.x.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.translate.value.x.unit);
            Assert.AreEqual(-50f, element.style.translate.value.y.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.translate.value.y.unit);
        }

        [Test]
        public void Apply_AnchorTopCenter_PinsTopAndCentersHorizontally() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.TopCenter });

            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword);
            Assert.AreEqual(0f, element.style.top.value.value, "TopCenter pins top, it does not centre vertically");

            Assert.AreNotEqual(StyleKeyword.Null, element.style.left.keyword, "TopCenter must centre horizontally");
            Assert.AreEqual(50f, element.style.left.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.left.value.unit);

            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);

            Assert.AreNotEqual(StyleKeyword.Null, element.style.translate.keyword);
            Assert.AreEqual(-50f, element.style.translate.value.x.value);
            Assert.AreEqual(0f, element.style.translate.value.y.value, "vertical translate stays 0 — top is pinned, not centred");
        }

        [Test]
        public void Apply_AnchorMiddleLeft_PinsLeftAndCentersVertically() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Anchor = LayoutAnchor.MiddleLeft });

            Assert.AreNotEqual(StyleKeyword.Null, element.style.left.keyword);
            Assert.AreEqual(0f, element.style.left.value.value, "MiddleLeft pins left, it does not centre horizontally");

            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword, "MiddleLeft must centre vertically");
            Assert.AreEqual(50f, element.style.top.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.top.value.unit);

            Assert.AreEqual(StyleKeyword.Null, element.style.right.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);

            Assert.AreNotEqual(StyleKeyword.Null, element.style.translate.keyword);
            Assert.AreEqual(0f, element.style.translate.value.x.value, "horizontal translate stays 0 — left is pinned, not centred");
            Assert.AreEqual(-50f, element.style.translate.value.y.value);
        }

        // FINDING 3 regression coverage: default(LayoutAspectRatio) reports HasValue == true on
        // a LayoutHint (it's a non-nullable value type wrapped in Nullable<T>) but its .Ratio
        // throws by design (default bypasses the validating constructor). Apply must not let
        // that exception escape mid-render — it should warn and skip, same as the
        // over-constrained case already does.
        [Test]
        public void Apply_WithDefaultAspectRatio_DoesNotThrow() {
            var element = new VisualElement();
            LogAssert.Expect(LogType.Warning, new Regex("AspectRatio", RegexOptions.IgnoreCase));

            Assert.DoesNotThrow(() => LayoutApplier.Apply(element, new LayoutHint {
                Width = LayoutLength.Percent(50f),
                AspectRatio = default(LayoutAspectRatio)
            }));

            Assert.AreEqual(StyleKeyword.Null, element.style.aspectRatio.keyword);
        }

        [Test]
        public void Apply_ExplicitInsets_AreWrittenToTheMatchingEdges() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Left = LayoutLength.Px(210f),
                Right = LayoutLength.Px(215f),
                Top = LayoutLength.Px(324f)
            });

            Assert.AreEqual(210f, element.style.left.value.value);
            Assert.AreEqual(215f, element.style.right.value.value);
            Assert.AreEqual(324f, element.style.top.value.value);
            Assert.AreEqual(StyleKeyword.Null, element.style.bottom.keyword);
        }

        [Test]
        public void Apply_ExplicitInset_OverridesTheAnchorsImpliedZero() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Anchor = LayoutAnchor.TopLeft,
                Left = LayoutLength.Px(210f)
            });

            // Anchor alone would pin left to 0; the explicit inset must win.
            Assert.AreEqual(210f, element.style.left.value.value);
            // Top is still pinned by the anchor, since Top is Auto.
            Assert.AreEqual(0f, element.style.top.value.value);
            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword);
        }

        [Test]
        public void Apply_PercentInset_ResolvesAsPercent() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint { Left = LayoutLength.Percent(13.125f) });
            Assert.AreEqual(LengthUnit.Percent, element.style.left.value.unit);
            Assert.AreEqual(13.125f, element.style.left.value.value);
        }

        // Design decision (not left as a documented quirk): an explicit inset on an axis
        // overrides the anchor's centering translate on that SAME axis too — the translate is
        // part of how a centering anchor pins the edge, not a separate concern, so leaving it
        // behind would honour the override rule's letter while breaking its intent (a
        // half-centred element). The other axis, if still centred, must be untouched.
        [Test]
        public void Apply_ExplicitLeftInsetWithCenterAnchor_OverridesHorizontalCenteringOnly() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Anchor = LayoutAnchor.Center,
                Left = LayoutLength.Px(210f)
            });

            // The inset overrides left...
            Assert.AreEqual(210f, element.style.left.value.value);
            Assert.AreEqual(LengthUnit.Pixel, element.style.left.value.unit);

            // ...and the horizontal translate component no longer shifts the element — the
            // centering offset on THIS axis was cleared along with the edge it belonged to.
            Assert.AreEqual(0f, element.style.translate.value.x.value);

            // The vertical axis is still fully centred (top=50% + translateY=-50%) since no
            // Top inset was given — only the overridden axis is touched.
            Assert.AreEqual(50f, element.style.top.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.top.value.unit);
            Assert.AreEqual(-50f, element.style.translate.value.y.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.translate.value.y.unit);
        }

        // Mirrors the test above on the other axis. Together the pair proves the override is
        // genuinely per-axis rather than a blanket clear that happens to pass one case: if
        // ApplyInsets cleared both translate components whenever ANY inset was explicit, this
        // test's assertion that horizontal centering survives would fail.
        [Test]
        public void Apply_ExplicitTopInsetWithCenterAnchor_OverridesVerticalCenteringOnly() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Anchor = LayoutAnchor.Center,
                Top = LayoutLength.Px(180f)
            });

            // The inset overrides top...
            Assert.AreEqual(180f, element.style.top.value.value);
            Assert.AreEqual(LengthUnit.Pixel, element.style.top.value.unit);

            // ...and the vertical translate component no longer shifts the element.
            Assert.AreEqual(0f, element.style.translate.value.y.value);

            // The horizontal axis is still fully centred (left=50% + translateX=-50%) since no
            // Left inset was given.
            Assert.AreEqual(50f, element.style.left.value.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.left.value.unit);
            Assert.AreEqual(-50f, element.style.translate.value.x.value);
            Assert.AreEqual(LengthUnit.Percent, element.style.translate.value.x.unit);
        }

        // (d) Partial-centering anchor + an inset on the centred axis. TopCenter centres only
        // horizontally (top is pinned, not centred), so an explicit Left must still clear only
        // the horizontal translate — the vertical axis was never centred to begin with, so
        // there is nothing there to disturb, but this proves the same axis-scoped clearing
        // applies when the anchor itself only centres one axis.
        [Test]
        public void Apply_ExplicitLeftInsetWithTopCenterAnchor_OverridesTheHorizontalCentering() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Anchor = LayoutAnchor.TopCenter,
                Left = LayoutLength.Px(210f)
            });

            // The inset overrides left...
            Assert.AreEqual(210f, element.style.left.value.value);
            Assert.AreEqual(LengthUnit.Pixel, element.style.left.value.unit);

            // ...top is still pinned by the anchor (TopCenter pins top, it does not centre it).
            Assert.AreEqual(0f, element.style.top.value.value);
            Assert.AreNotEqual(StyleKeyword.Null, element.style.top.keyword);

            // Horizontal translate cleared; vertical translate stays 0 (TopCenter never set it).
            Assert.AreEqual(0f, element.style.translate.value.x.value);
            Assert.AreEqual(0f, element.style.translate.value.y.value);
        }

        // (e) Full-centering anchor with BOTH axes explicit. This is the case that proves two
        // sequential ClearTranslateAxis calls COMPOSE rather than one clobbering the other: if
        // the second call re-read a stale/cached translate instead of the one the first call
        // just wrote, the horizontal clear from the first call would be lost when the vertical
        // clear runs (or vice versa, depending on call order).
        [Test]
        public void Apply_ExplicitLeftAndTopInsetsWithCenterAnchor_ClearsBothTranslateComponents() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Anchor = LayoutAnchor.Center,
                Left = LayoutLength.Px(210f),
                Top = LayoutLength.Px(100f)
            });

            Assert.AreEqual(210f, element.style.left.value.value);
            Assert.AreEqual(100f, element.style.top.value.value);

            // BOTH translate components must be cleared — neither ClearTranslateAxis call may
            // have clobbered the other's write.
            Assert.AreEqual(0f, element.style.translate.value.x.value);
            Assert.AreEqual(0f, element.style.translate.value.y.value);
        }

        // Guards ClearTranslateAxis's early-return: a non-centering anchor never wrote a
        // translate (style.translate.keyword stays Null, per ApplyAnchor's else-branch), so an
        // explicit inset must not manufacture one. Without the `keyword != StyleKeyword.Undefined`
        // guard, reading `current.value` off a Null StyleTranslate and writing it back would turn
        // this into an explicit zero translate instead of leaving it Null.
        [Test]
        public void Apply_ExplicitLeftInsetWithNonCenteringAnchor_LeavesTranslateNull() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Anchor = LayoutAnchor.TopLeft,
                Left = LayoutLength.Px(210f)
            });

            Assert.AreEqual(210f, element.style.left.value.value);
            Assert.AreEqual(StyleKeyword.Null, element.style.translate.keyword);
        }

        // Padding is a new, independent field on LayoutHint (like Flow/Grid) — a hint with no
        // Padding at all must not touch style.padding*, exactly as a null Flow/Grid does not
        // touch flex/grid styles.
        [Test]
        public void Apply_WithNullPadding_LeavesPaddingUntouched() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint());
            Assert.AreEqual(StyleKeyword.Null, element.style.paddingLeft.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.paddingTop.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.paddingRight.keyword);
            Assert.AreEqual(StyleKeyword.Null, element.style.paddingBottom.keyword);
        }

        // 143/87 — asymmetric, non-round: nothing an implementation would hardcode. Left and
        // Bottom are explicit; Top and Right are left at LayoutPadding's Auto default and must
        // stay untouched — proves the applier writes only the EXPLICIT sides, not "all four,
        // some zeroed".
        [Test]
        public void Apply_Padding_WritesOnlyTheExplicitSides() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Padding = new LayoutPadding {
                    Left = LayoutLength.Px(143f),
                    Bottom = LayoutLength.Percent(87f)
                }
            });

            Assert.AreEqual(LengthUnit.Pixel, element.style.paddingLeft.value.unit);
            Assert.AreEqual(143f, element.style.paddingLeft.value.value);

            Assert.AreEqual(LengthUnit.Percent, element.style.paddingBottom.value.unit);
            Assert.AreEqual(87f, element.style.paddingBottom.value.value);

            Assert.AreEqual(StyleKeyword.Null, element.style.paddingTop.keyword,
                "Top was left Auto on the LayoutPadding — Apply must not write it");
            Assert.AreEqual(StyleKeyword.Null, element.style.paddingRight.keyword,
                "Right was left Auto on the LayoutPadding — Apply must not write it");
        }

        // Auto means "no opinion", not "reset to auto" — same established semantics as
        // Apply_WithAutoSize_LeavesExistingSizeAlone above, just for padding: Apply must be
        // safely layerable onto an element another system (e.g. USS) already padded.
        [Test]
        public void Apply_PaddingWithAutoSides_LeavesExistingPaddingAlone() {
            var element = new VisualElement();
            element.style.paddingTop = 12f;
            element.style.paddingRight = 9f;

            LayoutApplier.Apply(element, new LayoutHint {
                Padding = new LayoutPadding { Left = LayoutLength.Px(143f) }
            });

            Assert.AreEqual(143f, element.style.paddingLeft.value.value, "Left was explicit — must be written");
            Assert.AreEqual(12f, element.style.paddingTop.value.value, "Top was Auto — the pre-existing value must survive");
            Assert.AreEqual(9f, element.style.paddingRight.value.value, "Right was Auto — the pre-existing value must survive");
        }

        // Review finding: Apply_Padding_WritesOnlyTheExplicitSides only ever sets Left/Bottom, so
        // a copy-paste bug that transposed which style property a branch writes to (e.g. Top's
        // branch writing paddingRight instead of paddingTop) would pass every existing padding
        // test — Top and Right are only ever asserted to stay Auto, never asserted against an
        // explicit value. Four distinct, asymmetric, non-round values (no two sides sharing a
        // number) with units mixed across sides close that gap: each edge must get its OWN
        // side's value AND unit, not just "some value, some unit".
        [Test]
        public void Apply_PaddingWithAllFourSidesExplicit_WritesEachSideToItsOwnEdge() {
            var element = new VisualElement();
            LayoutApplier.Apply(element, new LayoutHint {
                Padding = new LayoutPadding {
                    Left = LayoutLength.Px(251f),
                    Top = LayoutLength.Percent(38.5f),
                    Right = LayoutLength.Px(79f),
                    Bottom = LayoutLength.Percent(164f)
                }
            });

            Assert.AreEqual(LengthUnit.Pixel, element.style.paddingLeft.value.unit);
            Assert.AreEqual(251f, element.style.paddingLeft.value.value);

            Assert.AreEqual(LengthUnit.Percent, element.style.paddingTop.value.unit);
            Assert.AreEqual(38.5f, element.style.paddingTop.value.value);

            Assert.AreEqual(LengthUnit.Pixel, element.style.paddingRight.value.unit);
            Assert.AreEqual(79f, element.style.paddingRight.value.value);

            Assert.AreEqual(LengthUnit.Percent, element.style.paddingBottom.value.unit);
            Assert.AreEqual(164f, element.style.paddingBottom.value.value);
        }

        // CellWidth/CellHeight are deliberately asymmetric, non-round numbers (not 200/180, not
        // even multiples of each other) — round matching values would let a hardcoded
        // implementation reproduce these exact assertions by coincidence, which is precisely what
        // happened the first time this test was written (see the falsification note in the task
        // report: 200/180 fixtures could not distinguish "derived from grid.CellWidth/CellHeight"
        // from "hardcoded to 200/180"). These values make that coincidence effectively impossible.
        [Test]
        public void ApplyGridPlacement_PxCells_PositionsAtExactPixelOffsets() {
            var child = new VisualElement();
            var grid = new LayoutGrid {
                CellWidth = LayoutLength.Px(143f),
                CellHeight = LayoutLength.Px(87f),
                Columns = 3,
                Rows = 2
            };
            var placement = new LayoutGridPlacement { Column = 2, Row = 1 };

            LayoutApplier.ApplyGridPlacement(child, grid, placement);

            Assert.AreEqual(Position.Absolute, child.style.position.value);
            Assert.AreEqual(LengthUnit.Pixel, child.style.left.value.unit);
            Assert.AreEqual(286f, child.style.left.value.value, "2 columns x 143px cell width");
            Assert.AreEqual(LengthUnit.Pixel, child.style.top.value.unit);
            Assert.AreEqual(87f, child.style.top.value.value, "1 row x 87px cell height");
            Assert.AreEqual(143f, child.style.width.value.value, "1-column span x 143px cell width");
            Assert.AreEqual(87f, child.style.height.value.value, "1-row span x 87px cell height");
        }

        [Test]
        public void ApplyGridPlacement_TwoByTwoSpan_DoublesWidthAndHeight() {
            var child = new VisualElement();
            var grid = new LayoutGrid {
                CellWidth = LayoutLength.Px(143f),
                CellHeight = LayoutLength.Px(87f),
                Columns = 3,
                Rows = 2
            };
            var placement = new LayoutGridPlacement { Column = 2, Row = 1, ColumnSpan = 2, RowSpan = 2 };

            LayoutApplier.ApplyGridPlacement(child, grid, placement);

            Assert.AreEqual(286f, child.style.left.value.value, "2 columns x 143px cell width");
            Assert.AreEqual(87f, child.style.top.value.value, "1 row x 87px cell height");
            Assert.AreEqual(286f, child.style.width.value.value, "2-column span x 143px cell width");
            Assert.AreEqual(174f, child.style.height.value.value, "2-row span x 87px cell height");
        }

        // Proves the two axes scale independently: a percent CellWidth must not force CellHeight
        // (or vice versa) into percent, and a px axis must not force the other into px. Only one
        // direction would leave the other axis's independence unasserted, so both are covered.
        [Test]
        public void ApplyGridPlacement_MixedUnits_PercentWidthWithPxHeight_ScalesEachAxisIndependently() {
            var child = new VisualElement();
            var grid = new LayoutGrid {
                CellWidth = LayoutLength.Percent(12f),
                CellHeight = LayoutLength.Px(87f),
                Columns = 8,
                Rows = 4
            };
            var placement = new LayoutGridPlacement { Column = 3, Row = 2 };

            LayoutApplier.ApplyGridPlacement(child, grid, placement);

            Assert.AreEqual(LengthUnit.Percent, child.style.left.value.unit);
            Assert.AreEqual(36f, child.style.left.value.value, "3 columns x 12% cell width");
            Assert.AreEqual(LengthUnit.Percent, child.style.width.value.unit);
            Assert.AreEqual(12f, child.style.width.value.value);

            Assert.AreEqual(LengthUnit.Pixel, child.style.top.value.unit);
            Assert.AreEqual(174f, child.style.top.value.value, "2 rows x 87px cell height");
            Assert.AreEqual(LengthUnit.Pixel, child.style.height.value.unit);
            Assert.AreEqual(87f, child.style.height.value.value);
        }

        [Test]
        public void ApplyGridPlacement_MixedUnits_PxWidthWithPercentHeight_ScalesEachAxisIndependently() {
            var child = new VisualElement();
            var grid = new LayoutGrid {
                CellWidth = LayoutLength.Px(143f),
                CellHeight = LayoutLength.Percent(20f),
                Columns = 3,
                Rows = 5
            };
            var placement = new LayoutGridPlacement { Column = 2, Row = 1 };

            LayoutApplier.ApplyGridPlacement(child, grid, placement);

            Assert.AreEqual(LengthUnit.Pixel, child.style.left.value.unit);
            Assert.AreEqual(286f, child.style.left.value.value, "2 columns x 143px cell width");
            Assert.AreEqual(LengthUnit.Pixel, child.style.width.value.unit);
            Assert.AreEqual(143f, child.style.width.value.value);

            Assert.AreEqual(LengthUnit.Percent, child.style.top.value.unit);
            Assert.AreEqual(20f, child.style.top.value.value, "1 row x 20% cell height");
            Assert.AreEqual(LengthUnit.Percent, child.style.height.value.unit);
            Assert.AreEqual(20f, child.style.height.value.value);
        }

        // THE test that proves the reason this vocabulary exists: a percentage cell size must
        // yield a percentage position AND size, not silently collapse to pixels — otherwise the
        // grid could never reflow when its container resizes.
        [Test]
        public void ApplyGridPlacement_PercentCells_YieldsPercentagePositionAndSize() {
            var child = new VisualElement();
            var grid = new LayoutGrid {
                CellWidth = LayoutLength.Percent(10f),
                CellHeight = LayoutLength.Percent(20f),
                Columns = 10,
                Rows = 5
            };
            var placement = new LayoutGridPlacement { Column = 2, Row = 1 };

            LayoutApplier.ApplyGridPlacement(child, grid, placement);

            Assert.AreEqual(LengthUnit.Percent, child.style.left.value.unit);
            Assert.AreEqual(20f, child.style.left.value.value);
            Assert.AreEqual(LengthUnit.Percent, child.style.top.value.unit);
            Assert.AreEqual(20f, child.style.top.value.value);
            Assert.AreEqual(LengthUnit.Percent, child.style.width.value.unit);
            Assert.AreEqual(10f, child.style.width.value.value);
            Assert.AreEqual(LengthUnit.Percent, child.style.height.value.unit);
            Assert.AreEqual(20f, child.style.height.value.value);
        }

        // LayoutGrid's cell sizes default to Auto, and Newtonsoft creates the object fresh — so
        // `"Grid": {"Columns": 4, "Rows": 4}` in an override arrives here with no cell size. A
        // cell offset is Column x CellWidth, and "intrinsic" is not a length that can be
        // multiplied: writing `auto` into left/top/width/height would collapse every cell of the
        // grid without a word. Refused loudly, and the child is left untouched.
        [Test]
        public void ApplyGridPlacement_AutoCells_RefusesAndLeavesTheChildUntouched() {
            var child = new VisualElement();
            var grid = new LayoutGrid { Columns = 4, Rows = 4 };
            var placement = new LayoutGridPlacement { Column = 2, Row = 1 };

            LogAssert.Expect(LogType.Error, new Regex("cell size is Auto"));
            LayoutApplier.ApplyGridPlacement(child, grid, placement);

            Assert.AreEqual(StyleKeyword.Null, child.style.left.keyword, "left must be left alone");
            Assert.AreEqual(StyleKeyword.Null, child.style.top.keyword, "top must be left alone");
            Assert.AreEqual(StyleKeyword.Null, child.style.width.keyword, "width must be left alone");
            Assert.AreEqual(StyleKeyword.Null, child.style.height.keyword, "height must be left alone");
            Assert.AreEqual(StyleKeyword.Null, child.style.position.keyword,
                "not even the position is written — a refusal changes nothing");
        }

        // Only ONE axis Auto is still unusable: the other axis would be placed and this one would
        // not, which is a half-built grid rather than an honest one.
        [Test]
        public void ApplyGridPlacement_OneAutoAxis_IsRefusedToo() {
            var child = new VisualElement();
            var grid = new LayoutGrid { CellWidth = LayoutLength.Px(40f), Columns = 4, Rows = 4 };

            LogAssert.Expect(LogType.Error, new Regex("cell size is Auto"));
            LayoutApplier.ApplyGridPlacement(child, grid, new LayoutGridPlacement { Column = 1 });

            Assert.AreEqual(StyleKeyword.Null, child.style.left.keyword);
        }

        // ---- LayoutHint.Slice: refused, not ignored -----------------------------------------

        private static LayoutHint SliceHint(int left, int top, int right, int bottom) =>
            new LayoutHint {
                Position = LayoutPosition.Absolute,
                Slice = new NineSlice(left, top, right, bottom),
            };

        [Test]
        public void Slice_WhenAuthored_IsRefusedLoudly() {
            // Nine-slice is in the authoring vocabulary and has no implementation: no extractor
            // writes one and nothing renders one. An author who sets it used to get silence.
            LogAssert.Expect(LogType.Error, new Regex(@"LayoutHint\.Slice cannot be resolved"));

            LayoutApplier.Apply(new VisualElement(), SliceHint(4, 4, 4, 4));
        }

        [Test]
        public void Slice_AtDefault_SaysNothing() {
            // ALL 315 Slice keys across the shipped corpus are (0,0,0,0). This is what makes the
            // refusal safe to add — it fires for an author, never for stock data. An earlier note
            // held the guard back believing the shipped values were non-default.
            LayoutApplier.Apply(new VisualElement(), SliceHint(0, 0, 0, 0));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Slice_WithOneEdgeSet_IsStillRefused() {
            // A slice is refused on ANY non-zero edge, not only when all four are set — a border
            // with one sliced edge is as unimplemented as a fully sliced one.
            LogAssert.Expect(LogType.Error, new Regex(@"LayoutHint\.Slice cannot be resolved"));

            LayoutApplier.Apply(new VisualElement(), SliceHint(0, 0, 3, 0));
        }

        [Test]
        public void Slice_IsRefusedButTheElementIsStillPlaced() {
            // Degrade to "no border artwork", not "no layout": the refusal must not cost the
            // element its size and position.
            LogAssert.Expect(LogType.Error, new Regex(@"LayoutHint\.Slice cannot be resolved"));
            var element = new VisualElement();
            LayoutHint hint = SliceHint(2, 2, 2, 2);
            hint.Left = LayoutLength.Px(40f);
            hint.Width = LayoutLength.Px(120f);

            LayoutApplier.Apply(element, hint);

            Assert.AreEqual(40f, element.style.left.value.value);
            Assert.AreEqual(120f, element.style.width.value.value);
        }

        // ---- LayoutFlow.Gap: refused, not ignored -------------------------------------------

        private static LayoutHint FlowHint(LayoutLength gap) => new LayoutHint {
            Position = LayoutPosition.InFlow,
            Flow = new LayoutFlow {
                Direction = LayoutFlowDirection.Row,
                Justify = LayoutFlowJustify.Center,
                Gap = gap,
            },
        };

        [Test]
        public void Gap_WhenAuthored_IsRefusedLoudly() {
            // This Unity's UI Toolkit has no flex-gap property at all, so the spacing cannot be
            // honoured. An author who sets it used to get silence.
            LogAssert.Expect(LogType.Error, new Regex(@"LayoutFlow\.Gap cannot be resolved"));

            LayoutApplier.Apply(new VisualElement(), FlowHint(LayoutLength.Px(24f)));
        }

        [Test]
        public void Gap_AtZero_SaysNothing() {
            // Every Gap emitted across the shipped corpus is 0px, so the refusal must not fire for
            // stock data — otherwise it is noise on every screen instead of a signal.
            LayoutApplier.Apply(new VisualElement(), FlowHint(LayoutLength.Px(0f)));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Gap_AuthoredAsAPercentage_IsAlsoRefused() {
            // The unit does not rescue it: there is no property to put either form into.
            LogAssert.Expect(LogType.Error, new Regex(@"LayoutFlow\.Gap cannot be resolved"));

            LayoutApplier.Apply(new VisualElement(), FlowHint(LayoutLength.Percent(5f)));
        }

        [Test]
        public void Gap_DoesNotStopTheRestOfTheFlowBeingApplied() {
            // Refusing is not abandoning: direction and justification are perfectly resolvable and
            // must still land, so the screen degrades to "no spacing" rather than "no layout".
            LogAssert.Expect(LogType.Error, new Regex(@"LayoutFlow\.Gap cannot be resolved"));
            var element = new VisualElement();

            LayoutApplier.Apply(element, FlowHint(LayoutLength.Px(24f)));

            Assert.AreEqual(FlexDirection.Row, element.style.flexDirection.value);
            Assert.AreEqual(Justify.Center, element.style.justifyContent.value);
        }
    }
}

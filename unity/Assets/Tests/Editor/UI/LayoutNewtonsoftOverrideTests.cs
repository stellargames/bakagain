namespace BakAgain.Tests.Editor.UI {
    using GameData.Resources.Credits;
    using GameData.Resources.Dialog;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.Layout;
    using Newtonsoft.Json;
    using NUnit.Framework;

    /// <summary>
    /// FINDING 1 regression gate: <see cref="LayoutLength"/> and <see cref="LayoutAspectRatio"/>
    /// carry System.Text.Json <c>[JsonConverter]</c> attributes, but the mod-override path
    /// (<see cref="BakAgain.ResourceManagement.OverrideResourceProvider"/>) deserializes with
    /// Newtonsoft.Json — see its <c>JsonConvert.DeserializeObject(json, resourceType,
    /// JsonSettings)</c> call. Newtonsoft ignores STJ attributes and has no built-in handling for
    /// these structs, so before the fix, loading an override containing either type threw a
    /// <c>JsonSerializationException</c> ("Error converting value ... to type ...").
    ///
    /// <para>These tests deserialize the exact committed on-disk string form — the shape actually
    /// written to <c>generated/REQ/*.json</c> and read back by <c>OverrideResourceLocator</c> for
    /// <c>CRED.DAT</c> → <c>Overrides/DAT/CRED.json</c> — through bare <c>JsonConvert</c>, with no
    /// custom settings, so they fail exactly the way the real load path would if the
    /// <c>[TypeConverter]</c> fix were missing. Verified (see the task's final report):
    /// FAILED with <c>JsonSerializationException</c> before the <c>[TypeConverter]</c> attributes
    /// were added; PASSED after.</para>
    /// </summary>
    public class LayoutNewtonsoftOverrideTests {
        [Test]
        public void CreditsLayout_DeserializesFromNewtonsoft_UsingTheCommittedOnDiskStringForm() {
            // Post-Phase-2b shape: Title/Window/Row are nested LayoutHint boxes, not bare
            // LayoutLength properties — TitleY/WindowTop/WindowBottom no longer exist. Each
            // LayoutHint's own LayoutLength members (Top, Height, ...) still go through the
            // same [TypeConverter] this test exists to gate, just one level deeper.
            const string json = "{\"Title\":{\"Top\":\"246px\"},\"Window\":{\"Top\":\"324px\"}}";

            CreditsLayout layout = JsonConvert.DeserializeObject<CreditsLayout>(json);

            Assert.IsNotNull(layout);
            Assert.AreEqual(LayoutLength.Px(246f), layout.Title.Top);
            Assert.AreEqual(LayoutLength.Px(324f), layout.Window.Top);
            // Untouched properties must keep CreditsLayout's own faithful defaults — a partial
            // override should not zero out the rest of the geometry.
            Assert.AreEqual(LayoutLength.Px(624f), layout.Window.Height);
            Assert.AreEqual(LayoutLength.Px(66f), layout.Row.Height);
        }

        [Test]
        public void LayoutHint_DeserializesFromNewtonsoft_WidthHeightAndAspectRatioAsStrings() {
            const string json = "{\"Width\":\"auto\",\"Height\":\"50%\",\"AspectRatio\":\"10:9\"}";

            LayoutHint hint = JsonConvert.DeserializeObject<LayoutHint>(json);

            Assert.IsNotNull(hint);
            Assert.AreEqual(LayoutLength.Auto, hint.Width);
            Assert.AreEqual(LayoutLength.Percent(50f), hint.Height);
            Assert.IsTrue(hint.AspectRatio.HasValue);
            Assert.AreEqual(new LayoutAspectRatio(10f, 9f), hint.AspectRatio.Value);
        }

        // The Left/Top/Right/Bottom insets are LayoutLength, so they inherit the same
        // [TypeConverter] that made Width/Height/AspectRatio readable by Newtonsoft above — but
        // that inheritance is exactly the kind of thing that regresses silently if a future edit
        // adds a converter-less property, so it gets its own explicit coverage rather than
        // resting on "they're the same type as Width/Height".
        [Test]
        public void LayoutHint_DeserializesFromNewtonsoft_InsetsAsStrings() {
            const string json = "{\"Left\":\"210px\",\"Right\":\"215px\",\"Top\":\"27%\",\"Bottom\":\"auto\"}";

            LayoutHint hint = JsonConvert.DeserializeObject<LayoutHint>(json);

            Assert.IsNotNull(hint);
            Assert.AreEqual(LayoutLength.Px(210f), hint.Left);
            Assert.AreEqual(LayoutLength.Px(215f), hint.Right);
            Assert.AreEqual(LayoutLength.Percent(27f), hint.Top);
            // "auto" must deserialize to LayoutLength.Auto, not throw and not silently zero.
            Assert.AreEqual(LayoutLength.Auto, hint.Bottom);
        }

        // Position (Absolute/InFlow) is a plain enum, so Newtonsoft's default int-based
        // conversion "just works" with no [TypeConverter] needed — but that is exactly the kind
        // of assumption this whole file exists to gate rather than trust. A mod override
        // authoring an InFlow container (e.g. reflowing a fixed grid) goes through this same
        // Newtonsoft path, so it gets explicit coverage like every other LayoutHint property.
        [Test]
        public void LayoutHint_DeserializesFromNewtonsoft_Position() {
            const string json = "{\"Position\":\"InFlow\"}";

            LayoutHint hint = JsonConvert.DeserializeObject<LayoutHint>(json);

            Assert.IsNotNull(hint);
            Assert.AreEqual(LayoutPosition.InFlow, hint.Position);
        }

        // An override that omits Position entirely must still get the faithful default —
        // Absolute — the same "partial override doesn't zero out the rest" guarantee the
        // CreditsLayout test above already covers for Window.Height/Row.Height.
        [Test]
        public void LayoutHint_DeserializesFromNewtonsoft_PositionDefaultsToAbsoluteWhenOmitted() {
            const string json = "{\"Left\":\"210px\"}";

            LayoutHint hint = JsonConvert.DeserializeObject<LayoutHint>(json);

            Assert.IsNotNull(hint);
            Assert.AreEqual(LayoutPosition.Absolute, hint.Position);
        }

        // LayoutGrid/LayoutGridPlacement are new (grid layout task) — their only Newtonsoft-risky
        // members are the nested LayoutLength cells, which already go through the gated
        // [TypeConverter]; this proves that inheritance actually holds one level down, the same
        // way CreditsLayout above proves it for LayoutHint's nested Top/Height.
        [Test]
        public void LayoutGrid_DeserializesFromNewtonsoft_CellLengthsAsStrings() {
            const string json = "{\"CellWidth\":\"200px\",\"CellHeight\":\"15%\",\"Columns\":3,\"Rows\":2}";

            LayoutGrid grid = JsonConvert.DeserializeObject<LayoutGrid>(json);

            Assert.IsNotNull(grid);
            Assert.AreEqual(LayoutLength.Px(200f), grid.CellWidth);
            Assert.AreEqual(LayoutLength.Percent(15f), grid.CellHeight);
            Assert.AreEqual(3, grid.Columns);
            Assert.AreEqual(2, grid.Rows);
        }

        [Test]
        public void LayoutGridPlacement_DeserializesFromNewtonsoft_SpansDefaultToOneWhenOmitted() {
            const string json = "{\"Column\":2,\"Row\":1}";

            LayoutGridPlacement placement = JsonConvert.DeserializeObject<LayoutGridPlacement>(json);

            Assert.IsNotNull(placement);
            Assert.AreEqual(2, placement.Column);
            Assert.AreEqual(1, placement.Row);
            Assert.AreEqual(1, placement.ColumnSpan);
            Assert.AreEqual(1, placement.RowSpan);
        }

        // LayoutPadding is new (padding task) — its only Newtonsoft-risky members are the nested
        // LayoutLength sides, which already go through the gated [TypeConverter]; this proves
        // that inheritance holds one level down for LayoutPadding too, the same way LayoutGrid's
        // test above proves it for cell lengths. 143/87 — asymmetric, non-round — so a bare
        // number assertion elsewhere in this file can't coincidentally pass against them.
        [Test]
        public void LayoutPadding_DeserializesFromNewtonsoft_SidesAsStrings() {
            const string json = "{\"Left\":\"143px\",\"Bottom\":\"87%\"}";

            LayoutPadding padding = JsonConvert.DeserializeObject<LayoutPadding>(json);

            Assert.IsNotNull(padding);
            Assert.AreEqual(LayoutLength.Px(143f), padding.Left);
            Assert.AreEqual(LayoutLength.Percent(87f), padding.Bottom);
            // Untouched sides must keep their faithful Auto default.
            Assert.AreEqual(LayoutLength.Auto, padding.Top);
            Assert.AreEqual(LayoutLength.Auto, padding.Right);
        }

        [Test]
        public void LayoutHint_DeserializesFromNewtonsoft_Padding() {
            const string json = "{\"Padding\":{\"Left\":\"143px\",\"Bottom\":\"87%\"}}";

            LayoutHint hint = JsonConvert.DeserializeObject<LayoutHint>(json);

            Assert.IsNotNull(hint);
            Assert.IsNotNull(hint.Padding);
            Assert.AreEqual(LayoutLength.Px(143f), hint.Padding.Left);
            Assert.AreEqual(LayoutLength.Percent(87f), hint.Padding.Bottom);
            Assert.AreEqual(LayoutLength.Auto, hint.Padding.Top);
            Assert.AreEqual(LayoutLength.Auto, hint.Padding.Right);
        }

        // DialogStyle joins the override path in Task 3, and it is the first resource here whose
        // area is a whole nested LayoutHint rather than a bare length — so the percentage case
        // (the entire reason the old int rect was replaced) gets its own Newtonsoft coverage
        // instead of resting on "LayoutHint is already gated". The pen/pad values are asymmetric
        // and non-round so a field the parser transposed cannot land on the expected number.
        [Test]
        public void DialogStyle_DeserializesFromNewtonsoft_WithAPercentageDefaultArea() {
            const string json =
                "{\"FillPenColor\":3,\"BorderPenColor\":9,\"ShadowPenColor\":14,"
                + "\"BodyTextPenColor\":6,\"TextShadowPenSource\":11,"
                + "\"DefaultArea\":{\"Anchor\":\"Center\",\"Left\":\"7.5%\",\"Top\":\"11.25%\","
                + "\"Width\":\"63.75%\",\"Height\":\"29.5%\"},"
                + "\"TextPadLeft\":4.75,\"TextPadRight\":8.125}";

            DialogStyle style = JsonConvert.DeserializeObject<DialogStyle>(json);

            Assert.IsNotNull(style);
            Assert.AreEqual(3, style.FillPenColor);
            Assert.AreEqual(9, style.BorderPenColor);
            Assert.AreEqual(14, style.ShadowPenColor);
            Assert.AreEqual(6, style.BodyTextPenColor);
            Assert.AreEqual(11, style.TextShadowPenSource);
            Assert.AreEqual(4.75f, style.TextPadLeft);
            Assert.AreEqual(8.125f, style.TextPadRight);
            Assert.IsNotNull(style.DefaultArea);
            Assert.AreEqual(LayoutAnchor.Center, style.DefaultArea.Anchor);
            Assert.AreEqual(LayoutLength.Percent(7.5f), style.DefaultArea.Left);
            Assert.AreEqual(LayoutLength.Percent(11.25f), style.DefaultArea.Top);
            Assert.AreEqual(LayoutLength.Percent(63.75f), style.DefaultArea.Width);
            Assert.AreEqual(LayoutLength.Percent(29.5f), style.DefaultArea.Height);
        }

        // A settable-property class deserializes field by field, so an override may name only what
        // it wants to change and the rest lands on the type's own defaults. That is the property
        // Task 3's override path is built on, and it is exactly what a positional record struct
        // could not promise — hence its own case here rather than an assumption.
        [Test]
        public void DialogStyle_DeserializesFromNewtonsoft_PartialDocumentKeepsTypeDefaults() {
            const string json = "{\"DefaultArea\":{\"Left\":\"7.5%\",\"Height\":\"29.5%\"}}";

            DialogStyle style = JsonConvert.DeserializeObject<DialogStyle>(json);

            Assert.IsNotNull(style);
            Assert.AreEqual(LayoutLength.Percent(7.5f), style.DefaultArea.Left);
            Assert.AreEqual(LayoutLength.Percent(29.5f), style.DefaultArea.Height);
            // Unnamed edges keep LayoutHint's Auto default rather than collapsing to zero px.
            Assert.AreEqual(LayoutLength.Auto, style.DefaultArea.Top);
            Assert.AreEqual(LayoutLength.Auto, style.DefaultArea.Width);
        }

        // Decision revised 2026-08-05 (see ResizeDialogAction's class doc comment): the four
        // fields are now LayoutLength, so the committed generated/DDX/*.json shape moved from
        // bare ints ("Left":315) to unit strings ("Left":"315px"). A px-unit JSON payload — the
        // shape the extractor itself always emits — must still produce a usable px hint through
        // Newtonsoft, the serializer the mod-override path actually reads with.
        [Test]
        public void ResizeDialogAction_DeserializesFromNewtonsoft_AndStillYieldsAPxHint() {
            const string json = "{\"Left\":\"315px\",\"Top\":\"738px\",\"Width\":\"1129px\",\"Height\":\"402px\"}";

            ResizeDialogAction resize = JsonConvert.DeserializeObject<ResizeDialogAction>(json);

            Assert.IsNotNull(resize);
            LayoutHint hint = resize.ToLayoutHint();
            Assert.AreEqual(LayoutLength.Px(315f), hint.Left);
            Assert.AreEqual(LayoutLength.Px(738f), hint.Top);
            Assert.AreEqual(LayoutLength.Px(1129f), hint.Width);
            Assert.AreEqual(LayoutLength.Px(402f), hint.Height);
        }

        // The load-bearing case for the 2026-08-05 decision: only a hand-authored override can
        // ever produce a percent-valued resize (the extractor never does — see the class doc
        // comment), and the override path is exactly this Newtonsoft path. Distinctive, asymmetric,
        // no shared numbers with the px case above so a converter that silently coerced to px
        // could not coincidentally pass.
        [Test]
        public void ResizeDialogAction_DeserializesFromNewtonsoft_AndPreservesAPercentHint() {
            const string json = "{\"Left\":\"3.5%\",\"Top\":\"88.25%\",\"Width\":\"46.75%\",\"Height\":\"17%\"}";

            ResizeDialogAction resize = JsonConvert.DeserializeObject<ResizeDialogAction>(json);

            Assert.IsNotNull(resize);
            LayoutHint hint = resize.ToLayoutHint();
            Assert.AreEqual(LayoutLength.Percent(3.5f), hint.Left);
            Assert.AreEqual(LayoutLength.Percent(88.25f), hint.Top);
            Assert.AreEqual(LayoutLength.Percent(46.75f), hint.Width);
            Assert.AreEqual(LayoutLength.Percent(17f), hint.Height);
            Assert.AreEqual(LayoutLengthUnit.Percent, hint.Left.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, hint.Top.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, hint.Width.Unit);
            Assert.AreEqual(LayoutLengthUnit.Percent, hint.Height.Unit);
        }

        [Test]
        public void LayoutHint_DeserializesFromNewtonsoft_Grid() {
            const string json = "{\"Grid\":{\"CellWidth\":\"200px\",\"CellHeight\":\"180px\",\"Columns\":3,\"Rows\":2}}";

            LayoutHint hint = JsonConvert.DeserializeObject<LayoutHint>(json);

            Assert.IsNotNull(hint);
            Assert.IsNotNull(hint.Grid);
            Assert.AreEqual(LayoutLength.Px(200f), hint.Grid.CellWidth);
            Assert.AreEqual(LayoutLength.Px(180f), hint.Grid.CellHeight);
            Assert.AreEqual(3, hint.Grid.Columns);
            Assert.AreEqual(2, hint.Grid.Rows);
        }
    }
}

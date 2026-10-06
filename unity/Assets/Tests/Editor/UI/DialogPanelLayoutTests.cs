namespace BakAgain.Tests.Editor.UI {
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// THE FENCE for <see cref="DialogPanelBuilder"/>'s eleven former constants (and the one bare
    /// <c>6</c> literal on the speaker pill's border). Each of them is now a property of
    /// <see cref="DialogLayout"/>, carried on the <c>DIALSTYL.DAT</c> resource — and a value that
    /// is "data" only because it was moved into a model, with nothing proving the renderer READS
    /// it, is exactly the failure this effort has already shipped twice: delete the data path and
    /// every gate stays green.
    ///
    /// <para>So every test below drives the real builder with a DISTINCTIVE layout — values
    /// deliberately unlike the faithful defaults, asymmetric, non-round, and with no two related
    /// values sharing a number (the four pill paddings are four different numbers, so an
    /// implementation that kept "one horizontal + one vertical" constant cannot satisfy them) —
    /// and reads back the actual <c>VisualElement.style</c> the builder produced. A builder still
    /// reading a hardcoded constant produces the OLD number here, not the distinctive one.</para>
    ///
    /// <para>Unit assertions are deliberate too: <c>style.top.value</c> is a
    /// <see cref="Length"/>, which carries its unit, so a translator that flattened a percentage
    /// to px would still report the right number and only the unit would betray it. That defect
    /// class has been shipped in this project five times.</para>
    ///
    /// <para>The faithful (shipped) numbers themselves are asserted on the .NET side in
    /// <c>ResourceExtraction.Tests.Layout.DialogLayoutTests</c>; the one Unity-side restatement is
    /// <see cref="WithNoLayout_TheShippedDefaultsStillRender_UnchangedFromBeforeTheConversion"/>,
    /// which is the faithfulness gate for the conversion itself.</para>
    /// </summary>
    public class DialogPanelLayoutTests {
        // Measures text with the game font, which is built from the player's own files (TASK-662).
        [NUnit.Framework.OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        // Not one of these is a faithful default (6 / 90 / 18 / 180 / 36 / 120 / 5).
        private const float RowTop = 37f;
        private const float PadLeft = 113f;
        private const float PadRight = 71f;
        private const float PadTop = 29f;
        private const float PadBottom = 43f;
        private const float PillShadow = 17f;
        private const float PillBorder = 23f;
        private const float NarrativeTop = 211f;
        private const float SpeakerTop = 53f;
        private const float ChromeBorder = 31f;
        private const float ChromeShadow = 19f;
        private const float ShadowX = 7f;
        private const float ShadowY = 11f;

        private static DialogLayout DistinctiveLayout() => new DialogLayout {
            SpeakerPillRow = new LayoutHint {
                Left = LayoutLength.Px(0f),
                Top = LayoutLength.Px(RowTop),
                Right = LayoutLength.Px(0f),
                Flow = new LayoutFlow {
                    Direction = LayoutFlowDirection.Row,
                    Justify = LayoutFlowJustify.Center,
                    Align = LayoutFlowAlign.Center,
                },
            },
            SpeakerPill = new LayoutHint {
                Position = LayoutPosition.InFlow,
                Flow = new LayoutFlow {
                    Direction = LayoutFlowDirection.Row,
                    Justify = LayoutFlowJustify.Center,
                    Align = LayoutFlowAlign.Center,
                },
                // Four different numbers: a builder that kept one horizontal and one vertical
                // padding constant cannot produce these, and neither can one that mixed up the
                // axes.
                Padding = new LayoutPadding {
                    Left = LayoutLength.Px(PadLeft),
                    Top = LayoutLength.Px(PadTop),
                    Right = LayoutLength.Px(PadRight),
                    Bottom = LayoutLength.Px(PadBottom),
                },
            },
            SpeakerPillShadowOffset = PillShadow,
            SpeakerPillBorderWidth = PillBorder,
            NarrativeBodyTop = LayoutLength.Px(NarrativeTop),
            SpeakerTop = LayoutLength.Px(SpeakerTop),
            ChromeBorderWidth = ChromeBorder,
            ChromeShadowOffset = ChromeShadow,
            TextShadowOffsetX = ShadowX,
            TextShadowOffsetY = ShadowY,
        };

        // Chrome pens like shipped row 2 (fill 1 / border 1 / bevel 4): the builder early-returns
        // on an all-zero style, so a chrome test needs a style that actually paints one.
        private static DialogStyle BoxedStyle() => new DialogStyle {
            FillPenColor = 0x01,
            BorderPenColor = 0x01,
            ShadowPenColor = 0x04,
            BodyTextPenColor = 0x00,
            TextShadowPenSource = 0x00,
            DefaultArea = LayoutHint.PxRect(65f, 66f, 1470f, 606f),
            TextPadLeft = 50f,
            TextPadRight = 50f,
        };

        // Borderless, with a text shadow — the cutscene speech row (shipped row 1). Borderless
        // matters for the body-inset tests: a bordered, speakerless panel centres its body
        // vertically (top/bottom = 0) instead of using the top inset at all.
        private static DialogStyle BorderlessShadowedStyle() => new DialogStyle {
            FillPenColor = 0x00,
            BorderPenColor = 0x00,
            ShadowPenColor = 0x00,
            BodyTextPenColor = 0x0A,
            TextShadowPenSource = 0x02,
            DefaultArea = LayoutHint.PxRect(40f, 720f, 1525f, 450f),
            TextPadLeft = 40f,
            TextPadRight = 40f,
            // Row 1's shipped vertical insets (field_7 = 5, field_8 = 2 VGA px). The body's top
            // inset comes from the ROW now, so a style without them cannot exercise the default.
            TextPadTop = 30f,
            TextPadBottom = 12f,
        };

        // Text-area pads unlike either shipped value (3.40136 / 2.62295), and unlike EACH OTHER,
        // so a builder that wired both sides to one datum — or to a constant — cannot satisfy them.
        private const float TextAreaPadLeft = 81.25f;
        private const float TextAreaPadRight = 43.75f;

        // Pens unlike any shipped row's (rows use 0x0A/0x02 or 0x00/0x00). ShadowSource - 1 is the
        // pen the shadow actually paints in (DialogStyle.TextShadowPenColor, the engine's
        // field_3 - 1 at 0x490c8), so the source and the resolved pen are different indices here
        // and an implementation that used the source directly lands on the wrong colour.
        private const byte BodyPen = 0x2B;
        private const byte ShadowSourcePen = 0x11;

        private static DialogStyle PennedStyle() => new DialogStyle {
            FillPenColor = 0x00,
            BorderPenColor = 0x00,
            ShadowPenColor = 0x00,
            BodyTextPenColor = BodyPen,
            TextShadowPenSource = ShadowSourcePen,
            DefaultArea = LayoutHint.PxRect(40f, 720f, 1525f, 450f),
            TextPadLeft = TextAreaPadLeft,
            TextPadRight = TextAreaPadRight,
        };

        private static Color[] Palette() => new Color[256];

        [Test]
        public void EveryBodyIsCentredInItsTextRect_StripsAndSpeakersIncluded() {
            // textwrap_draw_aligned centres the block on 0x10 (TEXTWRAP.C:127-131), which every style
            // row carries. Measured: Romney's six-line description starts higher than the Port
            // Exchange's four, and a one-line cutscene reply sits lower than a four-line one.
            foreach (DialogEntry entry in new[] { Narrative(), PillSpeaker() }) {
                DialogStyle style = BorderlessShadowedStyle();
                VisualElement body = DialogPanelBuilder.BuildPanel(entry, style, null, Palette())
                    .Q("BakDialogBody");
                Assert.AreEqual(Justify.Center, body.style.justifyContent.value, entry.DialogType.ToString());
                Assert.AreEqual(style.TextPadTop, body.style.top.value.value, "the rect's top is the row's pad");
                Assert.AreEqual(style.TextPadBottom, body.style.bottom.value.value, "and so is its bottom");
            }
        }

        [Test]
        public void AChoiceRecordCentresAboveItsMenuRow() {
            // DIALOG.C:645-647 takes the menu row off the rect before laying out. Measured on
            // Romney's gambler: the port's offer sat 10 VGA rows low until this was counted.
            DialogStyle style = BorderlessShadowedStyle();
            DialogEntry offer = Narrative();
            offer.Flags |= DialogEntryFlags.TextWithChoice;
            VisualElement body = DialogPanelBuilder.BuildPanel(offer, style, null, Palette()).Q("BakDialogBody");
            Assert.AreEqual(style.TextPadBottom + GameTextBlock.ChoiceMenuReserve, body.style.bottom.value.value);
        }

        [Test]
        public void TheFramelessColumnKeepsTheFillAndDropsBorderAndBevel() {
            // DIALOG.C:346-357. Measured on the Black Sheep's price quote: the original is flat.
            DialogStyle style = BoxedStyle();
            style.FramelessAtDefaultLeft = true;
            var host = new VisualElement();
            DialogPanelBuilder.BuildChrome(host, style, null, Palette(), area: style.DefaultArea);
            Assert.IsNull(host.Q("BakDialogShadow"), "no bevel");
            Assert.AreEqual(0f, host.Q("BakDialogChrome").style.borderTopWidth.value, "no border");

            var moved = new VisualElement();
            DialogPanelBuilder.BuildChrome(moved, style, null, Palette(),
                area: GameData.Resources.Layout.LayoutHint.PxRect(300, 300, 600, 300));
            Assert.IsNotNull(moved.Q("BakDialogShadow"), "a resized box gets its frame back");
        }

        [Test]
        public void ANamePillCanBeBuiltOnItsOwn_forALocationSign() {
            // dialog_draw_speech_bubble draws a town scene's "#Romney#" exactly as it draws a
            // speaker (TOWNSCN.C:213), so the sign is the same pill: fill 0x0B, rim 0x0F, ink 0x0A.
            Color[] palette = DistinctivePalette();

            VisualElement built = DialogPanelBuilder.BuildNamePill("Romney", null, palette);

            VisualElement pill = built.Q("BakDialogSpeakerPill");
            Assert.IsNotNull(pill);
            Assert.AreEqual(palette[0x0B], pill.style.backgroundColor.value);
            Assert.AreEqual(palette[0x0F], pill.style.borderTopColor.value);
            Assert.AreEqual("Romney", built.Q<Label>("BakDialogSpeaker").text);
        }

        // Every index a different, non-black, fully-opaque colour, so "the right pen" and "pen 0"
        // and "whatever Color's default is" are three distinguishable outcomes.
        private static Color[] DistinctivePalette() {
            var palette = new Color[256];
            for (var pen = 0; pen < palette.Length; pen++) {
                palette[pen] = new Color(pen / 255f, 1f - (pen / 255f), 0.25f, 1f);
            }
            return palette;
        }

        private static DialogEntry Narrative() =>
            new DialogEntry { Text = "Plain narrative body.", DialogType = DialogType.PlainWithoutBox };

        // A '#Name#' prefix is what makes the builder split off a speaker. PlainWithoutBox takes
        // the plain centred-title branch; ColoredWithoutBox takes the pill.
        private static DialogEntry TitledSpeaker() =>
            new DialogEntry { Text = "#Gorath#Spoken body.", DialogType = DialogType.PlainWithoutBox };

        private static DialogEntry PillSpeaker() =>
            new DialogEntry { Text = "#Gorath#Spoken body.", DialogType = DialogType.ColoredWithoutBox };

        /// <summary>
        /// Spoken lines are LEFT-aligned with an indent; only the CenterText flag centres them.
        /// Measured in the original on C31: every #James#/#Gorath# line reads left with a paragraph
        /// indent, and the one record carrying CenterText ("They are here.") is centred.
        /// </summary>
        [Test]
        public void ASpeakersBodyIsLeftAlignedUnlessTheRecordAsksForCentring() {
            VisualElement plain = DialogPanelBuilder.BuildPanel(PillSpeaker(), BorderlessShadowedStyle(), null, Palette());
            Assert.AreEqual(TextAnchor.UpperLeft, plain.Q("BakDialogBody").style.unityTextAlign.value);

            DialogEntry centred = PillSpeaker();
            centred.Flags |= DialogEntryFlags.CenterText;
            VisualElement title = DialogPanelBuilder.BuildPanel(centred, BorderlessShadowedStyle(), null, Palette());
            Assert.AreEqual(TextAnchor.UpperCenter, title.Q("BakDialogBody").style.unityTextAlign.value);
        }

        [Test]
        public void ChromeEdgeWidths_ComeFromTheLayout_OnAllFourEdges() {
            var panel = new VisualElement();

            DialogPanelBuilder.BuildChrome(panel, BoxedStyle(), DistinctiveLayout(), Palette());

            VisualElement box = panel.Q("BakDialogChrome");
            Assert.IsNotNull(box, "no chrome box was built");
            Assert.AreEqual(ChromeBorder, box.style.borderTopWidth.value, "top edge");
            Assert.AreEqual(ChromeBorder, box.style.borderRightWidth.value, "right edge");
            Assert.AreEqual(ChromeBorder, box.style.borderBottomWidth.value, "bottom edge");
            Assert.AreEqual(ChromeBorder, box.style.borderLeftWidth.value, "left edge");
        }

        /// <summary>
        /// The panel's drop shadow is offset down-LEFT: x negated, y not — and BOTH axes take the
        /// single <c>ChromeShadowOffset</c> scalar (which carries the original's vertical factor).
        /// Asserting the sign as well as the magnitude is what stops a "fix" that made this
        /// per-axis, or that lost the negation, from passing.
        /// </summary>
        [Test]
        public void ChromeShadow_IsOffsetDownLeftByTheLayoutsSingleScalar() {
            var panel = new VisualElement();

            DialogPanelBuilder.BuildChrome(panel, BoxedStyle(), DistinctiveLayout(), Palette());

            VisualElement shadow = panel.Q("BakDialogShadow");
            Assert.IsNotNull(shadow, "no drop shadow was built");
            Translate translate = shadow.style.translate.value;
            Assert.AreEqual(-ChromeShadow, translate.x.value, "x is negated (down-LEFT)");
            Assert.AreEqual(ChromeShadow, translate.y.value, "y is not");
        }

        /// <summary>
        /// The reason the default moved off <c>DialogLayout</c> and onto the style row: the rows
        /// disagree, and one layout-wide number cannot be right for all of them. Two styles that
        /// differ ONLY in their inset must render two different tops.
        /// </summary>
        [Test]
        public void WithNoLayout_TwoRowsWithDifferentInsetsRenderDifferently() {
            DialogStyle strip = BorderlessShadowedStyle();          // field_7 = 5 VGA -> 30
            DialogStyle fullScreen = BorderlessShadowedStyle();
            fullScreen.TextPadTop = 6f;                             // field_7 = 1 VGA -> 6

            VisualElement a = DialogPanelBuilder.BuildPanel(Narrative(), strip, null, Palette());
            VisualElement b = DialogPanelBuilder.BuildPanel(Narrative(), fullScreen, null, Palette());

            Assert.AreEqual(30f, a.Q("BakDialogBody").style.top.value.value);
            Assert.AreEqual(6f, b.Q("BakDialogBody").style.top.value.value);
        }

        /// <summary>
        /// And the knob still wins. <c>NarrativeTop</c> (211) is nothing like the row's 30, so an
        /// implementation that read the row unconditionally — which is the tempting simplification
        /// once the row carries the number — fails here.
        /// </summary>
        [Test]
        public void AnExplicitLayoutValueBeatsTheRowsInset() {
            VisualElement panel = DialogPanelBuilder.BuildPanel(
                Narrative(), BorderlessShadowedStyle(), DistinctiveLayout(), Palette());

            Assert.AreEqual(NarrativeTop, panel.Q("BakDialogBody").style.top.value.value);
        }

        [Test]
        public void NarrativeBody_TakesItsTopInsetFromTheLayout() {
            VisualElement panel = DialogPanelBuilder.BuildPanel(
                Narrative(), BorderlessShadowedStyle(), DistinctiveLayout(), Palette());

            VisualElement body = panel.Q("BakDialogBody");
            Assert.IsNotNull(body, "no body label was built");
            Assert.AreEqual(NarrativeTop, body.style.top.value.value);
            Assert.AreEqual(LengthUnit.Pixel, body.style.top.value.unit);
        }

        /// <summary>
        /// A speaker does not move the body (DIALOG.C:576-641): it starts at the same inset as a
        /// narrative body. The text's own "\n" after "#Name#" is what puts it a line lower.
        /// </summary>
        [Test]
        public void BodyUnderASpeaker_StartsWhereANarrativeBodyDoes() {
            VisualElement panel = DialogPanelBuilder.BuildPanel(
                TitledSpeaker(), BorderlessShadowedStyle(), DistinctiveLayout(), Palette());

            VisualElement speaker = panel.Q("BakDialogSpeaker");
            Assert.IsNotNull(speaker, "no speaker label was built");
            Assert.AreEqual(SpeakerTop, speaker.style.top.value.value, "the speaker's own inset");
            Assert.AreEqual(NarrativeTop, panel.Q("BakDialogBody").style.top.value.value);
        }

        [Test]
        public void SpeakerPill_IsPlacedPaddedAndBorderedByTheLayout() {
            VisualElement panel = DialogPanelBuilder.BuildPanel(
                PillSpeaker(), BorderlessShadowedStyle(), DistinctiveLayout(), Palette());

            VisualElement row = panel.Q("BakDialogSpeakerRow");
            Assert.IsNotNull(row, "no speaker pill row was built");
            Assert.AreEqual(Position.Absolute, row.style.position.value);
            Assert.AreEqual(RowTop, row.style.top.value.value, "the row's inset from the panel top");
            Assert.AreEqual(0f, row.style.left.value.value, "left/right 0 make the row span the panel");
            Assert.AreEqual(0f, row.style.right.value.value);
            Assert.AreEqual(Justify.Center, row.style.justifyContent.value,
                "the row is what centres the pill horizontally");

            VisualElement pill = panel.Q("BakDialogSpeakerPill");
            Assert.IsNotNull(pill, "no speaker pill was built");
            // Four distinct numbers, one per side.
            Assert.AreEqual(PadLeft, pill.style.paddingLeft.value.value, "padding left");
            Assert.AreEqual(PadRight, pill.style.paddingRight.value.value, "padding right");
            Assert.AreEqual(PadTop, pill.style.paddingTop.value.value, "padding top");
            Assert.AreEqual(PadBottom, pill.style.paddingBottom.value.value, "padding bottom");
            // Its OWN width, not the chrome's — the two must not be wired to the same datum, or an
            // author taking the dialog frame off loses the name bubble's outline too.
            Assert.AreEqual(PillBorder, pill.style.borderTopWidth.value, "pill border top");
            Assert.AreEqual(PillBorder, pill.style.borderRightWidth.value, "pill border right");
            Assert.AreEqual(PillBorder, pill.style.borderBottomWidth.value, "pill border bottom");
            Assert.AreEqual(PillBorder, pill.style.borderLeftWidth.value, "pill border left");
            Assert.AreNotEqual(ChromeBorder, pill.style.borderTopWidth.value,
                "the pill rim and the panel bevel are separate data");

            VisualElement pillShadow = panel.Q("BakDialogSpeakerPillShadow");
            Assert.IsNotNull(pillShadow, "no pill shadow was built");
            Translate translate = pillShadow.style.translate.value;
            Assert.AreEqual(PillShadow, translate.x.value, "the pill shadow is down-RIGHT on x");
            Assert.AreEqual(PillShadow, translate.y.value, "and on y, from the same single scalar");
        }

        /// <summary>
        /// The text shadow is the one per-axis pair, and it feeds BOTH text sites — the
        /// body/title labels and the label inside the pill. One datum, two consumers: a test that
        /// only checked one would let the other keep a hardcoded <c>(5, 6)</c>.
        /// </summary>
        [Test]
        public void TextShadowOffset_ReachesBothTheBodyLabelAndThePillLabel() {
            DialogLayout layout = DistinctiveLayout();

            VisualElement titled = DialogPanelBuilder.BuildPanel(
                TitledSpeaker(), BorderlessShadowedStyle(), layout, Palette());
            TextShadow bodyShadow = titled.Q("BakDialogBody").style.textShadow.value;
            Assert.AreEqual(new Vector2(ShadowX, ShadowY), bodyShadow.offset, "body label");
            TextShadow titleShadow = titled.Q("BakDialogSpeaker").style.textShadow.value;
            Assert.AreEqual(new Vector2(ShadowX, ShadowY), titleShadow.offset, "plain title label");

            VisualElement pilled = DialogPanelBuilder.BuildPanel(
                PillSpeaker(), BorderlessShadowedStyle(), layout, Palette());
            VisualElement pill = pilled.Q("BakDialogSpeakerPill");
            TextShadow pillShadow = pill.Q("BakDialogSpeaker").style.textShadow.value;
            Assert.AreEqual(new Vector2(ShadowX, ShadowY), pillShadow.offset, "pill label");
        }

        /// <summary>
        /// <c>TextPadLeft</c>/<c>TextPadRight</c> must reach the rendered LABELS, not just
        /// the model. They are the original's <c>field_9</c>/<c>field_A</c> (applied in
        /// <c>RenderDialogText</c> at 0x49043-0x4905f) and they bound the word-wrap region, so an
        /// author raising them expects wrapped text to move — the one thing the whole
        /// override path exists to let them do.
        ///
        /// <para>Nothing asserted this before. Replacing both with a hardcoded inset left every
        /// gate green: the two <c>TextPad*</c> values anywhere in this suite were the shipped
        /// numbers, used as fixture furniture and never compared to anything. So this drives the
        /// real builder with two DIFFERENT non-shipped pads and reads back the label's own
        /// resolved insets — and asserts the UNIT.</para>
        ///
        /// <para><b>The unit assertion flipped on 2026-09-13, and the old one was the bug.</b> It
        /// demanded <c>LengthUnit.Percent</c>, which is right only for a panel at its style row's
        /// shipped width. The original subtracts a BYTE (<c>field_9</c>, 10 for row 5) from
        /// whatever rect <c>dialog_getDialogArea</c> produced, and 550 shipped entries carry a
        /// <c>ResizeDialog</c> that changes that rect. Measured against the original on the
        /// item-inspect description: 184 VGA px of wrap region there, 190 here.</para>
        /// </summary>
        [Test]
        public void TextAreaPads_ReachTheBodyAndTitleLabels_AsAbsolutePixels() {
            VisualElement narrative = DialogPanelBuilder.BuildPanel(
                Narrative(), PennedStyle(), DistinctiveLayout(), Palette());

            VisualElement body = narrative.Q("BakDialogBody");
            Assert.IsNotNull(body, "no body label was built");
            Length bodyLeft = body.style.left.value;
            Length bodyRight = body.style.right.value;
            Assert.AreEqual(TextAreaPadLeft, bodyLeft.value, "the body's left inset is TextPadLeft");
            Assert.AreEqual(LengthUnit.Pixel, bodyLeft.unit,
                "and it is ABSOLUTE px — a percentage shrinks with a ResizeDialog, field_9 does not");
            Assert.AreEqual(TextAreaPadRight, bodyRight.value, "the body's right inset is TextPadRight");
            Assert.AreEqual(LengthUnit.Pixel, bodyRight.unit);
            Assert.AreNotEqual(bodyLeft.value, bodyRight.value,
                "the two sides are separate data — one datum wired to both would pass everything else");

            // The plain centred-title speaker takes the same pads; the pill does not (it is sized
            // by its own padding, not by the panel's text area).
            VisualElement titled = DialogPanelBuilder.BuildPanel(
                TitledSpeaker(), PennedStyle(), DistinctiveLayout(), Palette());
            VisualElement speaker = titled.Q("BakDialogSpeaker");
            Assert.IsNotNull(speaker, "no speaker label was built");
            Assert.AreEqual(TextAreaPadLeft, speaker.style.left.value.value, "title label, left");
            Assert.AreEqual(LengthUnit.Pixel, speaker.style.left.value.unit);
            Assert.AreEqual(TextAreaPadRight, speaker.style.right.value.value, "title label, right");
        }

        /// <summary>
        /// The pens must reach the label's RESOLVED COLOURS. The shadow OFFSET was fenced;
        /// the colour it paints in was not, and neither was the body text colour — so a builder
        /// that hardcoded black, or that indexed the palette with the shadow SOURCE instead of
        /// <c>TextShadowPenColor</c> (<c>field_3 - 1</c>), passed every test in this file.
        ///
        /// <para>The palette gives every index a different colour and the two pens are two
        /// indices apart from each other and from every shipped row's, so "right pen", "off by
        /// one", "pen 0" and "untouched default" are four distinguishable outcomes.</para>
        /// </summary>
        [Test]
        public void BodyAndShadowPens_ReachTheLabelsResolvedColours() {
            Color[] palette = DistinctivePalette();

            VisualElement narrative = DialogPanelBuilder.BuildPanel(
                Narrative(), PennedStyle(), DistinctiveLayout(), palette);

            VisualElement body = narrative.Q("BakDialogBody");
            Assert.IsNotNull(body, "no body label was built");
            Assert.AreEqual(palette[BodyPen], body.style.color.value,
                "the body text paints in BodyTextPenColor");
            Assert.AreNotEqual(palette[0], body.style.color.value,
                "...not in pen 0, which is what a hardcoded black would give");

            TextShadow shadow = body.style.textShadow.value;
            Assert.AreEqual(palette[ShadowSourcePen - 1], shadow.color,
                "the drop shadow paints in TextShadowPenColor, i.e. the SOURCE minus one");
            Assert.AreNotEqual(palette[ShadowSourcePen], shadow.color,
                "...not in the source pen itself (the engine's field_3 - 1 at 0x490c8)");
            // The offset is the other half of the same datum path and is fenced separately by
            // TextShadowOffset_ReachesBothTheBodyLabelAndThePillLabel; assert it is present at all
            // so "no shadow was built" cannot make the colour assertions vacuous.
            Assert.AreEqual(new Vector2(ShadowX, ShadowY), shadow.offset);
        }

        /// <summary>
        /// The whole point of the type change: an author can restate these as percentages and the
        /// PERCENTAGE arrives — not the bare number reinterpreted as px. Values chosen so the
        /// number alone could not be mistaken for a shipped px value.
        /// </summary>
        [Test]
        public void AnOverrideInPercent_ArrivesAsAPercentage_NotFlattenedToPixels() {
            DialogLayout layout = DistinctiveLayout();
            layout.NarrativeBodyTop = LayoutLength.Percent(13.75f);
            layout.SpeakerPillRow.Top = LayoutLength.Percent(4.5f);
            layout.SpeakerPill.Padding.Left = LayoutLength.Percent(6.25f);

            VisualElement narrative = DialogPanelBuilder.BuildPanel(
                Narrative(), BorderlessShadowedStyle(), layout, Palette());
            Length bodyTop = narrative.Q("BakDialogBody").style.top.value;
            Assert.AreEqual(LengthUnit.Percent, bodyTop.unit, "the body's top inset kept its unit");
            Assert.AreEqual(13.75f, bodyTop.value);

            VisualElement pilled = DialogPanelBuilder.BuildPanel(
                PillSpeaker(), BorderlessShadowedStyle(), layout, Palette());
            Length rowTop = pilled.Q("BakDialogSpeakerRow").style.top.value;
            Assert.AreEqual(LengthUnit.Percent, rowTop.unit, "the pill row's inset kept its unit");
            Assert.AreEqual(4.5f, rowTop.value);
            Length padLeft = pilled.Q("BakDialogSpeakerPill").style.paddingLeft.value;
            Assert.AreEqual(LengthUnit.Percent, padLeft.unit, "the pill's padding kept its unit");
            Assert.AreEqual(6.25f, padLeft.value);
            // The three sides the author did NOT restate keep their px values — an override that
            // silently reset the rest of the box would show up here.
            Assert.AreEqual(PadRight, pilled.Q("BakDialogSpeakerPill").style.paddingRight.value.value);
        }

        [Test]
        public void WithNoLayout_TheShippedDefaultsStillRender_UnchangedFromBeforeTheConversion() {
            var chromeHost = new VisualElement();
            DialogPanelBuilder.BuildChrome(chromeHost, BoxedStyle(), null, Palette());
            VisualElement box = chromeHost.Q("BakDialogChrome");
            Assert.AreEqual(6f, box.style.borderTopWidth.value);
            Assert.AreEqual(6f, box.style.borderRightWidth.value);
            Assert.AreEqual(6f, box.style.borderBottomWidth.value);
            Assert.AreEqual(6f, box.style.borderLeftWidth.value);
            Translate chromeShadow = chromeHost.Q("BakDialogShadow").style.translate.value;
            Assert.AreEqual(-6f, chromeShadow.x.value);
            Assert.AreEqual(6f, chromeShadow.y.value);

            VisualElement narrative = DialogPanelBuilder.BuildPanel(
                Narrative(), BorderlessShadowedStyle(), null, Palette());
            Length narrativeTop = narrative.Q("BakDialogBody").style.top.value;
            Assert.AreEqual(30f, narrativeTop.value,
                "with no layout the inset comes from the style row's field_7, not a constant");
            Assert.AreEqual(LengthUnit.Pixel, narrativeTop.unit);
            Assert.AreEqual(new Vector2(5f, 6f),
                narrative.Q("BakDialogBody").style.textShadow.value.offset);

            VisualElement titled = DialogPanelBuilder.BuildPanel(
                TitledSpeaker(), BorderlessShadowedStyle(), null, Palette());
            Assert.AreEqual(36f, titled.Q("BakDialogSpeaker").style.top.value.value);
            // The style row's own top pad, speaker or not (DIALOG.C:576-641). It was 216 — VGA 36
            // below the area top — until TASK-720 measured C31 in the original: the body starts the
            // style's top pad, plus the text's own leading newline, below the top.
            Assert.AreEqual(BorderlessShadowedStyle().TextPadTop, titled.Q("BakDialogBody").style.top.value.value);

            VisualElement pilled = DialogPanelBuilder.BuildPanel(
                PillSpeaker(), BorderlessShadowedStyle(), null, Palette());
            VisualElement row = pilled.Q("BakDialogSpeakerRow");
            Assert.AreEqual(Position.Absolute, row.style.position.value);
            Assert.AreEqual(6f, row.style.top.value.value);
            Assert.AreEqual(0f, row.style.left.value.value);
            Assert.AreEqual(0f, row.style.right.value.value);
            Assert.AreEqual(Justify.Center, row.style.justifyContent.value);
            Assert.AreEqual(Align.Center, row.style.alignItems.value);
            Assert.AreEqual(FlexDirection.Row, row.style.flexDirection.value);

            VisualElement pill = pilled.Q("BakDialogSpeakerPill");
            Assert.AreEqual(90f, pill.style.paddingLeft.value.value);
            Assert.AreEqual(90f, pill.style.paddingRight.value.value);
            Assert.AreEqual(18f, pill.style.paddingTop.value.value);
            Assert.AreEqual(18f, pill.style.paddingBottom.value.value);
            Assert.AreEqual(6f, pill.style.borderTopWidth.value);
            Assert.AreEqual(6f, pill.style.borderLeftWidth.value);
            Assert.AreEqual(Justify.Center, pill.style.justifyContent.value);
            Assert.AreEqual(Align.Center, pill.style.alignItems.value);
            Assert.AreEqual(FlexDirection.Row, pill.style.flexDirection.value);
            Translate pillShadow = pilled.Q("BakDialogSpeakerPillShadow").style.translate.value;
            Assert.AreEqual(6f, pillShadow.x.value);
            Assert.AreEqual(6f, pillShadow.y.value);
        }

        /// <summary>
        /// The layout the builder actually uses must be the one on the RESOURCE, so a mod
        /// author's document reaches it. <c>DialogStyleTable.Layout</c> is that hand-off point;
        /// this pins that a table's layout is a live, per-instance object rather than a shared
        /// static the override path could not reach.
        /// </summary>
        [Test]
        public void TheLayoutTravelsOnTheStyleTableResource_PerInstance() {
            var table = new DialogStyleTable();
            Assert.IsNotNull(table.Layout);
            Assert.AreNotSame(table.Layout, new DialogStyleTable().Layout);

            table.Layout.ChromeBorderWidth = ChromeBorder;
            var panel = new VisualElement();
            DialogPanelBuilder.BuildChrome(panel, table.Get(2), table.Layout, Palette());
            Assert.AreEqual(ChromeBorder, panel.Q("BakDialogChrome").style.borderTopWidth.value,
                "the builder must read the table's own layout instance");
        }
    }
}

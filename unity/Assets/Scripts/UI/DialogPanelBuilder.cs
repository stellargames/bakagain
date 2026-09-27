namespace BakAgain.UI {
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.UI.Layout;
    using GameData.Resources.Dialog;
    using GameData.Resources.Layout;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// Builds the UI Toolkit visual tree for a DDX dialog panel — chrome (fill /
    /// bevelled border / drop shadow), body text, speaker name / pill — from a
    /// resolved <see cref="DialogStyle"/> and the active palette. Pure view
    /// construction split out of <see cref="DialogManager"/>; the manager owns the
    /// flow, input layer, and panel placement. Every method is static and depends
    /// only on its arguments (plus the lifetime-cached wood-panel tile).
    /// </summary>
    internal static class DialogPanelBuilder {
        // Speaker-name pens. `dialog_DrawNameBubble` (0x488a5) and the
        // RenderDialogText '#'-title branch (0x48e58 / 0x48e87) both paint the
        // speaker/name in pen 0x0A with a pen-1 drop shadow — independent of the
        // body style's pens. (In the typical BaK palette: pen 0x0A ≈ bright
        // cream, pen 1 ≈ dark brown — the cream "Gorath" pill in the reference
        // screenshot.) Resolved against the active palette like every other pen.
        private const int NameTextPenColor = 0x0A;
        private const int NameShadowPenColor = 0x01;

        // Name-bubble (pill) chrome pens, from dialog_DrawNameBubble (0x488a5):
        // the central box + caps are filled in pen 0x0B (0x4899c) and outlined
        // with pen 0x0F edge lines / cap arcs (0x4896b). The original also lays
        // a 1px pen-1 rim offset down-right for a raised look (the +113/+120
        // fills) — approximated here by the bottom/right border sitting over the
        // pen-1 body shadow.
        private const int NameBubbleFillPen = 0x0B;
        private const int NameBubbleBorderPen = 0x0F;

        // The speaker pill's silhouette. The original bubble is a stadium (capsule):
        // a bordered box with full-semicircle caps (radius = half its 14px
        // height — dialog_DrawNameBubble at 0x488a5). UI Toolkit clamps a corner
        // radius PER AXIS, so a too-large radius yields a full ellipse instead;
        // a GeometryChangedEvent callback sets the radius to exactly half the
        // pill's resolved height (see SetPillRadius) to get a true stadium.
        //
        // Every NUMBER this file used to carry — the pill's padding and top inset,
        // the body/speaker text offsets, the chrome edge widths, the text-shadow
        // offsets — now lives in GameData as DialogLayout, hosted on the
        // DIALSTYL.DAT resource beside the per-row DefaultArea. Nothing here
        // converts from VGA any more: the data is already in canonical
        // design-frame px and a mod author can restate it (see DialogLayout).

        // The body font is the game font, at the one size the whole UI uses — the original selects
        // game.fnt once at boot and never swaps it, so a dialog-only size would be a fiction. That
        // is also why no size appears on DialogLayout: overriding it for dialogs alone would
        // desynchronise them from every other surface drawn in the same font.
        //
        // There is no font-size constant here at all any more. It was Canonical.MenuFontSizePx
        // (48 = a fabricated 8 × VgaScaleY), applied with no aspect stretch, which rendered the
        // body at a line pitch of 7.25 VGA rows against the original's 11 — the ~2/3 scale of
        // task-46. Sizing now goes through GameFontText, the single owner of BOTH halves of
        // matching the original: the size whose advances match GAME.FNT, AND the vertical stretch
        // canonical space's anisotropy demands. Keeping a local alias would have re-created the
        // seam where one half can be applied without the other.

        // Debug: outline the resolved dialog area in magenta so layout bugs
        // (viewport mapping, ResizeDialog conversion, etc.) are visible.
        private const bool DrawDebugAreaBorder = false;

        // Fallback fill tone, used only when the generated wood tile (below) is
        // missing. The original chrome (dialog_DrawChrome at 0x48632) weaves a
        // strip of the background buffer rather than laying a solid colour; the
        // preferred reproduction is the tiled OPTIONS0 wood texture, but this
        // opaque tone keeps the panel readable (and still hides the menu buttons
        // behind it) if the asset hasn't been generated. Sampled from the menu.
        private static readonly Color StripeFillApprox = new Color(132f / 255f, 91f / 255f, 47f / 255f, 1f);

        // Repeating wood-panel fill baked once per session from a clean window of the player's
        // OPTIONS0.SCX (the wooden scroll, below the title, above the vines): 128 px wide at
        // (96, 50), 80 rows tall. Same baker as the terrain textures.
        private static Texture2D _panelTile;
        private static bool _panelTileLoaded;

        private static Texture2D GetPanelTile() {
            if (!_panelTileLoaded) {
                _panelTileLoaded = true;
                Color[] scx = ScxTileBaker.LoadPixels("OPTIONS0.SCX", "OPTIONS.PAL");
                if (scx != null) {
                    _panelTile = ScxTileBaker.Bake(scx, 96, 50, 128, 80, 0.012f, new System.Random(42));
                }
            }
            return _panelTile;
        }

        public static VisualElement BuildPanel(
            DialogEntry entry, DialogStyle style, DialogLayout layout, Color[] palette) =>
            BuildPanel(entry, style, layout, palette, resolvedText: null);

        // <paramref name="resolvedText"/> overrides entry.Text for rendering (e.g. after @N
        // text-variable substitution) without mutating the entry itself — callers that load
        // through a cache (DialogResourceLoader.LoadDialogAsync caches the Dialog and its
        // DialogEntry instances) must not write the substituted text back onto the shared entry.
        // Null falls back to entry.Text, matching the 4-arg overload.
        //
        // <paramref name="layout"/> is the intra-panel geometry from the DIALSTYL.DAT resource
        // (DialogStyleTable.Layout) — where the speaker pill sits, how far down the body starts,
        // how thick the chrome edges are. It travels beside the style rather than being read from
        // a static so that a mod author's override document reaches it; see Shipped for what a
        // null means.
        public static VisualElement BuildPanel(
            DialogEntry entry, DialogStyle style, DialogLayout layout, Color[] palette, string resolvedText) {
            // The panel's canonical rect and the label font size are applied
            // by ApplyCanonicalLayout rather than baked in here. BuildPanel
            // only assembles the structure: flex layout, padding, debug
            // border, and the labels with their offsets/alignment/colour.
            var panel = new VisualElement {
                name = "BakDialogPanel",
                style = {
                    position = Position.Absolute,
                    // The panel element itself stays transparent; any chrome (fill,
                    // beveled border, drop shadow) is built as dedicated child layers
                    // by AddChrome below, BEFORE the text so it sits behind it. Styles
                    // whose effective DialogStyle has no chrome — the cutscene
                    // narrative strips (PlainWithoutBox / PlainFullScreen), whose
                    // wooden frame lives in the cutscene VGA buffer — add nothing here
                    // and keep the bare transparent panel, exactly as before.
                    // NOTE: overflow is left visible so the drop-shadow layer can
                    // extend a pixel beyond the panel's left/bottom edges.
                    backgroundColor = new StyleColor(Color.clear),
                    // Explicit flex: column with start-anchored children so margin-top
                    // on labels actually offsets them from the panel top instead of
                    // the labels growing to fill the available height (UI Toolkit's
                    // default theme stretches children on the cross-axis but the main
                    // axis can pick up flex-grow from the runtime stylesheet — pin it).
                    flexDirection = FlexDirection.Column,
                    justifyContent = Justify.FlexStart,
                    alignItems = Align.Stretch,
                    // No panel padding: every child (chrome layers + text labels) is
                    // absolutely positioned, so panel padding wouldn't inset them
                    // anyway. The text inset that keeps wrapped lines off the border is
                    // applied per-label from the resolved style's TextPadLeft/Right
                    // (the original's field_9/field_A), see AddDialogLabel.
                    paddingLeft = 0,
                    paddingRight = 0,
                    paddingTop = 0,
                    paddingBottom = 0
                }
            };

            // Debug: 1-px magenta outline around the resolved dialog area.
            // Visualises where the renderer thinks the panel is, so layout
            // bugs (viewport mapping, ResizeDialog conversion, etc.) are
            // obvious at a glance. Flip DrawDebugAreaBorder to disable.
            if (DrawDebugAreaBorder) {
                var debugColor = new StyleColor(Color.magenta);
                panel.style.borderTopColor = debugColor;
                panel.style.borderRightColor = debugColor;
                panel.style.borderBottomColor = debugColor;
                panel.style.borderLeftColor = debugColor;
                panel.style.borderTopWidth = 1;
                panel.style.borderRightWidth = 1;
                panel.style.borderBottomWidth = 1;
                panel.style.borderLeftWidth = 1;
            }

            AddChrome(panel, style, layout, palette, entry.Flags);
            BuildDialogText(panel, entry, style, layout, palette, resolvedText);

            return panel;
        }

        // The shipped geometry, used when a caller hands over no layout at all. A DialogLayout
        // constructed from its own defaults IS the faithful dialog (see the type's remarks), so
        // this is the same fallback DialogResourceLoader makes when the resource cannot be
        // loaded — not a second, quieter set of numbers.
        private static DialogLayout Shipped(DialogLayout layout) => layout ?? new DialogLayout();

        // Build the panel chrome — fill, beveled border, and drop shadow — from
        // the resolved DialogStyle's pens, the UI Toolkit equivalent of
        // dialog_DrawChrome (0x48632). Verified pixel-for-pixel against the
        // original (menu-dialog reference): a single 1px border that is
        // bevelled (highlight pen on TOP+RIGHT, border pen on LEFT+BOTTOM) plus
        // a 1px black drop shadow offset down-left, sitting just outside the
        // border on the left and bottom. Styles with no chrome (the cutscene
        // narrative rows, all pens 0) add nothing and the panel stays
        // transparent. Layers are absolute so they don't disturb the panel's
        // flex layout, and added before the text labels so they render behind.
        /// <summary>
        /// Add just the panel chrome — drop shadow, fill and bevelled border — to
        /// <paramref name="panel"/>, which the caller has already sized. Public so a screen that
        /// needs a dialog-styled box it draws its OWN content into can have the real chrome instead
        /// of approximating it: the inventory's "More Info" stat block sits on the same parchment as
        /// the description it replaces, but its content is a positioned two-column table rather than
        /// the flowing prose <see cref="BuildDialogText"/> lays out, so it reuses this half only.
        /// </summary>
        public static void BuildChrome(
            VisualElement panel, DialogStyle style, DialogLayout layout, Color[] palette,
            DialogEntryFlags flags = DialogEntryFlags.None) =>
            AddChrome(panel, style, layout, palette, flags);

        private static void AddChrome(VisualElement panel, DialogStyle style, DialogLayout layout,
            Color[] palette, DialogEntryFlags flags) {
            if (style == null || style is { UsesTexturedFill: false, HasBorder: false, HasDropShadow: false }) {
                return;
            }

            layout = Shipped(layout);

            // Drop shadow (pen 0 = black): a full-size layer behind the box,
            // nudged down-left so only its left + bottom edges peek out past the
            // border. Added first so the opaque box draws over the rest of it.
            if (style.HasDropShadow) {
                var shadow = new VisualElement {
                    name = "BakDialogShadow",
                    style = {
                        position = Position.Absolute,
                        left = 0,
                        top = 0,
                        right = 0,
                        bottom = 0,
                        // Down-LEFT: x negated, y not. One scalar for both axes — see
                        // DialogLayout.ChromeShadowOffset for why it carries the vertical factor
                        // on both, and why that is preserved rather than "corrected".
                        translate = new Translate(
                            new Length(-layout.ChromeShadowOffset), new Length(layout.ChromeShadowOffset)),
                        backgroundColor = new StyleColor(PaletteColors.ResolvePen(palette, 0))
                    }
                };
                panel.Add(shadow);
            }

            // Fill + bevelled border box, filling the panel rect.
            var box = new VisualElement {
                name = "BakDialogChrome",
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0
                }
            };
            if (style.UsesTexturedFill) {
                // Tileable wood sampled from OPTIONS0.SCX (see
                // GetPanelTile). Repeated at its native pixel size
                // so the grain matches the menu scroll behind it — reproducing
                // the original's "weave a strip of the background buffer" fill.
                // Falls back to a flat tone if the SCX could not be loaded.
                Texture2D tile = GetPanelTile();
                if (tile != null) {
                    box.style.backgroundImage = new StyleBackground(tile);
                    box.style.backgroundRepeat =
                        new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                    box.style.backgroundSize = new BackgroundSize(
                        new Length(tile.width), new Length(tile.height));
                    // Re-phase the weave per drawing, which is what the original's randomised
                    // source offset produces — unless the entry carries FixedStripePattern, whose
                    // whole job is to pin it. See DialogStripeFill: randomised is the DEFAULT and
                    // the flag turns it OFF, so reading it the other way round would freeze every
                    // ordinary dialog and animate only the ones meant to hold still.
                    int phase = DialogStripeFill.PhaseFor(flags, UnityEngine.Random.Range(0, 10000));
                    if (phase != 0) {
                        box.style.backgroundPositionX =
                            new BackgroundPosition(BackgroundPositionKeyword.Left, phase);
                    }
                } else {
                    box.style.backgroundColor = new StyleColor(StripeFillApprox);
                }
            }
            if (style.HasBorder) {
                // Bevelled single border: highlight (ShadowPenColor, e.g. pen 4
                // gold) on the lit TOP + RIGHT edges, border pen (pen 1, dark
                // brown) on the shaded LEFT + BOTTOM edges. HasDropShadow is
                // true whenever the highlight pen is non-zero.
                Color highlight = style.HasDropShadow
                    ? PaletteColors.ResolvePen(palette, style.ShadowPenColor)
                    : PaletteColors.ResolvePen(palette, style.BorderPenColor);
                var hi = new StyleColor(highlight);
                var lo = new StyleColor(PaletteColors.ResolvePen(palette, style.BorderPenColor));
                box.style.borderTopColor = hi;
                box.style.borderRightColor = hi;
                box.style.borderLeftColor = lo;
                box.style.borderBottomColor = lo;
                box.style.borderTopWidth = layout.ChromeBorderWidth;
                box.style.borderRightWidth = layout.ChromeBorderWidth;
                box.style.borderLeftWidth = layout.ChromeBorderWidth;
                box.style.borderBottomWidth = layout.ChromeBorderWidth;
            }
            panel.Add(box);
        }

        // Body-text render parameters, resolved exactly as the original engine
        // does in RenderDialogText (0x48d7b): the main body text is drawn in
        // `dialogTypeData.field_2` (every caller passes textColor = -1, so the
        // field is always used — 0x490ff), and a 1-pixel back-shadow in pen
        // `field_3 - 1` is drawn first when `field_3 != 0` (0x490bf-0x490ec).
        // Both are palette pen indices resolved against the active palette — no
        // hard-coded colours. Per-style results in the typical BaK palette:
        //   • ColoredWithoutBox → pen 0x0A (bright cream) + pen-1 shadow.
        //   • Normal / NormalInGame / PlainWithoutBox / PlainFullScreen →
        //     pen 0 (black), no shadow. (PlainWithoutBox is the cutscene
        //     narrative strip — black text, confirmed against the original.)
        // The chrome bevel (DialogStyle.HasDropShadow / ShadowPenColor =
        // field_5) is a *panel* concern, not the text shadow — it is
        // deliberately NOT consulted here.
        private static (Color textColor, bool hasShadow, Color shadowColor) ResolveTextRenderStyle(
            DialogStyle style, Color[] palette) {
            Color body = PaletteColors.ResolvePen(palette, style.BodyTextPenColor);
            if (style.HasTextShadow) {
                return (body, true, PaletteColors.ResolvePen(palette, style.TextShadowPenColor));
            }
            return (body, false, Color.clear);
        }

        private static void BuildDialogText(
            VisualElement panel, DialogEntry entry, DialogStyle style, DialogLayout layout,
            Color[] palette, string resolvedText) {
            layout = Shipped(layout);

            // Original RenderDialogText (0x48d7b) treats `#Name#` as a centered
            // title with drop shadow on its own line. We split the text into
            // an optional speaker label + body so each can carry its own
            // alignment/styling, and so future chrome (name banner sprite) can
            // hook onto a stable element name.
            string raw = resolvedText ?? entry.Text ?? string.Empty;
            string speaker = null;
            string body = raw;

            if (raw.Length > 0 && raw[0] == '#') {
                int end = raw.IndexOf('#', 1);
                if (end > 0) {
                    speaker = raw.Substring(1, end - 1);
                    body = raw.Substring(end + 1);
                }
            }

            // Character speech (ColoredWithoutBox) renders centered in its
            // area, typically with the speaker name above. Narrative
            // (PlainWithoutBox / Normal) is left-aligned with a leading
            // paragraph indent.
            //
            // The CenterText entry flag (0x0004) additionally centres each line
            // horizontally: RenderDialogText (0x49034-0x49040) rewrites the
            // style's alignment bitfield to (field_6 & 0xF8) | 2 when the flag is
            // set, and bit 0x02 is the per-line horizontal-centre bit in the
            // glyph-layout helper (font_DrawWrappedTextBlock at 0x4ba58). It is
            // what centres the chapter-intro box (dialog 294) and similar titled
            // panels. (The flag is *not* typographic justification — the renderer
            // has no flush-both-margins path — despite its former name.)
            bool centerText = entry.Flags.HasFlag(DialogEntryFlags.CenterText);
            bool centered = entry.DialogType == DialogType.ColoredWithoutBox || speaker != null || centerText;

            // Body text takes its colour + shadow from the resolved style's
            // pens; the speaker name always uses the engine's name pens
            // (0x0A main / 1 shadow), independent of the body style.
            var (bodyColor, bodyHasShadow, bodyShadowColor) = ResolveTextRenderStyle(style, palette);
            Color nameColor = PaletteColors.ResolvePen(palette, NameTextPenColor);
            Color nameShadowColor = PaletteColors.ResolvePen(palette, NameShadowPenColor);

            // Label `top` is an inset from the panel's top edge, in whatever unit the layout data
            // states it in (design-frame px by default, which is what the panel itself is
            // positioned/sized in). Fixed insets keep every dialog's text anchored the same
            // physical distance from the panel edge no matter how tall the resolved style happens
            // to be — no area-height denominators needed.
            StyleLength speakerTop = LayoutApplier.ToStyleLength(layout.SpeakerTop);
            StyleLength bodyTop = ResolveBodyTop(layout, style, hasSpeaker: speaker != null);

            if (speaker != null) {
                // ColoredWithoutBox names get the rounded "pill" bubble
                // (dialog_DrawNameBubble, 0x488a5 — the "Gorath" bubble); every
                // other style draws the name as a plain centered title (the
                // RenderDialogText '#'-title branch at 0x48e58 / 0x48e87). Both
                // paint the text in the name pens (0x0A main / 1 shadow).
                if (entry.DialogType == DialogType.ColoredWithoutBox) {
                    AddSpeakerPill(panel, speaker, layout, palette);
                } else {
                    AddDialogLabel(panel, speaker, "BakDialogSpeaker", nameColor,
                        FontStyle.Bold, TextAnchor.MiddleCenter, speakerTop,
                        true, nameShadowColor, layout, style.TextPadLeft, style.TextPadRight);
                }
            }

            // Bordered info boxes with no speaker (Normal / NormalInGame — the
            // menu's "Left clicking…" box and the chapter-intro box) vertically
            // CENTER their body text in the panel, matching the original engine
            // (every dialogTypeData row sets the field_6=0x10 centre-align bit).
            // A fixed viewport-relative top offset cannot work here: these
            // panels can be resized very short (chapter dialog 294 carries a
            // ResizeDialog to 15% of the viewport height — exactly
            // NarrativeBodyTopOffsetViewportPct), which drove the body to 100%
            // of the panel and spilled the text below the border. Centring is
            // both correct and resize-proof. Borderless narrative strips
            // (PlainWithoutBox / PlainFullScreen) keep their tuned top offset.
            // A full-screen parchment centres too: every g_dialog_style_table row carries 0x10,
            // which textwrap_draw_aligned reads as "centre the block" (TEXTWRAP.C:127-130).
            // Measured on the naphtha record (0x1b776e): four lines sit mid-page in the original.
            bool centerVertically = speaker == null
                && (style.HasBorder || entry.DialogType == DialogType.PlainFullScreen);
            TextAnchor bodyAlign = centerVertically
                ? (centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft)
                : (centered ? TextAnchor.UpperCenter : TextAnchor.UpperLeft);
            // *** A TextWithChoice RECORD KEEPS THE MENU ROW FREE ON EVERY PAGE. ***
            // `if (record->wFlags & 0x200) applied[3] -= 0x14` (DIALOG.C:645-647), before a line is
            // laid out — so the reserve belongs to the paging box, not to the page that happens to
            // show the buttons. See GameTextBlock.ChoiceMenuReserve. TASK-471.
            bool reservesChoiceRow = (entry.Flags & DialogEntryFlags.TextWithChoice) != 0;
            AddDialogBody(panel, DialogTextFormatter.Prepare(body, centered), palette,
                style.BodyTextPenColor, bodyColor, bodyAlign, bodyTop,
                bodyHasShadow, bodyShadowColor, layout, style.TextPadLeft, style.TextPadRight,
                centerVertically, style.TextPadTop, style.TextPadBottom, reservesChoiceRow);
        }

        /// <summary>
        /// The flowing body text, as a <see cref="GameTextBlock"/> — wrapped by the original's
        /// own rules and set at the original's line pitch, neither of which a single wrapping
        /// label can produce (see that type). The element keeps the name, placement, pens and
        /// shadow the body label had, so it is still the one thing a caller queries for "where
        /// the dialog's text is".
        /// </summary>
        private static void AddDialogBody(VisualElement panel, string prepared, Color[] palette,
            int bodyPen, Color color, TextAnchor align, StyleLength top,
            bool hasShadow, Color shadowColor, DialogLayout layout,
            float leftPad, float rightPad, bool centerVertically,
            float padTop = 0f, float padBottom = 0f, bool reserveChoiceMenu = false) {
            var block = new GameTextBlock {
                name = "BakDialogBody",
                style = {
                    position = Position.Absolute,
                    // Inset from the panel edges by the resolved style's text-area padding
                    // (DialogStyle.TextPadLeft/Right — the original's field_9/field_A applied in
                    // RenderDialogText at 0x49043-0x4905f). ABSOLUTE px, not a panel percentage:
                    // an entry's ResizeDialog replaces the area but not the inset, so a percentage
                    // widened the wrap region on every resized dialog. See TextPadLeft's remarks.
                    left = leftPad,
                    right = rightPad,
                    unityTextAlign = align,
                },
            };

            if (centerVertically) {
                // Span the panel and centre the stack of lines in it: the original's
                // (boxHeight - pitch * lineCount) / 2 for the 0x10 vertical-centre bit, which
                // every dialogTypeData row sets (0x4b9d8-0x4b9f8).
                block.style.top = 0;
                block.style.bottom = 0;
                block.style.justifyContent = Justify.Center;
            } else {
                block.style.top = top;
                block.style.justifyContent = Justify.FlexStart;
            }

            block.SetInk(color, hasShadow, new TextShadow {
                // Mirrors RenderDialogText's two-pass shadow: the back pass is drawn in
                // ShadowPenColor offset by 1 px before the main pen paints over it. UI Toolkit's
                // text-shadow composites the same way, so one shadow is enough.
                offset = new Vector2(layout.TextShadowOffsetX, layout.TextShadowOffsetY),
                color = shadowColor,
                blurRadius = 0,
            });
            // The vertical insets the ORIGINAL shrinks its text rect by before laying anything
            // out. They are not applied as layout here — the block's placement is already decided
            // above — only as the box a page is measured against. See GameTextBlock.SetPageBox.
            block.SetPageBox(padTop, padBottom, reserveChoiceMenu);
            block.SetContent(prepared, palette, bodyPen, align, GameFontText.DialogLineGapVgaRows);
            panel.Add(block);
        }

        /// <summary>
        /// Where the body text starts, as an inset from the panel's top edge.
        ///
        /// <para>With no speaker it is a datum outright (<c>NarrativeBodyTop</c>). With a speaker
        /// it is a SUM — <c>SpeakerTop</c> + one line of game text + <c>SpeakerToBodyGap</c> —
        /// which is the only arithmetic in this file. The middle term is
        /// <see cref="GameFontText.LineHeightPx"/>, GAME.FNT's 10 px character cell, because
        /// <c>SpeakerToBodyGap</c> is defined as the clearance below "the bottom of the speaker
        /// LINE". It used to be the body font SIZE, which stood in for the line height back when
        /// the two were confusable; they never were the same number, and once the aspect stretch
        /// arrived the font size stopped being a vertical measurement at all (task-46).</para>
        ///
        /// <para>That term is a design-frame px scalar, so the sum is a length only when
        /// <c>SpeakerTop</c> is px too: "4.1% + 60px + 120px" is not a length any single unit can
        /// carry, and stamping the bare numbers together would put the body somewhere neither the
        /// author nor the original asked for.</para>
        ///
        /// <para>So a percentage there is refused loudly and the body falls back to
        /// <c>NarrativeBodyTop</c> — a position the data really does state for "where the body
        /// starts" — rather than to a fabricated number. Auto is fine: <c>LayoutApplier.Derived</c>
        /// already degrades it to the design frame's own px, which is the space the line height is
        /// in.</para>
        ///
        /// <para><c>SpeakerToBodyGap</c> needs no check: it is a plain <c>float</c> of
        /// design-frame px precisely BECAUSE it can only ever be a term in this sum, so there is
        /// no unit for an author to state and nothing to refuse. See its remarks on
        /// <c>DialogLayout</c>.</para>
        /// </summary>
        /// <summary>
        /// The speaker-less body's top inset: the author's value if they set one, otherwise the
        /// style row's own <c>field_7</c>.
        /// </summary>
        /// <remarks>
        /// <b>Auto means "not overridden", not "zero".</b> The original reads this inset off the
        /// style row (<c>y += field_7</c> at 0x49050), and the rows disagree — 1 VGA px for the
        /// full-screen row against 5 for the strips — so the shipped default cannot live on
        /// <see cref="DialogLayout"/>, which has one value for every style. It lives on the row and
        /// the layout knob overrides it.
        ///
        /// <para>Keeping the knob matters: it is a <see cref="LayoutLength"/> precisely so an
        /// author can restate the inset as a percentage and have the unit survive to the rendered
        /// element. Reading the row unconditionally would silently flatten that to px.</para>
        /// </remarks>
        private static StyleLength NarrativeTop(DialogLayout layout, DialogStyle style) =>
            layout != null && layout.NarrativeBodyTop.Unit != LayoutLengthUnit.Auto
                ? LayoutApplier.ToStyleLength(layout.NarrativeBodyTop)
                : new StyleLength(style.TextPadTop);

        private static StyleLength ResolveBodyTop(
            DialogLayout layout, DialogStyle style, bool hasSpeaker) {
            if (!hasSpeaker) {
                return NarrativeTop(layout, style);
            }

            if (!LayoutApplier.IsDesignPx(layout.SpeakerTop)) {
                LayoutApplier.RefuseUnresolvable(
                    "DialogLayout.SpeakerTop / SpeakerToBodyGap",
                    "SpeakerTop " + layout.SpeakerTop + ", SpeakerToBodyGap " + layout.SpeakerToBodyGap,
                    "the body's top inset under a speaker is SpeakerTop + one line of game text ("
                    + GameFontText.LineHeightPx + "px) + SpeakerToBodyGap, and a percentage cannot "
                    + "be added to a px line height without measuring the panel — the body falls "
                    + "back to NarrativeBodyTop (" + layout.NarrativeBodyTop
                    + ") and may overlap the speaker");
                return NarrativeTop(layout, style);
            }

            return LayoutApplier.Derived(
                layout.SpeakerTop.Value + GameFontText.LineHeightPx + layout.SpeakerToBodyGap,
                layout.SpeakerTop);
        }

        private static void AddDialogLabel(VisualElement panel, string text, string name, Color color,
            FontStyle weight, TextAnchor align, StyleLength top,
            bool hasShadow, Color shadowColor, DialogLayout layout,
            float leftPad, float rightPad) {
            var label = new Label(text) {
                name = name,
                style = {
                    color = color,
                    unityFontStyleAndWeight = weight,
                    unityTextAlign = align,
                    whiteSpace = WhiteSpace.Normal,
                    position = Position.Absolute,
                    // Inset the text from the panel edges by the resolved style's
                    // text-area padding (DialogStyle.TextPadLeft/Right — the original's
                    // field_9/field_A applied in RenderDialogText at 0x49043-0x4905f). The
                    // label is absolutely positioned, so left/right are measured from the
                    // panel's padding box; setting them to 0 (the old behaviour) made
                    // wrapped text run flush to the border. Absolute px, matching the
                    // original's byte — see TextPadLeft's remarks for why not a percentage.
                    left = leftPad,
                    right = rightPad
                }
            };
            // Body text may carry inline <i>/<color> markup emitted by
            // DialogTextFormatter for the original engine's per-word
            // italic/highlight control codes. enableRichText defaults true,
            // but pin it so the tags are always parsed rather than shown
            // verbatim.
            label.enableRichText = true;
            label.style.top = top;
            // The game font's size and aspect stretch, from their single owner. The stretch needs
            // the edge the text is anchored from; this label is placed by its top.
            GameFontText.Apply(label, GameFontText.HorizontalAnchor(align), GameFontText.AnchorY.Top);
            if (hasShadow) {
                // Mirrors RenderDialogText's two-pass shadow at 0x48d7b: the
                // back pass is drawn in ShadowPenColor offset by 1 px before
                // the main pen color paints over it. UI Toolkit's text-shadow
                // composites the same way (back-buffer offset, then text on
                // top), so a single textShadow is enough — no second label.
                label.style.textShadow = new StyleTextShadow(new TextShadow {
                    offset = new Vector2(layout.TextShadowOffsetX, layout.TextShadowOffsetY),
                    color = shadowColor,
                    blurRadius = 0,
                });
            }
            panel.Add(label);
        }

        // Build the rounded "pill" name bubble that sits behind a
        // ColoredWithoutBox speaker name — the Unity equivalent of
        // dialog_DrawNameBubble (0x488a5). The original draws it from primitives
        // (two filled cap circles + a bordered box + edge lines); UI Toolkit
        // gives us the same silhouette directly with a border-radius'd element,
        // so we render a centered, content-hugging pill instead of the geometry.
        // Pens (resolved against the active palette): fill = 0x0B, outline =
        // 0x0F, text = 0x0A with a pen-1 shadow.
        private static void AddSpeakerPill(
            VisualElement panel, string speaker, DialogLayout layout, Color[] palette) {
            layout = Shipped(layout);

            // Absolute, full-width row that centres the pill horizontally (original x≈160) and
            // anchors its TOP just inside the dialog area's top edge, so the whole pill sits
            // within the area — below the cutscene image — and never overlaps the cutscene
            // viewport. Placed by the same single translator every other screen's layout goes
            // through, from DialogLayout.SpeakerPillRow.
            var row = new VisualElement { name = "BakDialogSpeakerRow" };
            LayoutApplier.Apply(row, layout.SpeakerPillRow);

            // Stack so a drop-shadow element can sit behind the pill, offset
            // down-right — the original lays a 1px pen-1 rim under the bubble
            // for a raised look. The pill is the in-flow child, so the stack
            // sizes to it; the shadow is absolute at 100% of that size.
            var stack = new VisualElement {
                name = "BakDialogSpeakerPillStack",
                style = {
                    flexDirection = FlexDirection.Row
                }
            };

            var shadow = new VisualElement {
                name = "BakDialogSpeakerPillShadow",
                style = {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0,
                    // Down-RIGHT, one scalar for both axes — see
                    // DialogLayout.SpeakerPillShadowOffset.
                    translate = new Translate(
                        layout.SpeakerPillShadowOffset, layout.SpeakerPillShadowOffset),
                    backgroundColor = new StyleColor(PaletteColors.ResolvePen(palette, NameShadowPenColor))
                }
            };

            // The pill: bordered, padded box. Radius is set to half the resolved
            // height in the geometry callback below (a fixed half-height radius
            // gives a true stadium; a too-large radius would clamp per-axis into
            // an ellipse). Hugs its label plus generous horizontal padding,
            // mirroring the original's width = getStringWidthInPixels(name) + 10
            // plus the semicircular caps.
            var pill = new VisualElement {
                name = "BakDialogSpeakerPill",
                style = {
                    backgroundColor = new StyleColor(PaletteColors.ResolvePen(palette, NameBubbleFillPen))
                }
            };
            // In-flow (the row places it), centring flow for its label, and the padding it hugs
            // that label with — all from DialogLayout.SpeakerPill, through the same translator.
            LayoutApplier.Apply(pill, layout.SpeakerPill);

            var borderColor = new StyleColor(PaletteColors.ResolvePen(palette, NameBubbleBorderPen));
            pill.style.borderTopColor = borderColor;
            pill.style.borderRightColor = borderColor;
            pill.style.borderBottomColor = borderColor;
            pill.style.borderLeftColor = borderColor;
            // Its own datum, not ChromeBorderWidth's: the pill rim and the panel bevel come from
            // two different routines in the original and must stay separately authorable, even
            // though the shipped data gives both the same width. (This was the bare `6` literal
            // that moved with neither.)
            pill.style.borderTopWidth = layout.SpeakerPillBorderWidth;
            pill.style.borderRightWidth = layout.SpeakerPillBorderWidth;
            pill.style.borderBottomWidth = layout.SpeakerPillBorderWidth;
            pill.style.borderLeftWidth = layout.SpeakerPillBorderWidth;

            // Recompute the stadium radius whenever the pill's size changes (font apply, resize)
            // — radius = half the height.
            stack.RegisterCallback<GeometryChangedEvent>(_ => {
                float radius = pill.resolvedStyle.height / 2f;
                SetPillRadius(pill, radius);
                SetPillRadius(shadow, radius);
            });

            var label = new Label(speaker) {
                name = "BakDialogSpeaker",
                style = {
                    color = PaletteColors.ResolvePen(palette, NameTextPenColor),
                    unityFontStyleAndWeight = FontStyle.Normal,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    whiteSpace = WhiteSpace.NoWrap,
                    textShadow = new StyleTextShadow(new TextShadow {
                        offset = new Vector2(layout.TextShadowOffsetX, layout.TextShadowOffsetY),
                        color = PaletteColors.ResolvePen(palette, NameShadowPenColor),
                        blurRadius = 0,
                    })
                }
            };
            // The pill hugs this label's UNSTRETCHED box, so the stretched glyphs grow a sixth of
            // a line beyond it — 5 canonical px at each edge, which the pill's 18 px vertical
            // padding absorbs. Centred, because the pill centres its label rather than placing it
            // by an edge.
            GameFontText.Apply(label, GameFontText.AnchorX.Centre, GameFontText.AnchorY.Middle);

            pill.Add(label);
            stack.Add(shadow);   // behind (added first → drawn under the pill)
            stack.Add(pill);
            row.Add(stack);
            panel.Add(row);
        }

        private static void SetPillRadius(VisualElement element, float radius) {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}

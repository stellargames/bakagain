namespace BakAgain.UI {
    using System.Collections.Generic;
    using BakAgain.Graphics;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A block of flowing game-font text laid out the way <c>font_DrawWrappedTextBlock</c>
    /// (<c>0x4b93f</c>) lays one out: broken into lines by <see cref="GameTextWrapper"/>, then
    /// placed one under the other at a pitch of <c>fontHeight + lineGapExtra</c>.
    ///
    /// <para><b>Why a stack of labels rather than one wrapping label.</b> The pitch and the break
    /// points are both the original's data, and UI Toolkit will give us neither: it has no
    /// line-height property at all, and the one spacing knob that reaches explicit breaks —
    /// <c>unityParagraphSpacing</c> — resolves through the text engine at about 0.55x the value
    /// set (measured: 5 -> 2.7 px, 55 -> 30 px at font size 55), a ratio that appears nowhere in
    /// the game data and would drift with the font asset. Placing each line is the only way the
    /// number on screen stays the number the original states.</para>
    ///
    /// <para>Each line is a <see cref="Label"/> exactly one pitch tall, drawing its text from the
    /// top of that box, so consecutive glyph cells sit <see cref="GameFontText.LinePitchPx"/>
    /// apart. The block sizes to the sum, which lets the vertical alignment be plain flex
    /// justification — and <c>justify-content: center</c> is literally the original's
    /// <c>(boxHeight - pitch * lineCount) / 2</c> (<c>0x4b9d8</c>-<c>0x4b9f8</c>).</para>
    ///
    /// <para>Wrapping needs a width, which UI Toolkit only knows after layout, so the block
    /// re-flows from <see cref="GeometryChangedEvent"/> whenever its resolved width changes. It
    /// is empty until then — nothing to draw is the honest state before we know how wide the box
    /// is, and the alternative (rendering unwrapped prose first) would flash the wrong breaks.</para>
    /// </summary>
    internal sealed class GameTextBlock : VisualElement {
        /// <summary>The name every line label carries, so a caller can query the rendered lines.</summary>
        internal const string LineName = "GameTextLine";

        private string _text = string.Empty;
        private Color[] _palette;
        private int _bodyPen;
        private GameFontText.AnchorX _lineAnchor = GameFontText.AnchorX.Left;
        private TextAnchor _lineAlign = TextAnchor.UpperLeft;
        private int _lineGapVgaRows = GameFontText.DialogLineGapVgaRows;
        private Color _color = Color.white;
        private bool _hasShadow;
        private TextShadow _shadow;
        private int _flowedAtWidth = -1;
        private int _flowedFit = -1;
        private int _flowedFirstLine = -1;
        private bool _paginate;
        private int _firstLine;
        private int _linesDrawn;
        private int _linesRemaining;
        private float _pagePadTop;
        private float _pagePadBottom;
        private bool _reserveChoiceMenu;
        private float _appliedNudge;

        internal GameTextBlock() {
            style.flexDirection = FlexDirection.Column;
            RegisterCallback<GeometryChangedEvent>(_ => Reflow());
        }

        /// <summary>
        /// Draw only the lines that fit the box and report the rest, instead of laying every line
        /// out and letting the panel's <c>overflow: hidden</c> cut them off.
        /// </summary>
        /// <remarks>
        /// <b>Opt-in, because the caller has to drive it.</b>
        /// <c>dialog_render_text_with_tokens</c> takes a <c>scroll_start</c> and leaves
        /// <c>g_wTextWrapLinesDrawn</c> / <c>g_wTextWrapLinesRemaining</c> behind for the play loop
        /// to page on (DIALOG.C:1361-1384) — a renderer that clipped on its own, with nobody
        /// advancing <see cref="FirstLine"/>, would silently LOSE the tail rather than merely hide
        /// it. So a block pages only where something calls <see cref="AdvancePage"/>.
        /// </remarks>
        internal bool Paginate {
            get => _paginate;
            set {
                if (_paginate == value) {
                    return;
                }
                _paginate = value;
                _flowedAtWidth = -1;
                Reflow();
            }
        }

        /// <summary>
        /// The style's own vertical text insets, which bound the box a page is measured against.
        /// </summary>
        /// <remarks>
        /// <c>RenderDialogText</c> shrinks the rect before laying anything out —
        /// <c>applied[1] += field_7; applied[3] -= field_7 + field_8</c> (0x49050, 0x4906e) — so
        /// the height the line count is computed from is the panel's LESS both insets.
        ///
        /// <para><b>Worth 36 canonical px and one whole line.</b> The shipped row-2 box is 101 VGA
        /// rows with 3-row insets: 101 fits nine lines, 95 fits eight. Measured on the Mac Mordain
        /// Cadal dwarf (id 2700045), whose first page in the original ends at "Now go back the way
        /// ya come" — the eighth line — while a port paging on the full panel height showed nine.
        /// </para>
        ///
        /// <para>They are not applied as layout here, only as the paging box: the block's vertical
        /// PLACEMENT is the caller's (a centred block spans the panel and equal insets would not
        /// move it anyway), and changing that would shift shipped pixels for the rows whose insets
        /// differ top from bottom.</para>
        /// </remarks>
        internal void SetPageBox(float padTop, float padBottom, bool reserveChoiceMenu = false) {
            _pagePadTop = padTop;
            _pagePadBottom = padBottom;
            _reserveChoiceMenu = reserveChoiceMenu;
            _flowedAtWidth = -1;
        }

        /// <summary>
        /// The menu row a <c>TextWithChoice</c> record keeps free, in canonical units.
        /// </summary>
        /// <remarks>
        /// <b>Reserved on EVERY page, not only the one that shows the buttons.</b>
        /// <c>RenderDialogText</c>'s choice arm is
        /// <c>if (record-&gt;wFlags &amp; 0x200) { ...; applied[3] -= 0x14; ... }</c>
        /// (DIALOG.C:645-652) — 0x14 VGA rows, so 20 * 6 = 120 canonical — and it runs before a
        /// single line is laid out. Measured 2026-09-13 on `DIAL_Z19` 1900104 (1022 characters,
        /// the Dimwood guards): the original fits 11 lines on page one and the port fitted 13,
        /// which is this reserve plus the nudge below. TASK-471.
        /// </remarks>
        internal const float ChoiceMenuReserve = 120f;

        /// <summary>
        /// How far the text starts DOWN the box when a choice record still will not fit.
        /// </summary>
        /// <remarks>
        /// <c>if (applied[3] &lt; lines * (fontHeight + 1)) applied[1] += 10;</c> — 10 VGA rows, 60
        /// canonical, and it moves the text ORIGIN without touching <c>applied[3]</c>. So the page
        /// still holds the same number of lines and simply sits lower; applying it as padding
        /// rather than as a smaller box is what keeps that true here.
        ///
        /// <para>The test is against the lines still REMAINING (<c>textwrap_compute_lines(...) -
        /// scroll_start</c>), not against the page — so a record nudges while it has more to show
        /// and stops once the last page fits.</para>
        /// </remarks>
        internal const float ChoiceOverflowTopNudge = 60f;

        /// <summary>The first wrapped line drawn — the original's <c>scroll_start</c>.</summary>
        internal int FirstLine => _firstLine;

        /// <summary>How many lines the last re-flow drew (<c>g_wTextWrapLinesDrawn</c>).</summary>
        internal int LinesDrawn => _linesDrawn;

        /// <summary>How many are still unshown (<c>g_wTextWrapLinesRemaining</c>).</summary>
        internal int LinesRemaining => _linesRemaining;

        /// <summary>
        /// Turn to the next page: <c>i += g_wTextWrapLinesDrawn</c>, then re-render from line
        /// <c>i</c> (DIALOG.C:1371-1374).
        /// </summary>
        /// <returns>True if lines still remain after this page.</returns>
        internal bool AdvancePage() {
            if (_linesRemaining <= 0) {
                return false;
            }
            _firstLine += _linesDrawn;
            _flowedAtWidth = -1;
            Reflow();
            return _linesRemaining > 0;
        }

        /// <summary>The lines the last re-flow produced, in order.</summary>
        internal IEnumerable<Label> Lines {
            get {
                foreach (VisualElement child in Children()) {
                    if (child is Label line) {
                        yield return line;
                    }
                }
            }
        }

        /// <summary>
        /// Set what the block draws. <paramref name="text"/> must already have been through
        /// <see cref="DialogTextFormatter.Prepare"/>: its inline formatting codes are still in
        /// place, which is what lets the wrap measure them at the zero width the original's own
        /// metrics give them.
        /// </summary>
        internal void SetContent(
            string text, Color[] palette, int bodyPen, TextAnchor align, int lineGapVgaRows) {
            _text = text ?? string.Empty;
            _palette = palette;
            _bodyPen = bodyPen;
            _lineAlign = HorizontalOnly(align);
            _lineAnchor = GameFontText.HorizontalAnchor(align);
            _lineGapVgaRows = lineGapVgaRows;
            _firstLine = 0;
            _flowedAtWidth = -1;
            Reflow();
        }

        /// <summary>
        /// The pen the lines paint in, and the 1-px back-shadow pass under them
        /// (<c>RenderDialogText</c> at <c>0x490bf</c>-<c>0x490ec</c>). Carried on the block as
        /// well as on each line: the block is the element callers style and query, the lines are
        /// what actually renders.
        /// </summary>
        internal void SetInk(Color color, bool hasShadow, TextShadow shadow) {
            _color = color;
            _hasShadow = hasShadow;
            _shadow = shadow;
            style.color = color;
            if (hasShadow) {
                style.textShadow = new StyleTextShadow(shadow);
            }
            foreach (Label line in Lines) {
                ApplyInk(line);
            }
        }

        private void Reflow() {
            float width = resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) {
                return;
            }

            // The original's box width is a whole VGA number and its overflow test is an integer
            // comparison, so the wrap runs in that space rather than in canonical px.
            int maxWidth = Mathf.FloorToInt(width / Canonical.VgaScaleX);
            if (maxWidth <= 0) {
                return;
            }

            float pitch = GameFontText.LinePitchPx(_lineGapVgaRows);
            int fit = int.MaxValue;
            float roomForFit = float.MaxValue;
            if (_paginate) {
                float room = AvailableHeight();
                roomForFit = room;
                if (float.IsNaN(room) || room <= 0f) {
                    return; // no box to fit to yet; the next geometry pass has one
                }
                // *** THE LAST LINE NEEDS NO TRAILING GAP. *** TEXTWRAP.C:115-120 grows
                // `remaining` while `max_height < (lh + ls) * n - ls`, so n lines fit whenever
                // `pitch * n - ls <= room` — one more line than `room / pitch` allows whenever the
                // remainder is at least a line minus the gap.
                //
                // Quantised to whole lines on purpose: sub-pixel jitter in the resolved height
                // would otherwise re-enter this from the GeometryChangedEvent every frame.
                float gap = _lineGapVgaRows * Canonical.VgaScaleY;
                fit = Mathf.Max(1, Mathf.FloorToInt((room + gap) / pitch));
            }

            if (maxWidth == _flowedAtWidth && fit == _flowedFit && _firstLine == _flowedFirstLine) {
                return;
            }
            _flowedAtWidth = maxWidth;
            _flowedFit = fit;
            _flowedFirstLine = _firstLine;

            Clear();
            _linesDrawn = 0;
            _linesRemaining = 0;
            if (_text.Length == 0) {
                return;
            }

            List<GameTextWrapper.Line> lines = GameTextWrapper.Wrap(_text, maxWidth);
            int start = Mathf.Clamp(_firstLine, 0, Mathf.Max(0, lines.Count - 1));
            // *** A CHOICE RECORD THAT STILL WILL NOT FIT STARTS LOWER. *** See
            // ChoiceOverflowTopNudge: the original tests the lines REMAINING against the already
            // reduced box and moves the text origin, not the box, so the page keeps the same line
            // count and simply sits further down. Padding is the element equivalent —
            // AvailableHeight reads `layout.y`, which padding does not move.
            float nudge = _reserveChoiceMenu && _paginate
                && roomForFit < (lines.Count - start) * pitch
                    ? ChoiceOverflowTopNudge
                    : 0f;
            // Written only when it CHANGES. A style write invalidates layout, and this runs from
            // the geometry callback — re-writing the same value every pass kept the block
            // re-laying out, and a caller that reads LinesRemaining one frame later then read it
            // before the block had settled.
            if (!Mathf.Approximately(_appliedNudge, nudge)) {
                _appliedNudge = nudge;
                style.paddingTop = nudge;
            }
            int end = fit == int.MaxValue ? lines.Count : Mathf.Min(lines.Count, start + fit);
            // *** NO SINGLE-LINE ORPHAN. *** `if (remaining == 1) remaining++` (TEXTWRAP.C:121) —
            // a page that would leave exactly one line behind gives that page's last line up
            // instead, so the next page is never a lone line.
            if (lines.Count - end == 1 && end - start > 1) {
                end--;
            }
            for (int i = start; i < end; i++) {
                Add(BuildLine(
                    DialogTextFormatter.Format(_text, lines[i].Start, lines[i].End, _palette, _bodyPen),
                    pitch));
            }
            _linesDrawn = end - start;
            _linesRemaining = lines.Count - end;
        }

        /// <summary>
        /// The vertical room the block has, which is NOT its own resolved height.
        /// </summary>
        /// <remarks>
        /// The body block is <c>position: absolute</c> with a <c>top</c> and, in the common case,
        /// no <c>bottom</c> — so it sizes to its CONTENT and its own height grows with the text
        /// instead of bounding it. The room is the panel's height less where the block starts in
        /// it. The vertically-centred case (<c>top: 0; bottom: 0</c>) is already bounded and gives
        /// the same answer through the same expression, since its offset is zero.
        /// </remarks>
        private float AvailableHeight() {
            VisualElement box = hierarchy.parent;
            if (box == null) {
                return resolvedStyle.height;
            }
            // The choice row is taken off the box BEFORE the fit is computed — see
            // ChoiceMenuReserve. It comes off the bottom, which is where the buttons go.
            float reserve = _reserveChoiceMenu ? ChoiceMenuReserve : 0f;
            // The text box starts at whichever is LOWER — the style's own top inset, or where the
            // body actually begins (below a speaker pill it is well below the inset) — and ends at
            // the style's bottom inset. A centred block has layout.y == 0 and gets both insets; a
            // top-anchored one already sits at the inset and must not be charged for it twice.
            float top = Mathf.Max(layout.y, _pagePadTop);
            return box.resolvedStyle.height - top - _pagePadBottom - reserve;
        }

        private Label BuildLine(string markup, float pitch) {
            var label = new Label(markup) {
                name = LineName,
                style = {
                    // One pitch tall with the text drawn from its top edge: the box IS the line
                    // advance, so the next line's glyphs start exactly one pitch lower.
                    height = pitch,
                    flexShrink = 0,
                    unityTextAlign = _lineAlign,
                    // The wrap already decided where this line ends. Letting the text engine
                    // re-break it would put a second row of glyphs inside a one-line box.
                    // Pre, not NoWrap: both refuse to re-break the line, but NoWrap still
                    // collapses runs of spaces and trims the leading ones, which is where a
                    // paragraph's indent lives.
                    whiteSpace = WhiteSpace.Pre,
                    unityFontStyleAndWeight = FontStyle.Normal,
                },
            };
            label.enableRichText = true;
            GameFontText.Apply(label, _lineAnchor, GameFontText.AnchorY.Top);
            ApplyInk(label);
            return label;
        }

        private void ApplyInk(Label line) {
            line.style.color = _color;
            if (_hasShadow) {
                line.style.textShadow = new StyleTextShadow(_shadow);
            }
        }

        // Each line is drawn from the top of its own one-pitch box, so only the horizontal half
        // of the caller's alignment applies to it; the vertical half is the block's flex
        // justification, which the caller sets.
        private static TextAnchor HorizontalOnly(TextAnchor align) => align switch {
            TextAnchor.UpperCenter or TextAnchor.MiddleCenter or TextAnchor.LowerCenter
                => TextAnchor.UpperCenter,
            TextAnchor.UpperRight or TextAnchor.MiddleRight or TextAnchor.LowerRight
                => TextAnchor.UpperRight,
            _ => TextAnchor.UpperLeft,
        };
    }
}

namespace BakAgain.UI {
    using BakAgain.Book;
    using BakAgain.Graphics;
    using GameData.Resources.Dialog;
    using System.Collections.Generic;
    using System.Text;
    using UnityEngine;

    /// <summary>
    /// Parses the original engine's per-glyph inline control codes embedded in
    /// DDX text and emits UI Toolkit rich-text markup (<c>&lt;i&gt;</c> /
    /// <c>&lt;color&gt;</c>) for the dialog renderer.
    /// <para>
    /// DDX text is decoded from DOS code page 437, so the original engine's
    /// single-byte formatting codes (high nibble <c>0xF</c>, low nibble = case)
    /// surface in the extracted string as CP437 glyphs. They are <b>not</b>
    /// literal characters: the per-glyph printer <c>drawCharacter</c>
    /// (<c>0x15e9f</c>) intercepts them and the low nibble selects one of six
    /// cases that toggle italic shear (font_flags bit <c>0x04</c>, applied as
    /// a 1px-per-3-rows slant in <c>sub_seg012_1A8</c> at <c>0x15d4f</c> /
    /// <c>0x15e69</c>) and/or remap the text pen.
    /// </para>
    /// <para>
    /// Faithful to the engine in two non-obvious ways:
    /// <list type="bullet">
    /// <item>Italic + pen changes are scoped <b>per word</b>.
    ///   <c>drawTextString</c> (<c>0x160ee</c>) emits a <c>0xFFF0</c> "reset"
    ///   before drawing every space, restoring the default pen and clearing
    ///   italic — which is why each highlighted word in the data carries its
    ///   own marker (<c>±Into ±a ±Dark ±Night</c>). State is reset at every
    ///   space and newline.</item>
    /// <item>The pen remap is stateful and depends on the current pen, matching
    ///   the <c>drawCharacter</c> case logic (<c>0x15eef</c>–<c>0x15fa5</c>).
    ///   For the common black-bodied dialog (pen 0) a <c>±</c> resolves to pen
    ///   5 — the cream/tan highlight seen on "Into a Dark Night" in the
    ///   chapter-intro box.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class DialogTextFormatter {

        /// <summary>
        /// Turn the raw DDX text into a display string: interpret the inline
        /// control codes as rich-text spans, apply the original tab / indent
        /// handling, and resolve pen indices against <paramref name="palette"/>.
        /// </summary>
        /// <param name="raw">The DDX entry's text as extracted (CP437-decoded).</param>
        /// <param name="centered">
        /// True for centered title/speech text — tabs are stripped and the
        /// string is fully trimmed. False for left-aligned narrative — tabs
        /// become 8-space indents and only the trailing whitespace is trimmed
        /// so the leading paragraph indent survives.
        /// </param>
        /// <param name="palette">Active palette used to resolve pen indices to RGB.</param>
        /// <param name="bodyPen">The body text's default pen, used as the
        /// "reset" pen between spaces and at the start.</param>
        /// <summary>
        /// The tab's advance in raw FNT px. <c>getCharMetrics</c> gives <c>'\t'</c> the current
        /// <c>tabWidth</c> global (<c>0x16176</c>) — a FIXED advance, not a tab stop — and
        /// <c>RenderDialogText</c> loads that global from <c>dialogTypeData.field_B</c> before
        /// every draw (<c>0x490b7</c>). Read off the raw table at <c>0x3a831</c>, all six style
        /// rows carry 0x14 there, so the dialog tab is 20 px whichever style resolves.
        /// </summary>
        private const int TabWidthPx = 0x14;

        /// <summary>
        /// Resolve the raw DDX text's whitespace, leaving the inline control codes in place for
        /// <see cref="Format(string,int,int,Color[],int)"/> — and, in between, for the wrap to
        /// measure at their true zero width.
        ///
        /// <para>The tab becomes the number of spaces that carries the same advance. That is an
        /// identity, not an approximation: <see cref="TabWidthPx"/> is 20 and GAME.FNT's space is
        /// 4, so five spaces measure exactly what the original's tab does — and since the
        /// paragraph indent sits at the head of a line, where a break can never fall, standing in
        /// with break-able characters costs nothing. (It was eight spaces, 60% too wide, for as
        /// long as nothing had read <c>field_B</c>.)</para>
        /// </summary>
        public static string Prepare(string raw, bool centered) {
            if (string.IsNullOrEmpty(raw)) {
                return string.Empty;
            }
            // Centered title/speech text drops the indent entirely; left-aligned narrative keeps
            // it. Neither trims a leading newline: after "#Name#" it is what drops the body a line
            // below the name (C61's centred poem was drawn through Pug's pill).
            if (centered) {
                return raw.Replace("\t", string.Empty).TrimStart(' ').TrimEnd();
            }
            string tab = new string(' ', TabWidthPx / BakFontData.GetRawCharWidth(' ', BakFontData.GameFontIndex));
            // Whitespace-only lines at the end are still lines to the original's wrap, and count
            // when the block is centred: 0x84 ends in three, the space the assessment rows are drawn
            // into (TASK-742). Only that record has them; anything else trims as before.
            if (System.Text.RegularExpressions.Regex.IsMatch(raw, "(\n[ \t]+)+$")) {
                return raw.Replace("\t", tab).TrimEnd('\r', '\n');
            }
            return raw.Replace("\t", tab).TrimEnd();
        }

        /// <summary>
        /// Whitespace-resolve and style in one step, for callers that render the text as a single
        /// unwrapped run.
        /// </summary>
        public static string Format(string raw, bool centered, Color[] palette, int bodyPen) {
            string prepared = Prepare(raw, centered);
            return Format(prepared, 0, prepared.Length, palette, bodyPen);
        }

        /// <summary>
        /// Emit the rich-text markup for <c>[start, end)</c> of an already-<see cref="Prepare"/>d
        /// string.
        ///
        /// <para>Styling a RANGE rather than the whole string is what lets the wrap run first.
        /// It is also how the original works: <c>font_DrawWrappedTextBlock</c> calls
        /// <c>drawTextString</c> once per wrapped line (<c>0x4bad9</c>), so each line is styled
        /// from the default pen on its own — which is the same thing this does, because state is
        /// reset at every space and line boundary anyway.</para>
        /// </summary>
        public static string Format(string text, int start, int end, Color[] palette, int bodyPen) {
            if (string.IsNullOrEmpty(text) || end <= start) {
                return string.Empty;
            }

            // WHICH characters are styled how is DDX decoding and lives in GameData; what a pen
            // looks like and how a run is marked up is ours. Runs are maximal, so one tag pair
            // covers a whole styled word instead of wrapping every glyph.
            List<DialogTextRuns.Run> runs = DialogTextRuns.Decode(text, start, end, bodyPen);
            var sb = new StringBuilder(end - start + 16);
            foreach (DialogTextRuns.Run run in runs) {
                bool coloured = run.Pen != bodyPen;
                if (coloured) {
                    sb.Append("<color=#");
                    sb.Append(ColorUtility.ToHtmlStringRGB(PaletteColors.ResolvePen(palette, run.Pen)));
                    sb.Append('>');
                }
                if (run.Italic) {
                    sb.Append("<i>");
                }
                sb.Append(text, run.Start, run.Length);
                if (run.Italic) {
                    sb.Append("</i>");
                }
                if (coloured) {
                    sb.Append("</color>");
                }
            }
            return sb.ToString();
        }
    }
}

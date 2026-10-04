namespace GameData.Resources.Dialog;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Decodes DDX inline formatting into styled runs — <c>drawCharacter</c> @0x15eef-0x15fa5.
///
/// <para>DDX text carries six formatting control bytes (0xF0-0xF5) inline. The format carries them
/// as tags and the renderer as private-use characters — see <see cref="FromCp437"/> and
/// <see cref="FromMarkup"/>. They are a small stateful machine over italic and the current
/// pen, not a markup language: two of them REMAP the pen relative to whatever it currently is, so a
/// consumer cannot interpret one in isolation.</para>
///
/// <para><b>What this does not do.</b> Turning a pen into a colour and a run into markup is the
/// consumer's job — this layer never sees a palette. It answers only "which characters are styled
/// how".</para>
/// </summary>
public static class DialogTextRuns {
    // *** THREE FORMS OF ONE CODE (TASK-774). ***
    //  - the original: a byte 0xE0-0xFF, which the CP437 decode surfaces as a glyph;
    //  - the format (generated JSON, mod files, PO packs): an explicit tag, so a translation can
    //    use ß and Greek as the letters they are;
    //  - at run time: a private-use character, U+E000 + the code's low nibble — zero width in
    //    every font, never a break point, and no consumer has to know about tags.
    // The extractor converts the first to the second (FromCp437), the loader the second to the
    // third (FromMarkup).

    /// <summary>0xF0 — italic off, pen back to the body default.</summary>
    public const char Reset = '\uE000';

    /// <summary>0xF1 / 0xF2 — italic on, with a pen shift. The original shares the case.</summary>
    public const char ItalicHighlight = '\uE001';

    /// <summary>0xF3 — italic on, pen untouched.</summary>
    public const char Italic = '\uE003';

    /// <summary>0xF4 — pen remap, applied twice (the original's case 4 falls through to case 5).</summary>
    public const char RemapTwice = '\uE004';

    /// <summary>0xF5 — pen remap, applied once.</summary>
    public const char RemapOnce = '\uE005';

    private static readonly (char Code, string Tag)[] Tags = {
        (Reset, "<reset/>"),
        (ItalicHighlight, "<hi/>"),
        (Italic, "<i/>"),
        (RemapTwice, "<shift2/>"),
        (RemapOnce, "<shift/>"),
    };

    /// <summary>
    /// The CP437 glyphs of bytes 0xE0-0xFF, in byte order. <b>The original draws none of them.</b>
    /// </summary>
    /// <remarks>
    /// <c>font_render_glyph_or_ctrl</c> (canassa GFX/FONT/FONT.C:217-218) takes any byte whose high
    /// nibble is 0xE0 OR 0xF0 as a control code and switches on the low nibble alone, so 0xE3 is the
    /// same italic as 0xF3; nibbles 6-15 have no arm and draw nothing. The telepathic voices of
    /// chapter 8's Seven Pillars put 0xE3 before every letter, and with only 0xF0-0xF5 known here
    /// they read "πWπe πsπeπvπeπn".
    /// </remarks>
    private const string Cp437ControlGlyphs =
        "αßΓπΣσµτΦΘΩδ∞φε∩"
        + "≡±≥≤⌠⌡÷≈°∙·√ⁿ²■\u00a0";

    /// <summary>
    /// The extractor's half: CP437-decoded DDX text with its control glyphs as tags.
    /// </summary>
    /// <remarks>
    /// Behaviour-preserving, not byte-preserving: the 0xE_/0xF_ twins and the two highlight
    /// codes become one tag each, and the codes with no arm (nibbles 6-15) are dropped — they draw
    /// nothing, move no style and have no width, and none occurs in the shipped data. Every code
    /// that IS kept stays one character at run time, because the auto-dismiss timer counts the
    /// text's length the way the original counts its bytes (so the Seven Pillars' voice keeps
    /// its <c>&lt;i/&gt;W&lt;i/&gt;e</c>).
    /// </remarks>
    public static string FromCp437(string decoded) {
        if (string.IsNullOrEmpty(decoded)) {
            return decoded;
        }
        var text = new StringBuilder(decoded.Length + 16);
        foreach (char c in decoded) {
            int index = Cp437ControlGlyphs.IndexOf(c);
            int nibble = index < 0 ? -1 : index & 0x0F;
            switch (nibble) {
                case -1:
                    text.Append(c);
                    break;
                case 0:
                    text.Append(TagFor(Reset));
                    break;
                case 1:
                case 2:
                    text.Append(TagFor(ItalicHighlight));
                    break;
                case 3:
                    text.Append(TagFor(Italic));
                    break;
                case 4:
                    text.Append(TagFor(RemapTwice));
                    break;
                case 5:
                    text.Append(TagFor(RemapOnce));
                    break;
            }
        }
        return text.ToString();
    }

    /// <summary>
    /// The loader's half: the format's tags as the run-time codes <see cref="Decode"/> reads.
    /// Anything that is not one of the five tags is text.
    /// </summary>
    public static string FromMarkup(string markup) {
        if (string.IsNullOrEmpty(markup) || markup.IndexOf('<') < 0) {
            return markup;
        }
        foreach ((char code, string tag) in Tags) {
            markup = markup.Replace(tag, code.ToString());
        }
        return markup;
    }

    private static string TagFor(char code) => Array.Find(Tags, t => t.Code == code).Tag;

    /// <summary>The run-time code's low nibble — the only part the original switches on — or -1.</summary>
    private static int ControlNibble(char c) =>
        c >= Reset && c <= RemapOnce && c != '\uE002' ? c - Reset : -1;

    /// <summary>Whether a character is a run-time control code rather than text.</summary>
    /// <remarks><b>Deliberately callerless.</b> Production decodes through <see cref="Decode"/>, which switches on the codes itself; this states the set for the tests.</remarks>
    public static bool IsControlCode(char c) => ControlNibble(c) >= 0;

    /// <summary>A maximal stretch of source characters sharing one style.</summary>
    public readonly struct Run {
        public Run(int start, int length, bool italic, int pen) {
            Start = start;
            Length = length;
            Italic = italic;
            Pen = pen;
        }

        /// <summary>Index into the source string.</summary>
        public int Start { get; }

        /// <summary>Characters covered. Control codes are never included.</summary>
        public int Length { get; }

        public bool Italic { get; }

        /// <summary>Pen index; equal to the body pen when unstyled.</summary>
        public int Pen { get; }
    }

    /// <summary>
    /// Splits <c>[start, end)</c> into styled runs, dropping the control codes.
    /// </summary>
    /// <remarks>
    /// <b>A RANGE, not the whole string, because the wrap runs first.</b> That mirrors the original:
    /// <c>font_DrawWrappedTextBlock</c> calls <c>drawTextString</c> once per wrapped line
    /// (@0x4bad9), so each line is styled from the default pen on its own. It is safe here for the
    /// same reason it is safe there — state resets at every space and newline, so no style can span
    /// a break.
    ///
    /// <para><b>Space and newline reset italic AND pen.</b> That is the original's behaviour and it
    /// is what makes styling a line at a time equivalent to styling the whole block.</para>
    /// </remarks>
    public static List<Run> Decode(string text, int start, int end, int bodyPen) {
        var runs = new List<Run>();
        if (string.IsNullOrEmpty(text) || end <= start) {
            return runs;
        }

        var italic = false;
        int pen = bodyPen;
        int runStart = -1;
        var runItalic = false;
        int runPen = bodyPen;

        void Flush(int endExclusive) {
            if (runStart >= 0 && endExclusive > runStart) {
                runs.Add(new Run(runStart, endExclusive - runStart, runItalic, runPen));
            }
            runStart = -1;
        }

        for (int i = start; i < end; i++) {
            char c = text[i];
            switch (ControlNibble(c)) {
                case 0:     // Reset
                    Flush(i);
                    italic = false;
                    pen = bodyPen;
                    continue;
                case 1:     // ItalicHighlight
                    // Pen 1 drops to 0, pen 0x0A stays, everything else becomes the highlight
                    // (0x15ef4-0x15f1e). For the common black-bodied dialog that is pen 5 — the
                    // cream highlight on the chapter-intro title.
                    Flush(i);
                    italic = true;
                    pen = pen == 1 ? 0 : pen == 0x0A ? 0x0A : 5;
                    continue;
                case 3:     // Italic
                    Flush(i);
                    italic = true;
                    continue;
                case 4:     // RemapTwice
                    // The original's case 4 falls THROUGH into case 5, so the remap runs twice.
                    Flush(i);
                    pen = RemapPen(RemapPenFirstStep(pen));
                    continue;
                case 5:     // RemapOnce
                    Flush(i);
                    pen = RemapPen(pen);
                    continue;
                case -1:
                    break;
            }

            switch (c) {
                case ' ':
                case '\n':
                    // Reset BEFORE the break character, so it belongs to the unstyled run that
                    // follows rather than trailing the styled word. Deliberately does NOT flush
                    // here: the shared logic below breaks the run only when the style actually
                    // changed, so a space inside already-unstyled text does not split it.
                    italic = false;
                    pen = bodyPen;
                    break;
                default:
                    break;
            }

            if (runStart < 0) {
                runStart = i;
                runItalic = italic;
                runPen = pen;
            } else if (runItalic != italic || runPen != pen) {
                Flush(i);
                runStart = i;
                runItalic = italic;
                runPen = pen;
            }
        }

        Flush(end);
        return runs;
    }

    /// <summary>The single remap step (the original's case 5, @0x15f59-0x15fa5).</summary>
    private static int RemapPen(int pen) =>
        pen == 0 ? 1 : pen == 1 ? 0x0B : pen == 0x0A ? 0 : 1;

    /// <summary>The extra step case 4 performs before falling through.</summary>
    private static int RemapPenFirstStep(int pen) =>
        pen == 0 ? 0x0A : pen == 1 ? 0x0A : pen == 0x0A ? 1 : 0x0A;
}

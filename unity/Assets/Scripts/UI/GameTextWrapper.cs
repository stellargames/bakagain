namespace BakAgain.UI {
    using System.Collections.Generic;
    using BakAgain.Book;

    /// <summary>
    /// The original engine's word wrap for game-font text — a port of
    /// <c>font_WrapTextIntoLines</c> (<c>0x4b74c</c>), which is what every dialog, and the spell
    /// list, breaks its lines with.
    ///
    /// <para><b>Why we wrap at all</b>, rather than handing UI Toolkit a block of prose: the text
    /// engine's break points are its own, and its line PITCH is the font's, while the original
    /// advances by <c>fontHeight + lineGapExtra</c> — 11 rows for dialog, not GAME.FNT's 10-row
    /// cell. Neither is reachable from a style (UI Toolkit exposes no line-height property), so
    /// the only way to have both is to decide the breaks here and place the lines ourselves. See
    /// <see cref="GameTextBlock"/>, which does the placing.</para>
    ///
    /// <para><b>The algorithm.</b> Scan forward accumulating advances until the next character
    /// would exceed the box width, then walk BACK to the last break point and end the line just
    /// past the last non-break character — so trailing spaces never reach the line. A line that
    /// contains no break point at all is cut where it overflowed. Leading spaces are then skipped
    /// before the next line starts. Everything is measured in raw FNT pixels, where the
    /// comparisons are the DOS build's own integer ones.</para>
    ///
    /// <para><b>Break points are not "spaces and hyphens".</b> <c>sub_ovr145_0</c>
    /// (<c>0x4b6d0</c>) allows a break at a space, and — the surprising half — at a LETTER that
    /// follows three literal <c>'.'</c> characters, i.e. immediately after an ellipsis, provided
    /// the line already holds more than three characters. There is no hyphen rule; the book
    /// wrapper's <c>-</c>/<c>.</c>/<c>\r</c> set (<c>bok_isBreakableChar</c> @0x4d080, ported in
    /// <see cref="BakTextWrapper"/>) belongs to a different routine over a different font and is
    /// deliberately not shared with this one.</para>
    /// </summary>
    internal static class GameTextWrapper {
        /// <summary>
        /// The line array <c>font_DrawWrappedTextBlock</c> hands the wrapper is 90 entries
        /// (<c>push 5Ah</c> at <c>0x4b966</c>/<c>0x4b972</c>), and the wrapper stops once it has
        /// filled them. Text longer than that is simply not laid out — the original pages it
        /// instead.
        /// </summary>
        internal const int MaxLines = 0x5A;

        /// <summary>A half-open character range <c>[Start, End)</c> of the source string.</summary>
        internal readonly struct Line {
            internal Line(int start, int end) {
                Start = start;
                End = end;
            }

            internal int Start { get; }
            internal int End { get; }
            internal int Length => End - Start;
        }

        /// <summary>
        /// Break <paramref name="text"/> into lines that fit <paramref name="maxWidth"/> raw FNT
        /// pixels. The returned ranges exclude the break whitespace, so they can be rendered
        /// as-is.
        /// </summary>
        internal static List<Line> Wrap(string text, int maxWidth, int maxLines = MaxLines) {
            var lines = new List<Line>();
            if (string.IsNullOrEmpty(text)) {
                return lines;
            }

            int lineStart = 0;
            while (lines.Count < maxLines && lineStart < text.Length) {
                int at = lineStart;
                int width = 0;
                // The original tracks the NEGATIVE — "no break point seen on this line yet"
                // (var_1, set at 0x4b765 and cleared at 0x4b7b8) — because that is the case the
                // back-search must not run for.
                bool sawBreakPoint = false;

                while (at < text.Length && text[at] != '\n') {
                    int advance = BakFontData.GetRawCharWidth(text[at], BakFontData.GameFontIndex);
                    if (width + advance > maxWidth) {
                        break;
                    }
                    width += advance;
                    if (IsBreakPoint(text, lineStart, at)) {
                        sawBreakPoint = true;
                    }
                    at++;
                }

                // Stopped on the terminator or a hard newline: the line ends there whether or not
                // it had a break point (0x4b7f8-0x4b836). Only an OVERFLOW walks back.
                bool overflowed = at < text.Length && text[at] != '\n';
                if (overflowed && sawBreakPoint) {
                    // Back to the last break point (0x4b83b)...
                    while (at != lineStart && !IsBreakPoint(text, lineStart, at)) {
                        at--;
                    }
                    // ...then back over the whole run of them, so a line broken at " " does not
                    // keep the spaces (0x4b850-0x4b868). Both exits of that loop step forward
                    // again, which is what makes End exclusive.
                    while (at != lineStart) {
                        at--;
                        if (!IsBreakPoint(text, lineStart, at)) {
                            break;
                        }
                    }
                    at++;
                }

                lines.Add(new Line(lineStart, at));

                // Advance past the break itself: a newline is consumed, otherwise the run of
                // spaces we just declined to keep is skipped (0x4b88d-0x4b8e5).
                lineStart = at;
                if (lineStart < text.Length && text[lineStart] == '\n') {
                    lineStart++;
                } else {
                    while (lineStart < text.Length && text[lineStart] == ' ') {
                        lineStart++;
                    }
                }
            }

            return lines;
        }

        /// <summary>
        /// May the line end just before <paramref name="at"/>? — <c>sub_ovr145_0</c>
        /// (<c>0x4b6d0</c>). True for a space, and for a letter preceded by <c>"..."</c> once the
        /// line is more than three characters long. Everything else — punctuation, digits, the
        /// inline formatting codes — is not a break point, which is why an over-long unbroken run
        /// gets cut mid-word instead.
        /// </summary>
        private static bool IsBreakPoint(string text, int lineStart, int at) {
            if (at >= text.Length) {
                return false;
            }

            char ch = text[at];
            if (ch == ' ') {
                return true;
            }

            // `test characterTypes[toupper(ch)], upper or lower` (0x4b70a). The DOS ctype table
            // flags ASCII letters only; the high-byte formatting codes index its negative padding
            // and come back unflagged.
            if (!IsAsciiLetter(ch)) {
                return false;
            }

            // `if (at - lineStart <= 3) return 0` (0x4b716) — the ellipsis rule needs three
            // characters behind it AND one more, so it can never fire at the head of a line.
            if (at - lineStart <= 3) {
                return false;
            }

            for (int back = 1; back <= 3; back++) {
                if (text[at - back] != '.') {
                    return false;
                }
            }
            return true;
        }

        private static bool IsAsciiLetter(char ch) =>
            (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
    }
}

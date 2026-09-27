namespace BakAgain.Book {
    using System.Collections.Generic;
    using System.Text;
    using GameData.Resources.Book;

    /// <summary>
    /// Replicates the original game's word-wrap algorithm (sub_ovr148_51F at 0x4d09f).
    /// Pre-wraps text so that line breaks match the original DOS game exactly.
    /// </summary>
    public static class BakTextWrapper {
        /// <summary>
        /// Wraps a paragraph's text segments to match original game line-breaking.
        /// Returns the concatenated text with '\n' inserted at break points.
        /// </summary>
        /// <param name="segments">Text segments from the paragraph</param>
        /// <param name="availableWidth">Pixel width for non-first lines</param>
        /// <param name="firstLineWidth">Pixel width for the first line (after indent)</param>
        /// <param name="fontIndex">Font slot (0=BOOK.FNT, 1=GAME.FNT)</param>
        /// <param name="extraCharSpacing">Extra pixels per character (fontFlags &amp; 2)</param>
        public static string WrapParagraph(
            IReadOnlyList<TextSegment> segments,
            int availableWidth,
            int firstLineWidth,
            int fontIndex = 0,
            int extraCharSpacing = 0
        ) {
            // Concatenate all segment text
            var sb = new StringBuilder();
            foreach (var seg in segments)
                sb.Append(seg.Text);
            string text = sb.ToString();

            if (text.Length == 0)
                return text;

            return WrapText(text, availableWidth, firstLineWidth, fontIndex, extraCharSpacing);
        }

        /// <summary>
        /// Wraps plain text to match original game line-breaking.
        /// </summary>
        public static string WrapText(
            string text,
            int availableWidth,
            int firstLineWidth,
            int fontIndex = 0,
            int extraCharSpacing = 0
        ) {
            var result = new StringBuilder(text.Length + text.Length / 40);
            int maxWidth = firstLineWidth;
            int linePixelWidth = 0;
            int lineStart = 0;
            int lastBreakPos = -1; // index into text of last valid break point
            int lastBreakWidth = 0;

            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];

                // Explicit newline — emit line, reset (disables justification in original)
                if (ch == '\n') {
                    if (lineStart < i)
                        result.Append(text, lineStart, i - lineStart);
                    result.Append('\n');
                    lineStart = i + 1;
                    linePixelWidth = 0;
                    lastBreakPos = -1;
                    maxWidth = availableWidth;
                    continue;
                }

                // Accumulate character width
                int charWidth = BakFontData.GetCharWidth(ch, fontIndex) + extraCharSpacing;
                linePixelWidth += charWidth;

                // Check for line overflow
                if (linePixelWidth > maxWidth) {
                    if (lastBreakPos >= lineStart) {
                        // Break at last saved break point (include break char on current line)
                        result.Append(text, lineStart, lastBreakPos - lineStart + 1);
                        result.Append('\n');

                        // Next line starts after break point, skip leading spaces
                        lineStart = lastBreakPos + 1;
                        while (lineStart < text.Length && text[lineStart] == ' ')
                            lineStart++;

                        // Re-measure from new lineStart to current position
                        linePixelWidth = 0;
                        for (int j = lineStart; j <= i; j++) {
                            linePixelWidth += BakFontData.GetCharWidth(text[j], fontIndex)
                                              + extraCharSpacing;
                        }
                    } else {
                        // No break point found — force break before current char
                        if (lineStart < i)
                            result.Append(text, lineStart, i - lineStart);
                        result.Append('\n');
                        lineStart = i;
                        linePixelWidth = charWidth;
                    }

                    lastBreakPos = -1;
                    maxWidth = availableWidth;

                    // Check if the re-measured content already overflows (unlikely but handle it)
                    if (linePixelWidth > maxWidth)
                        continue;
                }

                // Check for break points (matching bok_isBreakableChar + space logic)
                if (i + 1 < text.Length) {
                    char nextCh = text[i + 1];

                    // Before a space, when current is not a space (word boundary)
                    if (nextCh == ' ' && ch != ' ') {
                        lastBreakPos = i;
                        lastBreakWidth = linePixelWidth;
                    }
                    // After breakable characters (- . \r) but only if next is not also breakable
                    else if (IsBreakable(ch) && !IsBreakable(nextCh)) {
                        lastBreakPos = i;
                        lastBreakWidth = linePixelWidth;
                    }
                }
            }

            // Append remaining text
            if (lineStart < text.Length)
                result.Append(text, lineStart, text.Length - lineStart);

            return result.ToString();
        }

        /// <summary>
        /// Version that handles per-line width changes from reserved areas.
        /// Computes available width per line based on Y position and reserved areas.
        /// </summary>
        public static string WrapParagraphWithReservedAreas(
            IReadOnlyList<TextSegment> segments,
            Page page,
            Paragraph paragraph,
            int paragraphScreenY,
            int fontIndex = 0,
            int extraCharSpacing = 0
        ) {
            var sb = new StringBuilder();
            foreach (var seg in segments)
                sb.Append(seg.Text);
            string text = sb.ToString();

            if (text.Length == 0)
                return text;

            int baseAvailableWidth = page.Width - paragraph.XOffset - paragraph.Width;
            bool isFirstLine = true;

            var result = new StringBuilder(text.Length + text.Length / 40);
            int lineY = paragraphScreenY;
            int linePixelWidth = 0;
            int lineStart = 0;
            int lastBreakPos = -1;

            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];

                if (ch == '\n') {
                    if (lineStart < i)
                        result.Append(text, lineStart, i - lineStart);
                    result.Append('\n');
                    lineStart = i + 1;
                    linePixelWidth = 0;
                    lastBreakPos = -1;
                    lineY += paragraph.LineSpacing;
                    isFirstLine = false;
                    continue;
                }

                // Compute available width for current line considering reserved areas
                int maxWidth = ComputeAvailableWidth(
                    page, paragraph, lineY, paragraph.LineSpacing,
                    isFirstLine ? paragraph.StartIndent : 0
                );

                int charWidth = BakFontData.GetCharWidth(ch, fontIndex) + extraCharSpacing;
                linePixelWidth += charWidth;

                if (linePixelWidth > maxWidth) {
                    if (lastBreakPos >= lineStart) {
                        result.Append(text, lineStart, lastBreakPos - lineStart + 1);
                        result.Append('\n');

                        lineStart = lastBreakPos + 1;
                        while (lineStart < text.Length && text[lineStart] == ' ')
                            lineStart++;

                        lineY += paragraph.LineSpacing;
                        isFirstLine = false;

                        linePixelWidth = 0;
                        for (int j = lineStart; j <= i; j++) {
                            linePixelWidth += BakFontData.GetCharWidth(text[j], fontIndex)
                                              + extraCharSpacing;
                        }
                    } else {
                        if (lineStart < i)
                            result.Append(text, lineStart, i - lineStart);
                        result.Append('\n');
                        lineStart = i;
                        linePixelWidth = charWidth;
                        lineY += paragraph.LineSpacing;
                        isFirstLine = false;
                    }

                    lastBreakPos = -1;
                    continue;
                }

                // Break point detection (same as simple version)
                if (i + 1 < text.Length) {
                    char nextCh = text[i + 1];
                    if (nextCh == ' ' && ch != ' ') {
                        lastBreakPos = i;
                    } else if (IsBreakable(ch) && !IsBreakable(nextCh)) {
                        lastBreakPos = i;
                    }
                }
            }

            if (lineStart < text.Length)
                result.Append(text, lineStart, text.Length - lineStart);

            return result.ToString();
        }

        /// <summary>
        /// Computes available pixel width for a text line at the given Y position,
        /// accounting for reserved areas (image exclusion zones).
        /// Mirrors sub_ovr148_39 at 0x4cbb9.
        /// </summary>
        private static int ComputeAvailableWidth(
            Page page,
            Paragraph paragraph,
            int screenY,
            int lineHeight,
            int indent
        ) {
            int leftOffset = paragraph.XOffset + indent;
            int rightMargin = paragraph.Width;

            // Check reserved areas — push text right if overlapping
            foreach (var ra in page.ReservedAreas) {
                int raRight = ra.X2;
                int raBottom = ra.Y2;

                // Check vertical overlap: line Y range vs reserved area Y range
                if (screenY < raBottom && screenY + lineHeight > ra.Y) {
                    int newLeft = raRight + 1 - page.XOffset;
                    if (newLeft > leftOffset)
                        leftOffset = newLeft;
                }
            }

            int available = page.Width - leftOffset - rightMargin;
            return available > 0 ? available : 0;
        }

        /// <summary>
        /// Matches bok_isBreakableChar at 0x4d080.
        /// Characters after which a line break is allowed: '-', '.', '\r'
        /// </summary>
        private static bool IsBreakable(char ch) {
            return ch == '-' || ch == '.' || ch == '\r';
        }
    }
}

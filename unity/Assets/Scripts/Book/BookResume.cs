namespace BakAgain.Book {
    using GameData.Resources.Book;
    using System.Collections.Generic;

    /// <summary>
    /// Where a paragraph continued on the next page picks up: a character offset into its text.
    /// </summary>
    /// <remarks>
    /// The original resumes from a pointer, skipping the spaces after the break
    /// (<c>pResumeSave</c>, BOOKTEXT.C:356-361), and lays the rest out with the new page's geometry.
    /// Resuming by line number re-wrapped the whole paragraph against the new page and skipped N
    /// lines; page 1's drop cap had wrapped it differently, so C21's page 2 lost "the".
    /// </remarks>
    public static class BookResume {
        /// <summary>The segments from <paramref name="charOffset"/> on, leading spaces dropped.</summary>
        public static IReadOnlyList<TextSegment> Tail(IReadOnlyList<TextSegment> segments, int charOffset) {
            var tail = new List<TextSegment>(segments.Count);
            int skip = charOffset;
            bool leading = true;
            foreach (TextSegment seg in segments) {
                string text = seg.Text ?? string.Empty;
                if (skip >= text.Length && skip > 0) {
                    skip -= text.Length;
                    continue;
                }
                text = text.Substring(skip);
                skip = 0;
                if (leading) {
                    text = text.TrimStart(' ');
                    leading = text.Length == 0;
                }
                tail.Add(new TextSegment {
                    Font = seg.Font, YOffset = seg.YOffset, FontStyle = seg.FontStyle, Color = seg.Color,
                    Text = text,
                });
            }
            return tail;
        }

        /// <summary>
        /// An offset into <see cref="Tail"/>'s text as an offset into the whole paragraph: the tail
        /// started at <paramref name="startChar"/> after dropping the spaces there.
        /// </summary>
        public static int Absolute(IReadOnlyList<TextSegment> segments, int startChar, int tailOffset) {
            var text = new System.Text.StringBuilder();
            foreach (TextSegment seg in segments) {
                text.Append(seg.Text);
            }
            int at = startChar;
            while (at < text.Length && text[at] == ' ') {
                at++;
            }
            return at + tailOffset;
        }
    }
}

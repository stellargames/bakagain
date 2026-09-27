namespace BakAgain.Book {
    using System.Collections.Generic;
    using BakAgain.UI.Navigation;
    using GameData.Resources.Book;
    using UnityEngine;

    /// <summary>
    /// The BOK book surface. An <see cref="IScreen"/> — the ChapterScenesPlayer pushes it over the
    /// cutscene screen for a book part; the presenter's own Show/Hide within that lifetime remain
    /// direct canvas toggles.
    /// </summary>
    public interface IBookView : IScreen {
        void SetBackground(Sprite background);

        /// <summary>
        /// Renders a page using layout from <paramref name="page"/> (images, reserved areas, page number)
        /// and text from <paramref name="allParagraphs"/> starting at <paramref name="startParagraph"/>.
        /// When <paramref name="startLineOffset"/> is &gt; 0, the first paragraph is a continuation
        /// and rendering starts from that line (no indent, no inter-paragraph spacing).
        /// Returns (paragraphIndex, lineOffset) for the next page's continuation point.
        /// </summary>
        (int paragraphIndex, int lineOffset) ShowPage(Page page, Sprite[] bookSprites, Color[] palette,
                     IReadOnlyList<Paragraph> allParagraphs, int startParagraph, int startLineOffset = 0);

        void Show();
        void Hide();
    }
}

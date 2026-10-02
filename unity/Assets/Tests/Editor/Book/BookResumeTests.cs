namespace BakAgain.Tests.Editor.Book {
    using BakAgain.Book;
    using GameData.Resources.Book;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// A paragraph continued on the next page resumes at a CHARACTER, not a line number.
    /// </summary>
    /// <remarks>
    /// The original keeps a pointer (<c>pResumeSave</c>, BOOKTEXT.C:356-361). The port re-wrapped
    /// the whole paragraph with the new page's geometry and skipped N lines; page 1's drop cap had
    /// wrapped it differently, so C21's page 2 lost "the" from "the enemy...".
    /// </remarks>
    public class BookResumeTests {
        private static List<TextSegment> Segments() => new() {
            new TextSegment { Text = "and he was the enemy..." },
            new TextSegment { Text = "", FontStyle = FontStyle.Italic },
        };

        [Test]
        public void TheTailStartsAtTheOffsetWithItsLeadingSpacesDropped() {
            IReadOnlyList<TextSegment> tail = BookResume.Tail(Segments(), "and he was".Length);
            Assert.AreEqual("the enemy...", tail[0].Text);
        }

        [Test]
        public void LaterSegmentsKeepTheirStyle() {
            IReadOnlyList<TextSegment> tail = BookResume.Tail(Segments(), 4);
            Assert.AreEqual(2, tail.Count);
            Assert.AreEqual(FontStyle.Italic, tail[1].FontStyle);
        }

        [Test]
        public void ATailOffsetMapsBackPastTheDroppedSpaces() {
            // Resume twice in one paragraph: the second resume point must land on "enemy", not on
            // a character the dropped space shifted.
            int first = "and he was".Length;                 // at the space
            int inTail = "the ".Length;                      // "enemy..." within the tail
            int abs = BookResume.Absolute(Segments(), first, inTail);
            Assert.AreEqual("enemy...", BookResume.Tail(Segments(), abs)[0].Text);
        }

        [Test]
        public void AnOffsetPastTheFirstSegmentStartsInTheNext() {
            var segs = new List<TextSegment> {
                new TextSegment { Text = "ab " }, new TextSegment { Text = "cd", Color = 5 },
            };
            IReadOnlyList<TextSegment> tail = BookResume.Tail(segs, 3);
            Assert.AreEqual(1, tail.Count);
            Assert.AreEqual("cd", tail[0].Text);
            Assert.AreEqual(5, tail[0].Color);
        }
    }
}

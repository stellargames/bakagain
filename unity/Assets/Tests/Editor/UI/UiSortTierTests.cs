namespace BakAgain.Tests.Editor.UI {
    using BakAgain.UI;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// The z-order rule: documents that can be up at the same time must not share a sortingOrder,
    /// because ties are broken by registration order — which nothing controls.
    /// </summary>
    public class UiSortTierTests {
        private static UiSortTier.Layer L(string name, float order) => new UiSortTier.Layer(name, order);

        [Test]
        public void DistinctTiersAreFine() {
            IReadOnlyList<string> conflicts = UiSortTier.FindConflicts(new[] {
                L("InGameScreen", -1), L("InGameMenu", 0), L("MetaMenu", 1),
                L("DialogOverlay", 10), L("CursorOverlay", 1000),
            });

            Assert.IsEmpty(conflicts);
        }

        [Test]
        public void TwoDocumentsOnTheSameTierAreReported_WithBothNames() {
            IReadOnlyList<string> conflicts = UiSortTier.FindConflicts(new[] {
                L("BookView", 10), L("DialogOverlay", 10),
            });

            Assert.AreEqual(1, conflicts.Count);
            StringAssert.Contains("BookView", conflicts[0]);
            StringAssert.Contains("DialogOverlay", conflicts[0]);
            StringAssert.Contains("10", conflicts[0]);
        }

        /// <summary>
        /// The shipped tier-0 crowd is only safe because ScreenNavigator enables exactly one of
        /// them — so if two ever ARE up together, that has to be reported, not excused.
        /// </summary>
        [Test]
        public void TheTierZeroCrowdIsReportedIfTwoAreEverUpTogether() {
            IReadOnlyList<string> conflicts = UiSortTier.FindConflicts(new[] {
                L("MainMenu", 0), L("PreferencesScreen", 0),
            });

            Assert.AreEqual(1, conflicts.Count);
        }

        [Test]
        public void EachSharedTierIsReportedSeparately() {
            IReadOnlyList<string> conflicts = UiSortTier.FindConflicts(new[] {
                L("A", 0), L("B", 0), L("C", 10), L("D", 10), L("E", 5),
            });

            Assert.AreEqual(2, conflicts.Count);
        }

        [Test]
        public void ThreeOnOneTierAreAllNamed() {
            IReadOnlyList<string> conflicts = UiSortTier.FindConflicts(new[] {
                L("A", 0), L("B", 0), L("C", 0),
            });

            Assert.AreEqual(1, conflicts.Count);
            StringAssert.Contains("A", conflicts[0]);
            StringAssert.Contains("B", conflicts[0]);
            StringAssert.Contains("C", conflicts[0]);
        }

        [Test]
        public void NothingVisibleIsNotAConflict() {
            Assert.IsEmpty(UiSortTier.FindConflicts(new UiSortTier.Layer[0]));
            Assert.IsEmpty(UiSortTier.FindConflicts(null));
        }

        [Test]
        public void OneDocumentAloneOnATierIsFine() {
            Assert.IsEmpty(UiSortTier.FindConflicts(new[] { L("OnlyMe", 0) }));
        }

        /// <summary>
        /// sortingOrder is a float. Rounding it to int here would invent a conflict between two
        /// documents that are, in fact, correctly ordered.
        /// </summary>
        [Test]
        public void FractionalTiersAreNotTreatedAsATie() {
            Assert.IsEmpty(UiSortTier.FindConflicts(new[] { L("A", 0.4f), L("B", 0.6f) }));
        }
    }
}

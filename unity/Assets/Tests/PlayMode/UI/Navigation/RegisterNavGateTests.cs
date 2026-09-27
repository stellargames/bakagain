namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using BakAgain.ResourceManagement.Loaders;
    using GameData.Resources.Menu;
    using NUnit.Framework;

    /// <summary>
    /// The keyboard-nav gate. menupage_navigate (canassa MENUPAGE.C:222-311) skips an entry only on
    /// wEnable_gate — our <see cref="UiElement.Disabled"/>. It does NOT consult the draw flag
    /// (<see cref="UiElement.Visible"/>), which is 0 for every hit-only zone backed by SCX art:
    /// REQ_INV's portraits, container window and gold readout, and CONTENTS' nine chapter rows.
    /// </summary>
    public class RegisterNavGateTests {
        private static UiElement Entry(bool visible, int disabled) =>
            new UiElement { Visible = visible, Disabled = disabled, ActionId = 7 };

        [Test]
        public void AFacelessHotspot_IsNavigable() {
            // The case that is broken today: drawn by the SCX, but still a live entry.
            Assert.IsTrue(UserInterfaceLoader.IsNavigable(Entry(visible: false, disabled: 0)));
        }

        [Test]
        public void ADrawnEntry_IsNavigable() {
            Assert.IsTrue(UserInterfaceLoader.IsNavigable(Entry(visible: true, disabled: 0)));
        }

        [Test]
        public void ADisabledEntry_IsNotNavigable_EvenWhenDrawn() {
            Assert.IsFalse(UserInterfaceLoader.IsNavigable(Entry(visible: true, disabled: 1)));
        }

        [Test]
        public void ADisabledInvisibleEntry_IsNotNavigable() {
            // REQ_OPT0's two label-less entries are exactly this shape. The gate — not the draw
            // flag — is what must keep them unfocusable once the draw flag stops being consulted.
            Assert.IsFalse(UserInterfaceLoader.IsNavigable(Entry(visible: false, disabled: 1)));
        }

        [Test]
        public void APaddingEntry_IsNotNavigable() {
            // 48 entries across the shipped REQs (28 in REQ_INV) are ActionId -1, zero-size
            // padding. The Visible skip used to mask them; once it goes, the action id is what
            // keeps them out. Keyed on the id and NOT on the rect, because LOAD/SAVE's FilePickers
            // are zero-size in the DAT and are real, navigable widgets.
            var padding = new UiElement { Visible = false, Disabled = 0, ActionId = -1 };
            Assert.IsFalse(UserInterfaceLoader.IsNavigable(padding));
        }
    }
}

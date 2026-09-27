namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System.IO;
    using BakAgain.ResourceManagement.Loaders;
    using GameData.Resources.Menu;
    using NUnit.Framework;

    /// <summary>
    /// A panel that supplies click areas and paints none of its own chrome.
    /// </summary>
    /// <remarks>
    /// <b>The cipher puzzle is the case.</b> <c>UI_RunCipherPuzzle</c> @0x78c60 blits PUZZLE.SCX,
    /// repositions the click areas and renders its own text — it never runs a menu draw at all — so
    /// REQ_PUZL's exit ImageButton, the file's ONLY entry marked visible, is never painted by the
    /// original. Ours drew it on the chest plate, which the first side-by-side against the real game
    /// caught.
    /// </remarks>
    public class HitTestOnlyPanelTests {
        private static UiElement Entry(bool visible) =>
            new UiElement { Visible = visible, Disabled = 0, ActionId = 18 };

        [Test]
        public void AnOrdinaryPanelPaintsItsVisibleEntries() {
            Assert.IsTrue(UserInterfaceLoader.PaintsFace(Entry(visible: true), hitTestOnly: false));
        }

        [Test]
        public void AHitTestOnlyPanelPaintsNothing_EvenWhatTheDataMarksVisible() {
            // REQ_PUZL action 18 exactly: Visible:true in the file, never drawn by the original.
            Assert.IsFalse(UserInterfaceLoader.PaintsFace(Entry(visible: true), hitTestOnly: true));
        }

        [Test]
        public void TheDataFlagStillSuppressesAFaceOnAnOrdinaryPanel() {
            Assert.IsFalse(UserInterfaceLoader.PaintsFace(Entry(visible: false), hitTestOnly: false));
        }

        [Test]
        public void ADisabledImageButtonWearsTheBlankStone_NotItsOwnIcon() {
            // widget_menu_draw (canassa UI/WIDGET.C:253) is `if (enable_gate) { blit(0x32); return; }`
            // — the entry's own sprite is never reached.
            var disabled = new UiElement { Visible = true, Disabled = 1, ActionId = 2, IconBase = 10 };
            Assert.AreEqual(UserInterfaceLoader.DisabledButtonIcon,
                UserInterfaceLoader.FaceIcon(disabled, disabled.IconBase));
        }

        [Test]
        public void AnEnabledImageButtonKeepsItsOwnIcon() {
            var live = new UiElement { Visible = true, Disabled = 0, ActionId = 7, IconBase = 109 };
            Assert.AreEqual(109, UserInterfaceLoader.FaceIcon(live, live.IconBase));
        }

        [Test]
        public void AnEntryTheSCREENGatesWearsTheStoneToo_NotJustOneTheDATAShips() {
            // *** THE HALF THAT WAS MISSING. *** widget_menu_draw reads ONE gate; it does not care
            // who set it. The authored Disabled field covers REQ_CAST's dead schools, but the travel
            // HUD's follow-road and cast buttons ship Disabled:0 and go unavailable at RUNTIME —
            // measured off-road on 2026-09-07, the original wore BICONS1#25 there and we wore the
            // road wedge, so nothing said road travel was off.
            var live = new UiElement { Visible = true, Disabled = 0, ActionId = 19, IconBase = 44 };

            Assert.AreEqual(44, UserInterfaceLoader.FaceIcon(live, live.IconBase, gated: false));
            Assert.AreEqual(UserInterfaceLoader.DisabledButtonIcon,
                UserInterfaceLoader.FaceIcon(live, live.IconBase, gated: true));
        }

        [Test]
        public void TheHeldFaceIsTheOtherHalfOfTheButtonSpritePair() {
            // *** THE SPRITE PAIR IS THE EVIDENCE. *** Measured on the original 2026-09-12: with
            // the camp panel open, REQ_MAIN's encamp button (action 18, IconBase 14) matches
            // BICONS2#7 at a mean per-channel difference of 10.2 against 29.5 for its own
            // BICONS1#7, and the two swap on the travel screen (7.5 / 29.0). The +1 frame is a
            // DIFFERENT FILE, not a darkened copy of the same one -- which is why three sessions
            // of looking for a shading routine found nothing.
            Assert.AreEqual("BICONS1.BMX#7", UiElement.IconKeyForCombined(14));
            Assert.AreEqual("BICONS2.BMX#7",
                UiElement.IconKeyForCombined(14 + UserInterfaceLoader.HeldIconOffset));
        }

        [Test]
        public void TheTwoGatesAgree_soAnAuthoredDisableCannotBeUngatedAtRuntime() {
            // There is one gate in the original, not two that could disagree. A screen asking for
            // an authored-disabled entry to be ungated must not get its icon back.
            var dead = new UiElement { Visible = true, Disabled = 1, ActionId = 46, IconBase = 10 };

            Assert.AreEqual(UserInterfaceLoader.DisabledButtonIcon,
                UserInterfaceLoader.FaceIcon(dead, dead.IconBase, gated: false));
        }

        [Test]
        public void TheOneArgFormIsTheUngatedForm_soExistingCallersAreUnchanged() {
            var live = new UiElement { Visible = true, Disabled = 0, ActionId = 7, IconBase = 109 };

            Assert.AreEqual(UserInterfaceLoader.FaceIcon(live, live.IconBase, gated: false),
                UserInterfaceLoader.FaceIcon(live, live.IconBase));
        }

        [Test]
        public void TheCastScreensFourDeadSchoolButtonsAreTheReasonThisExists() {
            // *** REQ_CAST AND REQ_MAIN SHARE A FRAME. *** REQ_CAST switches four of its six school
            // buttons off and leaves REQ_MAIN's encamp/journal/map icons (10, 16, 14, 14) sitting in
            // the shipped data behind them. Painting IconBase regardless put the travel HUD's faces
            // on the cast screen, which CastScreen "fixed" by overwriting all six buttons with
            // INVSPELL icons — lighting four schools the original ships switched off.
            //
            // Asserting the whole shipped row, not one entry, because it is the PAIRING of a live
            // icon with a dead one that the old code got wrong.
            foreach (int icon in new[] { 10, 16, 14, 14 }) {
                var dead = new UiElement { Visible = true, Disabled = 1, IconBase = icon };
                Assert.AreEqual(UserInterfaceLoader.DisabledButtonIcon,
                    UserInterfaceLoader.FaceIcon(dead, dead.IconBase));
            }

            // The two the original really does offer, which must be untouched.
            foreach (int icon in new[] { 109, 111 }) {
                var live = new UiElement { Visible = true, Disabled = 0, IconBase = icon };
                Assert.AreEqual(icon, UserInterfaceLoader.FaceIcon(live, live.IconBase));
            }
        }

        [Test]
        public void HidingChromeIsNotTheSameAsHidingTheElement() {
            // *** THE ONE THAT COST AN HOUR. *** `req-hidden` is visibility:hidden, and UI Toolkit
            // does not PICK a hidden element — so suppressing chrome by adding that class takes the
            // click area down with the face. Measured live: the puzzle's exit region stopped
            // answering panel.Pick entirely until the paint gate and the class were split apart.
            //
            // The same fact bit REQ_CAMP's Stop button, which is Visible:false in the data and
            // switched on for the duration of a rest: `SetEntryState` set display:Flex while the
            // class kept visibility:hidden, so the button stayed invisible AND unpickable and a rest
            // could not be abandoned. SetEntryState toggles the class now.
            //
            // This pins the stylesheet fact both depend on. If the rule changes, the loader's
            // draw/hit model needs rereading, not a green test.
            string tss = Path.Combine(UnityEngine.Application.dataPath,
                "UI Toolkit", "UnityThemes", "ClassicTheme.tss");
            if (!File.Exists(tss)) {
                Assert.Ignore("ClassicTheme.tss not present in this checkout");
            }

            string text = File.ReadAllText(tss);
            int rule = text.IndexOf(".req-hidden", System.StringComparison.Ordinal);
            Assert.Greater(rule, -1, "the .req-hidden rule is gone — the loader still adds the class");
            StringAssert.Contains("visibility: hidden",
                text.Substring(rule, System.Math.Min(120, text.Length - rule)),
                "req-hidden no longer hides by visibility; re-check whether it still blocks picking");
        }
    }
}

namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Graphics;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// A button's caption carries the vertical stretch; its chrome does not.
    /// </summary>
    /// <remarks>
    /// <b>Every button in the game rendered a sixth too short.</b> Canonical space is anisotropic —
    /// x5 across against x6 down — and a font renderer scales isotropically, so text sized for
    /// correct ADVANCES has to be stretched back vertically. <c>.req-label</c> gets
    /// <c>scale: 1 1.2</c> in the theme because a label is only text; a UI Toolkit button IS its
    /// text element, so the same rule on <c>.text-button</c> would stretch the wooden bevel with it.
    ///
    /// <para>Hence the caption child. These pin the two halves of that: the caption is stretched,
    /// and the chrome is not.</para>
    /// </remarks>
    public class TextButtonCaptionTests {
        private static float ExpectedStretch =>
            (float)Canonical.VgaScaleY / Canonical.VgaScaleX;

        [Test]
        public void TheCaptionIsStretchedVerticallyAndNotHorizontally() {
            var chrome = new Button();
            Label caption = GameFontText.Caption(chrome, "Yes");

            Assert.AreEqual(1f, caption.style.scale.value.value.x, 0.0001f,
                "the advances are already right; only the height is short");
            Assert.AreEqual(ExpectedStretch, caption.style.scale.value.value.y, 0.0001f);
            Assert.Greater(ExpectedStretch, 1f, "and it is a stretch, not a squash");
        }

        [Test]
        public void THECHROMEIsNotScaled() {
            // *** The whole reason the caption is a child. *** Putting the stretch on the button
            // itself would take the background, the borders and the bevel with it.
            var chrome = new Button();
            chrome.AddToClassList("text-button");
            GameFontText.Caption(chrome, "Yes");

            Assert.AreEqual(StyleKeyword.Null, chrome.style.scale.keyword);
        }

        [Test]
        public void TheCaptionCarriesTheTextAndTheChromeDoesNot() {
            var chrome = new Button();
            Label caption = GameFontText.Caption(chrome, "Farewell");

            Assert.AreEqual("Farewell", caption.text);
            Assert.IsTrue(string.IsNullOrEmpty(chrome.text),
                "two copies of the caption would draw twice, one of them unstretched");
        }

        [Test]
        public void TheCaptionIsINERTToThePointer() {
            // The manipulators and nav widgets are registered on the chrome; a child that takes hits
            // changes what the click target is.
            var chrome = new Button();
            Label caption = GameFontText.Caption(chrome, "Yes");

            Assert.AreEqual(PickingMode.Ignore, caption.pickingMode);
        }

        [Test]
        public void TheCaptionDoesNotWRAP_unlikeTheProseApplyIsUsuallyUsedFor() {
            // Apply sets PreWrap because dialog text's spaces are content. A button caption is sized
            // by the REQ's own rect and reflowing inside it would push the text out of the bevel.
            var chrome = new Button();
            Label caption = GameFontText.Caption(chrome, "A rather long caption");

            Assert.AreEqual(WhiteSpace.NoWrap, caption.style.whiteSpace.value);
        }

        [Test]
        public void ARowCaptionScalesAboutItsLEFTEdge_notItsCentre() {
            // *** The reason the anchor is a parameter. *** A file-picker row is middle-LEFT
            // aligned; scaling it about its centre would slide the text sideways as it stretched,
            // which on a list of save names reads as the column being misaligned rather than as a
            // transform being wrong.
            var chrome = new UnityEngine.UIElements.VisualElement();
            Label caption = GameFontText.Caption(chrome, "SAVE01", GameFontText.AnchorX.Left);

            Assert.AreEqual(0f, caption.style.transformOrigin.value.x.value, 0.0001f);
            Assert.AreEqual(50f, caption.style.transformOrigin.value.y.value, 0.0001f);
            Assert.AreEqual(ExpectedStretch, caption.style.scale.value.value.y, 0.0001f);
        }

        [Test]
        public void AButtonCaptionScalesAboutItsCENTRE() {
            var chrome = new Button();
            Label caption = GameFontText.Caption(chrome, "Yes");

            Assert.AreEqual(50f, caption.style.transformOrigin.value.x.value, 0.0001f);
        }

        [Test]
        public void EveryCaptionGetsTheCalibratedFontSize() {
            // .file-picker-row used to set 54px of its own, within 2% of correct and looking
            // deliberate. Caption applies the size the advances were actually calibrated against.
            var chrome = new UnityEngine.UIElements.VisualElement();
            Label caption = GameFontText.Caption(chrome, "SAVE01", GameFontText.AnchorX.Left);

            Assert.AreEqual(Canonical.GameFontSizePx, caption.style.fontSize.value.value, 0.0001f);
        }

        [Test]
        public void ItIsFoundByTheNameTheRestOfTheCodeQueries() {
            var chrome = new Button();
            GameFontText.Caption(chrome, "Yes");

            Assert.IsNotNull(chrome.Q<Label>("caption"));
        }
    }
}

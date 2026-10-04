namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Graphics;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// A button's caption is a child label in the game font; nothing is scaled.
    /// </summary>
    /// <remarks>
    /// The caption used to carry a 1.2x vertical stretch, because the font was built with square
    /// pixels. Since TASK-765 the font declares its pixels' shape and is built at it, so text
    /// renders at the original's proportions with no transform — and a square-pixel mod font is
    /// not stretched. These pin that nothing reintroduces a scale.
    /// </remarks>
    public class TextButtonCaptionTests {
        [Test]
        public void TheCaptionCarriesNoStretch_theFontHasTheAspect() {
            var chrome = new Button();
            Label caption = GameFontText.Caption(chrome, "Yes");

            Assert.AreEqual(StyleKeyword.Null, caption.style.scale.keyword);
        }

        [Test]
        public void THECHROMEIsNotScaled() {
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
        public void EveryCaptionGetsTheCalibratedFontSize() {
            // .file-picker-row used to set 54px of its own, within 2% of correct and looking
            // deliberate. Caption applies the size the advances were actually calibrated against.
            var chrome = new UnityEngine.UIElements.VisualElement();
            Label caption = GameFontText.Caption(chrome, "SAVE01");

            Assert.AreEqual(55f, caption.style.fontSize.value.value, 0.0001f);   // (10 + 1) x 5, from GAME.FNT
        }

        [Test]
        public void ItIsFoundByTheNameTheRestOfTheCodeQueries() {
            var chrome = new Button();
            GameFontText.Caption(chrome, "Yes");

            Assert.IsNotNull(chrome.Q<Label>("caption"));
        }
    }
}

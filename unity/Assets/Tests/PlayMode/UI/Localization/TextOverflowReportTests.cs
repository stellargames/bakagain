namespace BakAgain.Tests.PlayMode.UI.Localization {
    using System.Collections;
    using System.Linq;
    using BakAgain.UI;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The overflow report (TASK-779): text that is cut because its box cannot page, and captions
    /// still too wide at the smallest fit, are listed by the text they lost.
    /// </summary>
    public class TextOverflowReportTests {
        private GameObject _go;
        private PanelSettings _panel;

        [SetUp]
        public void SetUp() => TextOverflowReport.Clear();

        [TearDown]
        public void TearDown() {
            Object.Destroy(_go);
            Object.Destroy(_panel);
            TextOverflowReport.Clear();
        }

        private UIDocument Doc() {
            _go = new GameObject("Overflow");
            var doc = _go.AddComponent<UIDocument>();
            _panel = ScriptableObject.CreateInstance<PanelSettings>();
            doc.panelSettings = _panel;
            return doc;
        }

        private static GameTextBlock Body(VisualElement root, float top, string text) {
            // Two lines' room, no paging: the full map's chapter caption box.
            var box = new VisualElement { style = { width = 800, height = 180, top = top, position = Position.Absolute } };
            var body = new GameTextBlock { style = { position = Position.Absolute, left = 0, right = 0, top = 0 } };
            box.Add(body);
            root.Add(box);
            body.SetContent(text, null, 0, TextAnchor.UpperLeft, GameFontText.DialogLineGapVgaRows);
            return body;
        }

        [UnityTest]
        public IEnumerator TextCutByABoxThatCannotPageIsReported_AndTextThatFitsIsNot() {
            UIDocument doc = Doc();
            yield return null;
            const string fits = "Chapter One:  Into a Dark Night\nEscort Gorath to Krondor!";
            const string cut = "Hoofdstuk Eén:  In een Donkere Nacht\nBreng Gorath naar Krondor!";
            Body(doc.rootVisualElement, 0, fits);
            Body(doc.rootVisualElement, 300, cut);
            yield return new WaitForSecondsRealtime(1f);

            Assert.IsFalse(TextOverflowReport.Entries.Any(e => e.Text == fits), "English fits its box");
            Assert.IsTrue(TextOverflowReport.Entries.Any(e => e.Text == cut && e.Kind == TextOverflowReport.Box),
                "the Dutch caption wraps to three lines in a two-line box");
        }

        /// <summary>The dialog manager sets the text, then turns paging on a frame later: not a cut.</summary>
        [UnityTest]
        public IEnumerator ABlockThatStartsPagingAFrameLaterIsNotReported() {
            UIDocument doc = Doc();
            yield return null;
            const string longText = "One two three four five six seven eight nine ten.\nEleven twelve.\nThirteen.\nFourteen.";
            GameTextBlock body = Body(doc.rootVisualElement, 0, longText);
            yield return null;
            yield return null;
            body.Paginate = true;
            yield return new WaitForSecondsRealtime(1f);

            Assert.IsFalse(TextOverflowReport.Entries.Any(e => e.Text == longText));
        }

        [UnityTest]
        public IEnumerator ACaptionTooWideEvenAtTheSmallestFitIsReported() {
            UIDocument doc = Doc();
            yield return null;
            var narrow = new VisualElement { style = { width = 60, height = 60, position = Position.Absolute } };
            doc.rootVisualElement.Add(narrow);
            const string text = "Onwaarschijnlijk lange knoptekst";
            GameFontText.Caption(narrow, text);
            for (int i = 0; i < 10; i++) {
                yield return null;
            }

            Assert.IsTrue(TextOverflowReport.Entries.Any(e => e.Text == text && e.Kind == TextOverflowReport.Caption));
        }
    }
}

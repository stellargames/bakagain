namespace BakAgain.Tests.PlayMode.UI.Localization {
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using System.Collections;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// DDX formatting through the real load path (TASK-774): the archive's control bytes arrive as
    /// tags, a translation's tags and letters go in through the pack, and the dialog loader hands
    /// the renderer codes it styles exactly as before.
    /// </summary>
    /// <remarks>
    /// One test, because the pack is applied as the DDX is extracted and the extraction is cached:
    /// a second test on DIAL_Z00 would read the first one's copy.
    /// </remarks>
    public class DialogMarkupLoadTests {
        private const int ChapterOneTitle = 294;                       // "Chapter One:  ±Into ±a ±Dark ±Night"
        private const string ExpectedKey = "base:ddx:dial_z00:10274";  // "...hadn't been ±expected≡."

        private string _dir;
        private string _savedLanguage;
        private string _savedOverridePath;

        [SetUp]
        public void SetUp() {
            _savedLanguage = BakResourceSettings.Language;
            _savedOverridePath = BakResourceSettings.OverridePath;
            _dir = Path.Combine(Path.GetTempPath(), "bak-lang-" + System.Guid.NewGuid().ToString("N"));
            string po = LanguagePacks.PathFor(_dir, "xx");
            Directory.CreateDirectory(Path.GetDirectoryName(po));
            File.WriteAllText(po,
                "msgid \"\"\nmsgstr \"\"\n\"Language: xx\\n\"\n\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n"
                + "msgctxt \"base:ddx:dial_z00:69230\"\nmsgid \"Chapter One\"\n"
                + "msgstr \"Kapitel Eins:  <hi/>Straße\"\n");
            BakResourceSettings.OverridePath = _dir;
            BakResourceSettings.Language = "xx";
            LanguagePacks.Reload();
        }

        [TearDown]
        public void TearDown() {
            BakResourceSettings.Language = _savedLanguage;
            BakResourceSettings.OverridePath = _savedOverridePath;
            LanguagePacks.Reload();
            Directory.Delete(_dir, true);
        }

        [UnityTest]
        public IEnumerator FormattingSurvivesTheFormat_AndATranslationMayUseAnyLetter() => UniTask.ToCoroutine(async () => {
            Dialog dialog;
            try {
                dialog = await new DialogResourceLoader(null).LoadDialogAsync(ChapterOneTitle);
            } catch (System.Exception) {
                dialog = null;
            }
            if (dialog == null) {
                Assert.Ignore("No game data to extract from.");
                return;
            }

            var palette = new Color[16];
            palette[5] = new Color(1f, 0f, 0f);

            // Untranslated: rendered exactly as the CP437 codes were.
            DialogEntry english = dialog.Entries.Single(e => e.Key == ExpectedKey);
            StringAssert.EndsWith("been <color=#FF0000><i>expected</i></color>.\"",
                DialogTextFormatter.Format(english.Text, centered: false, palette, bodyPen: 0));

            // Translated: the tag is a highlight, the ß is a letter.
            DialogEntry title = dialog.Entries.Single(e => e.Id == ChapterOneTitle);
            Assert.AreEqual("Kapitel Eins:  <color=#FF0000><i>Straße</i></color>",
                DialogTextFormatter.Format(title.Text, centered: true, palette, bodyPen: 0));
        });
    }
}

namespace BakAgain.Tests.PlayMode.UI.Localization {
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Text;
    using NUnit.Framework;
    using System.Collections;
    using System.IO;
    using UnityEngine.TestTools;

    /// <summary>
    /// A PO pack in the override folder reaches the UI strings through the real loader (TASK-773).
    /// </summary>
    public class LanguagePackLoadTests {
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
            string key = System.Linq.Enumerable.First(UiStringCatalog.Embedded.Entries.Keys);
            // msgid is the English for a translator's eyes only; the key is msgctxt.
            File.WriteAllText(po,
                "msgid \"\"\nmsgstr \"\"\n\"Language: xx\\n\"\n\"Content-Type: text/plain; charset=UTF-8\\n\"\n\n"
                + $"msgctxt \"{key}\"\nmsgid \"English\"\nmsgstr \"ÜBERSETZT\"\n");
            BakResourceSettings.OverridePath = _dir;
            BakResourceSettings.Language = "xx";
            LanguagePacks.Reload();
        }

        [TearDown]
        public void TearDown() {
            BakResourceSettings.Language = _savedLanguage;
            BakResourceSettings.OverridePath = _savedOverridePath;
            LanguagePacks.Reload();
            UiStrings.Catalog = UiStringCatalog.Embedded;
            Directory.Delete(_dir, true);
        }

        [UnityTest]
        public IEnumerator TheLoaderInstallsTheTranslatedCatalog() => UniTask.ToCoroutine(async () => {
            string key = System.Linq.Enumerable.First(UiStringCatalog.Embedded.Entries.Keys);

            await UiStringLoader.InstallAsync(resources: null, logger: null);

            Assert.AreEqual("xx", LanguagePacks.Current.Locale);
            Assert.AreEqual("ÜBERSETZT", UiStrings.Catalog.Get(key));
        });

        [UnityTest]
        public IEnumerator EnglishIsTheOriginalCatalog() => UniTask.ToCoroutine(async () => {
            BakResourceSettings.Language = "en";
            LanguagePacks.Reload();
            string key = System.Linq.Enumerable.First(UiStringCatalog.Embedded.Entries.Keys);

            await UiStringLoader.InstallAsync(resources: null, logger: null);

            Assert.AreEqual(UiStringCatalog.Embedded.Get(key), UiStrings.Catalog.Get(key));
        });

        /// <summary>
        /// A resource extracted from the archive arrives translated — the hook in
        /// <c>BakResourceProvider</c>, through Addressables as every screen loads it. Keyword 255 is
        /// "Yes". Skips without the game data.
        /// </summary>
        [Test]
        public void AnArchiveResourceArrivesTranslated() {
            File.AppendAllText(LanguagePacks.PathFor(_dir, "xx"),
                $"\nmsgctxt \"{TextKey.Keyword(255)}\"\nmsgid \"Yes\"\nmsgstr \"Jawohl\"\n");
            LanguagePacks.Reload();
            GameData.Resources.Data.KeywordList keywords;
            try {
                keywords = UnityEngine.AddressableAssets.Addressables
                    .LoadAssetAsync<GameData.Resources.Data.KeywordList>("KEYWORD.DAT")
                    .WaitForCompletion();
            } catch (System.Exception) {
                Assert.Ignore("No game data to extract from.");
                return;
            }
            if (keywords == null) {
                Assert.Ignore("No game data to extract from.");
            }

            Assert.AreEqual("Jawohl", keywords.Keywords[255]);
        }
    }
}

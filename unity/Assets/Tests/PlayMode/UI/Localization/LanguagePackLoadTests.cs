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
        private UiStringCatalog _savedCatalog;
        private GameData.Resources.Combat.CombatPanelLayout _savedPanelLayout;

        [SetUp]
        public void SetUp() {
            _savedLanguage = BakResourceSettings.Language;
            _savedOverridePath = BakResourceSettings.OverridePath;
            _savedCatalog = UiStrings.Catalog;   // whatever the game installed, which need not be Embedded
            _savedPanelLayout = GameData.Resources.Combat.CombatPanelLayout.Current;
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
            UiStrings.Catalog = _savedCatalog;
            GameData.Resources.Combat.CombatPanelLayout.Current = _savedPanelLayout;
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
        /// Resolves through the REAL override locator and provider, built after the test's pack is in
        /// place — the path <c>AddressableResourceProviderService</c> takes for an override key.
        /// </summary>
        private sealed class OverrideOnlyResources : IResourceProviderService {
            private readonly OverrideResourceLocator _locator = new();

            public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
                UniTask.FromResult(_locator.Locate(key, typeof(T),
                    out System.Collections.Generic.IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> at)
                    ? (T)OverrideResourceProvider.LoadOverrideObject(at[0].InternalId, typeof(T), key.ToString())
                    : null);

            public void ReleaseAssets(object owner) {
            }
        }

        private string PanelLayoutPath => Path.Combine(_dir, "Lang", "xx", "JSON",
            GameData.Resources.Combat.CombatPanelLayout.ResourceId);

        /// <summary>No column file: the panels keep the original's columns (TASK-825).</summary>
        [UnityTest]
        public IEnumerator WithoutAColumnFile_ThePanelsKeepTheOriginalsColumns() => UniTask.ToCoroutine(async () => {
            GameData.Resources.Combat.CombatPanelLayout.Current = null;   // proves the install sets it

            await UiStringLoader.InstallAsync(new OverrideOnlyResources(), logger: null);

            var stats = GameData.Resources.Combat.ActorStatsPanel.Lines("Locklear", new[] { 41, 33, 7, 25 });
            Assert.AreEqual(0x85 * 5, System.Linq.Enumerable.Single(stats, l => l.Text == "41").X);
        });

        /// <summary>A pack's <c>JSON/combatpanels.json</c> moves the column it names, and only it.</summary>
        [UnityTest]
        public IEnumerator APacksColumnFileMovesThatColumn() => UniTask.ToCoroutine(async () => {
            Directory.CreateDirectory(Path.GetDirectoryName(PanelLayoutPath));
            File.WriteAllText(PanelLayoutPath, "{ \"StatsValueX\": \"700px\" }");

            await UiStringLoader.InstallAsync(new OverrideOnlyResources(), logger: null);

            var stats = GameData.Resources.Combat.ActorStatsPanel.Lines("Locklear", new[] { 41, 33, 7, 25 });
            Assert.AreEqual(700, System.Linq.Enumerable.Single(stats, l => l.Text == "41").X);
            Assert.AreEqual(0x4e * 5, stats[1].X, "the label column is not named, so it is the original's");
        });

        [Test]
        public void AColumnThatIsNotPxIsRefused_AndABrokenFileMovesNothing() {
            BakAgain.Tests.TestSupport.ExpectedLog.Expect(UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("StatsValueX cannot be resolved"));
            var layout = UiStringLoader.ReadPanelLayout("{ \"StatsValueX\": \"50%\", \"TargetValueX\": \"800px\" }");
            Assert.AreEqual(0x85 * 5, layout.StatsValueX.Value);
            Assert.AreEqual(800, layout.TargetValueX.Value);

            BakAgain.Tests.TestSupport.ExpectedLog.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("combatpanels.json skipped"));
            Assert.AreEqual(0x99 * 5, UiStringLoader.ReadPanelLayout("{ \"TargetValueX\": 800 ").TargetValueX.Value);
        }

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

namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>A language pack installed as a <c>language</c> mod is offered and applied like a dev-folder pack (mod system spec 4.5).</summary>
    public class LanguageModTests {
        private const string Po = "msgid \"\"\nmsgstr \"\"\n\"Language: xx\\n\"\n\nmsgctxt \"k\"\nmsgid \"a\"\nmsgstr \"b\"\n";

        private TempOverrideDirectory _dev;
        private EnhancedPrefsStash _enhanced;
        private string _savedLanguage;
        private string _mod;

        [SetUp]
        public void SetUp() {
            _dev = new TempOverrideDirectory();
            _enhanced = EnhancedPrefsStash.Take();
            GameOptions.Enhanced = true;
            _savedLanguage = BakResourceSettings.Language;
            _mod = Path.Combine(ModLayers.ModsRoot, "lang-xx");
            Directory.CreateDirectory(Path.Combine(_mod, "Lang", "xx", "fonts"));
            File.WriteAllText(Path.Combine(_mod, "mod.json"),
                "{\"schema\":1,\"id\":\"lang-xx\",\"name\":\"xx\",\"version\":\"1.0.0\",\"kinds\":[\"language\"],\"modes\":[\"enhanced\",\"classic\"]}");
            File.WriteAllText(Path.Combine(_mod, "Lang", "xx", "xx.po"), Po);
            ModLayers.Initialize();
            BakResourceSettings.Language = "xx";
            LanguagePacks.Reload();
        }

        [TearDown]
        public void TearDown() {
            BakResourceSettings.Language = _savedLanguage;
            LanguagePacks.Reload();
            _enhanced.Restore();
            _dev.Dispose();
        }

        [Test]
        public void Installed_IncludesLanguageModLocales() {
            Assert.Contains("xx", LanguagePacks.Installed().ToList());
        }

        [Test]
        public void ActiveFolder_ResolvesIntoMod() {
            Assert.AreEqual(Path.Combine(_mod, "Lang", "xx"), LanguagePacks.ActiveFolder());
            Assert.IsTrue(LanguagePacks.Current.TryGet("k", out string text) && text == "b", "the mod's PO is the one read");
        }

        [Test]
        public void FontPath_ResolvesIntoMod() {
            Assert.AreEqual(Path.Combine(_mod, "Lang", "xx", "fonts", "GAME.bdf"), LanguagePacks.ActiveFontPath("GAME.FNT"));
        }

        [Test]
        public void DevFolderBeatsModForSameLocale() {
            _dev.Write(Path.Combine("Lang", "xx"), "xx.po", Po.Replace("msgstr \"b\"", "msgstr \"dev\""));
            LanguagePacks.Reload();

            Assert.AreEqual(Path.Combine(_dev.Root, "Lang", "xx"), LanguagePacks.ActiveFolder());
            Assert.IsTrue(LanguagePacks.Current.TryGet("k", out string text) && text == "dev", "the dev folder's PO is the one read");
            Assert.AreEqual(1, LanguagePacks.Installed().Count(l => l == "xx"));
        }

        [Test]
        public void LanguageMod_OnlyInItsModes() {
            string mod = Path.Combine(ModLayers.ModsRoot, "lang-yy");
            Directory.CreateDirectory(Path.Combine(mod, "Lang", "yy"));
            File.WriteAllText(Path.Combine(mod, "mod.json"),
                "{\"schema\":1,\"id\":\"lang-yy\",\"name\":\"yy\",\"version\":\"1.0.0\",\"kinds\":[\"language\"],\"modes\":[\"classic\"]}");
            File.WriteAllText(Path.Combine(mod, "Lang", "yy", "yy.po"), Po);
            string root = ModLayers.ModsRoot;

            foreach (bool enhanced in new[] { true, false }) {
                ModLayers.Reset();
                ModLayers.ModsRootOverride = root;
                GameOptions.Enhanced = enhanced;
                ModLayers.Initialize();

                Assert.AreEqual(!enhanced, LanguagePacks.Installed().Contains("yy"), enhanced ? "Enhanced" : "Classic");
            }
        }

        [Test]
        public void ActiveFontPath_NoPack_IsNull() {
            BakResourceSettings.Language = "zz";
            LogAssert.Expect(LogType.Warning, new Regex("Language 'zz' selected but no pack"));
            LanguagePacks.Reload();

            Assert.IsNull(LanguagePacks.ActiveFontPath("GAME.FNT"));
        }

        [Test]
        public void Pseudo_ArtAndFontsFromTheDevFolder() {
            BakResourceSettings.Language = GameData.Resources.Text.PseudoLocalization.Locale;
            LanguagePacks.Reload();

            Assert.AreEqual(Path.Combine(_dev.Root, "Lang", "qps"), LanguagePacks.ActiveFolder());
            Assert.AreEqual(Path.Combine(_dev.Root, "Lang", "qps", "fonts", "GAME.bdf"), LanguagePacks.ActiveFontPath("GAME.FNT"));
        }
    }
}

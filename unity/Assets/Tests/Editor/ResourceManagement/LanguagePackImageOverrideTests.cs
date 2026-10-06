namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.ResourceManagement.ResourceLocations;

    /// <summary>
    /// A language pack's own art — the German release redrew ~40 images with lettering in them —
    /// is found under <c>Lang/&lt;locale&gt;/</c> in the same layout as a mod's overrides (TASK-826).
    /// </summary>
    public class LanguagePackImageOverrideTests {
        private TempOverrideDirectory _overrides;
        private string _savedLanguage;

        [SetUp]
        public void SetUp() {
            _savedLanguage = BakResourceSettings.Language;
            _overrides = new TempOverrideDirectory();
            _overrides.Write(Path.Combine("Lang", "xx"), "xx.po",
                "msgid \"\"\nmsgstr \"\"\n\"Language: xx\\n\"\n\nmsgctxt \"k\"\nmsgid \"a\"\nmsgstr \"b\"\n");
            BakResourceSettings.Language = "xx";
            LanguagePacks.Reload();
        }

        [TearDown]
        public void TearDown() {
            BakResourceSettings.Language = _savedLanguage;
            LanguagePacks.Reload();
            _overrides.Dispose();
        }

        private static string Located(string key) {
            bool found = new OverrideResourceLocator().Locate(key, typeof(Sprite), out IList<IResourceLocation> locations);
            return found ? locations[0].InternalId : null;
        }

        [Test]
        public void APacksImageIsFoundWithModOverridesOff() {
            string pack = _overrides.Write(Path.Combine("Lang", "xx", "SCX"), "OPTIONS1.png", "png");
            BakResourceSettings.OverrideEnabled = false;

            Assert.AreEqual(pack, Located("OPTIONS1.SCX"));
            Assert.IsTrue(OverrideResourceLocator.HasAnyRoot(), "the override provider must be registered for a pack alone");
        }

        [Test]
        public void APacksImageWinsOverTheModFolders() {
            _overrides.Write("SCX", "OPTIONS1.png", "png");
            string pack = _overrides.Write(Path.Combine("Lang", "xx", "SCX"), "OPTIONS1.png", "png");

            Assert.AreEqual(pack, Located("OPTIONS1.SCX"));
        }

        [Test]
        public void ABmxFrameIsFoundInThePack() {
            string pack = _overrides.Write(Path.Combine("Lang", "xx", "BMX", "POINTERG"), "5.png", "png");

            Assert.AreEqual(pack, Located("POINTERG.BMX#5"));
        }

        [Test]
        public void ANameIsMatchedWhateverItsCase() {
            // INTRO.TTM loads "credits.SCR" and "credits.PAL" in lower case; the archive's lookup
            // ignores case, so an override has to as well, or it is never found on Linux/Android.
            string pack = _overrides.Write(Path.Combine("Lang", "xx", "SCX"), "CREDITS.png", "png");
            string frame = _overrides.Write(Path.Combine("Lang", "xx", "BMX", "INT_DYN"), "9.png", "png");

            Assert.AreEqual(pack, Located("credits.SCR"));
            Assert.AreEqual(frame, Located("int_dyn.bmp#9"));
        }

        [Test]
        public void EnglishIgnoresThePackFolder() {
            string mod = _overrides.Write("SCX", "OPTIONS1.png", "png");
            _overrides.Write(Path.Combine("Lang", "xx", "SCX"), "OPTIONS1.png", "png");
            BakResourceSettings.Language = "en";
            LanguagePacks.Reload();

            Assert.AreEqual(mod, Located("OPTIONS1.SCX"));
        }
    }
}

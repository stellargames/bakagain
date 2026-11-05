namespace BakAgain.Tests.PlayMode.ResourceManagement {
    using System.IO;
    using System.Linq;
    using BakAgain.ResourceManagement;
    using NUnit.Framework;

    /// <summary>
    /// Linux players built with Unity 6000.5 and earlier kept PlayerPrefs in unity3d/unknown/unknown/prefs;
    /// 6000.6 uses unity3d/StellarGameStudio/BaK-Again (TASK-1025). The old file is shared by every Unity
    /// game with that fault, so only this game's keys may come across.
    /// </summary>
    public class LegacyPrefsTests {
        private const string File =
            "<unity_prefs version_major=\"1\" version_minor=\"1\">\n" +
            "\t<pref name=\"Screenmanager Resolution Width\" type=\"int\">1280</pref>\n" +
            "\t<pref name=\"original game path\" type=\"string\">L2hvbWUvanZlL2Jhay9PcmlnaW5hbEdhbWU=</pref>\n" +
            "\t<pref name=\"enhanced\" type=\"int\">1</pref>\n" +
            "\t<pref name=\"enhanced PageTurn\" type=\"int\">0</pref>\n" +
            "\t<pref name=\"some other game volume\" type=\"float\">0.5</pref>\n" +
            "\t<pref name=\"language\" type=\"string\">bmw=</pref>\n" +
            "</unity_prefs>\n";

        [Test]
        public void Read_KeepsOnlyThisGamesKeys_AndDecodesStrings() {
            string path = Path.GetTempFileName();
            try {
                System.IO.File.WriteAllText(path, File);
                var prefs = LegacyPrefs.Read(path).ToDictionary(p => p.Key, p => p.Value);

                CollectionAssert.AreEquivalent(
                    new[] { "original game path", "enhanced", "enhanced PageTurn", "language" }, prefs.Keys);
                Assert.AreEqual("/home/jve/bak/OriginalGame", prefs["original game path"]);
                Assert.AreEqual(1, prefs["enhanced"]);
                Assert.AreEqual(0, prefs["enhanced PageTurn"]);
                Assert.AreEqual("nl", prefs["language"]);
            } finally {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void Read_NoFile_IsEmpty() =>
            Assert.IsEmpty(LegacyPrefs.Read(Path.Combine(Path.GetTempPath(), "no-such-dir", "prefs")));
    }
}

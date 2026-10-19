namespace BakAgain.Tests.Editor.ResourceManagement {
    using System.Collections.Generic;
    using BakAgain.ResourceManagement;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The Enhanced settings change the folders "at the next start": the running game keeps reading
    /// the ones it started with (a restored game's mod items, a pack's images), and the choice becomes
    /// the settings only when the game next starts.
    /// </summary>
    public class FoldersForTheNextStartTests {
        private static readonly string[] Keys = {
            "original game path", "enable overrides", "overrides directory", "next start",
            "next start: original game path", "next start: enable overrides", "next start: overrides directory",
        };

        private readonly Dictionary<string, (bool Has, string S, int I)> _saved = new();

        [SetUp]
        public void SetUp() {
            foreach (string k in Keys) {
                _saved[k] = (PlayerPrefs.HasKey(k), PlayerPrefs.GetString(k, null), PlayerPrefs.GetInt(k, 0));
                PlayerPrefs.DeleteKey(k);
            }
            BakResourceSettings.GamePath = "/games/bak";
            BakResourceSettings.OverrideEnabled = false;
            BakResourceSettings.OverridePath = "/mods/a";
        }

        [TearDown]
        public void TearDown() {
            foreach (string k in Keys) {
                PlayerPrefs.DeleteKey(k);
                (bool has, string s, int i) = _saved[k];
                if (!has) continue;
                if (k == "enable overrides" || k == "next start" || k == "next start: enable overrides") PlayerPrefs.SetInt(k, i);
                else PlayerPrefs.SetString(k, s);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void AChoiceForTheNextStart_LeavesTheRunningFoldersAlone_UntilThen() {
            BakResourceSettings.SetForNextStart("/games/other", true, "/mods/b");

            Assert.AreEqual("/games/bak", BakResourceSettings.GamePath, "the running game keeps its folders");
            Assert.IsFalse(BakResourceSettings.OverrideEnabled);
            Assert.AreEqual(("/games/other", true, "/mods/b"), BakResourceSettings.NextStart, "the screen shows the choice");

            BakResourceSettings.ApplyPending();

            Assert.AreEqual("/games/other", BakResourceSettings.GamePath);
            Assert.IsTrue(BakResourceSettings.OverrideEnabled);
            Assert.AreEqual("/mods/b", BakResourceSettings.OverridePath);
            Assert.AreEqual(("/games/other", true, "/mods/b"), BakResourceSettings.NextStart, "nothing left waiting");
        }

        [Test]
        public void ChoosingTheRunningFoldersAgain_LeavesNothingWaiting() {
            BakResourceSettings.SetForNextStart("/games/other", true, "/mods/b");
            BakResourceSettings.SetForNextStart("/games/bak", false, "/mods/a");

            BakResourceSettings.ApplyPending();

            Assert.AreEqual(("/games/bak", false, "/mods/a"), BakResourceSettings.NextStart);
        }
    }
}

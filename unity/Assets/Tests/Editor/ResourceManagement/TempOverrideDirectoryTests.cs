namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using System;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// Two hardening fixes to <see cref="TempOverrideDirectory"/>, the shared PlayerPrefs-stash
    /// scope every override test borrows the developer's real overrides directory through.
    ///
    /// <list type="bullet">
    /// <item><b>Nesting must fail loudly, not corrupt the outer scope.</b> The constructor
    /// unconditionally treats an existing stash as crash debris and restores it. If a second
    /// instance were constructed while an outer one is still active, that "restore" would consume
    /// the outer instance's live stash and hand the outer instance's own temp path back to
    /// PlayerPrefs as if it were the developer's real setting — the outer instance's eventual
    /// <c>Dispose()</c> would then have nothing left to restore. No caller nests today, but this
    /// is shared test infrastructure, so the guard below turns that shape into a thrown exception
    /// instead of a silent corruption.</item>
    /// <item><b>A stale stash must never beat a deliberate change.</b> Crash a run (leaving the
    /// stash in place and the live setting pointed at that run's now-orphaned temp directory),
    /// then deliberately change the overrides directory for real. The next
    /// <c>RestoreStashFromACrashedRun</c> must recognise that the live value is no longer one of
    /// its own temp directories and leave it alone, rather than silently overwriting the
    /// deliberate change with whatever was live before the crashed run started.</item>
    /// </list>
    /// </summary>
    public class TempOverrideDirectoryTests {
        // ---------------------------------------------------------------------------------------
        // Nesting guard. These two go through the public constructor/Dispose only, so — unlike
        // the RestoreStashFromACrashedRun tests below — there is no real PlayerPrefs value at risk
        // if an assertion fails: TempOverrideDirectory's own stash/restore machinery protects it.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ConstructingWhileAnotherIsActive_ThrowsInsteadOfCorruptingTheOuterScope() {
            var outer = new TempOverrideDirectory();
            try {
                Assert.Throws<InvalidOperationException>(() => new TempOverrideDirectory());
            } finally {
                outer.Dispose();
            }
        }

        [Test]
        public void AfterTheOuterInstanceIsDisposed_ANewInstanceConstructsNormally() {
            using (new TempOverrideDirectory()) { }

            // The guard must not latch permanently — only one instance may be active AT A TIME.
            Assert.DoesNotThrow(() => {
                using (new TempOverrideDirectory()) { }
            });
        }

        // ---------------------------------------------------------------------------------------
        // RestoreStashFromACrashedRun, driven directly (it is internal — see its own remarks).
        // Every test below fabricates its OWN stash/live state and restores whatever was actually
        // on the machine in a `finally`, regardless of outcome: this bypasses the constructor's
        // normal stash-what's-really-there safety net, so it must supply its own.
        // ---------------------------------------------------------------------------------------

        private sealed class RealSettingsGuard : IDisposable {
            private readonly string _realPath;
            private readonly bool _realEnabled;
            private readonly bool _hadStash;
            private readonly string _stashedPath;
            private readonly int _stashedEnabled;

            public RealSettingsGuard() {
                _realPath = BakResourceSettings.OverridePath;
                _realEnabled = BakResourceSettings.OverrideEnabled;
                _hadStash = PlayerPrefs.HasKey(TempOverrideDirectory.StashedPathKey);
                _stashedPath = _hadStash ? PlayerPrefs.GetString(TempOverrideDirectory.StashedPathKey) : null;
                _stashedEnabled = _hadStash ? PlayerPrefs.GetInt(TempOverrideDirectory.StashedEnabledKey) : 0;
            }

            public void Dispose() {
                BakResourceSettings.OverridePath = _realPath;
                BakResourceSettings.OverrideEnabled = _realEnabled;
                if (_hadStash) {
                    PlayerPrefs.SetString(TempOverrideDirectory.StashedPathKey, _stashedPath);
                    PlayerPrefs.SetInt(TempOverrideDirectory.StashedEnabledKey, _stashedEnabled);
                } else {
                    PlayerPrefs.DeleteKey(TempOverrideDirectory.StashedPathKey);
                    PlayerPrefs.DeleteKey(TempOverrideDirectory.StashedEnabledKey);
                }
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void RestoreStashFromACrashedRun_LeavesADeliberateChangeUntouched() {
            using (new RealSettingsGuard()) {
                // Fabricate exactly what a crash followed by a deliberate MetaMenu change leaves
                // behind: a stash from before the crash, and a live value that is NEITHER the
                // pre-crash setting NOR one of TempOverrideDirectory's own temp directories.
                PlayerPrefs.SetString(TempOverrideDirectory.StashedPathKey, "/dev/pre-crash/overrides");
                PlayerPrefs.SetInt(TempOverrideDirectory.StashedEnabledKey, 0);
                PlayerPrefs.Save();
                BakResourceSettings.OverridePath = "/deliberately/changed/overrides";
                BakResourceSettings.OverrideEnabled = true;

                TempOverrideDirectory.RestoreStashFromACrashedRun();

                Assert.AreEqual(
                    "/deliberately/changed/overrides", BakResourceSettings.OverridePath,
                    "a deliberate real setting must win over a stale crash-era stash");
                Assert.IsTrue(BakResourceSettings.OverrideEnabled);
                Assert.IsFalse(
                    PlayerPrefs.HasKey(TempOverrideDirectory.StashedPathKey),
                    "the stale stash no longer describes anything current and must be dropped, "
                    + "not left to mislead a later run");
            }
        }

        [Test]
        public void RestoreStashFromACrashedRun_StillRestoresWhenLiveValueIsItsOwnOrphanedTempDirectory() {
            using (new RealSettingsGuard()) {
                // The genuine crash case this method exists for: the live value is still one of
                // TempOverrideDirectory's own temp directories (nobody changed it since the crash).
                PlayerPrefs.SetString(TempOverrideDirectory.StashedPathKey, "/dev/pre-crash/overrides");
                PlayerPrefs.SetInt(TempOverrideDirectory.StashedEnabledKey, 1);
                PlayerPrefs.Save();
                BakResourceSettings.OverridePath = Path.Combine(
                    Path.GetTempPath(), TempOverrideDirectory.TempDirPrefix + "deadbeef");
                BakResourceSettings.OverrideEnabled = true;

                TempOverrideDirectory.RestoreStashFromACrashedRun();

                Assert.AreEqual("/dev/pre-crash/overrides", BakResourceSettings.OverridePath);
                Assert.IsTrue(BakResourceSettings.OverrideEnabled);
                Assert.IsFalse(PlayerPrefs.HasKey(TempOverrideDirectory.StashedPathKey));
            }
        }
    }
}

namespace BakAgain.Tests.TestSupport {
    using BakAgain.ResourceManagement;
    using NUnit.Framework;
    using NUnit.Framework.Interfaces;
    using NUnit.Framework.Internal;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.AddressableAssets.ResourceLocators;

    /// <summary>
    /// Borrows the live overrides directory for the duration of one test, and gives it back —
    /// even if the Editor dies mid-run.
    ///
    /// <para><b>Why this is not just save-and-restore in <c>[TearDown]</c>.</b>
    /// <see cref="BakResourceSettings.OverridePath"/> is not a field: its setter writes
    /// <c>PlayerPrefs</c> and calls <c>Save()</c>, so it is the developer's real, persisted
    /// overrides directory. A <c>[TearDown]</c> restores it only if teardown runs — an Editor
    /// crash, an aborted test run or a domain reload mid-test leaves the pref pointing at a temp
    /// directory this class then deletes, and the developer's own mod setup is silently gone.
    /// So the real value is <b>stashed in PlayerPrefs before</b> the live one is replaced, and any
    /// stash left behind by an earlier crashed run is restored on the next construction. The
    /// window in which a crash can lose the setting is therefore a few instructions wide instead
    /// of the whole test.</para>
    ///
    /// <para>Each instance owns its own temp directory and its own state, so two fixtures (or a
    /// future parallel runner) cannot trample each other through shared statics.</para>
    /// </summary>
    public sealed class TempOverrideDirectory : IDisposable {
        // PlayerPrefs keys holding the developer's real settings while a test has borrowed them.
        // Deliberately not in BakResourceSettings: these exist only to survive a crashed test run.
        // internal (not private): TempOverrideDirectoryTests drives RestoreStashFromACrashedRun
        // directly against fabricated stash/live states to test the crash-vs-deliberate-change
        // distinction without risking a real PlayerPrefs value on a failed test.
        internal const string StashedPathKey = "test stashed overrides directory";
        internal const string StashedEnabledKey = "test stashed enable overrides";

        // Every temp directory this class hands out is named with this prefix — see Root below.
        // RestoreStashFromACrashedRun uses it to tell "a crashed run's leftover temp path" apart
        // from "a real setting", so it never overwrites the latter. See Fix 3 in its remarks.
        internal const string TempDirPrefix = "bakagain-override-";

        // In-process reentrancy guard, deliberately NOT persisted in PlayerPrefs like the stash
        // above. Its only job is telling a live NESTED scope apart from a genuinely crashed
        // PREVIOUS run: a crash always means the next TempOverrideDirectory() runs in a fresh
        // process, which resets this to false, so the crash-recovery path is never suppressed for
        // the case it exists to serve. A nested construction, by contrast, runs in the SAME
        // process as its still-open outer instance, so this is already true and the guard below
        // fires instead of silently treating the outer instance's live stash as crash debris.
        private static bool s_instanceActive;

        /// <summary>The temp directory that is, for the lifetime of this scope, the game's
        /// overrides directory. Resource subdirectories are created by <see cref="Write"/>.</summary>
        public string Root { get; }

        public TempOverrideDirectory() {
            // No caller nests today, but this is shared test infrastructure and the shape is one
            // `using` away: an inner instance's unconditional restore-from-stash would consume the
            // still-active outer instance's stash, hand the outer instance's live temp path back
            // to PlayerPrefs as if it were the developer's real setting, and leave the outer
            // instance's own final Dispose() with nothing to restore. Fail loudly instead.
            if (s_instanceActive) {
                throw new InvalidOperationException(
                    "TempOverrideDirectory does not support nesting: another instance is already "
                    + "active in this process. Dispose the outer instance before constructing a "
                    + "new one.");
            }

            // Anything still stashed belongs to a run that never reached its teardown. Reached
            // only when s_instanceActive was false, so a stash found here cannot belong to a
            // still-active sibling — it can only be crash debris.
            RestoreStashFromACrashedRun();

            PlayerPrefs.SetString(StashedPathKey, BakResourceSettings.OverridePath);
            PlayerPrefs.SetInt(StashedEnabledKey, BakResourceSettings.OverrideEnabled ? 1 : 0);
            PlayerPrefs.Save();

            Root = Path.Combine(Path.GetTempPath(), TempDirPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            BakResourceSettings.OverridePath = Root;
            BakResourceSettings.OverrideEnabled = true;
            s_instanceActive = true;
        }

        /// <summary>
        /// Write an override document where the override locator looks for it:
        /// <c>&lt;OverridePath&gt;/&lt;resourceSubdirectory&gt;/&lt;filename&gt;</c> (e.g.
        /// <c>DAT/DIALSTYL.json</c>). Returns the full path.
        /// </summary>
        public string Write(string resourceSubdirectory, string filename, string contents) {
            string directory = Path.Combine(Root, resourceSubdirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose() {
            RestoreStashFromACrashedRun();
            if (Directory.Exists(Root)) {
                Directory.Delete(Root, true);
            }
            s_instanceActive = false;
        }

        /// <summary>
        /// Restores (and clears) the stashed settings if there are any — but only when the CURRENT
        /// live value still looks like one of our own temp directories.
        ///
        /// <para>Without that check, this is a trap: crash a run (leaving the stash in place and
        /// the live setting pointed at that run's now-orphaned temp directory), then deliberately
        /// change the overrides directory for real (e.g. in MetaMenu) — a directory that has
        /// nothing to do with any test. The NEXT test run's constructor would call this
        /// unconditionally, see the stale stash, and silently overwrite the deliberate change with
        /// whatever the developer's setting happened to be BEFORE the crashed run started. A
        /// setting only a crashed run could have produced is the one thing this is allowed to
        /// undo — never a real one, however it got there.</para>
        /// </summary>
        // internal (not private): TempOverrideDirectoryTests calls this directly to verify the
        // crash-vs-deliberate-change distinction without going through the public constructor,
        // which would stash whatever is ACTUALLY live on the running machine.
        internal static void RestoreStashFromACrashedRun() {
            if (!PlayerPrefs.HasKey(StashedPathKey)) {
                return;
            }
            if (!IsOwnedTempDirectory(BakResourceSettings.OverridePath)) {
                // The live value isn't one of ours, so it isn't debris from a crashed run — either
                // nothing crashed, or someone deliberately set it since. Either way it must win;
                // the stash no longer describes anything current, so drop it rather than let it
                // mislead a later run.
                PlayerPrefs.DeleteKey(StashedPathKey);
                PlayerPrefs.DeleteKey(StashedEnabledKey);
                PlayerPrefs.Save();
                return;
            }
            BakResourceSettings.OverridePath = PlayerPrefs.GetString(StashedPathKey);
            BakResourceSettings.OverrideEnabled = PlayerPrefs.GetInt(StashedEnabledKey, 0) == 1;
            PlayerPrefs.DeleteKey(StashedPathKey);
            PlayerPrefs.DeleteKey(StashedEnabledKey);
            PlayerPrefs.Save();
        }

        private static bool IsOwnedTempDirectory(string path) {
            if (string.IsNullOrEmpty(path)) {
                return false;
            }
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmed).StartsWith(TempDirPrefix, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Empties Addressables' locator list for the duration of one test and puts it back exactly
    /// as it was.
    ///
    /// <para>A test that drives the real resource system has to control locator ORDER, because
    /// Addressables takes the first locator that resolves a key. In a live Editor session the
    /// locators are already registered — and if <c>BakResourceLocator</c> happens to sit ahead of
    /// <c>OverrideResourceLocator</c> (which it does after any re-initialisation with overrides
    /// disabled) then the shipped resource wins and an override test passes or fails on session
    /// history rather than on the code under test. Starting from an empty list and letting
    /// <c>ResourceManagementInitializer</c> register in its production order makes the test
    /// deterministic.</para>
    /// </summary>
    public sealed class IsolatedResourceLocators : IDisposable {
        private readonly List<IResourceLocator> _saved;

        public IsolatedResourceLocators() {
            _saved = Addressables.ResourceLocators.ToList();
            foreach (IResourceLocator locator in _saved) {
                Addressables.RemoveResourceLocator(locator);
            }
        }

        public void Dispose() {
            foreach (IResourceLocator locator in Addressables.ResourceLocators.ToList()) {
                Addressables.RemoveResourceLocator(locator);
            }
            foreach (IResourceLocator locator in _saved) {
                Addressables.AddResourceLocator(locator);
            }
        }
    }

    /// <summary>
    /// The shipped game archive, which a few tests genuinely need (anything that constructs
    /// <c>BakResourceLocator</c> or goes through <c>ResourceManagementInitializer</c> reads
    /// <c>KRONDOR.001</c>'s directory). Present on dev machines, absent on a bare checkout — such
    /// a test ignores rather than fails there.
    /// </summary>
    public static class ShippedGameData {
        public const string ArchiveName = "KRONDOR.001";

        public static bool IsAvailable {
            get {
                string gamePath = BakResourceSettings.GamePath;
                return !string.IsNullOrEmpty(gamePath) && File.Exists(Path.Combine(gamePath, ArchiveName));
            }
        }

        internal const string MissingReason =
            "Needs the shipped game data: BakResourceSettings.GamePath must point at a directory containing "
            + ArchiveName + ".";

        /// <summary>Ignore the calling test unless the shipped archive is configured and present.</summary>
        public static void RequireOrIgnore() {
            if (!IsAvailable) {
                Assert.Ignore(MissingReason);
            }
        }
    }

    /// <summary>
    /// Marks a test (or every test in a class) as ignored when the shipped game data is absent — CI
    /// and a fresh clone have none. Unlike <see cref="ShippedGameData.RequireOrIgnore"/> it works for
    /// any test shape, including <c>=&gt; UniTask.ToCoroutine(...)</c>, because it is applied when the
    /// test is discovered rather than from inside the body.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RequiresShippedGameDataAttribute : NUnitAttribute, IApplyToTest {
        public void ApplyToTest(Test test) {
            if (test.RunState != RunState.NotRunnable && !ShippedGameData.IsAvailable) {
                test.RunState = RunState.Ignored;
                test.Properties.Set(PropertyNames.SkipReason, ShippedGameData.MissingReason);
            }
        }
    }
}

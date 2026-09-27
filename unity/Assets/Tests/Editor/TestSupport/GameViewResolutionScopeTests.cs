namespace BakAgain.Tests.Editor.TestSupport {
    using System.Reflection;
    using BakAgain.Tests.TestSupport;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// The scope must not leave anything behind — the fence for TASK-243.
    /// </summary>
    /// <remarks>
    /// <b>What went wrong without this.</b> Six fixtures each forced the Game view with their own
    /// preset name and "restored" it with a second one, and the third argument to
    /// <c>SetCustomRenderingResolution</c> is the preset's identity, not a label. Thirteen presets
    /// accumulated in the Editor's preferences, and every suite finished with one of them SELECTED —
    /// so an Editor opened after a test run showed whatever the last test had forced. A
    /// <c>*-restore</c> entry captured a 321x531 window that way, every later run inherited it, and
    /// it eventually failed a UI test on a machine that had never seen that size.
    ///
    /// <para>These two tests are cheap and they are the only thing standing between us and that
    /// happening again, because the symptom lives in Editor preferences rather than in the repo —
    /// nothing in a normal review would show it.</para>
    /// </remarks>
    public class GameViewResolutionScopeTests {
        private static int CustomSizeCount() {
            object group = CurrentGroup(out System.Type groupType);
            return group == null ? -1 : (int)groupType.GetMethod("GetCustomCount").Invoke(group, null);
        }

        private static object CurrentGroup(out System.Type groupType) {
            groupType = null;
            Assembly editor = typeof(EditorWindow).Assembly;
            System.Type sizesType = editor.GetType("UnityEditor.GameViewSizes");
            System.Type singleton = editor.GetType("UnityEditor.ScriptableSingleton`1")
                ?.MakeGenericType(sizesType);
            object instance = singleton?.GetProperty("instance",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object group = sizesType?.GetProperty("currentGroup",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
            groupType = group?.GetType();
            return group;
        }

        private static bool TrySetSelectedSizeIndex(int index) {
            System.Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            PropertyInfo property = gameView?.GetProperty("selectedSizeIndex",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null) {
                return false;
            }
            var any = false;
            foreach (Object window in Resources.FindObjectsOfTypeAll(gameView)) {
                property.SetValue(window, index);
                ((EditorWindow)window).Repaint();
                any = true;
            }
            return any;
        }

        private static int SelectedSizeIndex() {
            System.Type gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            PropertyInfo property = gameView?.GetProperty("selectedSizeIndex",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null) {
                return -1;
            }
            foreach (Object window in Resources.FindObjectsOfTypeAll(gameView)) {
                return (int)property.GetValue(window);
            }
            return -1;
        }

        [Test]
        public void REPEATEDForcingMintsAtMostOnePreset() {
            // *** THE RULE: one shared preset name, for ever. *** A per-fixture name is a per-fixture
            // saved preset, and six fixtures x two names each is how thirteen accumulated. Three
            // cycles here would have added up to six entries under the old spelling.
            int before = CustomSizeCount();
            if (before < 0) {
                Assert.Ignore("GameViewSizes internals unavailable in this Editor version");
            }

            for (var i = 0; i < 3; i++) {
                GameViewResolutionScope scope = GameViewResolutionScope.Force(1280, 1024);
                scope.Restore();
            }

            Assert.LessOrEqual(CustomSizeCount(), before + 1,
                "forcing may create the ONE shared preset and must never create a second");
        }

        [Test]
        public void RESTOREPutsTheSelectionBack_NotOnTheTestsOwnPreset() {
            // The half that actually bit: the suite finishing on a test's resolution. Restoring the
            // INDEX is what returns the developer to their own choice — normally the built-in
            // Full HD, which is the port's target aspect.
            if (SelectedSizeIndex() < 0) {
                Assert.Ignore("GameView.selectedSizeIndex unavailable in this Editor version");
            }

            // *** ESTABLISH THE BASELINE RATHER THAN INHERIT IT. *** The first version of this test
            // captured whatever the Game view happened to be on and asserted that forcing changed
            // it. That fails whenever the ambient selection is ALREADY the shared test preset --
            // which it routinely is, because a previous run's Restore put it back there. Depending
            // on ambient editor state is the exact flaw TASK-243 was filed about, and this test
            // had it too.
            const int fullHd = 3;   // a built-in, so it exists whatever customs are defined
            Assert.IsTrue(TrySetSelectedSizeIndex(fullHd), "could not set a known baseline");
            int before = SelectedSizeIndex();
            Assert.AreEqual(fullHd, before);

            GameViewResolutionScope scope = GameViewResolutionScope.Force(1280, 1024);
            Assert.AreNotEqual(before, SelectedSizeIndex(),
                "sanity: forcing must actually have changed the selection, or this proves nothing");

            scope.Restore();

            Assert.AreEqual(before, SelectedSizeIndex(),
                "the Game view must come back to whatever it was, not stay on the test's preset");
        }
    }
}

namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using System.Collections;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="DialogOpenWipe.PlayAsync"/> asked to reveal a panel that is already gone.
    /// </summary>
    /// <remarks>
    /// <b>The hazard is an await over a mutable field.</b> <c>DialogManager.ShowEntryCore</c> reads
    /// <c>_activePanel</c>, awaits <c>WaitForResolvedRect</c>, and used to read the field AGAIN to
    /// hand to <c>PlayAsync</c>. <c>RemovePanel</c> nulls that field, and a concurrent show or
    /// teardown calls it while the wait is in flight — so <c>PlayAsync</c> received <c>null</c> and
    /// threw at its first unguarded dereference. The exception escaped through <c>ShowById</c> to
    /// whoever awaited it and the dialog never appeared: 20 wipe frames, 18 "threw while rendering"
    /// in one 20 h Editor log (TASK-578).
    ///
    /// <para><b>The real fix is at the call site</b> — it captures the panel once and wipes it only
    /// while it is still the active one. This fixture covers the backstop, which is the part that
    /// can be driven deterministically.</para>
    ///
    /// <para><b>Why not a fixture that drives the race itself.</b> Three were written and all three
    /// passed against the UNFIXED code. The window is narrower than a frame: the panel's geometry
    /// has already resolved by the time <c>DisplayEntry</c> returns control to a test, so a
    /// teardown from test code always lands after the wait completed. Tearing down EARLIER does not
    /// help either — it detaches the panel, the rect then never resolves, and the call site's
    /// zero-size guard skips the wipe for an unrelated reason. Each of those greens was vacuous and
    /// only the negative control exposed them, which is why they are described here instead of
    /// committed.</para>
    /// </remarks>
    public class DialogPanelTornDownDuringTheOpenWipeTests {
        private static readonly Rect AnyRect = new Rect(10f, 10f, 200f, 60f);

        // Only the null argument is under test here, so the hint just has to be a real one.
        private static LayoutHint AnyArea() {
            var hint = new LayoutHint();
            hint.Left = LayoutLength.Px(10f);
            hint.Top = LayoutLength.Px(10f);
            hint.Width = LayoutLength.Px(200f);
            hint.Height = LayoutLength.Px(60f);

            return hint;
        }

        /// <summary>
        /// THE FENCE. Before the guard this threw <see cref="System.NullReferenceException"/> at
        /// <c>panel.RemoveFromHierarchy()</c> — the exact frame at the top of the logged stack.
        /// </summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator ANullPanel_SkipsTheWipe_InsteadOfThrowing() =>
            UniTask.ToCoroutine(async () => {
                var stage = new VisualElement();
                var cts = new CancellationTokenSource();

                // The warning is the point of the guard — a silent skip would hide the next
                // instance of this bug — so the fixture expects it rather than letting it fail the
                // run as an unexpected log.
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "DialogOpenWipe: asked to wipe with no panel"));

                // If this throws, UniTask.ToCoroutine surfaces it and the test fails. That IS the
                // assertion; no try/catch needed to state it.
                await DialogOpenWipe.PlayAsync(stage, null, AnyArea(), AnyRect, 0.05f, cts.Token);

                Assert.IsNull(stage.Q<VisualElement>("DialogOpenWipeMask"),
                    "a skipped wipe must not leave a mask on the stage — an empty mask over the "
                    + "screen is worse than no wipe at all");
                Assert.AreEqual(0, stage.childCount,
                    "a wipe with nothing to reveal must leave the stage exactly as it found it");
            });

        /// <summary>
        /// The other half of the same guard: a panel with no stage to put the mask on.
        /// </summary>
        [UnityTest]
        [Timeout(15000)]
        public IEnumerator ANullStage_SkipsTheWipe_InsteadOfThrowing() =>
            UniTask.ToCoroutine(async () => {
                var panel = new VisualElement();
                var cts = new CancellationTokenSource();

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "DialogOpenWipe: asked to wipe with no stage"));

                await DialogOpenWipe.PlayAsync(null, panel, AnyArea(), AnyRect, 0.05f, cts.Token);

                Assert.IsNull(panel.parent,
                    "the panel must be left where it was, not reparented into a mask that could "
                    + "not be created");
            });
    }
}

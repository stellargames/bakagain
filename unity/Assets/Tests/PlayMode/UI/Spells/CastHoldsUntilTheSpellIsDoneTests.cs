namespace BakAgain.Tests.PlayMode.UI.Spells {
    using System.Collections;
    using System.Reflection;
    using BakAgain.UI.Spells;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A field spell's text is drawn over the empty cast panel, and the world comes back only after
    /// the spell's handler returns (CSPELL.C:2394-2414, WORLDLP.C:312-314). The port popped the
    /// screen as the commit was raised, so the text came up over the live world (TASK-1128 row 1).
    /// </summary>
    public class CastHoldsUntilTheSpellIsDoneTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _go;

        private sealed class CountingNavigator : BakAgain.UI.Navigation.IScreenNavigator {
            public int Pops;
            public BakAgain.UI.Navigation.IScreen Current => null;
            public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;
            public UniTask Push(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Pop() { Pops++; return UniTask.CompletedTask; }
            public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        [TearDown]
        public void TearDown() {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [UnityTest]
        public IEnumerator TheScreenStaysUpUntilTheSpellHasRun() => UniTask.ToCoroutine(async () => {
            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            var nav = new CountingNavigator();
            screen.Construct(null, null, nav, null, new BakAgain.Core.GameSession(), null);
            typeof(CastScreen).GetField("_sliderSpell", Any).SetValue(screen, 3);
            var spell = new UniTaskCompletionSource();
            screen.Committed += (_, _, _) => screen.HoldUntil(spell.Task);

            screen.CommitPowerSelection();
            await UniTask.Yield();
            Assert.AreEqual(0, nav.Pops, "the panel stays behind the spell's text");

            spell.TrySetResult();
            await UniTask.Yield();
            await UniTask.Yield();
            Assert.AreEqual(1, nav.Pops, "and goes once the spell is done");
        });

        /// <summary>
        /// The caster's ring is gone while the spell runs: closing, the original pastes back the
        /// portrait strip it saved before the ring was drawn (CSPELL.C:2396-2406), then the handler
        /// runs. Seen in the original's Scent of Sarig: no ring on Owyn under the text (TASK-1128).
        /// </summary>
        [UnityTest]
        public IEnumerator TheCastersRingIsGoneWhileTheSpellRuns() => UniTask.ToCoroutine(async () => {
            _go = new GameObject("CastScreenUnderTest");
            var document = _go.AddComponent<UnityEngine.UIElements.UIDocument>();
            var panel = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = panel;
            var screen = _go.AddComponent<CastScreen>();
            var nav = new CountingNavigator();
            screen.Construct(null, null, nav, null, new BakAgain.Core.GameSession(), null);
            var ring = new UnityEngine.UIElements.VisualElement();
            ring.AddToClassList("cast-caster-highlight");
            document.rootVisualElement.Add(ring);
            var spell = new UniTaskCompletionSource();
            screen.Committed += (_, _, _) => screen.HoldUntil(spell.Task);

            screen.CommitPowerSelection();
            await UniTask.Yield();

            Assert.IsNull(ring.parent, "no caster ring under the spell's text");
            Assert.AreEqual(0, nav.Pops, "control: the screen is still up");
            spell.TrySetResult();
            Object.DestroyImmediate(panel);
        });

        [UnityTest]
        public IEnumerator WithNothingToWaitForItPopsAtOnce() => UniTask.ToCoroutine(async () => {
            _go = new GameObject("CastScreenUnderTest");
            _go.SetActive(false);
            var screen = _go.AddComponent<CastScreen>();
            var nav = new CountingNavigator();
            screen.Construct(null, null, nav, null, new BakAgain.Core.GameSession(), null);

            screen.CommitPowerSelection();
            await UniTask.Yield();
            Assert.AreEqual(1, nav.Pops, "a combat cast, or a cancel, pops as before");
        });
    }
}

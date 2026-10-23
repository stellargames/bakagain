namespace BakAgain.Tests.PlayMode.UI.Character {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Character;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <b>Opening the character sheet fades it in ONCE</b> (TASK-963). The original fades the screen
    /// out, draws the sheet under a zeroed palette and fades that up once, on the first draw only
    /// (CHARSCRN.C:275 <c>palette_fade_out</c>, :303-306 <c>palette_fade_in</c> under
    /// <c>firstDraw</c>). The port's navigator already fades every pushed screen's root in
    /// (ScreenBase.ShowAsync), and the sheet ran a second fade of its own after the push returned:
    /// fully shown, then snapped to 1/8 and faded up again.
    /// </summary>
    public class CharacterSheetFadesInOnceTests {
        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() => _stash.Restore();

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ThePortraitsSheetFadesInOnce([Values(false, true)] bool enhanced) => UniTask.ToCoroutine(async () => {
            GameOptions.Enhanced = enhanced;
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) Assert.Ignore("no LifetimeScope: DI has not built the screens");
            var sheet = (CharacterSheetScreen)scope.Container.Resolve(typeof(CharacterSheetScreen));
            FieldInfo sessionField = typeof(CharacterSheetScreen).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
            object realSession = sessionField.GetValue(sheet);
            var session = new GameSession();
            session.Initialize(new SaveGameBuilder().WithBackingBody().WithPartyActors()
                .WithParty(new[] { "A", "B", "C" }, new byte[] { 0, 1, 2 }).Build(), GameSessionSource.NewGame);
            sessionField.SetValue(sheet, session);
            try {
                UniTask run = sheet.RunAsync(0);
                List<float> trace = await TraceOpacityAsync(sheet);
                sheet.PrimaryAction(1);   // Exit
                await UniTask.WhenAny(run, UniTask.Delay(5000, ignoreTimeScale: true));
                Assert.That(FadeIns(trace), Is.EqualTo(1), $"enhanced={enhanced}: opacity per frame {string.Join(" ", trace)}");
            } finally {
                sessionField.SetValue(sheet, realSession);
            }
        });

        /// <summary>Control: another pushed screen fades in once through the navigator alone.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator AnotherPushedScreenFadesInOnce([Values(false, true)] bool enhanced) => UniTask.ToCoroutine(async () => {
            GameOptions.Enhanced = enhanced;
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) Assert.Ignore("no LifetimeScope: DI has not built the screens");
            var navigator = (IScreenNavigator)scope.Container.Resolve(typeof(IScreenNavigator));
            var menu = (BakAgain.UI.InGameMenu)scope.Container.Resolve(typeof(BakAgain.UI.InGameMenu));
            navigator.Push(menu).Forget();
            List<float> trace = await TraceOpacityAsync(menu);
            await navigator.Pop();
            Assert.That(FadeIns(trace), Is.EqualTo(1), $"enhanced={enhanced}: opacity per frame {string.Join(" ", trace)}");
        });

        /// <summary>The root's opacity each frame from activation until it has held full for 20 frames.</summary>
        private static async UniTask<List<float>> TraceOpacityAsync(MonoBehaviour screen) {
            var trace = new List<float>();
            int full = 0;
            for (int frame = 0; frame < 600 && full < 20; frame++) {
                await UniTask.NextFrame();
                if (!screen.gameObject.activeInHierarchy) continue;
                float opacity = screen.GetComponent<UIDocument>().rootVisualElement.resolvedStyle.opacity;
                trace.Add(Mathf.Round(opacity * 1000f) / 1000f);
                full = opacity >= 0.999f ? full + 1 : 0;
            }
            Assert.That(full, Is.GreaterThanOrEqualTo(20), "the screen came up and stayed at full opacity");
            return trace;
        }

        /// <summary>Rises from below full to full: one per fade-in.</summary>
        private static int FadeIns(List<float> trace) {
            int count = 0;
            float previous = 0f;
            foreach (float opacity in trace) {
                if (previous < 0.999f && opacity >= 0.999f) count++;
                previous = opacity;
            }
            return count;
        }
    }
}

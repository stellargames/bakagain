namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The navigator covers a transition with a fade — TASK-214, decided 2026-09-03.
    /// </summary>
    /// <remarks>
    /// <b>The ORDER is the whole point.</b> A fade that starts after the swap shows the change and
    /// then hides it, which is worse than cutting. That ordering is also why the fade lives here
    /// rather than at the two places that compute "the screen changed" — both are synchronous, so
    /// neither can hold a transition open while the screen darkens.
    /// </remarks>
    public class ScreenNavigatorFadeTests {
        private sealed class RecordingFade : IScreenFade {
            public readonly List<string> Calls = new List<string>();

            public UniTask FadeOutAsync() {
                Calls.Add("out");
                return UniTask.CompletedTask;
            }

            public UniTask FadeInAsync() {
                Calls.Add("in");
                return UniTask.CompletedTask;
            }
        }

        private sealed class StubScreen : IScreen {
            public StubScreen(RecordingFade fade, string id) {
                _fade = fade;
                _id = id;
            }

            private readonly RecordingFade _fade;
            private readonly string _id;

            public UniTask ShowAsync() {
                _fade.Calls.Add("show:" + _id);
                return UniTask.CompletedTask;
            }

            public UniTask HideAsync() {
                _fade.Calls.Add("hide:" + _id);
                return UniTask.CompletedTask;
            }
        }

        [UnityTest]
        public IEnumerator APushDarkensBeforeTheSwapAndClearsAfterIt() =>
            UniTask.ToCoroutine(async () => {
                var fade = new RecordingFade();
                var navigator = new ScreenNavigator { Fade = fade };

                await navigator.Push(new StubScreen(fade, "a"));

                Assert.AreEqual("out", fade.Calls[0], "the screen must darken BEFORE anything swaps");
                Assert.AreEqual("in", fade.Calls[fade.Calls.Count - 1],
                    "and clear only once the new screen is up");
                CollectionAssert.Contains(fade.Calls, "show:a");
            });

        [UnityTest]
        public IEnumerator EveryScreenReplacingTransitionFades() => UniTask.ToCoroutine(async () => {
            var fade = new RecordingFade();
            var navigator = new ScreenNavigator { Fade = fade };
            var a = new StubScreen(fade, "a");

            await navigator.ResetTo(a);
            await navigator.Push(new StubScreen(fade, "b"));
            await navigator.Replace(new StubScreen(fade, "c"));
            await navigator.Pop();

            Assert.AreEqual(4, CountOf(fade.Calls, "out"), "ResetTo, Push, Replace and Pop each fade");
            Assert.AreEqual(4, CountOf(fade.Calls, "in"));
        });

        [UnityTest]
        public IEnumerator ClearDoesNotFade_BecauseTheResetThatFollowsIt_Does() =>
            UniTask.ToCoroutine(async () => {
                // Leaving a configuration is half a transition: Clear empties the stack and a ResetTo
                // enters the next one. Fading both would darken twice, with the cleared screen
                // flashing in between.
                var fade = new RecordingFade();
                var navigator = new ScreenNavigator { Fade = fade };

                await navigator.Clear();

                Assert.AreEqual(0, CountOf(fade.Calls, "out"));
            });

        [UnityTest]
        public IEnumerator WithNoFadeWiredTheNavigatorBehavesExactlyAsBefore() =>
            UniTask.ToCoroutine(async () => {
                // The default is NullScreenFade, so adding the seam changed nothing on its own.
                var navigator = new ScreenNavigator();
                var fade = new RecordingFade();

                await navigator.Push(new StubScreen(fade, "a"));

                Assert.AreEqual(new List<string> { "show:a" }, fade.Calls);
            });

        private sealed class FadingScreen : ScreenBase { }   // ScreenBase's own 0.15 s dip

        /// <summary>
        /// An unfaded push or pop CUTS: the outgoing screen is gone and the incoming one is fully
        /// opaque in the same frame, with no frame between (TASK-887).
        /// </summary>
        /// <remarks>
        /// <b>Unfaded has to reach the screens' own dips, not only the navigator's fade.</b> The cast
        /// screen pushes unfaded because its wipe is the original's only transition there
        /// (CSPELL.C:2108-2128, the travel view stays on screen until the wipe). Measured 2026-10-22
        /// with a 20 Hz burst: the travel HUD still faded out to an empty frame and the cast screen
        /// faded in over its own cover, the "blank frame and ghost frame" of TASK-887.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnUnfadedPushAndPopCutInOneFrame() => UniTask.ToCoroutine(async () => {
            var hudGo = new GameObject("hud", typeof(UIDocument));
            var hud = hudGo.AddComponent<FadingScreen>();
            hudGo.SetActive(false);
            var castGo = new GameObject("cast", typeof(UIDocument));
            var cast = castGo.AddComponent<FadingScreen>();
            castGo.SetActive(false);
            try {
                var navigator = new ScreenNavigator();
                await navigator.ResetTo(hud);

                int frame = Time.frameCount;
                await navigator.PushUnfaded(cast);
                Assert.AreEqual(frame, Time.frameCount, "the push must not spend frames dipping");
                Assert.IsFalse(hudGo.activeSelf, "the screen beneath is gone at once");
                Assert.AreEqual(1f, castGo.GetComponent<UIDocument>().rootVisualElement.style.opacity.value,
                    0.001f, "and the pushed one is opaque at once, not fading in");

                frame = Time.frameCount;
                await navigator.PopUnfaded();
                Assert.AreEqual(frame, Time.frameCount, "the pop must not spend frames dipping either");
                Assert.IsFalse(castGo.activeSelf);
                Assert.AreEqual(1f, hudGo.GetComponent<UIDocument>().rootVisualElement.style.opacity.value,
                    0.001f);
            } finally {
                Object.DestroyImmediate(hudGo);
                Object.DestroyImmediate(castGo);
            }
        });

        private static int CountOf(List<string> calls, string value) {
            var n = 0;
            foreach (string call in calls) {
                if (call == value) {
                    n++;
                }
            }
            return n;
        }
    }
}

namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;

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

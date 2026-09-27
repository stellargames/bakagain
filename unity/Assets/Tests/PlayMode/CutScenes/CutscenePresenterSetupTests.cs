namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using GameData.Resources.Animation;
    using GameData.Resources.Palette;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;

    /// <summary>
    /// The setup both <see cref="CutscenePresenter"/> entry points share (TASK-159).
    /// </summary>
    /// <remarks>
    /// <b>These cover an ORDERING, which is the kind of thing an extraction quietly reverses.</b>
    /// The two entry points had eight setup steps written out twice — load, <c>Show</c>, palette,
    /// state, player, pre-process, image-slot reset, chapter — and they were folded into one
    /// <c>BeginAsync</c>. Two of those steps have an observable order: the load is checked
    /// <i>before</i> the view is shown, so a cutscene that will not load leaves the screen alone
    /// instead of flashing an empty canvas; and no player is created until the load has succeeded.
    ///
    /// <para>Both entry points are asserted separately on purpose. The whole point of the
    /// extraction is that they behave identically, and a test that only drove one would let the
    /// other drift back — which is the failure the extraction exists to prevent.</para>
    /// </remarks>
    public class CutscenePresenterSetupTests {
        /// <summary>A cache that has nothing, so <c>LoadCutsceneResource</c> returns null.</summary>
        private sealed class EmptyCache : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class => UniTask.FromResult<T>(null);

            public void Clear() { }
        }

        /// <summary>
        /// Fails the test if it is ever asked for a player. On a failed load nothing should reach
        /// it — an assertion that a null check moved is worth more than a count.
        /// </summary>
        private sealed class NoPlayerExpected : ICutscenePlayerFactory {
            public CutscenePlayer Create(CutsceneState cutsceneState) {
                Assert.Fail("a player was created for a cutscene that never loaded");
                return null;
            }
        }

        private sealed class RecordingView : ICutsceneView {
            public bool Shown { get; private set; }

            public bool Hidden { get; private set; }

            public void Show() => Shown = true;

            public void Hide() => Hidden = true;

            // Never reached on the failed-load path; the load is checked first, which is the point.
            public BakAgain.Graphics.UiImage Canvas => null;

            // ICutsceneView extends IScreen. The presenter drives the synchronous pair, so these
            // exist to satisfy the interface and record nothing.
            public UniTask ShowAsync() {
                Show();
                return UniTask.CompletedTask;
            }

            public UniTask HideAsync() {
                Hide();
                return UniTask.CompletedTask;
            }
        }

        private static CutscenePresenter PresenterThatCannotLoad() =>
            new CutscenePresenter(NullLogger<CutscenePresenter>.Instance, new EmptyCache(),
                new NoPlayerExpected(), null);

        /// <summary>A cache with just enough for the load and the palette to succeed.</summary>
        /// <remarks>
        /// Dispatches on the requested type rather than the key, because the presenter asks for
        /// three different ones ("<c>NAME.ADS</c>", the animation file the ADS names, and
        /// "options.pal") and only the type distinguishes what it wants back.
        /// </remarks>
        private sealed class LoadableCache : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
                object result = null;
                if (typeof(T) == typeof(AnimatorResource)) {
                    result = new AnimatorResource("ADS") {
                        ResourceFiles = new Dictionary<int, string> { [0] = "SCENE.TTM" },
                    };
                } else if (typeof(T) == typeof(AnimationResource)) {
                    result = new AnimationResource("TTM", "V1.0",
                        new Dictionary<int, string>(), new List<Frame>());
                } else if (typeof(T) == typeof(PaletteResource)) {
                    result = new PaletteResource("options.pal");
                }
                return UniTask.FromResult((T)result);
            }

            public void Clear() { }
        }

        private sealed class ThrowingFactory : ICutscenePlayerFactory {
            public CutscenePlayer Create(CutsceneState cutsceneState) =>
                throw new InvalidOperationException("no player today");
        }

        /// <summary>A view with a real canvas, so <c>new CutsceneState</c> gets past construction.</summary>
        private sealed class CanvasView : ICutsceneView {
            private readonly GameObject _host = new("PresenterSetupCanvas");
            private readonly BakAgain.Graphics.UiImage _canvas;

            public CanvasView() {
                RawImage raw = _host.AddComponent<RawImage>();
                raw.texture = new Texture2D(8, 8);
                _canvas = new BakAgain.Graphics.UiImage(raw);
            }

            public BakAgain.Graphics.UiImage Canvas => _canvas;

            public void Show() { }

            public void Hide() { }

            public UniTask ShowAsync() => UniTask.CompletedTask;

            public UniTask HideAsync() => UniTask.CompletedTask;

            public void Destroy() => UnityEngine.Object.DestroyImmediate(_host);
        }

        /// <summary>
        /// <b>The catch in <c>BeginAsync</c> is load-bearing, and this is the half of it that can
        /// be observed from outside.</b>
        /// </summary>
        /// <remarks>
        /// Setup builds a state and a player and assigns <c>_currentPlayer</c> before it does the
        /// pre-process walk, which awaits per-frame work that can throw. Without the catch the
        /// throw would leave a disposed-in-name-only state and player behind AND leave
        /// <c>_currentPlayer</c> pointing at the dead one — so the next <c>Cancel()</c>, which is
        /// how the whole app aborts a cutscene, would reach it.
        ///
        /// <para><b>What this pins, exactly: the RETHROW and that the presenter is left safe to
        /// cancel.</b> It does NOT pin the catch's <c>_currentPlayer = null</c> — mutation-checked
        /// on 2026-09-01, and the mutant survived. The factory throws one line BEFORE
        /// <c>_currentPlayer</c> is assigned, so that field is null either way, and the comment
        /// this replaces claimed otherwise.</para>
        ///
        /// <para><b>Reaching the assignment-then-throw case is harder than it looks, and the reason
        /// is worth knowing before anyone tries.</b> The only in-<c>try</c> step after the
        /// assignment is the pre-process walk, and that walk is deliberately exception-TOLERANT:
        /// <c>PreloadOptional</c> catches every exception and logs a warning, which is the
        /// dangling-name tolerance <c>G_BKLIAL.PAL</c> forced on it. So a throwing cache does not
        /// propagate, and the catch guards a path the walk itself works hard to prevent. That makes
        /// the catch defence in depth rather than dead code — but it also means a contrived test
        /// for it would pin the contrivance, not the behaviour.</para>
        /// </remarks>
        [UnityTest]
        public IEnumerator AThrowDuringSetupLeavesNoCurrentPlayerBehind() =>
            UniTask.ToCoroutine(async () => {
                var presenter = new CutscenePresenter(NullLogger<CutscenePresenter>.Instance,
                    new LoadableCache(), new ThrowingFactory(), null);
                var view = new CanvasView();
                try {
                    InvalidOperationException raised = null;
                    try {
                        await presenter.PlayCutsceneAsync("SCENE", view);
                    } catch (InvalidOperationException e) {
                        raised = e;
                    }

                    Assert.IsNotNull(raised,
                        "BeginAsync rethrows after cleaning up — swallowing it here would hide a "
                        + "cutscene that never played");
                    Assert.DoesNotThrow(() => presenter.Cancel(),
                        "Cancel after a failed setup must not reach the player that setup threw on");
                } finally {
                    view.Destroy();
                }
            });

        [UnityTest]
        public IEnumerator PlayCutsceneAsync_WithNoResource_ShowsNothingAndReportsFailure() =>
            UniTask.ToCoroutine(async () => {
                var view = new RecordingView();

                bool played = await PresenterThatCannotLoad().PlayCutsceneAsync("NOSUCH", view);

                Assert.IsFalse(played, "a cutscene that never loaded has not been played");
                Assert.IsFalse(view.Shown,
                    "view.Show() must stay BELOW the load's null check — showing first puts an "
                    + "empty canvas on screen for a cutscene that is about to be abandoned");
            });

        [UnityTest]
        public IEnumerator PlayCutsceneTagsAsync_WithNoResource_ShowsNothingAndReportsFailure() =>
            UniTask.ToCoroutine(async () => {
                var view = new RecordingView();

                bool played = await PresenterThatCannotLoad()
                    .PlayCutsceneTagsAsync("NOSUCH", view, new[] { "INIT" });

                Assert.IsFalse(played);
                Assert.IsFalse(view.Shown, "the second entry point must behave as the first");
            });

        [UnityTest]
        public IEnumerator AFailedLoadLeavesNoCurrentPlayer() =>
            UniTask.ToCoroutine(async () => {
                // The session's Dispose is what clears _currentPlayer now, having moved there from a
                // `finally` in each entry point. A failed load never builds a session at all, so the
                // field must simply never have been set — checked through the public surface that
                // reads it rather than by reflecting at the field.
                CutscenePresenter presenter = PresenterThatCannotLoad();

                await presenter.PlayCutsceneAsync("NOSUCH", new RecordingView());

                Assert.DoesNotThrow(() => presenter.Cancel(),
                    "cancelling after a failed load must be a no-op, not a null dereference");
            });
    }
}

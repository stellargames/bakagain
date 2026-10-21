namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.CutScenes;
    using BakAgain.UI.InGame;
    using BakAgain.UI.Navigation;
    using BakAgain.World.Scenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Palette;
    using GameData.Resources.Scene;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using VContainer;

    /// <summary>
    /// A location's description arrives with its picture, under the fade, never over the world
    /// (TASK-904).
    /// </summary>
    /// <remarks>
    /// The original paints the description bubble while the palette is still black and fades the
    /// picture and the text in together (TOWNSCN.C:378-404). The port started the entry animation
    /// before fading out, and the animation's hold raised the description at once — so entering
    /// LaMut printed "In many ways the town looked like..." over the travel view, with the HUD still
    /// up, for the whole fade-out. Drives the real <see cref="LocationScenePlayer.ShowAsync"/> and a
    /// real <see cref="CutscenePresenter"/>; only the data, the fade and the frame processor are
    /// stand-ins.
    /// </remarks>
    public class TownEntryFadeOrderTests {
        private const int DescriptionId = 4242;

        private GameObject _hudGo;
        private GameObject _screenGo;
        private GameObject _canvasGo;

        [TearDown]
        public void TearDown() {
            foreach (GameObject go in new[] { _hudGo, _screenGo, _canvasGo }) {
                if (go != null) { Object.DestroyImmediate(go); }
            }
        }

        [UnityTest]
        public IEnumerator TheDescriptionWaitsForTheFadeOutAndTheHudAndPrecedesTheFadeIn() =>
            UniTask.ToCoroutine(async () => {
                // A travel HUD that is on screen: the stray state TravelHudGoesDown... builds, which
                // HideAsync takes down synchronously, so "down" is observable without waiting.
                _hudGo = new GameObject("TravelHud");
                _hudGo.SetActive(false);
                InGameScreen hud = _hudGo.AddComponent<InGameScreen>();
                _hudGo.SetActive(true);
                _hudGo.SetActive(true);
                Assume.That(_hudGo.activeSelf, Is.True, "the HUD starts on screen");

                var builder = new VContainer.ContainerBuilder();
                builder.RegisterInstance(hud);
                VContainer.IObjectResolver resolver = builder.Build();

                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                _screenGo = new GameObject("Location");
                LocationScreen screen = _screenGo.AddComponent<LocationScreen>();
                var session = new BakAgain.Core.GameSession();
                screen.Construct(dialogs, null, session, null, null, null, null, null, null, null);

                var cache = new TownCache();
                var presenter = new CutscenePresenter(NullLogger<CutscenePresenter>.Instance, cache,
                    new HoldingPlayerFactory(), null, null);
                var player = new LocationScenePlayer(NullLogger<LocationScenePlayer>.Instance, presenter,
                    new CanvasView(_canvasGo = new GameObject("LocationCanvas")),
                    new GdsSceneLoader(cache, session), cache, screen, null, resolver, fade);

                try {
                    // Arriving, then walking into a second sub-scene: the second show awaits the
                    // first one's held animation, which is where a stray continuation on it throws.
                    foreach (int sub in new[] { 1, 2 }) {
                        fade.Events.Clear();
                        dialogs.Moment = null;

                        GdsScene shown = await player.ShowAsync(1, sub);

                        Assert.That(shown, Is.Not.Null, $"sub-scene {sub} must show for the order to mean anything");
                        Assert.That(dialogs.Moment, Is.Not.Null, $"sub-scene {sub}: the description was never raised");
                        Assert.That(dialogs.Moment, Is.EqualTo("covered, hud down, before fade-in"),
                            $"sub-scene {sub}: the description must be painted under the fade, after the "
                            + "travel HUD is down and before the fade-in starts, as TOWNSCN.C:392-404 does");
                        Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }));
                    }
                } finally {
                    player.Hide();
                }
            });

        /// <summary>A fade that takes a few frames to cover, as the real one does.</summary>
        private sealed class RecordingFade : IScreenFade {
            public readonly List<string> Events = new();
            public bool Covered;

            public async UniTask FadeOutAsync() {
                Events.Add("out");
                await UniTask.Yield();
                await UniTask.Yield();
                Covered = true;
                Events.Add("covered");
            }

            public UniTask FadeInAsync() {
                Covered = false;
                Events.Add("in");
                return UniTask.CompletedTask;
            }
        }

        /// <summary>Records the screen's state at the moment the description is asked for.</summary>
        private sealed class MomentRecordingDialogs : EndChapterHotspotTests.ClearCountingDialogs {
            private readonly RecordingFade _fade;
            private readonly GameObject _hud;

            public MomentRecordingDialogs(RecordingFade fade, GameObject hud) {
                _fade = fade;
                _hud = hud;
            }

            public string Moment { get; set; }

            public override UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
                CancellationToken cancellationToken = default) {
                if (id == DescriptionId) {
                    Moment = (_fade.Covered ? "covered" : "NOT covered")
                        + (_hud.activeSelf ? ", HUD UP" : ", hud down")
                        + (_fade.Events.Contains("in") ? ", AFTER fade-in" : ", before fade-in");
                }
                return UniTask.FromResult<GameData.Resources.Dialog.DialogPlay>(null);
            }
        }

        /// <summary>
        /// One scene with an entry and an idle script, both empty, so the presenter runs straight
        /// to its hold. No REQ layout, so no hotspots are built — the order is what is under test.
        /// </summary>
        private sealed class TownCache : IResourceCache {
            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
                object result = null;
                if (typeof(T) == typeof(GdsScene)) {
                    result = new GdsScene(key) {
                        AnimationResource = "G_TOWN", EntryAnimationTag = 1, IdleAnimationTag = 2,
                        SceneDialogId = DescriptionId,
                    };
                } else if (typeof(T) == typeof(AnimatorResource)) {
                    result = new AnimatorResource("G_TOWN.ADS") {
                        ResourceFiles = new Dictionary<int, string> { [0] = "G_TOWN.TTM" },
                        Animations = new List<AnimatorScript> {
                            new() { Id = 1, Tag = "ENTRY", Script = "" },
                            new() { Id = 2, Tag = "IDLE", Script = "" },
                        },
                    };
                } else if (typeof(T) == typeof(AnimationResource)) {
                    result = new AnimationResource("TTM", "V1.0", new Dictionary<int, string>(), new List<Frame>());
                } else if (typeof(T) == typeof(PaletteResource)) {
                    result = new PaletteResource("options.pal");
                }
                return UniTask.FromResult((T)result);
            }

            public void Clear() { }
        }

        private sealed class HoldingPlayerFactory : ICutscenePlayerFactory {
            public CutscenePlayer Create(CutsceneState cutsceneState) =>
                new(cutsceneState, null, null, new HoldingProcessor(), null);
        }

        /// <summary>Holds until the location's token is cancelled; nothing else is reached.</summary>
        private sealed class HoldingProcessor : ICutsceneFrameProcessor {
            public UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
                CancellationToken cancellationToken, System.Func<UniTask> showDialogs = null) => UniTask.CompletedTask;
            public UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
                CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands, CutsceneState state) =>
                UniTask.CompletedTask;
            public void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands, CutsceneState state) { }
            public UniTask HoldPaletteCyclesAsync(CutsceneState state, CancellationToken cancellationToken) =>
                UniTask.WaitUntilCanceled(cancellationToken);
        }

        /// <summary>A view with a real canvas, so <c>new CutsceneState</c> gets past construction.</summary>
        private sealed class CanvasView : ICutsceneView {
            private readonly BakAgain.Graphics.UiImage _canvas;

            public CanvasView(GameObject host) {
                RawImage raw = host.AddComponent<RawImage>();
                raw.texture = new Texture2D(8, 8);
                _canvas = new BakAgain.Graphics.UiImage(raw);
            }

            public BakAgain.Graphics.UiImage Canvas => _canvas;
            public void Show() { }
            public void Hide() { }
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => UniTask.CompletedTask;
        }
    }
}

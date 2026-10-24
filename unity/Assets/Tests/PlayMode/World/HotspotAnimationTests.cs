namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.CutScenes;
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
    /// Talking to a keeper redraws the scene without him, and the scene comes back afterwards
    /// (TASK-981).
    /// </summary>
    /// <remarks>
    /// TOWNSCN.C:455-463 plays the hotspot's animation channel before its dialog; the loop tail
    /// replays the idle one (:630). In the Blue Wheel (GDS1B, TVRN2.ADS) Sumani's hotspot carries
    /// channel 7, "BW-SUMANI GONE": the inn drawn with nobody behind the counter. The port played
    /// nothing, so Sumani stood in the background while he talked. Drives the real
    /// <see cref="LocationScenePlayer"/>, <see cref="LocationScreen"/> and
    /// <see cref="CutscenePresenter"/>; the frame processor records which scene each frame is.
    /// </remarks>
    public class HotspotAnimationTests {
        private const int Entry = 1, Idle = 4, SumaniGone = 7, TalkDialog = 99;

        // One frame per script; the presenter hands each frame's own list to the processor, so the
        // list's identity names the picture being drawn.
        private static readonly List<FrameCommand> EntryFrame = new(), IdleFrame = new(), GoneFrame = new();

        private GameObject _screenGo, _canvasGo;

        [TearDown]
        public void TearDown() {
            if (_screenGo != null) { Object.DestroyImmediate(_screenGo); }
            if (_canvasGo != null) { Object.DestroyImmediate(_canvasGo); }
        }

        [UnityTest]
        public IEnumerator TalkingToTheKeeperDrawsTheSceneWithoutHimThenPutsHimBack() =>
            UniTask.ToCoroutine(async () => {
                var processor = new RecordingProcessor();
                var dialogs = new TalkDialogs(processor);
                (LocationScenePlayer player, LocationScreen screen) = Rig(processor, dialogs,
                    new GdsHotspot { ActionCode = 2, ActionDialogId = TalkDialog, AnimationSceneId = SumaniGone });
                try {
                    await Act(player, screen, processor, dialogs);

                    Assert.That(dialogs.PictureDuringTalk, Is.EqualTo(SumaniGone),
                        "the dialog plays over the hotspot's own animation (TOWNSCN.C:462)");
                    Assert.That(processor.Played[^1], Is.EqualTo(Idle),
                        "and the idle picture is back afterwards (TOWNSCN.C:630)");
                } finally {
                    player.Hide();
                }
            });

        [UnityTest]
        public IEnumerator AHotspotWithNoAnimationLeavesTheSceneAsItIs() =>
            UniTask.ToCoroutine(async () => {
                var processor = new RecordingProcessor();
                var dialogs = new TalkDialogs(processor);
                (LocationScenePlayer player, LocationScreen screen) = Rig(processor, dialogs,
                    new GdsHotspot { ActionCode = 2, ActionDialogId = TalkDialog });
                try {
                    int before = await Act(player, screen, processor, dialogs);

                    Assert.That(dialogs.PictureDuringTalk, Is.EqualTo(Idle));
                    Assert.That(processor.Played.Count, Is.EqualTo(before), "nothing was replayed");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>Enters the scene, clicks hotspot 0 and waits for the redraw after it.</summary>
        private static async UniTask<int> Act(LocationScenePlayer player, LocationScreen screen,
            RecordingProcessor processor, TalkDialogs dialogs) {
            player.RunAsync(1, 2).Forget();
            await Until(() => processor.Played.Contains(Idle), "the scene never drew");
            for (int i = 0; i < 10; i++) { await UniTask.Yield(); }
            int before = processor.Played.Count;

            screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 2)) {
                AnimationResource = "TVRN2", EntryAnimationTag = Entry, IdleAnimationTag = Idle,
                Hotspots = new[] { dialogs.Hotspot },
            }, 1, 2);
            screen.PrimaryAction(GdsSceneRules.ActionIdFor(0));
            await Until(() => dialogs.PictureDuringTalk != 0, "the dialog never played");
            for (int i = 0; i < 30; i++) { await UniTask.Yield(); }
            return before;
        }

        private (LocationScenePlayer, LocationScreen) Rig(RecordingProcessor processor, TalkDialogs dialogs,
            GdsHotspot hotspot) {
            dialogs.Hotspot = hotspot;
            _screenGo = new GameObject("Location");
            LocationScreen screen = _screenGo.AddComponent<LocationScreen>();
            var session = new BakAgain.Core.GameSession();
            var cache = new TavernCache(hotspot);
            var presenter = new CutscenePresenter(NullLogger<CutscenePresenter>.Instance, cache,
                new Factory(processor), null, null);
            var fade = new BakAgain.UI.Navigation.NullScreenFade();
            var player = new LocationScenePlayer(NullLogger<LocationScenePlayer>.Instance, presenter,
                new CanvasView(_canvasGo = new GameObject("LocationCanvas")),
                new GdsSceneLoader(cache, session), cache, screen, null, null, fade);
            // The screen finds the player through its container, as it does in the game.
            var builder = new VContainer.ContainerBuilder();
            builder.RegisterInstance(player);
            VContainer.IObjectResolver resolver = builder.Build();
            screen.Construct(dialogs, null, session, null, null, null, null, cache, null, null, resolver);
            return (player, screen);
        }

        private static async UniTask Until(System.Func<bool> condition, string failure) {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!condition()) {
                if (Time.realtimeSinceStartup > deadline) {
                    Assert.Fail(failure);
                }
                await UniTask.Yield();
            }
        }

        /// <summary>Notes which picture is up when the hotspot's dialog plays.</summary>
        private sealed class TalkDialogs : EndChapterHotspotTests.ClearCountingDialogs, BakAgain.UI.IDialogManager {
            private readonly RecordingProcessor _processor;
            public GdsHotspot Hotspot;
            public int PictureDuringTalk;

            public TalkDialogs(RecordingProcessor processor) => _processor = processor;

            public new UniTask<int> ShowById(int id, CancellationToken cancellationToken = default) {
                if (id == TalkDialog) {
                    PictureDuringTalk = _processor.Played[^1];
                }
                return UniTask.FromResult(-1);
            }

            public UniTask<int> ShowByIdLeavingLastPage(int id, CancellationToken cancellationToken = default) =>
                ShowById(id, cancellationToken);
        }

        /// <summary>TVRN2's entry, idle and "BW-SUMANI GONE" scripts, one tagged frame each.</summary>
        private sealed class TavernCache : IResourceCache {
            private readonly GdsHotspot _hotspot;
            public TavernCache(GdsHotspot hotspot) => _hotspot = hotspot;

            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
                object result = null;
                if (typeof(T) == typeof(GdsScene)) {
                    result = new GdsScene(key) {
                        AnimationResource = "TVRN2", EntryAnimationTag = Entry, IdleAnimationTag = Idle,
                        Hotspots = new[] { _hotspot },
                    };
                } else if (typeof(T) == typeof(AnimatorResource)) {
                    result = new AnimatorResource("TVRN2.ADS") {
                        ResourceFiles = new Dictionary<int, string> { [0] = "TVRN2.TTM" },
                        Animations = new List<AnimatorScript> {
                            new() { Id = Entry, Tag = "INIT TAVERN2", Script = Start(Entry) },
                            new() { Id = Idle, Tag = "BLUE WHEEL", Script = Start(Idle) },
                            new() { Id = SumaniGone, Tag = "BW-SUMANI GONE", Script = Start(SumaniGone) },
                        },
                    };
                } else if (typeof(T) == typeof(AnimationResource)) {
                    result = new AnimationResource("TTM", "V1.0", new Dictionary<int, string>(), new List<Frame> {
                        new() { Tag = Entry, Commands = EntryFrame },
                        new() { Tag = Idle, Commands = IdleFrame },
                        new() { Tag = SumaniGone, Commands = GoneFrame },
                    });
                } else if (typeof(T) == typeof(PaletteResource)) {
                    result = new PaletteResource("options.pal");
                }
                return UniTask.FromResult((T)result);
            }

            private static string Start(int scene) =>
                $"IF NOT PLAYED scene_{scene}\n    START scene_{scene}\nEND IF\n";

            public void Clear() { }
        }

        private sealed class Factory : ICutscenePlayerFactory {
            private readonly RecordingProcessor _processor;
            public Factory(RecordingProcessor processor) => _processor = processor;
            public CutscenePlayer Create(CutsceneState cutsceneState) =>
                new(cutsceneState, new BakAgain.UI.InputCore.InputLayerStack(), null, _processor, null);
        }

        /// <summary>Records each frame's scene and ends the scene there; holds until cancelled.</summary>
        private sealed class RecordingProcessor : ICutsceneFrameProcessor {
            public readonly List<int> Played = new();

            public UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
                CancellationToken cancellationToken, System.Func<UniTask> showDialogs = null) {
                Played.Add(ReferenceEquals(commands, EntryFrame) ? Entry
                    : ReferenceEquals(commands, IdleFrame) ? Idle
                    : ReferenceEquals(commands, GoneFrame) ? SumaniGone : -1);
                state.EndScene = true;
                return UniTask.CompletedTask;
            }

            public UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
                CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands, CutsceneState state) =>
                UniTask.CompletedTask;
            public void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands, CutsceneState state) { }
            public UniTask HoldPaletteCyclesAsync(CutsceneState state, CancellationToken cancellationToken) =>
                UniTask.WaitUntilCanceled(cancellationToken);
        }

        private sealed class CanvasView : ICutsceneView {
            public CanvasView(GameObject host) {
                RawImage raw = host.AddComponent<RawImage>();
                raw.texture = new Texture2D(8, 8);
                Canvas = new BakAgain.Graphics.UiImage(raw);
            }

            public BakAgain.Graphics.UiImage Canvas { get; }
            public void Show() { }
            public void Hide() { }
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => UniTask.CompletedTask;
        }
    }
}

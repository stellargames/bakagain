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
    using System.Reflection;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using VContainer;

    /// <summary>
    /// A location's description arrives with its picture, under the fade, never over the world
    /// (TASK-904); a sub-scene cuts instead of fading, and leaving with a teleport queued charges the
    /// step first and moves the party under the one fade (TASK-906 items 3 and 4).
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
        public IEnumerator ArrivingFadesASubSceneCutsAndLeavingFades() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud), teleport: null);

                try {
                    UniTask run = player.RunAsync(1, 1);

                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    Assert.That(dialogs.Moments, Is.EqualTo(new[] { "covered, hud down, before fade-in" }),
                        "arriving: the description is painted under the fade, after the travel HUD is down "
                        + "and before the fade-in starts, as TOWNSCN.C:378-404 does");
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }));

                    // Into a second sub-scene. This show awaits the first one's held animation, which
                    // is where a stray continuation on it throws (TASK-904).
                    fade.Events.Clear();
                    Choose(player, 2);
                    await Until(() => dialogs.Moments.Count == 2 && fade.Events.Count >= 2,
                        "sub-scene 2 never showed");
                    await UniTask.Yield();
                    Assert.That(fade.Events, Is.EqualTo(new[] { "freeze", "thaw" }),
                        "a sub-scene CUTS: fadeFlag is 1 only for the first scene (TOWNSCN.C:325, :376, "
                        + ":398), and the owner chose the cut for the remake on 2026-10-21");
                    Assert.That(dialogs.Moments[1], Is.EqualTo("frozen, hud down"),
                        "the new description is painted while the old frame is still held, so it "
                        + "appears with its picture at the cut (TOWNSCN.C:392 then :409)");

                    // Leaving still fades: TOWNSCN.C:680 raises g_nSceneReloadPending and the world
                    // loop fades out, repaints and fades in (WORLDLP.C:102-139).
                    fade.Events.Clear();
                    Choose(player, 0);
                    await run;
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }));
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// Leaving a town with a teleport queued (TASK-906 item 3): the step is charged first, then
        /// the party moves under the one fade that takes the town down.
        /// </summary>
        /// <remarks>
        /// The town returns into the world loop's dispatch pass, which charges the step there and
        /// then (WORLDLP.C:191-198). Only the NEXT pass sees the queued record and moves the party
        /// (:94-95, modalscreen_pending_scene_trans), and its answer raises the one fade-out /
        /// repaint / fade-in (:102-139), so the destination is what fades in. The port faded the
        /// old position in, then moved, and charged the step after the move.
        /// </remarks>
        [UnityTest]
        public IEnumerator LeavingWithAQueuedTeleportChargesTheStepFirstAndMovesUnderTheSameFade() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var flow = new RecordingFlow();
                var fade = new RecordingFade();
                var teleport = new GameData.Resources.Location.PendingTeleport();
                // The location keeps its own session (its loader reads only the chapter); the step,
                // the clock and the gate are the rig's.
                LocationScenePlayer player = Player(fade, new MomentRecordingDialogs(fade, _hudGo),
                    Resolver(hud, flow), teleport);
                TownGateStepTests.Rig rig = TownGateStepTests.Build(player, () => flow);
                flow.Rig = rig;
                flow.Fade = fade;

                try {
                    long before = rig.Clock.Ticks;
                    rig.Dialogs.Answer.TrySetResult(0);   // Yes, as soon as it is asked
                    rig.Movement.MoveForward();
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");

                    // TELEPORT.DAT row 19, the Krondor sewer door's (GDS2E hotspot 0, dialog 2300001).
                    fade.Events.Clear();
                    teleport.Queue(new GameData.Resources.Location.TeleportDestination {
                        Location = new GameData.Resources.Location.Location {
                            ZoneNumber = 11, X = 10, Y = 10, XOffset = 38, YOffset = 22,
                        },
                        Id = 19,
                    });
                    Choose(player, 0);
                    await Until(() => flow.At != null && fade.Events.Contains("in"), "the party never moved");
                    await UniTask.Yield();
                    await UniTask.Yield();

                    Assert.That(flow.At.Value.Ticks, Is.EqualTo(before + TownGateStepTests.TicksPerLargeStep),
                        "the step is charged before the teleport moves the party (WORLDLP.C:191-198, then :94-95)");
                    Assert.That(flow.At.Value.Covered, Is.True,
                        "the party moves while the town's fade-out still covers the screen");
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }),
                        "one fade: out of the town and in on the destination (WORLDLP.C:102-139)");
                    Assert.That(rig.Clock.Ticks, Is.EqualTo(before + TownGateStepTests.TicksPerLargeStep),
                        "and the step is charged once");
                } finally {
                    player.Hide();
                }
            });

        // A travel HUD that is on screen, which HideAsync takes down synchronously, so "down" is
        // observable without waiting.
        private InGameScreen TravelHud() {
            _hudGo = new GameObject("TravelHud");
            _hudGo.SetActive(false);
            InGameScreen hud = _hudGo.AddComponent<InGameScreen>();
            // Twice, as the original test did: the first activation runs the screen's own OnEnable,
            // which takes it down again.
            _hudGo.SetActive(true);
            _hudGo.SetActive(true);
            Assume.That(_hudGo.activeSelf, Is.True, "the HUD starts on screen");
            return hud;
        }

        private static VContainer.IObjectResolver Resolver(InGameScreen hud,
            BakAgain.Core.Services.IGameFlow flow = null) {
            var builder = new VContainer.ContainerBuilder();
            builder.RegisterInstance(hud);
            if (flow != null) {
                builder.RegisterInstance(flow).As<BakAgain.Core.Services.IGameFlow>();
            }
            return builder.Build();
        }

        private LocationScenePlayer Player(RecordingFade fade, BakAgain.UI.IDialogManager dialogs,
            VContainer.IObjectResolver resolver, GameData.Resources.Location.PendingTeleport teleport) {
            _screenGo = new GameObject("Location");
            LocationScreen screen = _screenGo.AddComponent<LocationScreen>();
            var session = new BakAgain.Core.GameSession();
            screen.Construct(dialogs, null, session, null, null, null, null, null, null, null);

            var cache = new TownCache();
            var presenter = new CutscenePresenter(NullLogger<CutscenePresenter>.Instance, cache,
                new HoldingPlayerFactory(), null, null);
            return new LocationScenePlayer(NullLogger<LocationScenePlayer>.Instance, presenter,
                new CanvasView(_canvasGo = new GameObject("LocationCanvas")),
                new GdsSceneLoader(cache, session), cache, screen, teleport, resolver, fade);
        }

        /// <summary>What a hotspot's transition hands the loop (LocationScreen's bound callback).</summary>
        private static void Choose(LocationScenePlayer player, int letter) =>
            typeof(LocationScenePlayer)
                .GetField("_pendingTransition", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player, (int?)letter);

        private static async UniTask Until(System.Func<bool> condition, string failure) {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!condition()) {
                if (Time.realtimeSinceStartup > deadline) {
                    Assert.Fail(failure);
                }
                await UniTask.Yield();
            }
        }

        /// <summary>Records the clock and the cover at the moment the party is moved.</summary>
        private sealed class RecordingFlow : BakAgain.Core.Services.IGameFlow {
            public TownGateStepTests.Rig Rig;
            public RecordingFade Fade;
            public (long Ticks, bool Covered)? At;

            public UniTask<GameData.Resources.Location.ZoneTransitionKind> TransitionTo(
                GameData.Resources.Location.Location destination) {
                At = (Rig.Clock.Ticks, Fade.Covered);
                return UniTask.FromResult(GameData.Resources.Location.ZoneTransitionKind.ChangeZone);
            }

            public UniTask<bool> LoadSave(string path) => UniTask.FromResult(false);
            public UniTask Boot() => UniTask.CompletedTask;
            public UniTask ShowMainMenu() => UniTask.CompletedTask;
            public UniTask StartNewGame(bool playChapterScenes = true) => UniTask.CompletedTask;
            public UniTask GoToChapter(int chapter) => UniTask.CompletedTask;
            public UniTask EnterWorld() => UniTask.CompletedTask;
        }

        /// <summary>A fade that takes a few frames to cover, as the real one does.</summary>
        private sealed class RecordingFade : IScreenFade {
            public readonly List<string> Events = new();
            public bool Covered;
            public bool Frozen;

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

            public async UniTask CutAsync(System.Func<UniTask> change) {
                Events.Add("freeze");
                Frozen = true;
                try {
                    await change();
                } finally {
                    Frozen = false;
                    Events.Add("thaw");
                }
            }
        }

        /// <summary>Records the screen's state each time the description is asked for.</summary>
        private sealed class MomentRecordingDialogs : EndChapterHotspotTests.ClearCountingDialogs {
            private readonly RecordingFade _fade;
            private readonly GameObject _hud;

            public MomentRecordingDialogs(RecordingFade fade, GameObject hud) {
                _fade = fade;
                _hud = hud;
            }

            public readonly List<string> Moments = new();

            public override UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
                CancellationToken cancellationToken = default) {
                if (id == DescriptionId) {
                    string hud = _hud.activeSelf ? ", HUD UP" : ", hud down";
                    Moments.Add(_fade.Frozen
                        ? "frozen" + hud
                        : (_fade.Covered ? "covered" : "NOT covered") + hud
                            + (_fade.Events.Contains("in") ? ", AFTER fade-in" : ", before fade-in"));
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

namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.Tests.TestSupport;
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEngine.AddressableAssets;
    using UnityEngine.TestTools;

    /// <summary>
    /// Every shipped cutscene script, driven through the REAL frame loop.
    /// </summary>
    /// <remarks>
    /// <b>The loop's own remark names two failure modes it cannot see: "a cutscene that loops for
    /// ever or one that stops early", neither of which raises an exception.</b>
    /// <see cref="CutscenePlayerFrameLoopTests"/> pins that logic against hand-written frames, and
    /// <c>ShippedCutsceneScriptTests</c> (GameData) checks the shipped DATA against the loop's
    /// contract. Neither puts the two together, so nothing asserted that the 44 scripts the game
    /// actually ships terminate when the real loop runs them.
    ///
    /// <para><b>The frame processor is still a stand-in, and that is the point rather than a
    /// shortcut.</b> What is under test is the ORDER logic — jumps, tags, EndScene — over real
    /// frame data. A real processor would drag in the renderer, the palettes and the images, which
    /// is a different test with different failure modes; using one here would mean a broken image
    /// failed a test about looping.</para>
    ///
    /// <para><b>Termination is asserted by BOUNDING it, not by waiting.</b> A genuinely looping
    /// script would hang the Editor, so the stand-in cancels the player once it has seen far more
    /// frames than the longest script has, and the test asserts it never had to.</para>
    ///
    /// <para><b>Skips rather than fails when the game data is absent</b> — the ADS simply does not
    /// load, the same contract the other resource-backed tests use.</para>
    /// </remarks>
    public class ShippedCutsceneLoopTests {
        /// <summary>
        /// The shipped ADS names, from <c>generated/ADS/</c>.
        /// </summary>
        /// <remarks>
        /// Spelled out rather than discovered because Addressables has no key enumeration to ask,
        /// and the alternative — reading <c>generated/</c> — would test the extractor's output
        /// instead of the archive the game loads. A name that disappears from the archive fails
        /// loudly here, which is the behaviour worth having.
        /// </remarks>
        private static readonly string[] ShippedScenes = {
            "C11", "C12", "C21", "C22", "C31", "C32", "C41", "C42", "C51", "C52",
            "C61", "C62", "C71", "C72", "C81", "C82", "C91", "C92", "C93",
            "CHAPTER1", "CHAPTER2", "CHAPTER3", "CHAPTER4", "CHAPTER5", "CHAPTER6",
            "CHAPTER7", "CHAPTER8", "CHAPTER9",
            "GDS50", "GDS60", "GDS70", "G_MISC", "G_TOWN", "INTRO",
            "SHOP1", "SHOP2", "SHOP3", "SHOP4", "TEMPLE",
            "TVRN1", "TVRN2", "TVRN3", "TVRN4", "TVRN5",
        };

        /// <summary>
        /// Well past the longest shipped script, so reaching it means the loop is not advancing.
        /// </summary>
        private const int FrameBudget = 20000;

        /// <summary>
        /// Applies the two commands that steer the loop, and pulls the plug if it will not stop.
        /// </summary>
        /// <remarks>
        /// <b>It executes <c>GotoFrame</c> and <c>EndScene</c> and nothing else</b>, mirroring
        /// <c>GotoFrameExtensions</c> and <c>EndSceneExtensions</c> one line each. Those two are the
        /// only commands the loop's control flow reads, so honouring them is what makes this a test
        /// of the real ordering over real data rather than of a frame counter. Everything else — the
        /// drawing, the palettes, the audio — is deliberately inert.
        /// </remarks>
        private sealed class BoundedProcessor : ICutsceneFrameProcessor {
            private readonly System.Action _stop;

            public BoundedProcessor(System.Action stop) { _stop = stop; }

            public int Frames { get; private set; }
            public bool HitTheBudget { get; private set; }

            public UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands,
                CutsceneState state, CancellationToken cancellationToken) {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (FrameCommand command in commands) {
                    switch (command) {
                        case GotoFrame goto_:
                            state.GotoTag = goto_.NextFrame;
                            break;
                        case EndScene:
                            state.EndScene = true;
                            break;
                    }
                }
                if (++Frames < FrameBudget) {
                    return UniTask.CompletedTask;
                }
                HitTheBudget = true;
                _stop();
                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }

            public UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands,
                CutsceneState state, CancellationToken cancellationToken) => UniTask.CompletedTask;

            public UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands,
                CutsceneState state) => UniTask.CompletedTask;

            public void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands,
                CutsceneState state) { }

            public UniTask HoldPaletteCyclesAsync(CutsceneState state,
                CancellationToken cancellationToken) => UniTask.CompletedTask;
        }

        /// <summary>The loop touches only these; a real one needs shaders and a texture.</summary>
        private static CutsceneState BareState() {
            var state = (CutsceneState)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(CutsceneState));
            state.RequestedAudio = new Queue<GameData.Resources.Audio.AudioResource>();
            state.RequestedDialogs = new Queue<CutsceneDialogRequest>();
            return state;
        }

        /// <summary>
        /// The ADS names the TTM; the TTM carries the frames — the same two steps
        /// <c>CutscenePresenter.LoadCutsceneResource</c> takes.
        /// </summary>
        /// <remarks>
        /// <c>WaitForCompletion</c> rather than an await, matching the production
        /// <c>AddressablesBlockingCache</c>. Returns null on any failure so an absent archive skips.
        /// </remarks>
        private static List<Frame> LoadFrames(string scene) {
            try {
                var ads = Addressables.LoadAssetAsync<AnimatorResource>($"{scene}.ADS")
                    .WaitForCompletion();
                if (ads == null || ads.ResourceFiles.Count == 0) {
                    return null;
                }

                var animation = Addressables.LoadAssetAsync<AnimationResource>(
                    System.Linq.Enumerable.First(ads.ResourceFiles.Values)).WaitForCompletion();
                return animation?.Frames;
            }
            catch (System.Exception) {
                return null;   // no game data — skip, do not fail
            }
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EveryShippedSceneRunsToItsEnd() => UniTask.ToCoroutine(async () => {
            var ran = 0;
            var looped = new List<string>();
            var empty = new List<string>();

            foreach (string scene in ShippedScenes) {
                List<Frame> frames = LoadFrames(scene);
                if (frames == null) {
                    continue;   // absent game data
                }
                if (frames.Count == 0) {
                    empty.Add(scene);
                    continue;
                }

                CutscenePlayer player = null;
                var processor = new BoundedProcessor(() => player.Cancel());
                player = new CutscenePlayer(BareState(),
                    new BakAgain.UI.InputCore.InputLayerStack(),
                    midiPlayer: null, processor, dialogManager: null);

                await player.PlayCutScene(frames);
                ran++;

                if (processor.HitTheBudget) {
                    looped.Add($"{scene} (past {FrameBudget} frames over {frames.Count})");
                }
            }

            if (ran == 0) {
                return;   // game data absent throughout — skip rather than assert vacuously
            }

            CollectionAssert.IsEmpty(looped, "these scripts never reach an end");
            CollectionAssert.IsEmpty(empty, "these scripts have no frames to run");
            Assert.Greater(ran, 40, "far fewer scenes loaded than ship — the list or the archive moved");
        });

        [UnityTest]
        public IEnumerator ASceneThatJumpsToItselfWouldBeCaught() => UniTask.ToCoroutine(async () => {
            // *** THE GUARD NEEDS ITS OWN GUARD. *** The check above passes both when every script
            // terminates and when the budget is too generous to ever trip, and those look identical
            // from the outside. A script that jumps to its own tag for ever proves the budget is
            // reachable and that hitting it is reported rather than swallowed.
            var frames = new List<Frame> {
                new Frame {
                    Tag = 7,
                    Commands = new List<FrameCommand> { new GotoFrame { NextFrame = 7 } },
                },
            };

            CutscenePlayer player = null;
            var processor = new BoundedProcessor(() => player.Cancel());
            player = new CutscenePlayer(BareState(), new BakAgain.UI.InputCore.InputLayerStack(),
                midiPlayer: null, processor, dialogManager: null);

            await player.PlayCutScene(frames);

            Assert.IsTrue(processor.HitTheBudget, "an endless jump ran off the end of the budget");
            Assert.AreEqual(FrameBudget, processor.Frames);
        });
    }
}

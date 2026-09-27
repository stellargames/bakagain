namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// The frame loop — <c>CutscenePlayer.PlayCutScene</c>: which frame runs next, when the scene
    /// stops, and what a jump does.
    /// </summary>
    /// <remarks>
    /// <b>The failure modes here are a cutscene that loops for ever or one that stops early</b>, and
    /// neither shows up as an exception. The loop already carries two comments about bugs it has
    /// had — a jump that stepped past its own target, and a missing tag that was silently ignored —
    /// and nothing asserted either.
    ///
    /// <para>The frame processor is a stand-in that records the frames it was handed and can write
    /// to the state the way a real command would. That is the whole seam: the loop's job is to
    /// decide the ORDER, and the order is exactly what the recording shows.</para>
    /// </remarks>
    public class CutscenePlayerFrameLoopTests {
        /// <summary>Records the frames it processed, and can act on the state per frame.</summary>
        private sealed class ScriptedProcessor : ICutsceneFrameProcessor {
            public readonly List<int> Processed = new();

            /// <summary>Keyed by how many frames have run so far, so a test can fire once.</summary>
            public readonly Dictionary<int, System.Action<CutsceneState>> OnFrame = new();

            public UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands,
                CutsceneState state, CancellationToken cancellationToken) {
                cancellationToken.ThrowIfCancellationRequested();
                // The marker command carries the frame's identity; the loop never looks at it.
                foreach (FrameCommand c in commands) {
                    if (c is SetFramesDuration marker) {
                        Processed.Add(marker.Amount);
                    }
                }
                if (OnFrame.TryGetValue(Processed.Count - 1, out System.Action<CutsceneState> act)) {
                    act(state);
                }
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

        private ScriptedProcessor _processor;
        private CutsceneState _state;
        private CutscenePlayer _player;

        /// <summary>A frame that announces itself as <paramref name="id"/>, optionally tagged.</summary>
        private static Frame Frame(int id, int? tag = null) => new Frame {
            Tag = tag,
            Commands = new List<FrameCommand> { new SetFramesDuration { Amount = id } },
        };

        [SetUp]
        public void SetUp() {
            _processor = new ScriptedProcessor();
            // Deliberately not a real CutsceneState: the loop touches only the queues and the two
            // flags, and a real one needs shaders and a texture.
            _state = (CutsceneState)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(CutsceneState));
            _state.RequestedAudio = new Queue<GameData.Resources.Audio.AudioResource>();
            _state.RequestedDialogs = new Queue<CutsceneDialogRequest>();
            _player = new CutscenePlayer(_state, new BakAgain.UI.InputCore.InputLayerStack(),
                midiPlayer: null, _processor, dialogManager: null);
        }

        private bool Play(List<Frame> frames, int startFrame = 0) =>
            _player.PlayCutScene(frames, startFrame).GetAwaiter().GetResult();

        [Test]
        public void FramesRunInOrder() {
            Assert.IsTrue(Play(new List<Frame> { Frame(1), Frame(2), Frame(3) }));
            Assert.AreEqual(new[] { 1, 2, 3 }, _processor.Processed);
        }

        [Test]
        public void StartFrameSkipsWhatCameBeforeIt() {
            Play(new List<Frame> { Frame(1), Frame(2), Frame(3) }, startFrame: 1);
            Assert.AreEqual(new[] { 2, 3 }, _processor.Processed);
        }

        [Test]
        public void AJumpEXECUTESTheFrameItLandsOn() {
            // *** The bug the loop carries a comment about. *** A tagged frame is an ordinary frame
            // that happens to be labelled, so `index = target` would step past it and drop
            // everything it was going to do — a scene missing a step rather than a jump visibly
            // going wrong. Frame 3 must appear in the trace.
            var frames = new List<Frame> { Frame(1), Frame(2), Frame(3, tag: 7), Frame(4) };
            _processor.OnFrame[0] = s => s.GotoTag = 7;   // during frame 1

            Play(frames);

            Assert.AreEqual(new[] { 1, 3, 4 }, _processor.Processed);
        }

        [Test]
        public void AJumpBACKWARDSReRunsTheTargetFrame() {
            // The same rule seen from the direction that makes a loop: without the -1 a backward
            // jump would resume AFTER its own target and the loop would be one frame short.
            var frames = new List<Frame> { Frame(1, tag: 3), Frame(2) };
            _processor.OnFrame[1] = s => s.GotoTag = 3;      // during frame 2, jump back
            _processor.OnFrame[2] = s => s.EndScene = true;  // and stop on the re-run

            Play(frames);

            Assert.AreEqual(new[] { 1, 2, 1 }, _processor.Processed);
        }

        [Test]
        public void ATagNoFrameCarriesIsIgnoredAndTheSceneCarriesOn() {
            // Was silent, which looks exactly like the jump was never asked for; it warns now, and
            // either way must not derail the sequence.
            var frames = new List<Frame> { Frame(1), Frame(2) };
            _processor.OnFrame[0] = s => s.GotoTag = 99;

            Assert.IsTrue(Play(frames));
            Assert.AreEqual(new[] { 1, 2 }, _processor.Processed);
        }

        [Test]
        public void TheJumpIsCLEAREDAfterItIsTaken_orTheSceneWouldNeverAdvance() {
            // GotoTag is sticky state, not an event. Leaving it set re-fires the same jump at the
            // end of every following frame: a cutscene that plays two frames for ever.
            var frames = new List<Frame> { Frame(1), Frame(2, tag: 5), Frame(3) };
            _processor.OnFrame[0] = s => s.GotoTag = 5;

            Play(frames);

            Assert.AreEqual(new[] { 1, 2, 3 }, _processor.Processed);
            Assert.AreEqual(0, _state.GotoTag);
        }

        [Test]
        public void EndSceneStopsAfterTheFrameThatAskedForIt() {
            var frames = new List<Frame> { Frame(1), Frame(2), Frame(3) };
            _processor.OnFrame[1] = s => s.EndScene = true;

            Assert.IsTrue(Play(frames), "an ended scene still reports success");
            Assert.AreEqual(new[] { 1, 2 }, _processor.Processed);
        }

        [Test]
        public void EndSceneIsRESETSoTheNextSceneDoesNotStopOnItsFirstFrame() {
            // The flag lives on the state, which outlives one scene. A leftover EndScene would end
            // the NEXT cutscene after a single frame — and it would look like that scene was broken.
            var frames = new List<Frame> { Frame(1), Frame(2) };
            _processor.OnFrame[0] = s => s.EndScene = true;
            Play(frames);
            Assert.IsFalse(_state.EndScene);

            _processor.Processed.Clear();
            _processor.OnFrame.Clear();
            Play(frames);

            Assert.AreEqual(new[] { 1, 2 }, _processor.Processed);
        }

        [Test]
        public void CancellingReportsFAILUREWhereEndingReportsSuccess() {
            // The two exits are not interchangeable: the caller uses the answer to decide whether to
            // carry on with whatever the cutscene was introducing.
            var frames = new List<Frame> { Frame(1), Frame(2) };
            _processor.OnFrame[0] = _ => _player.Cancel();

            Assert.IsFalse(Play(frames));
            Assert.AreEqual(new[] { 1 }, _processor.Processed);
        }

        [Test]
        public void AnEmptyScriptIsAnImmediateSuccess() {
            Assert.IsTrue(Play(new List<Frame>()));
            Assert.IsEmpty(_processor.Processed);
        }
    }
}

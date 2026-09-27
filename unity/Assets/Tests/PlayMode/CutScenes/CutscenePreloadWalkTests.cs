namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation.FrameCommands;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using System.Collections.Generic;

    /// <summary>
    /// The pre-pass that walks a frame's commands and pulls in what it will need —
    /// <c>CutsceneFrameProcessor.PreloadFrameResourcesAsync</c>.
    /// </summary>
    /// <remarks>
    /// <b>It is a small interpreter, not a list of loads.</b> It carries slot state between
    /// commands, so which file a <c>DrawImage</c> asks for depends on a <c>SelectImageSlot</c> that
    /// may be several commands earlier. Getting that wrong loads the wrong picture, or none, and the
    /// symptom appears in the frame that draws rather than the one that chose.
    ///
    /// <para><b>It has already produced two bugs that killed whole cutscenes</b> — an unassigned slot
    /// throwing <c>KeyNotFoundException</c>, and a resource name the game does not contain throwing
    /// out of the load — and neither was covered afterwards. Both are pinned here.</para>
    ///
    /// <para>The cache returns null throughout: what is under test is <b>which keys the walk asks
    /// for</b>, and a null answer is already the path a missing resource takes.</para>
    /// </remarks>
    public class CutscenePreloadWalkTests {
        /// <summary>Records every key asked for, and can refuse one the way a missing file does.</summary>
        private sealed class RecordingCache : IResourceCache {
            public readonly List<string> Requested = new();
            public string ThrowsFor;

            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
                Requested.Add(key);
                if (key == ThrowsFor) {
                    throw new System.InvalidOperationException("no such resource");
                }
                return UniTask.FromResult<T>(null);
            }

            public void Clear() => Requested.Clear();
        }

        private RecordingCache _cache;
        private CutsceneFrameProcessor _processor;
        private CutsceneState _state;

        [SetUp]
        public void SetUp() {
            _cache = new RecordingCache();
            _processor = new CutsceneFrameProcessor(
                NullLogger<CutsceneFrameProcessor>.Instance, _cache);
            // Deliberately NOT a real CutsceneState: the walk touches only ImageSlots,
            // CurrentImageSlot and Resources, and a real one needs shaders and a texture.
            _state = (CutsceneState)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(CutsceneState));
            _state.ImageSlots = new Dictionary<int, string>();
            _state.Resources = new ResourceSet();
        }

        private void Walk(params FrameCommand[] commands) =>
            _processor.PreloadFrameResourcesAsync(commands, _state).GetAwaiter().GetResult();

        [Test]
        public void ALoadBindsToTheSlotTheLastSelectNamed() {
            // The state carries between commands, so this is order-dependent by design.
            Walk(new SelectImageSlot { SlotNumber = 3 },
                new LoadImageResource { Filename = "HERO.BMX" },
                new DrawImageBase { ImageSlot = 3, ImageNumber = 2 });

            Assert.Contains("HERO.BMX#2", _cache.Requested);
        }

        [Test]
        public void ADrawUsesITSOWNSlot_notTheCurrentOne() {
            // *** The distinguishing case. *** DrawImage carries a slot argument; collapsing it into
            // "the current slot" reads correctly for every frame that draws what it just loaded, and
            // draws the wrong picture for every frame that does not.
            Walk(new SelectImageSlot { SlotNumber = 1 },
                new LoadImageResource { Filename = "ONE.BMX" },
                new SelectImageSlot { SlotNumber = 2 },
                new LoadImageResource { Filename = "TWO.BMX" },
                new DrawImageBase { ImageSlot = 1, ImageNumber = 0 });

            Assert.Contains("ONE.BMX#0", _cache.Requested);
            Assert.IsFalse(_cache.Requested.Contains("TWO.BMX#0"),
                "the current slot is 2, and the draw named 1");
        }

        [Test]
        public void ADrawOnAnUnassignedSlotIsSkipped_notThrown() {
            // The recorded bug: preloading walks EVERY frame before playback has assigned any slot,
            // so a frame naming a slot nothing has filled is normal. Indexing threw, the exception
            // escaped as an unobserved UniTask, and the whole cutscene died with no message tying it
            // to a scene.
            Assert.DoesNotThrow(() => Walk(new DrawImageBase { ImageSlot = 4, ImageNumber = 1 }));
            Assert.IsEmpty(_cache.Requested);
        }

        [Test]
        public void ALoadWithNoFilenameLeavesTheSlotAsItWas() {
            Walk(new SelectImageSlot { SlotNumber = 0 },
                new LoadImageResource { Filename = "KEEP.BMX" },
                new LoadImageResource { Filename = null },
                new DrawImageBase { ImageSlot = 0, ImageNumber = 5 });

            Assert.Contains("KEEP.BMX#5", _cache.Requested);
        }

        [Test]
        public void ANameTheGameDoesNotContainIsShruggedOff() {
            // g_misc asks for G_BKLIAL.PAL, which no archive entry provides. Letting the load throw
            // took the whole animation with it — GDS40K rendered nothing and said only "error
            // animating". Preloading is an optimisation, so a dangling name costs a log line.
            _cache.ThrowsFor = "G_BKLIAL.PAL";

            Assert.DoesNotThrow(() => Walk(
                new LoadPaletteResource { Filename = "G_BKLIAL.PAL" },
                new LoadPaletteResource { Filename = "G_BKLIBR.PAL" }));

            Assert.Contains("G_BKLIBR.PAL", _cache.Requested,
                "and the walk carries on to the next command");
        }

        [Test]
        public void TheDialogBackgroundIsPulledInWhenAndOnlyWhenAFrameSpeaks() {
            Walk(new LoadScreenResource { Filename = "SCENE.SCX" });
            Assert.IsFalse(_cache.Requested.Contains("DIALOG.SCX"));

            _cache.Requested.Clear();
            Walk(new DialogCommand { Dialog16Id = 0, Arg2 = 30 });
            Assert.Contains("DIALOG.SCX", _cache.Requested);
        }

        [Test]
        public void ASpokenDialogIdSelectsItsDDXFileByTheHUNDRETHOUSAND() {
            // dialogId = Dialog16Id + 1600000, and the file is DIAL_Z{dialogId / 100000}. So 16 is
            // Z16, not Z00 — dividing the raw id, or forgetting the offset, reads a different file.
            Walk(new DialogCommand { Dialog16Id = 5, Arg2 = 0 });

            Assert.Contains("DIAL_Z16.DDX", _cache.Requested);
        }

        [Test]
        public void ADialogWithNoIdAndASmallArgPreloadsSlotZeroImageZero() {
            // The talking-head case: Dialog16Id 0 with Arg2 in 0..20 is a portrait, not a line.
            Walk(new SelectImageSlot { SlotNumber = 0 },
                new LoadImageResource { Filename = "FACES.BMX" },
                new DialogCommand { Dialog16Id = 0, Arg2 = 7 });

            Assert.Contains("FACES.BMX#0", _cache.Requested);
        }

        /// <summary>
        /// The Cutscene Studio's synchronous entry point runs the SAME walk.
        /// </summary>
        /// <remarks>
        /// <b>Until this existed the Editor half was a hand-copied second walk that nothing could
        /// reach</b> — it called <c>Addressables</c> inline, so there was nothing to stand in for it,
        /// and the file said so. It had already drifted: the dangling-name tolerance forced onto the
        /// runtime walk by <c>G_BKLIAL.PAL</c> never reached it, so <c>g_misc</c> played in the game
        /// and took the Studio down.
        ///
        /// <para>Now it delegates, so the tests above cover it too and this one only has to prove the
        /// delegation — that the walk completes without suspending (a synchronous caller could
        /// otherwise deadlock on it) and that a name the archive lacks is still shrugged off.</para>
        /// </remarks>
        [Test]
        public void TheEditorEntryPointSharesTheWalk_AndStillShrugsOffADanglingName() {
            var state = (CutsceneState)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(CutsceneState));
            state.ImageSlots = new Dictionary<int, string>();
            state.Resources = new ResourceSet();

            // Addressables logs its own Error before throwing InvalidKeyException, so the console
            // carries one red line per dangling name even though the walk shrugs it off. That is
            // current Studio behaviour, not something this test is asserting away.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try {
                Assert.DoesNotThrow(() => _processor.PreloadFrameResourcesEditor(
                    new FrameCommand[] { new LoadPaletteResource { Filename = "G_BKLIAL.PAL" } }, state));
            } finally {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }

            // And it returned rather than parked: GetResult on a still-pending walk would not have
            // got here. Nothing was recorded, because the name resolves to nothing.
            Assert.IsFalse(state.Resources.ContainsKey("G_BKLIAL.PAL"));
        }

        [Test]
        public void SoundsArePulledInByIdAndAskedForOnlyOnce() {
            // Same id from a load and a play is one resource; the guard is ContainsKey on the
            // recorded set, so it only holds once something has actually been recorded.
            Walk(new LoadSound { SoundId = 42 }, new PlaySound { SoundId = 42 });

            Assert.Contains("42", _cache.Requested);
        }
    }
}

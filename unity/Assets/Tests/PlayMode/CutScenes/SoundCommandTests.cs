namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using GameData.Resources.Animation.FrameCommands;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// The three MIDI commands, driven with no <c>MidiPlaybackManager</c> on the state.
    ///
    /// <para><b>That is the path the shipped scenes take most often, not an artificial one.</b>
    /// <c>CutsceneState.MidiPlayer</c> is supplied by whoever raises the cutscene, and every path
    /// that does not — the test fixtures, and any scene played before audio is wired — reaches the
    /// same guard. 38 shipped uses across 14 scripts go through it.</para>
    ///
    /// <para><b>Why the null path is worth pinning rather than skipping as trivial.</b>
    /// <c>CutsceneFrameProcessor</c> catches a throwing command and logs it, so a
    /// <c>NullReferenceException</c> here would not stop the scene — it would leave an error line
    /// per command and the command silently doing nothing. That exact shape was a live defect in
    /// <c>DisposeCurrentPalette</c> until 2026-09-01, where it hid in 10 of the 42 scripts for as
    /// long as the command existed. A guard is only trivial while it is still there.</para>
    ///
    /// <para>Driven through <see cref="AnimationCommandMap.GetAction"/>, as
    /// <see cref="FontCommandTests"/> is and for the same reason: the map is what a frame goes
    /// through, and a command dropped from it fails differently from one that runs and does
    /// nothing.</para>
    /// </summary>
    public class SoundCommandTests {
        private GameObject _host;
        private CutsceneState _state;

        [SetUp]
        public void SetUp() {
            _host = new GameObject("SoundCommandHost");
            RawImage raw = _host.AddComponent<RawImage>();
            raw.texture = new Texture2D(64, 64);
            _state = new CutsceneState(new UiImage(raw), new Color[256]);
        }

        [TearDown]
        public void TearDown() {
            _state?.Dispose();
            if (_host != null) {
                UnityEngine.Object.DestroyImmediate(_host);
            }
        }

        /// <summary>
        /// <b>0xC041 and 0xC061 are different commands that legitimately share our one call.</b>
        /// </summary>
        /// <remarks>
        /// 0xC041 unloads the resource and 0xC061 stops playback, so folding them together looks
        /// wrong — but <c>audio_unload</c> @0x3581a calls <c>audio_stopSound</c> at 0x3588b before
        /// unlinking, so the original stops on both and merely frees on one more. The half we drop
        /// is the half the resource cache already owns. Asserted as a pair so that anyone
        /// "correcting" one of them has to face the other.
        ///
        /// <para><b>The error log is expected, and that is the point of asserting it.</b> A
        /// cutscene reaching a sound command with no player IS a misconfiguration, so an error is
        /// the right level — but it means the command does nothing, and pinning the log here is
        /// what stops someone "fixing the noise" by demoting it to debug and hiding a real one.
        /// </para>
        /// </remarks>
        [Test]
        public void BothStopCommandsRunWithNoPlayerWired() {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "MidiPlaybackManager is not available.*Cannot stop sound ID 3"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                "MidiPlaybackManager is not available.*Cannot stop playback of sound ID 3"));

            Assert.DoesNotThrow(() => AnimationCommandMap.GetAction(new StopSound { SoundId = 3 })(_state),
                "StopSound is 35 shipped uses across 13 scripts — a throw here is an error line on "
                + "every one of them");
            Assert.DoesNotThrow(
                () => AnimationCommandMap.GetAction(new StopSoundPlayback { SoundId = 3 })(_state));
        }

        /// <summary>
        /// The only filename the shipped scripts ever load, and the only one accepted.
        /// </summary>
        /// <remarks>
        /// Both uses in all 42 scripts are <c>FRP.SX</c> — C11 and INTRO, once each — which is why
        /// the extension can reject everything else outright.
        /// </remarks>
        [Test]
        public void TheSHIPPEDSoundResourceLoadsWithNoPlayerWired() {
            Assert.DoesNotThrow(
                () => AnimationCommandMap.GetAction(new LoadSoundResource { Filename = "FRP.SX" })(_state));
        }

        /// <summary>
        /// <b>Anything else THROWS, and a modder can reach that.</b>
        /// </summary>
        /// <remarks>
        /// Deliberate: the extension supports one resource and says so rather than silently
        /// loading nothing. <c>CutsceneFrameProcessor</c> catches it, so an override script naming
        /// another sound file gets one logged error per frame and a scene that still runs — not a
        /// crash. Pinned because "it only ever gets FRP.SX" is true of the SHIPPED data only, and
        /// the override path exists precisely to supply data that is not shipped.
        /// </remarks>
        [Test]
        public void ANY_OTHERSoundResourceIsRejectedRatherThanSilentlyIgnored() {
            Assert.Throws<System.InvalidOperationException>(
                () => AnimationCommandMap.GetAction(new LoadSoundResource { Filename = "OTHER.SX" })(_state));
            Assert.Throws<System.InvalidOperationException>(
                () => AnimationCommandMap.GetAction(new LoadSoundResource { Filename = null })(_state),
                "Filename is nullable on the model, so a malformed script reaches the same guard");
        }

        /// <summary>All three stay dispatched — dropping one makes it an UNKNOWN command, which is
        /// a different failure and warns differently.</summary>
        [Test]
        public void AllThreeSoundCommandsAreStillDispatched() {
            Assert.IsNotNull(AnimationCommandMap.GetAction(new StopSound { SoundId = 1 }));
            Assert.IsNotNull(AnimationCommandMap.GetAction(new StopSoundPlayback { SoundId = 1 }));
            Assert.IsNotNull(AnimationCommandMap.GetAction(new LoadSoundResource { Filename = "X.SND" }));
        }
    }
}

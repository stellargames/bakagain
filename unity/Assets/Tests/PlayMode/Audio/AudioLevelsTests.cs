namespace BakAgain.Tests.PlayMode.Audio {
    using System.Collections;
    using System.IO;
    using BakAgain.Audio;
    using BakAgain.Tests.TestSupport;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Audio;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>The music and sound volume levels (TASK-1145) reach what actually sounds: the song
    /// player, after a fade too, and the sound effect players and the sample source.</summary>
    public class AudioLevelsTests {
        private EnhancedPrefsStash _stash;
        private GameObject _go;
        private MidiPlaybackManager _midi;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _stash = EnhancedPrefsStash.Take();
            _go = new GameObject("Music");
            _midi = MidiPlaybackManager.Instance;
            if (_midi == null) {
                _midi = _go.AddComponent<MidiPlaybackManager>();
                yield return null;
            }
        }

        [TearDown]
        public void TearDown() {
            AudioLevels.Music = 1f;
            AudioLevels.Sound = 1f;
            Object.DestroyImmediate(_go);
            _stash.Restore();
            AudioLevels.ResetToSaved();
        }

        [Test]
        public void TheSongPlayerFollowsTheMusicLevel() {
            AudioLevels.Music = 0.4f;
            Assert.AreEqual(0.4f, _midi.SongVolume, 0.001f);
            AudioLevels.Music = 0.7f;
            Assert.AreEqual(0.7f, _midi.SongVolume, 0.001f, "at once, not at the next song");
        }

        private sealed class BareSongs : BakAgain.ResourceManagement.IResourceProviderService {
            public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
                UniTask.FromResult(new AudioResource(key.ToString()) as T);

            public void ReleaseAssets(object owner) { }
        }

        [UnityTest]
        public IEnumerator AFadeEndsAtTheMusicLevelNotFull() => UniTask.ToCoroutine(async () => {
            using var notes = ExpectedLog.CaptureAll(new System.Text.RegularExpressions.Regex("does not contain variant"));
            var songs = new BareSongs();
            await _midi.PlayTrackAsync(1005, songs);
            UniTask change = _midi.PlayTrackAsync(1007, songs); // fades 1005 out first
            await UniTask.Yield();
            AudioLevels.Music = 0.5f; // moved during the fade, as on the settings screen
            await change;
            Assert.AreEqual(0.5f, _midi.SongVolume, 0.001f, "the next song starts at the player's level");
            await _midi.PlayTrackAsync(MusicPlayback.NoTrack, songs);
        });

        [Test]
        public void SoundEffectsPlayAtTheSoundLevel() {
            AudioLevels.Sound = 0.3f;
            var strike = new AudioResource("65") { Name = "SWORD2" };
            strike.Variants[0x00] = new AudioDataResource { WavData = Riff(11025, 2205) };
            strike.Variants[0x07] = strike.Variants[0x00];

            _midi.PlaySfx(strike);

            Assert.AreEqual(0.3f, _midi.SampleVolume, 0.001f, "the digitised sample");
            foreach (float v in _midi.SfxPlayerVolumes) Assert.AreEqual(0.3f, v, 0.001f, "every MIDI effect player");
        }

        private static byte[] Riff(int rate, int samples) {
            using var s = new MemoryStream();
            using var w = new BinaryWriter(s);
            w.Write("RIFF".ToCharArray()); w.Write(36 + samples); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate); w.Write((short)1); w.Write((short)8);
            w.Write("data".ToCharArray()); w.Write(samples);
            for (int i = 0; i < samples; i++) { w.Write((byte)(i % 2 == 0 ? 160 : 96)); }
            return s.ToArray();
        }
    }
}

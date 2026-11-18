namespace BakAgain.Tests.PlayMode.Audio {
    using System.Collections;
    using System.IO;
    using BakAgain.Audio;
    using GameData.Resources.Audio;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A digitised sample counts as playing until it ends, so a repeated cue waits for it (forum p=50:
    /// the Armorer's Hammer's three strikes, a sample, sounded as one). The original's
    /// <c>audio_sfx_play_n_times</c> waits for the driver's channel to free before each repeat
    /// (AUDIO.C:433-438), whatever the sound is.
    /// </summary>
    public class SampleCueIsPlayingTests {
        [UnityTest]
        public IEnumerator ASampleIsPlayingUntilItsLengthHasPassed() {
            var go = new GameObject("Music");
            try {
                MidiPlaybackManager midi = MidiPlaybackManager.Instance;
                if (midi == null) {
                    midi = go.AddComponent<MidiPlaybackManager>();
                    yield return null;
                }
                const int rate = 11025, length = rate / 5; // 0.2 s
                var strike = new AudioResource("65") { Name = "SWORD2" };
                // Whichever driver the manager is set to (Sound Blaster or General MIDI).
                strike.Variants[0x00] = new AudioDataResource { WavData = Riff(rate, length) };
                strike.Variants[0x07] = strike.Variants[0x00];

                midi.PlaySfx(strike);

                Assert.IsTrue(midi.IsSfxPlaying("65"), "the strike has just started; the next must wait for it");
                float until = Time.realtimeSinceStartup + 0.5f;
                while (Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.IsFalse(midi.IsSfxPlaying("65"), "and it is over once its 0.2 s have passed");
            } finally {
                Object.DestroyImmediate(go);
            }
        }

        // Mono 8-bit unsigned PCM, the shape the extractor writes.
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

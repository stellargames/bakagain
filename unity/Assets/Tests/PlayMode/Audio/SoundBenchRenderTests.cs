namespace BakAgain.Tests.PlayMode.Audio {
    using System.IO;
    using System.Linq;
    using BakAgain.Audio;
    using GameData.Resources.Audio;
    using MeltySynth;
    using NUnit.Framework;

    /// <summary>The sound bench (TASK-1090) renders a sound offline under a chosen bank, variant, gain,
    /// velocity and repeat count, and reports its peak so near-silent combinations stand out.</summary>
    public class SoundBenchRenderTests {
        private const int Rate = 22050;
        private static SoundFont _bank;

        private static SoundFont Bank => _bank ??= new SoundFont(new MemoryStream(
            File.ReadAllBytes("Assets/BuiltIn/Audio/GeneralUser-GS.bytes")));

        /// <summary>One piano middle C held a quarter note at 96 ticks per beat.</summary>
        private static byte[] OneNote(byte velocity = 100) {
            byte[] track = { 0x00, 0xC0, 0x00, 0x00, 0x90, 0x3C, velocity, 0x60, 0x80, 0x3C, 0x00, 0x00, 0xFF, 0x2F, 0x00 };
            return new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 0, 96,
                    (byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, (byte)track.Length }
                .Concat(track).ToArray();
        }

        [Test]
        public void ANoteRendersAudibly() {
            float[] pcm = SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options());
            Assert.Greater(SoundBenchRender.Peak(pcm), 0.05f);
        }

        [Test]
        public void GainScalesThePeak() {
            float full = SoundBenchRender.Peak(SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options()));
            float half = SoundBenchRender.Peak(SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options(Gain: 0.5f)));
            Assert.AreEqual(full / 2f, half, full * 0.01f);
        }

        [Test]
        public void VelocityScalingSoftensTheNotes() {
            float full = SoundBenchRender.Peak(SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options()));
            float soft = SoundBenchRender.Peak(SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options(Velocity: 0.3f)));
            Assert.Less(soft, full * 0.8f);
        }

        [Test]
        public void RepeatsPlayOneAfterAnother() {
            int once = SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options()).Length;
            int thrice = SoundBenchRender.RenderMidi(Bank, OneNote(), Rate, new SoundBenchRender.Options(Repeats: 3)).Length;
            Assert.AreEqual(once * 3, thrice);
        }

        [Test]
        public void AWavIsDecodedToStereo() {
            // 8-bit unsigned mono, four samples: silence, full up, silence, full down.
            byte[] data = { 128, 255, 128, 0 };
            byte[] riff = Wav(data, 11025);
            float[] pcm = SoundBenchRender.DecodeWav(riff, Rate);
            Assert.NotNull(pcm);
            Assert.Greater(SoundBenchRender.Peak(pcm), 0.9f);
            Assert.AreEqual(0, pcm.Length % 2, "interleaved stereo");
        }

        [Test]
        public void TheGamesChoiceIsWhatTheGamePlays() {
            byte[] wav = { 1 }, sbMidi = { 2 }, gmMidi = { 3 };
            var sample = new AudioResource("5");
            sample.Variants[0x00] = new AudioDataResource { WavData = wav };
            sample.Variants[0x07] = new AudioDataResource { MidiData = gmMidi };
            var effect = new AudioResource("18");
            effect.Variants[0x00] = new AudioDataResource { MidiData = sbMidi };
            effect.Variants[0x07] = new AudioDataResource { MidiData = gmMidi };
            var song = new AudioResource("1001");
            song.Variants[0x00] = new AudioDataResource { MidiData = sbMidi };
            song.Variants[0x07] = new AudioDataResource { MidiData = gmMidi };

            Assert.AreSame(wav, SoundBenchRender.Payload(sample, SoundBenchRender.GamesChoice, preferSample: true).Wav);
            Assert.AreSame(gmMidi, SoundBenchRender.Payload(effect, SoundBenchRender.GamesChoice, true).Midi, "effects: General MIDI (TASK-1089)");
            Assert.AreSame(sbMidi, SoundBenchRender.Payload(song, SoundBenchRender.GamesChoice, true).Midi, "songs: the game's driver");
            Assert.AreSame(sbMidi, SoundBenchRender.Payload(effect, 0x00, true).Midi, "a variant asked for by name");
            Assert.IsNull(SoundBenchRender.Payload(sample, 0x00, preferSample: false).Wav, "MIDI asked for over the sample");
        }

        private static byte[] Wav(byte[] data, int rate) {
            using var m = new MemoryStream();
            using var w = new BinaryWriter(m);
            w.Write("RIFF".ToCharArray()); w.Write(36 + data.Length); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate); w.Write((short)1); w.Write((short)8);
            w.Write("data".ToCharArray()); w.Write(data.Length); w.Write(data);
            return m.ToArray();
        }
    }
}

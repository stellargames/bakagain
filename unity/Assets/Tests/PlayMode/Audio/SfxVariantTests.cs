namespace BakAgain.Tests.PlayMode.Audio {
    using BakAgain.Audio;
    using GameData.Resources.Audio;
    using NUnit.Framework;

    /// <summary>
    /// Sound effects play the General MIDI variant, the one written for the soundfont the game plays
    /// through (owner's call, TASK-1089): on the Sound Blaster variant 8 of 117 effects render near-silent,
    /// the Herbal Pack's swoosh among them (0.007 against 0.383). A digitised sample, which only the Sound
    /// Blaster variant carries, still wins.
    /// </summary>
    public class SfxVariantTests {
        private static readonly byte[] SbMidi = { 1 }, GmMidi = { 7 }, Wav = { 9 };

        [Test]
        public void AnEffectPlaysTheGeneralMidiVariant() {
            var swoosh = new AudioResource("18") { Name = "SWOOSH" };
            swoosh.Variants[0x00] = new AudioDataResource { MidiData = SbMidi };
            swoosh.Variants[0x07] = new AudioDataResource { MidiData = GmMidi };

            (byte[] wav, byte[] midi) = MidiPlaybackManager.SfxPayload(swoosh, 0x00);

            Assert.IsNull(wav);
            Assert.AreSame(GmMidi, midi);
        }

        [Test]
        public void ASampleStillWins() {
            var strike = new AudioResource("65") { Name = "SWORD2" };
            strike.Variants[0x00] = new AudioDataResource { MidiData = SbMidi, WavData = Wav };
            strike.Variants[0x07] = new AudioDataResource { MidiData = GmMidi };

            (byte[] wav, _) = MidiPlaybackManager.SfxPayload(strike, 0x00);

            Assert.AreSame(Wav, wav);
        }

        [Test]
        public void WithoutAGeneralMidiVariantTheDriversOwnIsPlayed() {
            var odd = new AudioResource("5") { Name = "ODD" };
            odd.Variants[0x00] = new AudioDataResource { MidiData = SbMidi };

            Assert.AreSame(SbMidi, MidiPlaybackManager.SfxPayload(odd, 0x00).Midi);
        }
    }
}

namespace BakAgain.Audio {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using GameData.Resources.Audio;
    using MeltySynth;

    /// <summary>
    /// The sound bench's engine (TASK-1090, an Editor window under BaK → Sound Bench): renders a game
    /// sound offline under a chosen bank, variant, gain, velocity and repeat count, as interleaved
    /// stereo at one rate, so any two choices can be heard and their peaks compared.
    /// </summary>
    public static class SoundBenchRender {
        /// <summary>Asks <see cref="Payload"/> for what the game itself plays.</summary>
        public const int GamesChoice = -1;

        /// <summary>The driver the game plays (MidiPlaybackManager.prefab's soundDriver: Sound Blaster).</summary>
        private const byte GameDriver = 0x00;

        /// <param name="Gain">Output scale.</param>
        /// <param name="Velocity">Scale on every note-on's velocity (1..127 kept).</param>
        /// <param name="Repeats">Played one after another, as an item's SoundRepeat does.</param>
        /// <param name="MaxSeconds">Each play is cut here: songs loop for minutes.</param>
        public sealed record Options(float Gain = 1f, float Velocity = 1f, int Repeats = 1, float MaxSeconds = 30f);

        /// <summary>The two names a driver byte has in the code; others are shown as hex.</summary>
        public static string DriverName(byte driver) => driver switch {
            0x00 => "Sound Blaster",
            0x07 => "General MIDI",
            0x0C => "MT-32",
            0x12 => "PC speaker",
            0x13 => "Tandy",
            _ => $"0x{driver:X2}",
        };

        /// <summary>
        /// The bytes a choice plays: <paramref name="variant"/> is a driver byte, or
        /// <see cref="GamesChoice"/> for what <see cref="MidiPlaybackManager"/> picks (an effect: the
        /// driver's sample, else General MIDI; a song: the driver's MIDI).
        /// </summary>
        public static (byte[] Wav, byte[] Midi) Payload(AudioResource sound, int variant, bool preferSample) {
            bool song = int.TryParse(sound.Id, out int id) && id >= 1001;
            if (variant == GamesChoice) {
                if (!song) return MidiPlaybackManager.SfxPayload(sound, GameDriver);
                variant = GameDriver;
            }
            if (!sound.Variants.TryGetValue((byte)variant, out AudioDataResource data)) return (null, null);
            if (preferSample && data.WavData != null) return (data.WavData, null);
            return (null, data.MidiData);
        }

        /// <summary>Renders a Standard MIDI File: interleaved stereo at <paramref name="rate"/>.</summary>
        public static float[] RenderMidi(SoundFont bank, byte[] smf, int rate, Options options) {
            var midi = new MidiFile(new MemoryStream(smf, writable: false));
            var synth = new Synthesizer(bank, rate);
            var sequencer = new MidiFileSequencer(synth);
            if (Math.Abs(options.Velocity - 1f) > 0.001f) {
                sequencer.OnSendMessage = (s, channel, command, data1, data2) => {
                    if (command == 0x90 && data2 > 0) {
                        data2 = Math.Clamp((int)MathF.Round(data2 * options.Velocity), 1, 127);
                    }
                    s.ProcessMidiMessage(channel, command, data1, data2);
                };
            }
            const int block = 1024;
            float[] left = new float[block], right = new float[block];
            var once = new List<float>();
            int max = (int)(options.MaxSeconds * rate);
            sequencer.Play(midi, false);
            // The song, then a second of release tail so a cut note dies out rather than clicks.
            for (int done = 0, tail = rate; done < max && tail > 0; done += block) {
                if (sequencer.EndOfSequence) tail -= block;
                sequencer.Render(left, right);
                for (int i = 0; i < block; i++) {
                    once.Add(left[i] * options.Gain);
                    once.Add(right[i] * options.Gain);
                }
            }
            return Repeat(once.ToArray(), options.Repeats);
        }

        /// <summary>A digitised sample as interleaved stereo at <paramref name="rate"/>, or null.</summary>
        /// <remarks>ponytail: linear resampling; fine for judging loudness, not for a mastering ear.</remarks>
        public static float[] DecodeWav(byte[] riff, int rate, Options options = null) {
            options ??= new Options();
            if (WavePcm.Decode(riff) is not { } pcm || pcm.Samples.Length == 0) return null;
            int frames = pcm.Samples.Length / pcm.Channels;
            int outFrames = (int)((long)frames * rate / pcm.SampleRate);
            var stereo = new float[outFrames * 2];
            for (int f = 0; f < outFrames; f++) {
                double at = (double)f * pcm.SampleRate / rate;
                int i = (int)at;
                float frac = (float)(at - i);
                for (int c = 0; c < 2; c++) {
                    int ch = Math.Min(c, pcm.Channels - 1);
                    float a = pcm.Samples[i * pcm.Channels + ch];
                    float b = i + 1 < frames ? pcm.Samples[(i + 1) * pcm.Channels + ch] : a;
                    stereo[f * 2 + c] = (a + (b - a) * frac) * options.Gain;
                }
            }
            return Repeat(stereo, options.Repeats);
        }

        public static float Peak(float[] samples) {
            float peak = 0f;
            foreach (float s in samples) peak = Math.Max(peak, Math.Abs(s));
            return peak;
        }

        private static float[] Repeat(float[] once, int repeats) {
            if (repeats <= 1) return once;
            var all = new float[once.Length * repeats];
            for (int r = 0; r < repeats; r++) Array.Copy(once, 0, all, r * once.Length, once.Length);
            return all;
        }
    }
}

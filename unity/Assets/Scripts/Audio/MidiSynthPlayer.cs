namespace BakAgain.Audio {
    using System;
    using System.IO;
    using MeltySynth;
    using UnityEngine;

    /// <summary>
    /// One MIDI voice for <see cref="MidiPlaybackManager"/>: a MeltySynth synthesizer + sequencer
    /// rendered straight into this GameObject's audio through <c>OnAudioFilterRead</c>.
    /// </summary>
    /// <remarks>
    /// The sequencer is touched from two threads — Load/Play/Stop on the main thread, Render on the
    /// audio thread — so every access goes through <see cref="_gate"/>. The soundfont is loaded once
    /// and shared: a MeltySynth <see cref="Synthesizer"/> only reads it.
    /// </remarks>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MidiSynthPlayer : MonoBehaviour {
        /// <summary>GeneralUser GS 2.0.3 (its license: Resources/Audio/GeneralUser-GS-LICENSE.txt).</summary>
        private const string SoundFontResource = "Audio/GeneralUser-GS";

        private static SoundFont _soundFont;

        private readonly object _gate = new();
        private MidiFileSequencer _sequencer;
        private MidiFile _loaded;
        private bool _playing;
        private float[] _left = Array.Empty<float>();
        private float[] _right = Array.Empty<float>();

        /// <summary>The name of the loaded song, for logs.</summary>
        public string MidiName { get; set; }

        /// <summary>Output volume, 0..1 (the fade ramps this).</summary>
        public float Volume {
            get => Source.volume;
            set => Source.volume = value;
        }

        /// <summary>True from Play until the song ends (never, for a looping one) or Stop.</summary>
        public bool IsPlaying {
            get {
                lock (_gate) {
                    return _playing && _sequencer != null && !_sequencer.EndOfSequence;
                }
            }
        }

        private AudioSource Source => GetComponent<AudioSource>();

        private void Awake() {
            _soundFont ??= LoadSoundFont();
            var synthesizer = new Synthesizer(_soundFont, AudioSettings.outputSampleRate);
            _sequencer = new MidiFileSequencer(synthesizer);

            // No clip: OnAudioFilterRead generates the signal on a playing source.
            AudioSource source = Source;
            source.playOnAwake = false;
            source.loop = true;
            source.Play();
        }

        private static SoundFont LoadSoundFont() {
            var asset = Resources.Load<TextAsset>(SoundFontResource);
            if (asset == null) {
                throw new InvalidOperationException($"Soundfont resource '{SoundFontResource}' is missing.");
            }
            using var stream = new MemoryStream(asset.bytes, writable: false);
            var soundFont = new SoundFont(stream);
            Resources.UnloadAsset(asset);

            return soundFont;
        }

        /// <summary>Parse a Standard MIDI File; <see cref="Play"/> starts it.</summary>
        public void Load(byte[] smf) {
            using var stream = new MemoryStream(smf, writable: false);
            var midiFile = new MidiFile(stream);
            lock (_gate) {
                _loaded = midiFile;
            }
        }

        public void Play(bool loop) {
            lock (_gate) {
                if (_loaded == null) {
                    return;
                }
                _sequencer.Play(_loaded, loop);
                _playing = true;
            }
        }

        public void Stop() {
            lock (_gate) {
                _sequencer.Stop();
                _playing = false;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels) {
            int frames = data.Length / channels;
            if (_left.Length < frames) {
                _left = new float[frames];
                _right = new float[frames];
            }

            lock (_gate) {
                // The audio thread can call in before Awake has built the sequencer; render silence
                // rather than throw on every buffer (a NullReferenceException storm that filled the
                // Editor log at ~100 a second).
                if (_sequencer == null) {
                    Array.Clear(data, 0, data.Length);
                    return;
                }
                _sequencer.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames));
            }

            for (int i = 0, o = 0; i < frames; i++, o += channels) {
                if (channels == 1) {
                    data[o] = 0.5f * (_left[i] + _right[i]);
                    continue;
                }
                data[o] = _left[i];
                data[o + 1] = _right[i];
                for (int c = 2; c < channels; c++) {
                    data[o + c] = 0f;
                }
            }
        }
    }
}

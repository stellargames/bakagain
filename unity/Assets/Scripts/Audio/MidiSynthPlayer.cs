namespace BakAgain.Audio {
    using System;
    using System.IO;
    using BakAgain.Core;
    using Cysharp.Threading.Tasks;
    using MeltySynth;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

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
        /// <summary>GeneralUser GS 2.0.3 (its license: BuiltIn/Audio/GeneralUser-GS-LICENSE.txt).</summary>
        private const string SoundFontResource = "Audio/GeneralUser-GS";

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(MidiSynthPlayer));
        private static SoundFont _soundFont;
        private static bool _soundFontUnusable;

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
            // No clip: OnAudioFilterRead generates the signal on a playing source.
            AudioSource source = Source;
            source.playOnAwake = false;
            source.loop = true;
            source.Play();
        }

        /// <summary>
        /// Loads the sound bank, once the boot has registered the mod layers, so a mod's bank is the one
        /// played. The players are made before that (<see cref="MidiPlaybackManager"/>'s Start) and
        /// build their synthesizer on their first <see cref="Load"/>.
        /// </summary>
        public static async UniTask PreloadSoundFontAsync() {
            if (_soundFont != null || _soundFontUnusable) {
                return;
            }
            AsyncOperationHandle<TextAsset> handle = Addressables.LoadAssetAsync<TextAsset>(SoundFontResource);
            try {
                _soundFont = Parse(await handle.ToUniTask());
            } catch (Exception e) {
                Unusable(e);
            } finally {
                Addressables.Release(handle);
            }
        }

        /// <summary>The bank, loading it now only if the boot's preload never ran; null once it proved unusable.</summary>
        private static SoundFont SoundFontOrNull() {
            if (_soundFont == null && !_soundFontUnusable) {
                try {
                    _soundFont = LoadSoundFont();
                } catch (Exception e) {
                    Unusable(e);
                }
            }
            return _soundFont;
        }

        /// <summary>A missing or broken bank (a mod's corrupt .sf2) is said once; the game then plays without music.</summary>
        private static void Unusable(Exception e) {
            _soundFontUnusable = true;
            Logger.LogError("Sound bank '{Resource}' is unusable, no music this session: {Reason}", SoundFontResource, e.Message);
        }

        private static SoundFont LoadSoundFont() {
            // Only when the boot's preload has not run (a test making a player directly):
            // WaitForCompletion throws inside another load's Completed callback, which the preload
            // keeps the game out of.
            AsyncOperationHandle<TextAsset> handle = Addressables.LoadAssetAsync<TextAsset>(SoundFontResource);
            try {
                TextAsset asset = handle.WaitForCompletion();
                if (asset == null) {
                    throw new InvalidOperationException($"Soundfont resource '{SoundFontResource}' is missing.");
                }
                return Parse(asset);
            } finally {
                Addressables.Release(handle);
            }
        }

        private static SoundFont Parse(TextAsset asset) {
            using var stream = new MemoryStream(asset.bytes, writable: false);
            return new SoundFont(stream);
        }

        /// <summary>Parse a Standard MIDI File; <see cref="Play"/> starts it.</summary>
        public void Load(byte[] smf) {
            using var stream = new MemoryStream(smf, writable: false);
            var midiFile = new MidiFile(stream);
            SoundFont bank = SoundFontOrNull();
            if (bank == null) {
                return;
            }
            MidiFileSequencer sequencer = _sequencer ?? new MidiFileSequencer(new Synthesizer(bank, AudioSettings.outputSampleRate));
            lock (_gate) {
                _sequencer = sequencer;
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
                _sequencer?.Stop();
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
                // The audio thread calls in before the first song has built the sequencer; render silence
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

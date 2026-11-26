namespace BakAgain.Editor.Audio {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using BakAgain.Audio;
    using BakAgain.ResourceManagement;
    using GameData.Resources.Audio;
    using MeltySynth;
    using ResourceExtraction.Providers;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Plays any game sound under each bank, driver variant, gain, velocity and repeat count, with its
    /// rendered peak beside it, so the choice can be made by ear (TASK-1090). Editor only: nothing here
    /// ships. Sounds are read straight from the player's FRP.SX; banks are GeneralUser GS plus any .sf2
    /// added by hand.
    /// </summary>
    public sealed class SoundBench : EditorWindow {
        private const string BuiltInBank = "Assets/BuiltIn/Audio/GeneralUser-GS.bytes";
        private const string BanksPref = "BaK.SoundBench.Banks";
        /// <summary>Below this peak a sound is marked as near-silent.</summary>
        private const float Quiet = 0.05f;

        private enum Kind { All, Effects, Songs }

        private readonly Dictionary<string, AudioResource> _sounds = new();
        private readonly Dictionary<string, SoundFont> _banks = new();
        private readonly Dictionary<string, float> _peaks = new();
        private List<string> _ids = new();
        private byte[] _drivers = Array.Empty<byte>();
        private string _error;

        private int _bank, _variant; // _variant 0 = the game's choice, else _drivers[_variant - 1]
        private bool _preferSample = true;
        private float _gain = 1f, _velocity = 1f, _maxSeconds = 30f;
        private int _repeats = 1;
        private Kind _kind;
        private string _search = string.Empty, _selected;
        private Vector2 _scroll;
        private AudioSource _source;

        [MenuItem("BaK/Sound Bench")]
        public static void Open() => GetWindow<SoundBench>("Sound Bench");

        private void OnEnable() => Load();

        private void OnDisable() {
            if (_source != null) DestroyImmediate(_source.gameObject);
        }

        private void Load() {
            _sounds.Clear();
            _error = null;
            try {
                var provider = new AudioResourceProvider(BakResourceSettings.GamePath);
                foreach (string id in provider.GetDictionary().Keys) {
                    _sounds[id] = provider.GetResource<AudioResource>(id);
                }
            } catch (Exception e) {
                _error = $"Could not read the game's sounds from '{BakResourceSettings.GamePath}': {e.Message}";
            }
            _ids = _sounds.Keys.OrderBy(k => int.TryParse(k, out int n) ? n : int.MaxValue).ToList();
            _drivers = _sounds.Values.SelectMany(s => s.Variants.Keys).Distinct().OrderBy(b => b).ToArray();
            if (_sounds.Count == 0 && _error == null) _error = "No sounds found: is the game folder set (first start of the game)?";
        }

        private List<string> BankPaths() {
            var paths = new List<string> { BuiltInBank };
            paths.AddRange(EditorPrefs.GetString(BanksPref, string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries));
            return paths;
        }

        private SoundFont Bank() {
            List<string> paths = BankPaths();
            string path = paths[Mathf.Clamp(_bank, 0, paths.Count - 1)];
            if (!_banks.TryGetValue(path, out SoundFont bank)) {
                bank = new SoundFont(new MemoryStream(File.ReadAllBytes(path)));
                _banks[path] = bank;
            }
            return bank;
        }

        private SoundBenchRender.Options Options() => new(_gain, _velocity, _repeats, _maxSeconds);

        private int Variant => _variant == 0 ? SoundBenchRender.GamesChoice : _drivers[_variant - 1];

        /// <summary>The sound under a choice, or null when that choice has nothing to play.</summary>
        private float[] Render(string id, int variant, bool preferSample) {
            (byte[] wav, byte[] midi) = SoundBenchRender.Payload(_sounds[id], variant, preferSample);
            int rate = AudioSettings.outputSampleRate;
            if (wav != null) return SoundBenchRender.DecodeWav(wav, rate, Options());
            return midi == null ? null : SoundBenchRender.RenderMidi(Bank(), midi, rate, Options());
        }

        private string Key(string id, int variant, bool sample) =>
            $"{id}|{variant}|{sample}|{_bank}|{_gain}|{_velocity}|{_repeats}|{_maxSeconds}";

        private float? Measure(string id, int variant, bool sample) {
            string key = Key(id, variant, sample);
            if (_peaks.TryGetValue(key, out float peak)) return peak;
            float[] pcm = Render(id, variant, sample);
            if (pcm == null) return null;
            return _peaks[key] = SoundBenchRender.Peak(pcm);
        }

        private float? Known(string id, int variant, bool sample) =>
            _peaks.TryGetValue(Key(id, variant, sample), out float p) ? p : null;

        private void Play(string id, int variant, bool sample) {
            float[] pcm = Render(id, variant, sample);
            if (pcm == null) return;
            _peaks[Key(id, variant, sample)] = SoundBenchRender.Peak(pcm);
            if (_source == null) {
                var go = new GameObject("SoundBench") { hideFlags = HideFlags.HideAndDontSave };
                _source = go.AddComponent<AudioSource>();
                _source.playOnAwake = false;
            }
            _source.Stop();
            var clip = AudioClip.Create($"{id} {_sounds[id].Name}", pcm.Length / 2, 2, AudioSettings.outputSampleRate, false);
            clip.SetData(pcm, 0);
            _source.clip = clip;
            _source.Play();
            _selected = id;
        }

        private void Stop() {
            if (_source != null) _source.Stop();
        }

        private IEnumerable<string> Shown() => _ids.Where(id => {
            bool song = int.TryParse(id, out int n) && n >= 1001;
            if (_kind == Kind.Effects && song || _kind == Kind.Songs && !song) return false;
            return _search.Length == 0 || id.Contains(_search)
                || _sounds[id].Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        });

        private void OnGUI() {
            if (_error != null) {
                EditorGUILayout.HelpBox(_error, MessageType.Warning);
                if (GUILayout.Button("Reload")) Load();
                return;
            }
            Toolbar();
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope()) {
                using (var list = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.ExpandWidth(true))) {
                    _scroll = list.scrollPosition;
                    using (new EditorGUILayout.HorizontalScope()) {
                        GUILayout.Label("Sound", EditorStyles.miniBoldLabel, GUILayout.Width(200));
                        GUILayout.Label("Variants (w = has a sample)", EditorStyles.miniBoldLabel, GUILayout.Width(170));
                        GUILayout.Label("Peak", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                    }
                    foreach (string id in Shown()) Row(id);
                }
                if (_selected != null) {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(330))) Compare(_selected);
                }
            }
        }

        private void Toolbar() {
            List<string> banks = BankPaths();
            using (new EditorGUILayout.HorizontalScope()) {
                _bank = EditorGUILayout.Popup("Bank", _bank, banks.Select(Path.GetFileNameWithoutExtension).ToArray());
                if (GUILayout.Button("Add .sf2…", GUILayout.Width(80))) {
                    string path = EditorUtility.OpenFilePanel("Add a sound bank", string.Empty, "sf2");
                    if (!string.IsNullOrEmpty(path) && !banks.Contains(path)) {
                        EditorPrefs.SetString(BanksPref, string.Join("\n", banks.Skip(1).Append(path)));
                        _bank = banks.Count;
                    }
                }
            }
            string[] variants = new[] { "Game's choice" }
                .Concat(_drivers.Select(d => $"{d:X2} {SoundBenchRender.DriverName(d)}")).ToArray();
            _variant = EditorGUILayout.Popup("Variant", _variant, variants);
            _preferSample = EditorGUILayout.Toggle(new GUIContent("Sample over MIDI",
                "Where a variant has both a digitised sample and MIDI, play the sample (the game does)."), _preferSample);
            _gain = EditorGUILayout.Slider("Gain", _gain, 0f, 4f);
            _velocity = EditorGUILayout.Slider(new GUIContent("Velocity", "Scale on every note's velocity."), _velocity, 0.1f, 2f);
            _repeats = EditorGUILayout.IntSlider(new GUIContent("Repeats", "Played one after another, as an item's SoundRepeat."), _repeats, 1, 6);
            _maxSeconds = EditorGUILayout.Slider(new GUIContent("Cut at (s)", "Each play stops here; songs run for minutes."), _maxSeconds, 2f, 300f);
            using (new EditorGUILayout.HorizontalScope()) {
                _kind = (Kind)EditorGUILayout.EnumPopup(_kind, GUILayout.Width(80));
                _search = EditorGUILayout.TextField(_search);
                if (GUILayout.Button("Measure all", GUILayout.Width(90))) MeasureAll();
                if (GUILayout.Button("Stop", GUILayout.Width(50))) Stop();
            }
        }

        private void MeasureAll() {
            List<string> ids = Shown().ToList();
            try {
                for (int i = 0; i < ids.Count; i++) {
                    if (EditorUtility.DisplayCancelableProgressBar("Sound Bench", $"Rendering {ids[i]} {_sounds[ids[i]].Name}", (float)i / ids.Count)) break;
                    Measure(ids[i], Variant, _preferSample);
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private void Row(string id) {
            AudioResource s = _sounds[id];
            using (new EditorGUILayout.HorizontalScope()) {
                bool selected = id == _selected;
                if (GUILayout.Toggle(selected, $"{id,5}  {s.Name}", EditorStyles.label, GUILayout.Width(200)) && !selected) _selected = id;
                GUILayout.Label(string.Join(" ", s.Variants.OrderBy(v => v.Key)
                    .Select(v => $"{v.Key:X2}{(v.Value.WavData != null ? "w" : string.Empty)}")), GUILayout.Width(170));
                PeakLabel(Known(id, Variant, _preferSample));
                if (GUILayout.Button("▶", GUILayout.Width(28))) Play(id, Variant, _preferSample);
            }
        }

        /// <summary>The selected sound under every variant, side by side: the comparison itself.</summary>
        private void Compare(string id) {
            AudioResource s = _sounds[id];
            EditorGUILayout.LabelField($"{id} {s.Name}", EditorStyles.boldLabel);
            if (GUILayout.Button("Measure every variant")) {
                Measure(id, SoundBenchRender.GamesChoice, true);
                foreach (KeyValuePair<byte, AudioDataResource> v in s.Variants) {
                    if (v.Value.WavData != null) Measure(id, v.Key, true);
                    if (v.Value.MidiData != null) Measure(id, v.Key, false);
                }
            }
            CompareRow("Game's choice", id, SoundBenchRender.GamesChoice, true);
            foreach (KeyValuePair<byte, AudioDataResource> v in s.Variants.OrderBy(v => v.Key)) {
                string name = $"{v.Key:X2} {SoundBenchRender.DriverName(v.Key)}";
                if (v.Value.WavData != null) CompareRow(name + " (sample)", id, v.Key, true);
                if (v.Value.MidiData != null) CompareRow(name + " (MIDI)", id, v.Key, false);
            }
        }

        private void CompareRow(string label, string id, int variant, bool sample) {
            using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label(label, GUILayout.Width(200));
                PeakLabel(Known(id, variant, sample));
                if (GUILayout.Button("▶", GUILayout.Width(28))) Play(id, variant, sample);
            }
        }

        private static void PeakLabel(float? peak) {
            var style = new GUIStyle(EditorStyles.label);
            if (peak < Quiet) style.normal.textColor = new Color(0.95f, 0.35f, 0.3f);
            GUILayout.Label(peak.HasValue ? $"peak {peak:0.000}" : "peak  —", style, GUILayout.Width(80));
        }
    }
}

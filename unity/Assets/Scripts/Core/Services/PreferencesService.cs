namespace BakAgain.Core.Services {
    using System;
    using System.IO;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;
    using UnityEngine;

    /// <inheritdoc cref="IPreferencesService"/>
    public sealed class PreferencesService : IPreferencesService {
        // The shipped 5-byte defaults. CDEFAULT.DAT (CD music on) is the CD-build
        // variant; we seed from DEFAULT.DAT until a CD-present concept exists.
        private const string DefaultsKey = "DEFAULT.DAT";
        private const string FileName = "preferences.json";

        private readonly IResourceProviderService _resources;
        private readonly ILogger<PreferencesService> _logger;

        private Preferences _current;
        private bool _loaded;

        /// <summary>The load in flight, so concurrent callers await it instead of racing it.</summary>
        private UniTask? _loading;

        public PreferencesService(IResourceProviderService resources, ILogger<PreferencesService> logger) {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// The live preferences — fabricated from the field initializers if nothing has loaded yet.
        /// </summary>
        /// <remarks>
        /// <b>The fabricated object is labelled <see cref="FallbackId"/>, not "USER".</b> It used to
        /// claim "USER", which made a defaults object indistinguishable from one read off disk: a
        /// probe taken during the load window reported Large/Medium/High while `preferences.json`
        /// said Small/Small/Maximum and the console had already logged the load, and the whole thing
        /// read as a failed boot fix. The label costs nothing and settles that in one look.
        ///
        /// <para><b>Deliberately does not throw</b> when nothing is loaded — tests and edit-time
        /// tooling read this without a load and must keep working.</para>
        /// </remarks>
        public Preferences Current => _current ??= new Preferences(FallbackId);

        /// <summary>What <see cref="Current"/> stamps on an object nobody loaded.</summary>
        public const string FallbackId = "FALLBACK";

        /// <summary>What a real load — from disk or from the shipped defaults — stamps.</summary>
        public const string LoadedId = "USER";

        public event Action Changed;

        private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// Read the preferences once, and make every concurrent caller wait for the same read.
        /// </summary>
        /// <remarks>
        /// <b><c>_loaded</c> is set at the END, not the start.</b> It used to be set before the
        /// first <c>await</c>, so for the length of the file read it claimed a load that had not
        /// happened — and any <see cref="Current"/> access in that window materialised the field
        /// initializers into <c>_current</c>. The await then overwrote them, which is why the
        /// settled value was right and this never looked like a bug; what it cost was a consumer
        /// that caches the *reference* rather than re-reading, which keeps the wrong object for
        /// good.
        ///
        /// <para>Re-entry is guarded by the in-flight <see cref="UniTask"/> rather than by the flag,
        /// so a second caller arriving mid-read awaits the same load instead of starting another
        /// one — which is what moving the flag would otherwise have allowed.</para>
        /// </remarks>
        public UniTask EnsureLoadedAsync() {
            if (_loaded) {
                return UniTask.CompletedTask;
            }
            if (_loading.HasValue) {
                return _loading.Value;
            }
            // *** .Preserve() IS REQUIRED. *** A UniTask is single-consumption: handing the same
            // one to a second caller throws rather than making them both wait. Preserve makes it
            // multi-awaitable, which is the whole point of caching it here.
            UniTask loading = LoadAsync().Preserve();
            _loading = loading;
            return loading;
        }

        private async UniTask LoadAsync() {
            if (File.Exists(FilePath)) {
                try {
                    string json = await File.ReadAllTextAsync(FilePath);
                    var parsed = JsonConvert.DeserializeObject<Preferences>(json);
                    if (parsed != null) {
                        parsed.Id = LoadedId;
                        _current = parsed;
                        _loaded = true;
                        _loading = null;
                        _logger.LogInformation("Loaded preferences from {Path}.", FilePath);
                        Changed?.Invoke();
                        return;
                    }
                    _logger.LogWarning("Preferences file {Path} was empty/invalid; seeding from defaults.", FilePath);
                } catch (Exception e) {
                    _logger.LogError(e, "Failed to read preferences {Path}; seeding from defaults.", FilePath);
                }
            }

            // First run (or unreadable file): seed from the shipped defaults.
            Preferences defaults = await GetDefaultsAsync();
            _current = defaults;
            _current.Id = LoadedId;
            _loaded = true;
            _loading = null;
            Changed?.Invoke();
        }

        public async UniTask<Preferences> GetDefaultsAsync() {
            try {
                var defaults = await _resources.LoadAssetAsync<Preferences>(DefaultsKey, owner: this);
                if (defaults != null) {
                    return defaults.Clone();
                }
                _logger.LogError("Default preferences {Key} not found; using built-in defaults.", DefaultsKey);
            } catch (Exception e) {
                _logger.LogError(e, "Failed to load default preferences {Key}; using built-in defaults.", DefaultsKey);
            } finally {
                _resources.ReleaseAssets(this);
            }
            // Preferences' property initializers match DEFAULT.DAT (02 01 02 00 0F):
            // Large/Medium/High/Slow, sound+music+combat+intro on. This comment claimed
            // "Medium/Medium" and the initializer agreed with the comment rather than the file,
            // so byte 0 was wrong by one preset. BuiltInDefaultsMatchShippedDefaultDat now
            // asserts the two against each other instead of a comment asserting it.
            return new Preferences("DEFAULT");
        }

        public void Apply(Preferences working) {
            if (working == null) {
                throw new ArgumentNullException(nameof(working));
            }
            _current = working.Clone();
            _current.Id = LoadedId;
            Save();
            // Consumers subscribe to Changed and read Current. Wired: step/turn size
            // (PartyMovement), sound + game music (MidiPlaybackManager), detail level (the
            // collision candidate gate — ProximityWorld reads FILTER.DAT's per-level draw
            // distances), text speed (dialog auto-dismiss — DialogManager), intro (GameFlow.Boot).
            //
            // Still unwired, and each is waiting on a system rather than on this class:
            //   combat music — there is no combat yet (TASK-94)
            //   CD music     — we play General MIDI through MeltySynth; there is no CD audio
            //                  path at all, so this one may never apply
            //   detail level — the RENDERER still ignores it; the original uses the same FILTER.DAT
            //                  draw distances to cull world objects, not just collision candidates
            //                  (world-rendering work, TASK-129)
            Changed?.Invoke();
        }

        private void Save() {
            try {
                string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                File.WriteAllText(FilePath, json);
                _logger.LogInformation("Saved preferences to {Path}.", FilePath);
            } catch (Exception e) {
                _logger.LogError(e, "Failed to save preferences to {Path}.", FilePath);
            }
        }
    }
}

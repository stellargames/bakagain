namespace BakAgain.Audio {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Audio;
    using JetBrains.Annotations;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class MidiPlaybackManager : MonoBehaviour {
        private static MidiPlaybackManager _instance;

        [SerializeField]
        private int sfxPoolSize = 2;

        [SerializeField]
        private SoundDriver soundDriver = SoundDriver.GeneralMidi;

        private MidiSynthPlayer _songPlayer;
        private readonly List<MidiSynthPlayer> _sfxPlayers = new();
        private static readonly ILogger _logger = LogManager.LoggerFactory.CreateLogger(nameof(MidiPlaybackManager));

        [CanBeNull]
        private string _currentSongId;

        // The same thing as _currentSongId, as the int the music policy speaks in. Kept in step with
        // it (set in PlaySong, cleared wherever the song stops) so a song started through the plain
        // PlaySong path — the cutscene player's, say — still counts as "what is playing" for the
        // no-restart guard in PlayTrackAsync.
        private int _currentTrack = MusicPlayback.NoTrack;

        private IPreferencesService _preferences;

        private readonly Dictionary<string, MidiSynthPlayer> _activeSfxPlayers = new();

        public static MidiPlaybackManager Instance => _instance;

        private void Awake() {
            if (_instance && _instance != this) {
                Destroy(gameObject);

                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        [VContainer.Inject]
        public void Construct(IPreferencesService preferences) {
            _preferences = preferences;
            _preferences.Changed += OnPreferencesChanged;
        }

        // When the game-music flag is cleared, silence the currently-playing song immediately —
        // faithful to audio_song_sub_1505A @ 0x1505a, which stops the current song when config.flags
        // & musicOn is clear. Turning music back on does not resume; the next PlaySong plays.
        private void OnPreferencesChanged() {
            if (_preferences != null && !_preferences.Current.GameMusic) {
                StopMusic();
            }
        }

        private void Start() {
            _songPlayer = CreatePlayer("SongPlayer");
            for (int i = 0; i < sfxPoolSize; i++) {
                _sfxPlayers.Add(CreatePlayer("SFXPlayer_" + i));
            }
        }

        private MidiSynthPlayer CreatePlayer(string playerName) {
            var go = new GameObject(playerName);
            go.transform.SetParent(transform, false);

            return go.AddComponent<MidiSynthPlayer>();
        }

        private void OnDestroy() {
            if (_preferences != null) {
                _preferences.Changed -= OnPreferencesChanged;
            }
            if (_instance == this) {
                _instance = null;
            }
        }

        public void PlaySong(AudioResource audioResource) {
            if (audioResource == null) {
                _logger.LogWarning("PlaySong: audioResource is null.");

                return;
            }
            // Record the track BEFORE the preference gate. With music off the original still does
            // the bookkeeping — it stops, loads the chunk and stores the new current song — and
            // simply never starts it (MusicPlayback.IsAudible). So a later query reports the track
            // that WOULD be playing, and turning music back on mid-game does not resync it.
            if (int.TryParse(audioResource.Id, out int trackId)) {
                _currentTrack = trackId;
            }
            // Songs (id >= 1001) gate on the game-music flag — audio_PlaySound @ 0x15789 routes songs
            // to audio_song_sub_1505A, which plays only when config.flags & musicOn (0x02) is set.
            if (_preferences != null && !_preferences.Current.GameMusic) {
                _logger.LogInformation("PlaySong suppressed: game music disabled in preferences ({Song}).", audioResource.Name);
                return;
            }
            if (!audioResource.Variants.ContainsKey((byte)soundDriver)) {
                _logger.LogWarning("Audio resource {AudioResourceName} (ID: {AudioId}) does not contain variant for sound driver {SoundDriver}.", audioResource.Name, audioResource.Id, soundDriver);

                return;
            }
            if (audioResource.Variants[(byte)soundDriver].MidiData is not { } midiData) {
                _logger.LogWarning("Audio resource {AudioResourceName} (ID: {AudioId}) does not contain MIDI data for sound driver {SoundDriver}.", audioResource.Name, audioResource.Id, soundDriver);

                return;
            }

            if (_songPlayer.IsPlaying) {
                _logger.LogInformation("Stopping currently playing song ID {PreviousSongId} (Name: {PreviousSongName}) before playing new song ID {NewSongId} (Name: {NewSongName}).", _currentSongId,
                    _songPlayer.MidiName, audioResource.Id, audioResource.Name);
            }
            _songPlayer.Stop();
            _songPlayer.MidiName = audioResource.Name;
            _songPlayer.Load(midiData);
            // Faithful to the original: a sound flagged looping (field_12_flag bit 0x02) repeats
            // when it reaches the end — e.g. the intro theme keeps playing through the credits.
            _songPlayer.Play(loop: audioResource.IsLooping);
            _currentSongId = audioResource.Id;
            _logger.LogInformation("Playing song: {SongName} (ID: {SongId})", audioResource.Name, _currentSongId);
        }

        public void PlaySfx(AudioResource audioResource) {
            if (audioResource == null) {
                _logger.LogWarning("PlaySfx: audioResource is null.");

                return;
            }
            // SFX (id < 1001) gate on the sound flag — audio_PlaySound @ 0x15789 plays only when
            // config.flags & soundOn (0x01) is set. Covers UI cues too (MenuSoundService -> PlaySfx).
            if (_preferences != null && !_preferences.Current.Sound) {
                _logger.LogInformation("PlaySfx suppressed: sound disabled in preferences ({Sfx}).", audioResource.Name);
                return;
            }
            if (!audioResource.Variants.ContainsKey((byte)soundDriver)) {
                _logger.LogWarning("Audio resource {AudioResourceName} (ID: {AudioId}) does not contain variant for sound driver {SoundDriver}.", audioResource.Name, audioResource.Id, soundDriver);

                return;
            }
            // *** A DIGITISED SAMPLE IS PLAYED AS A SAMPLE, AND IT WINS. *** A variant can carry
            // BOTH payloads -- the sentinel channel 0xFE and the real MIDI channels are read in the
            // same loop -- and AudioDataResource's own remark says which one sounds: the WAV. Until
            // this arm existed PlaySfx asked only for MidiData and took the warning return below,
            // so every one of the game's 54 digitised samples was dropped at the last step even
            // though the extractor had already produced it (TASK-601).
            //
            // It belongs HERE and not in PlaySong: audio_play (AUDIO.C:383) splits at 0x3e9, and
            // every sample id is below it. The arm spent one session in PlaySong, where nothing it
            // tests can ever be true -- and the warning below kept firing, which read as "the fix
            // does not work" rather than "the fix is in the other method".
            if (audioResource.Variants[(byte)soundDriver].WavData is { } wavData) {
                PlaySample(audioResource, wavData);

                return;
            }

            if (audioResource.Variants[(byte)soundDriver].MidiData is not { } midiData) {
                _logger.LogWarning("Audio resource {AudioResourceName} (ID: {AudioId}) does not contain MIDI data for sound driver {SoundDriver}.", audioResource.Name, audioResource.Id, soundDriver);

                return;
            }
            MidiSynthPlayer availablePlayer = _sfxPlayers.Find(p => !p.IsPlaying);

            if (availablePlayer) {
                availablePlayer.MidiName = audioResource.Name;
                availablePlayer.Load(midiData);
                // Reset/apply the per-sound loop flag (pool players are reused, so set it every time).
                availablePlayer.Play(loop: audioResource.IsLooping);
                _activeSfxPlayers[audioResource.Id] = availablePlayer;
                _logger.LogInformation("Playing SFX: {SfxName} (ID: {SfxId}) on {PlayerName}", audioResource.Name, audioResource.Id, availablePlayer.name);
            } else {
                _logger.LogWarning("No available SFX MIDI player for {SfxName} (ID: {SfxId}). Pool size {PoolSize} might be too small.", audioResource.Name, audioResource.Id, _sfxPlayers.Count);
            }
        }

        /// <summary>
        /// Play a digitised sample through an ordinary AudioSource.
        /// </summary>
        /// <remarks>
        /// <b>Not through a MidiSynthPlayer</b>, which has nothing to do with a PCM buffer. The
        /// samples come from <see cref="GameData.Resources.Audio.WavePcm"/>, which reads back the
        /// RIFF wrapper the extractor writes -- mono 8-bit unsigned PCM at the resource's own rate.
        ///
        /// <para>One pooled AudioSource, created on demand: these are short one-shots and
        /// PlayOneShot mixes them, so a second sample does not cut the first off.</para>
        /// </remarks>
        private void PlaySample(AudioResource audioResource, byte[] wavData) {
            GameData.Resources.Audio.WavePcm.Pcm? decoded =
                GameData.Resources.Audio.WavePcm.Decode(wavData);
            if (decoded is not { } pcm || pcm.Samples.Length == 0) {
                _logger.LogWarning(
                    "Audio resource {AudioResourceName} (ID: {AudioId}) carries a sample this build cannot decode.",
                    audioResource.Name, audioResource.Id);

                return;
            }

            if (_sampleSource == null) {
                _sampleSource = gameObject.AddComponent<AudioSource>();
                _sampleSource.playOnAwake = false;
            }

            AudioClip clip = AudioClip.Create(audioResource.Name ?? audioResource.Id.ToString(),
                pcm.Samples.Length / pcm.Channels, pcm.Channels, pcm.SampleRate, stream: false);
            clip.SetData(pcm.Samples, 0);
            _sampleSource.PlayOneShot(clip);
            _logger.LogInformation("Playing SAMPLE: {SfxName} (ID: {SfxId}), {Samples} samples at {Rate} Hz",
                audioResource.Name, audioResource.Id, pcm.Samples.Length, pcm.SampleRate);
        }

        /// <summary>The one AudioSource digitised samples are mixed through.</summary>
        private AudioSource _sampleSource;

        /// <summary>
        /// Whether the SFX with this id is still sounding.
        /// </summary>
        /// <remarks>
        /// <b>Exists so a repeated cue can be SEQUENCED rather than stacked.</b> An item's use sound
        /// carries an extra-repeat count (the Armorer's Hammer strikes three times), and firing
        /// <c>PlaySfx</c> three times in a frame takes three pool players and plays them together —
        /// one louder strike, not three. Asking when the last one finished is what turns that into
        /// a rhythm.
        ///
        /// <para>False for an id that was never played, and for one whose pool player has been
        /// recycled by another sound — both mean "not still going", which is the question.</para>
        /// </remarks>
        public bool IsSfxPlaying(string soundId) =>
            soundId != null
            && _activeSfxPlayers.TryGetValue(soundId, out MidiSynthPlayer player)
            && player != null
            && player.IsPlaying;

        public void StopSound(string soundIdToStop) {
            _logger.LogDebug("Attempting to stop sound with ID: {SoundId}", soundIdToStop);

            if (_currentSongId == soundIdToStop) {
                _logger.LogInformation("Stopping song: ID {SongId} (Current MIDI Name: {MidiName})", _currentSongId, _songPlayer.MidiName);
                _songPlayer.Stop();
                _currentSongId = null;
                _currentTrack = MusicPlayback.NoTrack;

                return;
            }

            if (_activeSfxPlayers.TryGetValue(soundIdToStop, out MidiSynthPlayer sfxPlayerToStop)) {
                _logger.LogInformation("Stopping SFX: ID {SfxId} (Player MIDI Name: {MidiName}) on player {PlayerName}", soundIdToStop, sfxPlayerToStop.MidiName, sfxPlayerToStop.name);
                sfxPlayerToStop.Stop();
                _activeSfxPlayers.Remove(soundIdToStop);

                return;
            }

            _logger.LogDebug("Sound ID {SoundId} not found playing as current song or active SFX. No action taken.", soundIdToStop);
        }

        /// <summary>
        /// Change the background music by track id — <c>audio_music_play</c>
        /// (<c>SRC/AUDIO/ENGINE/AUDIO.C</c>), the form every context in the original calls.
        /// </summary>
        /// <param name="trackId">
        /// The track wanted, <see cref="MusicPlayback.NoTrack"/> for silence, or
        /// <see cref="MusicPlayback.QueryOnly"/> to read the current track without changing it.
        /// </param>
        /// <returns>
        /// <b>What was playing before</b> — returned in every case, including when nothing changes.
        /// That return value is the whole point: a screen stashes it on entry and passes it back
        /// here on exit to restore the world's music. A version of this that returned void could not
        /// express the idiom.
        /// </returns>
        /// <remarks>
        /// Re-requesting the track already playing is a no-op, not a restart, so re-entering a zone
        /// whose music is already going does not stutter it at the boundary.
        /// </remarks>
        public async UniTask<int> PlayTrackAsync(int trackId, IResourceProviderService resources,
            object owner = null) {
            MusicPlayback.MusicChange change = MusicPlayback.Resolve(trackId, _currentTrack);

            switch (change.Action) {
                case MusicPlayback.MusicAction.Stop:
                    await FadeOutAsync();
                    StopMusic();

                    break;
                case MusicPlayback.MusicAction.Switch:
                    var song = await resources.LoadAssetAsync<AudioResource>(
                        trackId.ToString(), owner ?? this);

                    if (song == null) {
                        _logger.LogWarning("No audio resource for music track {TrackId}.", trackId);
                    } else {
                        // Fade the outgoing track before the new one starts. The load happens first
                        // so the gap between them is the fade and not the disk.
                        await FadeOutAsync();
                        PlaySong(song);
                    }

                    break;
            }

            return change.PreviousTrack;
        }

        /// <summary>
        /// Ramp the outgoing song to silence over <see cref="MusicPlayback.FadeSeconds"/>, then
        /// restore the player's volume so the next song starts at full.
        /// </summary>
        /// <remarks>
        /// The original hands a rate to the sound driver and waits a fixed number of ticks for it;
        /// <see cref="MusicPlayback.FadeRate"/> is a DOS driver value with no meaning here, so what
        /// is reproduced is the observable part — the outgoing track takes about that long to reach
        /// silence instead of being cut off.
        ///
        /// <para>Returns immediately when nothing is playing, which is what keeps the first track of
        /// a session from waiting through a fade of silence.</para>
        /// </remarks>
        private async UniTask FadeOutAsync() {
            if (_songPlayer == null || !MusicPlayback.NeedsFadeOut(_currentTrack)) {
                return;
            }

            float startVolume = _songPlayer.Volume;
            double elapsed = 0;
            double total = MusicPlayback.FadeSeconds;

            while (elapsed < total) {
                elapsed += Time.unscaledDeltaTime;
                _songPlayer.Volume =
                    startVolume * (float)MusicPlayback.FadeVolumeFractionAt(elapsed);
                await UniTask.Yield();
            }

            _songPlayer.Volume = startVolume;   // the next song starts at full volume
        }

        /// <summary>Stop the currently-playing background song (SFX are left alone). Mirrors the
        /// original's <c>audio_music_play(-1)</c>, which the zone load issues for every zone that
        /// has no music of its own — which is most of them; see
        /// <see cref="MusicSelection.ForZone"/>.</summary>
        public void StopMusic() {
            if (_songPlayer == null) {
                return;
            }
            _logger.LogInformation("Stopping background music (song ID {SongId}).", _currentSongId);
            _songPlayer.Stop();
            _currentSongId = null;
            _currentTrack = MusicPlayback.NoTrack;
        }

        public void StopAllSounds()
        {
            _logger.LogInformation("Stopping all sounds.");
            _songPlayer.Stop();
            _currentSongId = null;
            _currentTrack = MusicPlayback.NoTrack;

            IEnumerable<MidiSynthPlayer> activePlayers = _sfxPlayers.Where(sfxPlayer => sfxPlayer.IsPlaying);
            foreach (MidiSynthPlayer sfxPlayer in activePlayers) {
                sfxPlayer.Stop();
            }
            _activeSfxPlayers.Clear();
        }
    }

    internal enum SoundDriver : byte {
        SoundBlaster = 0x00,
        GeneralMidi = 0x07,
        MT32 = 0x0C,
        PcSpeaker = 0x12,
        Tandy = 0x13
    }
}
namespace BakAgain.Audio {
    using System;
    using BakAgain.Core;
    using UnityEngine;

    /// <summary>
    /// The music and sound levels the audio plays at now (TASK-1145). Read from
    /// <see cref="GameOptions"/>; the settings screen moves them while the player chooses, so the
    /// choice can be heard, and puts them back with <see cref="ResetToSaved"/> on Cancel.
    /// </summary>
    public static class AudioLevels {
        private static float? _music, _sound;

        /// <summary>Raised when either level changes.</summary>
        public static event Action Changed;

        public static float Music {
            get => _music ??= GameOptions.MusicVolume;
            set => Set(ref _music, value);
        }

        public static float Sound {
            get => _sound ??= GameOptions.SoundVolume;
            set => Set(ref _sound, value);
        }

        /// <summary>Back to the saved levels.</summary>
        public static void ResetToSaved() {
            _music = GameOptions.MusicVolume;
            _sound = GameOptions.SoundVolume;
            Changed?.Invoke();
        }

        private static void Set(ref float? level, float value) {
            level = Mathf.Clamp01(value);
            Changed?.Invoke();
        }
    }
}

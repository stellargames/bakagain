namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Audio;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.TestSupport;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>The Audio page's sliders are heard while they move (owner, 2026-11-26): the music at once,
    /// a sample effect when the sound slider is let go; Cancel puts the levels back; a song the page starts
    /// for listening stops whenever the page is left.</summary>
    public class EnhancedSettingsAudioTests : EnhancedSettingsBookFixture {
        [TearDown]
        public void Levels() => AudioLevels.ResetToSaved();

        private Slider SliderOf(string id) => Root.Q<Slider>(id + "-slider");

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator MovingTheMusicSliderChangesTheLiveLevel() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-audio");
                SliderOf("music_volume").value = 0.3f;
                await Frames(1);
                Assert.AreEqual(0.3f, AudioLevels.Music, 0.001f, "heard at once");
                Assert.AreEqual(1f, GameOptions.MusicVolume, 0.001f, "not saved");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator LettingGoOfTheSoundSliderPlaysTheSample() => UniTask.ToCoroutine(async () => {
            var cues = new List<int>();
            void Heard(int id) => cues.Add(id);
            MenuSoundService.CueRequested += Heard;
            try {
                await OpenAsync();
                await ClickAsync("bookmark-audio");
                Slider slider = SliderOf("sound_volume");
                slider.value = 0.5f;
                Assert.IsEmpty(cues, "not on every step");
                using (var e = PointerCaptureOutEvent.GetPooled(slider, null, PointerId.mousePointerId)) {
                    slider.SendEvent(e);
                }
                await Frames(1);
                CollectionAssert.AreEqual(new[] { MenuSoundService.PoundSoundId }, cues);
                Assert.AreEqual(0.5f, AudioLevels.Sound, 0.001f);
            } finally {
                MenuSoundService.CueRequested -= Heard;
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator CancelPutsTheLevelsBack() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-audio");
                SliderOf("music_volume").value = 0.2f;
                SliderOf("sound_volume").value = 0.4f;
                await ClickAsync("cancel");
                Assert.AreEqual(GameOptions.MusicVolume, AudioLevels.Music, 0.001f);
                Assert.AreEqual(GameOptions.SoundVolume, AudioLevels.Sound, 0.001f);
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Review Focus 3: with Game Music off, the page plays a song to listen to, and nothing it
        /// started is left playing by any way out.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ThePreviewSongStopsWhenTheAudioPageCloses([Values("page", "ok", "cancel")] string exit) =>
            UniTask.ToCoroutine(async () => {
                var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
                if (scope == null) Assert.Ignore("no LifetimeScope");
                var prefs = (IPreferencesService)scope.Container.Resolve(typeof(IPreferencesService));
                Preferences saved = prefs.Current.Clone();
                Preferences off = saved.Clone();
                off.GameMusic = false;
                MidiPlaybackManager midi = MidiPlaybackManager.Instance;
                try {
                    prefs.Apply(off);
                    await OpenAsync();
                    await ClickAsync("bookmark-audio");
                    for (int i = 0; i < 300 && midi.SongPlaying == null; i++) await Frames(1);
                    Assert.IsNotNull(midi.SongPlaying, "a song to listen to, with Game Music off");
                    switch (exit) {
                        case "page": await ClickAsync("bookmark-display"); break;
                        case "ok": await ClickAsync("ok"); break;
                        default: await ClickAsync("cancel"); break;
                    }
                    await Frames(2);
                    Assert.IsNull(midi.SongPlaying, $"left by {exit}: the listening song stops");
                } finally {
                    await CloseAsync();
                    prefs.Apply(saved);
                }
            });

        /// <summary>Owner, 2026-11-26: each volume slider shows its level as a percentage beside it.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ASliderShowsItsPercentage() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-audio");
                SliderOf("music_volume").value = 0.3f;
                await Frames(1);
                Assert.AreEqual("30%", Root.Q<Label>("music_volume-percent")?.text);
                SliderOf("sound_volume").value = 1f;
                await Frames(1);
                Assert.AreEqual("100%", Root.Q<Label>("sound_volume-percent")?.text);
            } finally {
                await CloseAsync();
            }
        });
    }
}

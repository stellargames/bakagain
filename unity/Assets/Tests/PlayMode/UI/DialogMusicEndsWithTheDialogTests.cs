namespace BakAgain.Tests.PlayMode.UI {
    using System;
    using System.Collections;
    using System.Reflection;
    using BakAgain.Audio;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Audio;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A song a dialog starts ends with the dialog: the track that was playing when it opened comes
    /// back (forum p=43, Philip's encounter music looping on the travel screen).
    /// </summary>
    /// <remarks>
    /// The original saves the track as the conversation opens (DIALOG.C:836,
    /// <c>audio_music_play(-999)</c>) and plays it again as it closes (DIALOG.C:1491), which fades
    /// the dialog's song out. The play is driven through the real private <c>Playing</c> bracket by
    /// reflection; the songs are bare <see cref="AudioResource"/>s, which the manager records as
    /// the current track before it finds they hold no MIDI.
    /// </remarks>
    public class DialogMusicEndsWithTheDialogTests {
        private sealed class BareSongs : IResourceProviderService {
            public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
                UniTask.FromResult(new AudioResource(key.ToString()) as T);

            public void ReleaseAssets(object owner) { }
        }

        private const int WorldTrack = 1005, DialogTrack = 1007;

        [UnityTest]
        public IEnumerator TheTrackPlayingWhenTheDialogOpenedComesBackWhenItEnds() =>
            UniTask.ToCoroutine(() => Run(WorldTrack));

        [UnityTest]
        public IEnumerator ADialogSongOverSilenceStopsWhenTheDialogEnds() =>
            UniTask.ToCoroutine(() => Run(MusicPlayback.NoTrack));

        /// <summary>
        /// Forum p=51: a fight's song came back and looped after a later shop. The fight's end
        /// fades its song out (1.5 s) and its outcome line opens a dialog inside that fade. The
        /// original cannot be asked in between, its fade blocks (AUDIO.C:111-113), so the dialog
        /// must find silence there, not the song on its way out.
        /// </summary>
        [UnityTest]
        public IEnumerator ADialogOpenedWhileTheFightsSongFadesOutDoesNotBringItBack() =>
            UniTask.ToCoroutine(async () => {
                using var notes = TestSupport.ExpectedLog.CaptureAll(
                    new System.Text.RegularExpressions.Regex("does not contain variant"));
                var midiGo = new GameObject("Music");
                var dialogGo = new GameObject("Dialogs");
                try {
                    MidiPlaybackManager midi = MidiPlaybackManager.Instance;
                    if (midi == null) {
                        midi = midiGo.AddComponent<MidiPlaybackManager>();
                        await UniTask.Yield();
                    }
                    var songs = new BareSongs();
                    midi.StopMusic();
                    await midi.PlayTrackAsync(DialogTrack, songs);
                    Assert.AreEqual(DialogTrack, midi.CurrentTrack, "control: the fight's song plays");

                    // The fight ends: back to the silence it found. Not awaited, as WorldRuntime does.
                    midi.PlayTrackAsync(MusicPlayback.NoTrack, songs).Forget();

                    DialogManager dialogs = dialogGo.AddComponent<DialogManager>();
                    typeof(DialogManager).GetField("_executor", BindingFlags.NonPublic | BindingFlags.Instance)
                        .SetValue(dialogs, new DialogExecutor(null, null, null, songs));
                    MethodInfo playing = typeof(DialogManager).GetMethod("Playing",
                        BindingFlags.NonPublic | BindingFlags.Instance, null,
                        new[] { typeof(Func<UniTask>), typeof(string) }, null);
                    // The outcome line, read for longer than the fade lasts.
                    Func<UniTask> outcome = () => UniTask.Delay(
                        TimeSpan.FromSeconds(MusicPlayback.FadeSeconds + 0.5), ignoreTimeScale: true);
                    await (UniTask)playing.Invoke(dialogs, new object[] { outcome, "test" });

                    float deadline = Time.realtimeSinceStartup + (float)MusicPlayback.FadeSeconds + 3f;
                    while (Time.realtimeSinceStartup < deadline) {
                        await UniTask.Yield();
                    }
                    Assert.AreEqual(MusicPlayback.NoTrack, midi.CurrentTrack,
                        "the fight's song came back after a dialog opened during its fade (forum p=51)");
                    midi.StopMusic();
                } finally {
                    UnityEngine.Object.DestroyImmediate(dialogGo);
                    UnityEngine.Object.DestroyImmediate(midiGo);
                }
            });

        private static async UniTask Run(int before) {
            // The bare songs hold no MIDI, which the manager notes for each one.
            using var notes = TestSupport.ExpectedLog.CaptureAll(
                new System.Text.RegularExpressions.Regex("does not contain variant"));
            var midiGo = new GameObject("Music");
            var dialogGo = new GameObject("Dialogs");
            try {
                // The dialog restores through the singleton, so drive that one: a booted game in
                // this run may already own it, and a second manager destroys itself in Awake.
                MidiPlaybackManager midi = MidiPlaybackManager.Instance;
                if (midi == null) {
                    midi = midiGo.AddComponent<MidiPlaybackManager>();
                    await UniTask.Yield(); // Start builds the song player
                }
                Assert.AreSame(midi, MidiPlaybackManager.Instance, "control: the manager the dialog restores through");
                var songs = new BareSongs();
                midi.StopMusic();
                if (before != MusicPlayback.NoTrack) {
                    await midi.PlayTrackAsync(before, songs);
                }
                Assert.AreEqual(before, midi.CurrentTrack, "control: the world's track before the dialog");

                DialogManager dialogs = dialogGo.AddComponent<DialogManager>();
                var executor = new DialogExecutor(null, null, null, songs);
                typeof(DialogManager).GetField("_executor", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(dialogs, executor);
                MethodInfo playing = typeof(DialogManager).GetMethod("Playing",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(Func<UniTask>), typeof(string) }, null);
                Assert.IsNotNull(playing, "Playing brackets every whole dialog play");

                Func<UniTask> play = async () => {
                    await midi.PlayTrackAsync(DialogTrack, songs);
                    Assert.AreEqual(DialogTrack, midi.CurrentTrack, "control: the dialog's song plays");
                };
                await (UniTask)playing.Invoke(dialogs, new object[] { play, "test" });

                // The restore fades the dialog's song out first (MusicPlayback.FadeSeconds, ~1.5 s).
                float deadline = Time.realtimeSinceStartup + (float)MusicPlayback.FadeSeconds + 3f;
                while (midi.CurrentTrack != before && Time.realtimeSinceStartup < deadline) {
                    await UniTask.Yield();
                }
                Assert.AreEqual(before, midi.CurrentTrack,
                    "the dialog's song must end with the dialog and the track it found come back "
                    + "(DIALOG.C:1491); it kept looping on the travel screen");
                midi.StopMusic();
            } finally {
                UnityEngine.Object.DestroyImmediate(dialogGo);
                UnityEngine.Object.DestroyImmediate(midiGo);
            }
        }
    }
}

namespace BakAgain.Core.Services {
    using BakAgain.Book;
    using BakAgain.Core.States;
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using BakAgain.UI.FullMap;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using Microsoft.Extensions.Logging;
    using System;

    /// <summary>
    /// Plays a chapter's scenes from the ChapterCatalog: the intro animation once, then the
    /// selected parts (each: book then animation, skippable). Mode StartOnly = intro + Parts[0]
    /// (chapter start = part 1); Full = intro + all parts (adds the end, part 2). New-game uses
    /// StartOnly; Contents replay uses StartOnly for the current chapter and Full only once you've
    /// moved past it. Cancelling a scene (ESC) does not abort the sequence — matching the original.
    ///
    /// An awaited script, not a state (2026-07-12 architecture doc §3.1.1): it pushes the cutscene
    /// screen once for the whole sequence (and the book screen over it per book part), so whatever
    /// was showing beneath — Contents during a replay, nothing during new-game — is restored by
    /// the navigator when the sequence pops. Replaces ChapterScenesState + its OnComplete relay.
    /// </summary>
    public sealed class ChapterScenesPlayer {
        private readonly ILogger<ChapterScenesPlayer> _logger;
        private readonly IFullMapView _fullMap;
        private readonly IDialogManager _dialogs;

        /// <summary><c>gmain_cutsc_play_fullmap_scene</c> (GMAIN.C:409) plays record
        /// <c>chapter + 0x186ab5</c>: the chapter's summary over the full map.</summary>
        private const int ChapterSummaryDialogBase = 0x186ab5;
        private readonly ICutscenePresenter _presenter;
        private readonly ICutsceneView _view;
        private readonly IBookPresenter _bookPresenter;
        private readonly IBookView _bookView;
        private readonly IResourceProviderService _resources;
        private readonly IScreenNavigator _navigator;

        // Optional so a test can build the player without an audio stack; a null one simply plays
        // no chapter track, which is the behaviour that existed before this was wired.
        private readonly BakAgain.Audio.MidiPlaybackManager _music;

        // CHAPSONG.DAT, loaded once per player. Null when it will not load — the chapter still
        // plays, on whatever music was already running, which is what happened before.
        private GameData.Resources.Audio.ChapterSongMap _songs;
        private bool _songsLoaded;

        private async Cysharp.Threading.Tasks.UniTask<GameData.Resources.Audio.ChapterSongMap>
            ChapterSongsAsync() {
            if (_songsLoaded) {
                return _songs;
            }

            _songsLoaded = true;
            try {
                _songs = await _resources.LoadAssetAsync<GameData.Resources.Audio.ChapterSongMap>(
                    "CHAPSONG.DAT", this);
            } catch (Exception e) {
                _logger.LogWarning(
                    "ChapterScenesPlayer: CHAPSONG.DAT did not load ({Message}); chapter books will "
                    + "keep whatever music is playing.", e.Message);
            }
            return _songs;
        }

        public ChapterScenesPlayer(ILogger<ChapterScenesPlayer> logger, ICutscenePresenter presenter,
            ICutsceneView view, IBookPresenter bookPresenter, IBookView bookView,
            IResourceProviderService resources, IScreenNavigator navigator,
            IFullMapView fullMap, IDialogManager dialogs,
            BakAgain.Audio.MidiPlaybackManager music = null) {
            _fullMap = fullMap;
            _dialogs = dialogs;
            _music = music;
            _logger = logger;
            _presenter = presenter;
            _view = view;
            _bookPresenter = bookPresenter;
            _bookView = bookView;
            _resources = resources;
            _navigator = navigator;
        }

        /// <summary>
        /// Play chapter <paramref name="chapterNr"/>'s scenes. Defensive: a playback error must
        /// never leave the pushed screens stranded, so the pops run in finally blocks and the
        /// method always completes (callers continue their flow).
        /// </summary>
        public async UniTask PlayAsync(int chapterNr, ChapterScenesMode mode) {
            _logger.LogInformation("Playing chapter scenes: chapter {Chapter}, mode {Mode}.", chapterNr, mode);
            try {
                if (_presenter == null || _view == null) {
                    _logger.LogError("ChapterScenesPlayer: cutscene presenter/view not injected; skipping scenes.");
                    return;
                }
                ChapterCatalog catalog =
                    await _resources.LoadAssetAsync<ChapterCatalog>(ChapterCatalog.ResourceId, this);
                Chapter chapter = catalog?.Chapters.Find(c => c.Number == chapterNr);
                if (chapter == null) {
                    _logger.LogError("ChapterScenesPlayer: chapter {Chapter} not in catalog; no scenes played.", chapterNr);
                    return;
                }

                (int first, int count) = ChapterScenes.PartRange(chapter, mode);
                bool intro = ChapterScenes.PlaysIntro(mode) && !string.IsNullOrEmpty(chapter.IntroAnimation);
                if (count == 0 && !intro) {
                    return;   // a one-part chapter's close: nothing to show, so no screen either
                }

                // One cutscene-screen push for the whole sequence, so the surface beneath
                // (Contents during a replay) doesn't flash back between parts.
                await _navigator.Push((IScreen)_view);
                try {
                    if (intro) {
                        await _presenter.PlayCutsceneAsync(chapter.IntroAnimation, _view);
                    }
                    GameData.Resources.Audio.ChapterSongMap songs = await ChapterSongsAsync();
                    for (int i = first; i < first + count; i++) {
                        if (mode == ChapterScenesMode.Full && i == 1) {
                            await ShowChapterSummaryAsync(chapterNr);
                        }
                        ChapterPart part = chapter.Parts[i];
                        // *** THE CHAPTER BOOK OPENS WITH ITS OWN TRACK -- EVEN WHEN THERE IS NO BOOK. ***
                        // gmain_play_chapter_intro reads CHAPSONG.DAT and starts the song
                        // BEFORE showing C<chapter><part>.BOK. Nothing did that here, so the
                        // chapter intros ran on whatever was already playing.
                        //
                        // The loop index IS the original's `part`: it composes the book name as
                        // C + chapter + part, which is why part 1 is C11.BOK and lands at i = 0.
                        //
                        // gmain_play_chapter_intro starts it before bookview_show, which simply fails
                        // when the part has no book (chapter 8's close plays 1025 over C82.ADS alone).
                        //
                        // -999 passes straight through — ChapterSongMap.NoChange and
                        // MusicPlayback.QueryOnly are the same sentinel, so the four chapters
                        // that leave the music alone for their second book need no special case.
                        if (_music != null) {
                            await _music.PlayTrackAsync(
                                GameData.Resources.Audio.MusicSelection.ForChapterBook(
                                    songs, chapterNr, i + 1),
                                _resources, this);
                        }
                        if (!string.IsNullOrEmpty(part.Book)) {
                            await _navigator.Push((IScreen)_bookView);
                            try {
                                await _bookPresenter.ShowBookAsync(part.Book);
                            } finally {
                                await _navigator.Pop();
                            }
                        }
                        if (!string.IsNullOrEmpty(part.Animation)) {
                            await _presenter.PlayCutsceneAsync(part.Animation, _view);
                        }
                    }
                } finally {
                    await _navigator.Pop();
                }
            } catch (Exception e) {
                _logger.LogError(e, "ChapterScenesPlayer: error playing chapter {Chapter} scenes; continuing.", chapterNr);
            } finally {
                _resources.ReleaseAssets(this);
            }
        }

        /// <summary>
        /// A finished chapter's Contents replay puts its summary over the full map between the
        /// opening and the close (GMAIN.C:644-647 → <c>gmain_cutsc_play_fullmap_scene</c>). The
        /// original draws FULLMAP.SCX with no party marker.
        /// </summary>
        private async UniTask ShowChapterSummaryAsync(int chapterNr) {
            if (_fullMap == null || _dialogs == null) {
                return;
            }
            _fullMap.SetMarker(false, 0, 0, 0);
            _fullMap.SetExitAffordance(null);
            await _navigator.Push(_fullMap);
            try {
                await _dialogs.ShowById(ChapterSummaryDialogBase + chapterNr);
            } finally {
                await _navigator.Pop();
            }
        }
    }
}

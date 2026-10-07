namespace BakAgain.Book {
    using GameData.Resources.Audio;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Book;
    using GameData.Resources.Palette;
    using Microsoft.Extensions.Logging;
    using UnityEngine;

    public class BookPresenter {
        // Parchment background variants synthesized by the extractor (see BookParchment):
        // even pages render BOOK.SCX as-is, odd pages render it vertically mirrored.
        private const string BookEvenBackground = "BOOK_EVEN.SCX";
        private const string BookOddBackground = "BOOK_ODD.SCX";

        private readonly BookView _view;
        private readonly BakAgain.UI.InputCore.InputLayerStack _stack;
        private readonly IResourceCache _resourceCache;
        private readonly BakAgain.ResourceManagement.IResourceProviderService _resources;
        private readonly BakAgain.Audio.MidiPlaybackManager _midi;
        private readonly ILogger<BookPresenter> _logger;
        private bool _cancelled;
        // Set by the page layer (Activate=forward, Cancel=cancel, MoveFocus Left/Right=back/forward).
        private PageNavigation? _navResult;

        public BookPresenter(
            BookView view,
            BakAgain.UI.InputCore.InputLayerStack stack,
            IResourceCache resourceCache,
            BakAgain.ResourceManagement.IResourceProviderService resources,
            BakAgain.Audio.MidiPlaybackManager midi,
            ILogger<BookPresenter> logger
        ) {
            _view = view;
            _stack = stack;
            _resourceCache = resourceCache;
            _resources = resources;
            _midi = midi;
            _logger = logger;
        }

        public async UniTask<bool> ShowBookAsync(string bokFileName) {
            _cancelled = false;

            // 1. Load book resource
            _logger.LogInformation("Loading book {FileName}.", bokFileName);
            var book = await _resourceCache.GetOrLoadAsync<BookResource>(bokFileName);
            if (book == null || book.Pages.Count == 0) {
                _logger.LogError("Failed to load book {FileName}.", bokFileName);
                return false;
            }

            // 2. Load palette
            var palette = await _resourceCache.GetOrLoadAsync<PaletteResource>("BOOK.PAL");
            UnityEngine.Color[] colors;
            if (palette != null) {
                colors = palette.Colors.Select(color => new UnityEngine.Color(
                    color.R / 255f, color.G / 255f, color.B / 255f
                )).ToArray();
            } else {
                _logger.LogWarning("Failed to load BOOK.PAL, using default palette.");
                colors = new UnityEngine.Color[256];
                for (int i = 0; i < 256; i++) {
                    colors[i] = UnityEngine.Color.white;
                }
            }

            // 3. The parchment background is loaded per page below: the DOS engine mirrors
            // BOOK.SCX vertically for odd page numbers (bok_DrawPage), which the extractor
            // surfaces as BOOK_EVEN.SCX (as-is) and BOOK_ODD.SCX (flipped).

            // 4. Load book sprites from BOOK.BMX
            int maxImageNumber = book.Pages
                .SelectMany(p => p.Images)
                .Select(img => img.ImageNumber)
                .DefaultIfEmpty(-1)
                .Max();

            var sprites = new Sprite[maxImageNumber + 1];
            for (int i = 0; i <= maxImageNumber; i++) {
                sprites[i] = await _resourceCache.GetOrLoadAsync<Sprite>($"BOOK.BMX#{i}");
            }

            // 5. Collect all paragraphs from all pages into a single text stream.
            // The original game stores all text contiguously after page 1's header
            // and dynamically paginates during rendering (bok_DrawPage at 0x4d4d4).
            var allParagraphs = new List<Paragraph>();
            foreach (var page in book.Pages) {
                allParagraphs.AddRange(page.Paragraphs);
            }

            // 6. Set up view (the parchment background is set per page below, by parity)
            _view.Show();

            // Push an Exclusive page-nav layer: Activate=forward, Cancel=cancel, MoveFocus Left/Prev=
            // back, Right/Next=forward. Replaces the old IInputHandler + Keyboard.current poll.
            // Any other key turns the page too: bookview_show breaks on any scancode or mouse button
            // (BOOKVIEW.C:199-205). Back and cancel are the port's additions.
            var pageLayer = new BakAgain.UI.InputCore.ActionLayer(
                "book",
                () => _navResult = PageNavigation.Forward,
                () => _navResult = PageNavigation.Cancel,
                dir => _navResult = (dir == BakAgain.UI.InputCore.NavDirection.Left
                                     || dir == BakAgain.UI.InputCore.NavDirection.Previous)
                    ? PageNavigation.Backward
                    : PageNavigation.Forward,
                anyIntentActivates: true);
            _stack.Push(pageLayer);
            try {

            // 7. Page navigation loop with dynamic text pagination
            var currentPage = book.Pages[0];
            int paragraphIndex = 0;
            int lineOffset = 0;
            bool completed = false;

            // History stack for backward navigation (stores state *before* the current page)
            var history = new List<(Page page, int paragraphIndex, int lineOffset)>();

            while (currentPage != null && !_cancelled) {
                _logger.LogInformation("Showing page {PageNumber} (paragraph {Index}/{Total}, line {LineOffset}).",
                    currentPage.PageNumber, paragraphIndex, allParagraphs.Count, lineOffset);

                // Parchment background by page-number parity: even pages use BOOK.SCX as-is,
                // odd pages use the vertically-mirrored variant (faithful to the DOS engine).
                // Falls back to plain BOOK.SCX if the variants aren't available.
                string backgroundId = currentPage.PageNumber % 2 == 0 ? BookEvenBackground : BookOddBackground;
                var background = await _resourceCache.GetOrLoadAsync<Sprite>(backgroundId)
                                 ?? await _resourceCache.GetOrLoadAsync<Sprite>("BOOK.SCX");
                if (background != null) {
                    _view.SetBackground(background);
                }

                await StartPageMusicAsync(currentPage);

                // Render page: layout from currentPage, text from allParagraphs[paragraphIndex..]
                var (nextParagraphIndex, nextLineOffset) = _view.ShowPage(
                    currentPage, sprites, colors, allParagraphs, paragraphIndex, lineOffset);

                // Wait for input
                var nav = await WaitForNav();
                if (nav == PageNavigation.Cancel || _cancelled) {
                    break;
                }

                if (nav == PageNavigation.Backward) {
                    if (history.Count > 0) {
                        var prev = history[^1];
                        history.RemoveAt(history.Count - 1);
                        currentPage = prev.page;
                        paragraphIndex = prev.paragraphIndex;
                        lineOffset = prev.lineOffset;
                    }
                    continue; // re-render
                }

                // Forward: save current state to history before advancing
                history.Add((currentPage, paragraphIndex, lineOffset));
                paragraphIndex = nextParagraphIndex;
                lineOffset = nextLineOffset;

                // Navigate to next page
                if (currentPage.NextPageNumber == -2 ||
                    (paragraphIndex >= allParagraphs.Count && lineOffset == 0)) {
                    completed = true;
                    break;
                }

                currentPage = book.Pages.FirstOrDefault(
                    page => page.PageNumber == currentPage.NextPageNumber
                );
            }

            // 8. Cleanup
            _view.Hide();

            if (completed) {
                _logger.LogInformation("Book {FileName} completed.", bokFileName);
            } else {
                _logger.LogInformation("Book {FileName} was skipped.", bokFileName);
            }

            return completed;
            } finally {
                _stack.Remove(pageLayer);
            }
        }

        /// <summary>
        /// Starts the track a page calls for, if it calls for one.
        /// </summary>
        /// <remarks>
        /// <b>Two pages in the whole game change the music, and every other page must LEAVE IT
        /// ALONE.</b> <see cref="MusicSelection.ForBookPage"/> answers QueryOnly for the rest, which
        /// the playback resolver treats as "tell me what is playing" and not as a request — so a
        /// page turn is silent about music rather than stopping it. Mapping the other pages to
        /// NoTrack instead would cut the music dead on every turn.
        ///
        /// <para>Re-requesting a track already playing is a no-op too, so paging back and forth
        /// across one of the two does not restart it.</para>
        /// </remarks>
        private async UniTask StartPageMusicAsync(Page page) {
            if (_midi == null || _resources == null || page == null) {
                return;
            }
            await _midi.PlayTrackAsync(
                MusicSelection.ForBookPage(page.PageDisplayNumber), _resources, owner: this);
        }

        public void Cancel() {
            _cancelled = true;
        }

        private enum PageNavigation { Forward, Backward, Cancel }

        // Wait for the page-nav layer to set a result (Activate/Cancel/MoveFocus → _navResult), or for
        // an external Cancel(). The layer is the sole input source — no device poll here.
        private async UniTask<PageNavigation> WaitForNav() {
            _navResult = null;
            while (!_cancelled && _navResult == null) {
                await UniTask.Yield();
            }
            return _cancelled ? PageNavigation.Cancel : _navResult!.Value;
        }
    }
}

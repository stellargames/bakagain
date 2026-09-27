namespace BakAgain.Core.States.Debug {
    using BakAgain.Book;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using ResourceExtraction;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Test state that skips intro/menu and iterates through all BOK files.
    /// Change BootstrapState to transition here for quick iteration.
    /// </summary>
    public class BookTestState {
        private readonly ILogger<BookTestState> _logger;
        private readonly IBookPresenter _bookPresenter;

        public BookTestState(ILogger<BookTestState> logger, IBookPresenter bookPresenter) {
            _logger = logger;
            _bookPresenter = bookPresenter;
        }

        public async UniTask RunAsync() {
            // Enumerate all BOK files from the game resource dictionary
            var bokFiles = GetAllBokFiles();
            _logger.LogInformation("Entering BookTestState — found {Count} BOK files.", bokFiles.Count);

            for (int i = 0; i < bokFiles.Count; i++) {
                string bokFile = bokFiles[i];
                _logger.LogInformation("Showing book {Index}/{Total}: {FileName}",
                    i + 1, bokFiles.Count, bokFile);

                bool completed = await _bookPresenter.ShowBookAsync(bokFile);
                _logger.LogInformation("Book {FileName} {Result}.",
                    bokFile, completed ? "completed" : "was skipped");
            }

            _logger.LogInformation("BookTestState finished — all books shown.");
        }

        private List<string> GetAllBokFiles() {
            try {
                var provider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
                return provider.GetDictionary().Keys
                    .Where(k => k.EndsWith(".BOK", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k)
                    .ToList();
            } catch (Exception e) {
                _logger.LogError(e, "Failed to enumerate BOK files, falling back to C11.BOK.");
                return new List<string> { "C11.BOK" };
            }
        }
    }
}

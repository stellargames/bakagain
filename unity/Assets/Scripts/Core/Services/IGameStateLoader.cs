namespace BakAgain.Core.Services {
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// Hydrates <see cref="GameSession"/> from on-disk game-state files.
    /// Sits between the state machine and the resource layer so that every entry
    /// point into gameplay (new game, load save, future chapter transition) uses
    /// the same code path to populate session state.
    /// </summary>
    public interface IGameStateLoader {
        /// <summary>
        /// Hydrate the session for a fresh game from <c>STARTUP.GAM</c>. The starting
        /// chapter is read from the hydrated state (<c>ChapterNumber</c>) and the matching
        /// <c>CHAPx.DAT</c> record is applied over the template — mirroring the DOS engine's
        /// <c>go_to_chapter</c>. No caller supplies the chapter; the game data does.
        /// </summary>
        /// <returns>True on success. On failure the session is left untouched and the
        /// caller should transition back to the main menu.</returns>
        UniTask<bool> LoadNewGameAsync();

        /// <summary>
        /// Apply one chapter's <c>CHAPx.DAT</c> over the live session, <b>without re-hydrating.</b>
        /// </summary>
        /// <remarks>
        /// The transition half of <c>go_to_chapter</c>. <see cref="LoadNewGameAsync"/> hydrates and
        /// then applies; a chapter CHANGE must only apply, because re-hydrating from STARTUP.GAM
        /// would reset the party, their inventories and every story flag. Chapter 2 is not a new
        /// game.
        /// </remarks>
        UniTask<bool> ApplyChapterStartAsync(int chapterNumber);

        /// <summary>
        /// Hydrate the session from a previously saved game.
        /// </summary>
        /// <param name="saveGameAddressableKey">Addressables key for the save file
        /// (e.g. <c>SAVE01.GAM</c>); will be resolved through the standard provider
        /// chain so the override system applies.</param>
        UniTask<bool> LoadFromSaveAsync(string saveGameAddressableKey);

        /// <summary>
        /// Hydrate the session from a save file at an absolute path on disk —
        /// the path the Restore Game screen produces when the player picks a
        /// slot under <c>Application.persistentDataPath/Saves</c>. Bypasses
        /// Addressables (user saves aren't packed) and runs
        /// <c>SaveGameExtractor</c> directly on the file stream.
        /// </summary>
        /// <param name="filePath">Absolute file-system path to a <c>SAVE*.GAM</c>.</param>
        UniTask<bool> LoadFromFileAsync(string filePath);
    }
}

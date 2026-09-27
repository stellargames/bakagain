namespace BakAgain.Core.Services {
    /// <summary>
    /// One save-game slot in a directory — the row a file-picker entry binds
    /// to. <see cref="DisplayName"/> falls back to the file name when the
    /// in-file save name is empty.
    /// </summary>
    public sealed class SaveSlotInfo {
        /// <summary>The on-disk file name, e.g. <c>SAVE00.GAM</c>.</summary>
        public string FileName { get; set; }

        /// <summary>Absolute path on disk.</summary>
        public string FullPath { get; set; }

        /// <summary>Save name from the header (CP437-decoded).</summary>
        public string DisplayName { get; set; }

        /// <summary>Chapter (1..9) from the header.</summary>
        public int ChapterNumber { get; set; }

        /// <summary>True when the header's version byte matches the engine's
        /// expected value (<c>0x16</c>); false for unreadable / wrong-version
        /// files which the loader should refuse.</summary>
        public bool IsValid { get; set; }

        /// <summary>The <c>SAVE##</c> numeric index, parsed from <see cref="FileName"/>.
        /// Callers address save/delete service calls with this rather than re-parsing.</summary>
        public int SlotIndex { get; set; }
    }

    /// <summary>
    /// A "saved games" directory — the left-pane row of the engine's Restore
    /// Game dialog. The original supports multiple per-playthrough directories
    /// (<c>SAVES.G01</c>, <c>SAVES.G02</c>, …) so the player can keep one set
    /// of slots per game.
    /// </summary>
    public sealed class SaveDirectoryInfo {
        /// <summary>Raw directory name on disk, e.g. <c>dir.G01</c> — the key used
        /// to locate the folder when listing its slots.</summary>
        public string Name { get; set; }

        /// <summary>Player-facing name: the part before the <c>.G##</c> game-number
        /// suffix, uppercased (the original shows DOS 8.3 names) — e.g.
        /// <c>dir.G01</c> → <c>DIR</c>.</summary>
        public string DisplayName { get; set; }

        /// <summary>Absolute path on disk.</summary>
        public string FullPath { get; set; }

        /// <summary>The <c>.G##</c> numeric game number — parsed from <see cref="Name"/>.
        /// Callers that need to allocate the next directory (or address it without
        /// re-parsing the folder name) use this.</summary>
        public int Number { get; set; }
    }
}

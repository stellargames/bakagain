namespace BakAgain.Core.Services {
    using System.Collections.Generic;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// Browses save directories and the <c>SAVE%02d.GAM</c> slots within them.
    /// The Restore Game dialog binds its two file pickers to this service —
    /// left pane is <see cref="ListDirectoriesAsync"/>, right pane is
    /// <see cref="ListSlotsAsync"/> for the currently-selected directory.
    /// <para>
    /// <see cref="SaveRoot"/> is configurable so future preferences-screen
    /// integration can point at a different drive. The default is
    /// <c>Application.persistentDataPath/Saves</c> per the Unity convention.
    /// </para>
    /// </summary>
    public interface ISaveGameDirectoryService {
        /// <summary>The on-disk root that holds the per-playthrough save
        /// directories. <see cref="EnsureRootAsync"/> creates it if missing.</summary>
        string SaveRoot { get; }

        /// <summary>Make sure <see cref="SaveRoot"/> exists; create it on first
        /// run. No-op when the directory is already there.</summary>
        UniTask EnsureRootAsync();

        /// <summary>Make sure at least one <c>&lt;name&gt;.G##</c> save-set directory
        /// exists under <see cref="SaveRoot"/>, seeding <c>SAVES.G01</c> when none
        /// do yet — mirrors the original's <c>MkSaveGameDir</c> @0x6faa1, which
        /// always creates <c>GAMES\SAVES.G01</c> when the save dialog opens.
        /// Returns the default/first save-set directory's name.</summary>
        UniTask<string> EnsureDefaultDirectoryAsync();

        /// <summary>Lowest <c>SAVE##</c> index not already present in
        /// <paramref name="directoryName"/>, in <c>[1,20]</c> (slot 0 is the
        /// auto "Bookmark", never a manual-save target), or <c>-1</c> if all
        /// 20 are taken.</summary>
        int LowestFreeSlotIndex(string directoryName);

        /// <summary>Lowest <c>&lt;name&gt;.G##</c> game number not already used by an
        /// existing save-set directory under <see cref="SaveRoot"/>, in
        /// <c>[1,20]</c>, or <c>-1</c> if all 20 are taken. Mirrors the original's
        /// directory-allocation scan in <c>dialog_SaveGame</c>.</summary>
        int LowestFreeDirectoryNumber();

        /// <summary>Create a new save-set directory named <c>&lt;baseName&gt;.G&lt;nn&gt;</c>
        /// at the lowest free game number (<see cref="LowestFreeDirectoryNumber"/>)
        /// and return its folder name. Returns <c>null</c> when no number is free
        /// or the directory could not be created, so the caller can show the
        /// original's "directory full" / "can't create" DDX messages (147/148).</summary>
        UniTask<string> CreateDirectoryAsync(string baseName);

        /// <summary>True when <paramref name="name"/> is a legal DOS 8.3 base name
        /// for a save-set directory: non-empty, at most 8 characters, every
        /// character a letter or digit, and not a reserved DOS device name
        /// (<c>CON, AUX, NUL, PRN, COM0-9, LPT0-9</c>, case-insensitive).
        /// Mirrors the original's <c>checkDirectoryName</c> @0x6f949.</summary>
        bool IsValidDirectoryName(string name);

        /// <summary>List the per-playthrough save directories under
        /// <see cref="SaveRoot"/>, in stable (name-sorted) order. Empty list on
        /// first run.</summary>
        UniTask<IReadOnlyList<SaveDirectoryInfo>> ListDirectoriesAsync();

        /// <summary>List the <c>SAVE*.GAM</c> slots inside one directory in
        /// stable (file-name-sorted) order — real slots only (a new save is made
        /// by typing a name in the game box, not by a placeholder row). Each
        /// slot's 100-byte header is parsed for the display name + chapter;
        /// entries whose version is wrong land with
        /// <see cref="SaveSlotInfo.IsValid"/> = <c>false</c> so the UI can
        /// show them greyed out.</summary>
        UniTask<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(string directoryName);

        /// <summary>Ensure a save sub-directory exists; returns its full path.</summary>
        UniTask<string> EnsureDirectoryAsync(string directoryName);

        /// <summary>Full path of a slot file: <c>SaveRoot/&lt;dir&gt;/SAVE{index:D2}.GAM</c>.</summary>
        string SlotFullPath(string directoryName, int slotIndex);

        /// <summary>Write the given bytes to <paramref name="fullPath"/> (overwrites).
        /// Returns <c>true</c> on success, <c>false</c> if the write failed (e.g. disk
        /// full or permission denied) so the caller can surface the failure instead of
        /// silently reporting success.</summary>
        UniTask<bool> WriteSlotAsync(string fullPath, byte[] bytes);

        /// <summary>Delete a slot file (no-op if absent).</summary>
        UniTask DeleteSlotAsync(string fullPath);

        /// <summary>Delete a save sub-directory and its contents (no-op if absent).</summary>
        UniTask DeleteDirectoryAsync(string directoryName);
    }
}

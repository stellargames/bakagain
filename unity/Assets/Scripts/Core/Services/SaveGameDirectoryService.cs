namespace BakAgain.Core.Services {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using Microsoft.Extensions.Logging;
    using ResourceExtraction.Extractors;
    using UnityEngine;

    /// <inheritdoc cref="ISaveGameDirectoryService"/>
    public sealed class SaveGameDirectoryService : ISaveGameDirectoryService {
        // Per the original game's naming, "SAVES.G%02d" sub-directories hold
        // "SAVE%02d.GAM" slots. We don't enforce the patterns when listing —
        // hand-renamed dirs are still surfaced — but the defaults follow
        // them so a fresh install matches the DOS layout.
        private const string DefaultRootDirectoryName = "Saves";
        private const string SaveFileSearchPattern = "SAVE*.GAM";

        // A save-set directory is "<name>.G<nn>" (nn = 2-digit game number). The
        // original scans GAMES\*.G?? and shows the part before ".Gnn", uppercased.
        private static readonly Regex SaveSetDirPattern =
            new(@"^(?<name>.+)\.G\d\d$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // A slot file is exactly SAVE<nn>.GAM (2 digits). Enumerating "SAVE*.GAM"
        // and filtering here excludes stray files like "SAVE00 - Copy.GAM".
        private static readonly Regex SaveSlotFilePattern =
            new(@"^SAVE(?<n>\d\d)\.GAM$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // The format string that produces names SaveSlotFilePattern parses back
        // (uppercase "SAVE", 2-digit index, ".GAM") — used by SlotFullPath so
        // freshly-written slots round-trip through ListSlotsAsync.
        private const string SaveSlotFileNameFormat = "SAVE{0:D2}.GAM";
        // The engine hardcodes slot 0's name to this, overriding the header
        // (sub_ovr181_236E @ 0x701eb) — it is the auto-save "Bookmark".
        private const string BookmarkSlotName = "Bookmark";
        private const string BookmarkFileName = "SAVE00.GAM";

        // The original's MkSaveGameDir @0x6faa1 always creates GAMES\SAVES.G01
        // when the save dialog opens (there's no "no directories yet" state in
        // the DOS UI). We seed the same default set name so a fresh install's
        // Save screen has something to select.
        private const string DefaultSaveSetDirectoryName = "SAVES.G01";

        private readonly ILogger<SaveGameDirectoryService> _logger;

        public SaveGameDirectoryService(ILogger<SaveGameDirectoryService> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Default to Unity's per-user persistent data path. The original
            // optional `saveRoot` constructor parameter was dropped because
            // VContainer's reflection injector refuses to resolve string
            // parameters even when a default is provided; if a configurable
            // root is wanted later, expose it via a separate IConfig service
            // or a setter rather than a ctor arg.
            SaveRoot = Path.Combine(Application.persistentDataPath, DefaultRootDirectoryName);
        }

        public string SaveRoot { get; }

        public UniTask EnsureRootAsync() {
            try {
                if (!Directory.Exists(SaveRoot)) {
                    Directory.CreateDirectory(SaveRoot);
                    _logger.LogInformation("Created save root {Path}.", SaveRoot);
                }
            } catch (Exception e) {
                _logger.LogError(e, "Failed to ensure save root {Path}.", SaveRoot);
            }
            return UniTask.CompletedTask;
        }

        public async UniTask<string> EnsureDefaultDirectoryAsync() {
            await EnsureRootAsync();
            try {
                string existing = Directory.EnumerateDirectories(SaveRoot)
                    .Select(Path.GetFileName)
                    .Where(name => SaveSetDirPattern.IsMatch(name))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (existing != null) {
                    return existing;
                }
                Directory.CreateDirectory(Path.Combine(SaveRoot, DefaultSaveSetDirectoryName));
                _logger.LogInformation("Seeded default save directory {Name} under {Path}.",
                    DefaultSaveSetDirectoryName, SaveRoot);
                return DefaultSaveSetDirectoryName;
            } catch (Exception e) {
                _logger.LogError(e, "Failed to ensure default save directory under {Path}.", SaveRoot);
                return DefaultSaveSetDirectoryName;
            }
        }

        // Highest game/slot number the original's dialog_SaveGame allocation
        // scan considers ("##" is a 2-digit DOS suffix, numbers 1..20; slot 0 is the Bookmark).
        private const int MaxAllocationNumber = 20;

        // Reserved DOS device names checkDirectoryName @0x6f949 rejects, regardless
        // of case. COM/LPT take a trailing digit 0-9; CON/AUX/NUL/PRN are exact.
        private static readonly HashSet<string> ReservedDeviceNames =
            new(StringComparer.OrdinalIgnoreCase) {
                "CON", "AUX", "NUL", "PRN",
                "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            };

        public int LowestFreeSlotIndex(string directoryName) {
            string dirPath = Path.Combine(SaveRoot, directoryName ?? string.Empty);
            var used = new HashSet<int>();
            try {
                if (Directory.Exists(dirPath)) {
                    foreach (string file in Directory.EnumerateFiles(dirPath, SaveFileSearchPattern)) {
                        Match m = SaveSlotFilePattern.Match(Path.GetFileName(file));
                        if (m.Success) {
                            used.Add(int.Parse(m.Groups["n"].Value));
                        }
                    }
                }
            } catch (Exception e) {
                _logger.LogWarning(e, "Failed to enumerate slots under {Path} for lowest-free-index.", dirPath);
            }
            // Slot 0 is the auto "Bookmark", never a manual-save target.
            for (int index = 1; index <= MaxAllocationNumber; index++) {
                if (!used.Contains(index)) {
                    return index;
                }
            }
            return -1; // all 20 manual slots taken
        }

        public int LowestFreeDirectoryNumber() {
            var used = new HashSet<int>();
            try {
                if (Directory.Exists(SaveRoot)) {
                    foreach (string p in Directory.EnumerateDirectories(SaveRoot)) {
                        string folder = Path.GetFileName(p);
                        if (SaveSetDirPattern.IsMatch(folder)) {
                            // ".G##" is always the last 4 characters of a match.
                            used.Add(int.Parse(folder.Substring(folder.Length - 2)));
                        }
                    }
                }
            } catch (Exception e) {
                _logger.LogWarning(e, "Failed to enumerate save directories under {Path} for lowest-free-number.", SaveRoot);
            }
            // Game numbers run 1..20: the original's mainmenu_savelist_nextfree_key
            // starts at 1, and its directory scan (mainmenu_save_parse_filename)
            // skips anything outside 1..20, so a .G00 would be invisible there.
            // Existing .G00 dirs are still listed here (ListDirectoriesAsync).
            for (int n = 1; n <= MaxAllocationNumber; n++) {
                if (!used.Contains(n)) {
                    return n;
                }
            }
            return -1; // all 20 game numbers taken
        }

        public async UniTask<string> CreateDirectoryAsync(string baseName) {
            await EnsureRootAsync();
            int n = LowestFreeDirectoryNumber();
            if (n < 0) {
                _logger.LogWarning("No free save-set directory number under {Path} (all {Max} taken).",
                    SaveRoot, MaxAllocationNumber);
                return null;
            }
            string folder = $"{baseName}.G{n:D2}";
            try {
                Directory.CreateDirectory(Path.Combine(SaveRoot, folder));
                _logger.LogInformation("Created save directory {Folder} under {Path}.", folder, SaveRoot);
                return folder;
            } catch (IOException e) {
                _logger.LogError(e, "Failed to create save directory {Folder} under {Path}.", folder, SaveRoot);
                return null;
            }
        }

        public bool IsValidDirectoryName(string name) =>
            !string.IsNullOrEmpty(name)
            && name.Length <= 8
            && name.All(char.IsLetterOrDigit)
            && !ReservedDeviceNames.Contains(name);

        public async UniTask<IReadOnlyList<SaveDirectoryInfo>> ListDirectoriesAsync() {
            await EnsureRootAsync();
            try {
                var result = new List<SaveDirectoryInfo>();
                foreach (string p in Directory.EnumerateDirectories(SaveRoot)
                             .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)) {
                    string folder = Path.GetFileName(p);
                    Match m = SaveSetDirPattern.Match(folder);
                    if (!m.Success) {
                        continue; // not a "<name>.G##" save-set directory
                    }
                    // No Bookmark (SAVE00.GAM) gate: a freshly-seeded, empty
                    // save-set (see EnsureDefaultDirectoryAsync) must still be
                    // listed so a fresh install has something to select.
                    // ".G##" is always the last 4 characters of a match.
                    int number = int.Parse(folder.Substring(folder.Length - 2));
                    result.Add(new SaveDirectoryInfo {
                        Name = folder,
                        DisplayName = m.Groups["name"].Value.ToUpperInvariant(),
                        FullPath = p,
                        Number = number,
                    });
                }
                return result;
            } catch (Exception e) {
                _logger.LogError(e, "Failed to enumerate save directories under {Path}.", SaveRoot);
                return Array.Empty<SaveDirectoryInfo>();
            }
        }

        public async UniTask<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(string directoryName) {
            if (string.IsNullOrWhiteSpace(directoryName)) {
                return Array.Empty<SaveSlotInfo>();
            }
            await EnsureRootAsync();
            string dirPath = Path.Combine(SaveRoot, directoryName);
            if (!Directory.Exists(dirPath)) {
                return Array.Empty<SaveSlotInfo>();
            }
            var slots = new List<SaveSlotInfo>();
            foreach (string file in Directory.EnumerateFiles(dirPath, SaveFileSearchPattern)
                                             .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)) {
                Match m = SaveSlotFilePattern.Match(Path.GetFileName(file));
                if (!m.Success) {
                    continue; // only exact SAVE##.GAM slots (skips "SAVE00 - Copy.GAM" etc.)
                }
                int slot = int.Parse(m.Groups["n"].Value);
                SaveSlotInfo info = ReadSlot(file, slot);
                if (info != null) {
                    info.SlotIndex = slot;
                    slots.Add(info);
                }
            }
            return slots;
        }

        // Returns null when the slot must not be shown: unreadable, wrong version,
        // or (for non-bookmark slots) an empty save-name. The original never falls
        // back to the file name — a name-less slot simply isn't listed.
        private SaveSlotInfo ReadSlot(string filePath, int slot) {
            string fileName = Path.GetFileName(filePath);
            try {
                using FileStream stream = File.OpenRead(filePath);
                if (stream.Length < SaveGameHeader.Size) {
                    return null;
                }
                SaveGameHeader header = SaveGameExtractor.ReadHeader(stream);
                if (!header.IsSupportedVersion) {
                    return null;
                }
                // Slot 0 is the auto "Bookmark"; the engine forces this name over
                // whatever the header holds. Other slots use the header name and
                // are hidden if it is empty (no file-name fallback).
                string display = slot == 0 ? BookmarkSlotName : header.Name;
                if (string.IsNullOrWhiteSpace(display)) {
                    return null;
                }
                return new SaveSlotInfo {
                    FileName = fileName,
                    FullPath = filePath,
                    DisplayName = display,
                    ChapterNumber = header.ChapterNumber,
                    IsValid = true,
                };
            } catch (Exception e) {
                _logger.LogWarning(e, "Failed to read save header from {Path}; omitting slot.", filePath);
                return null;
            }
        }

        public async UniTask<string> EnsureDirectoryAsync(string directoryName) {
            await EnsureRootAsync();
            string dir = Path.Combine(SaveRoot, directoryName);
            try {
                if (!Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                    _logger.LogInformation("Created save directory {Path}.", dir);
                }
            } catch (Exception e) {
                _logger.LogError(e, "Failed to ensure save directory {Path}.", dir);
            }
            return dir;
        }

        public string SlotFullPath(string directoryName, int slotIndex) =>
            Path.Combine(SaveRoot, directoryName, string.Format(SaveSlotFileNameFormat, slotIndex));

        public async UniTask<bool> WriteSlotAsync(string fullPath, byte[] bytes) {
            try {
                string directoryName = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(directoryName);
                await File.WriteAllBytesAsync(fullPath, bytes);
                _logger.LogInformation("Wrote save {Path} ({Bytes} bytes).", fullPath, bytes.Length);
                return true;
            } catch (Exception e) {
                _logger.LogError(e, "Failed to write save {Path}.", fullPath);
                return false;
            }
        }

        public UniTask DeleteSlotAsync(string fullPath) {
            try {
                if (File.Exists(fullPath)) {
                    File.Delete(fullPath);
                    _logger.LogInformation("Deleted save {Path}.", fullPath);
                }
            } catch (Exception e) {
                _logger.LogError(e, "Failed to delete save {Path}.", fullPath);
            }
            return UniTask.CompletedTask;
        }

        public UniTask DeleteDirectoryAsync(string directoryName) {
            string dir = Path.Combine(SaveRoot, directoryName);
            try {
                if (Directory.Exists(dir)) {
                    Directory.Delete(dir, recursive: true);
                    _logger.LogInformation("Deleted save directory {Path}.", dir);
                }
            } catch (Exception e) {
                _logger.LogError(e, "Failed to delete save directory {Path}.", dir);
            }
            return UniTask.CompletedTask;
        }
    }
}

namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using GameData.Mods;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using UnityEngine;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The installed mods as override layers (mod system spec 4.4 and 4.6), worked out once at start:
    /// a change to the mods or to the Enhanced switch takes effect at the next start.
    /// </summary>
    public static class ModLayers {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(ModLayers));
        private static readonly ModResolution Empty =
            new(Array.Empty<InstalledMod>(), Array.Empty<InstalledMod>(), Array.Empty<ModProblem>());
        // The mod id pattern; ids have no '.', so an interrupted install's <id>.tmp and <id>.old are skipped.
        private static readonly Regex ModFolder = new("^[a-z0-9][a-z0-9-]{1,47}\\z");

        private static bool _initialized;

        /// <summary>Tests point this at a temp folder.</summary>
        internal static string ModsRootOverride { get; set; }

        public static string ModsRoot => ModsRootOverride ?? Path.Combine(Application.persistentDataPath, "mods");

        public static ModMode Mode { get; private set; }

        public static ModResolution Current { get; private set; } = Empty;

        internal static string StatePath => Path.Combine(ModsRoot, "mods.json");

        /// <summary>
        /// Installs the zips dropped into <see cref="ModsRoot"/>, then resolves the installed mods for
        /// the current mode. A broken mod or state file is logged, never a failed start.
        /// </summary>
        public static void Initialize() {
            if (_initialized) {
                return;
            }
            _initialized = true;
            Mode = GameOptions.Enhanced ? ModMode.Enhanced : ModMode.Classic;
            try {
                Directory.CreateDirectory(ModsRoot);
                // Before the inbox: an install that put the old version back must not be lost to a new one.
                ModArchive.RecoverStrandedInstalls(ModsRoot);
                List<ModStateEntry> state = ReadState();
                foreach (string zip in Directory.GetFiles(ModsRoot, "*.zip")) {
                    InstallFromInbox(zip, state);
                }
                Current = ModResolver.Resolve(ReadInstalled(), state, Mode, GameVersion);
            } catch (Exception e) {
                Logger.LogError(e, "Mods could not be read from {Root}; starting without mods.", ModsRoot);
                Current = Empty;
            }

            if (GameVersion == null) {
                Logger.LogWarning("Mods' game versions are not checked: this is the Editor or a development build ({Version}).", Application.version);
            }
            string mode = Mode.ToString().ToLowerInvariant();
            string active = Current.Active.Count == 0
                ? "none"
                : string.Join(", ", Current.Active.Select(m => $"{m.Manifest.Id} {m.Manifest.Version}"));
            Logger.LogInformation("Mods ({Mode}): {Active}", mode, active);
            foreach (ModProblem p in Current.Problems) {
                Logger.LogWarning("Mod {Id} not active ({Kind}): {Detail}", p.ModId, p.Kind.ToString(), p.Detail);
            }
        }

        /// <summary>The game version mods are checked against; none in the Editor or a development build (spec 4.1).</summary>
        internal static SemVersion? GameVersion => GameVersionOf(Application.isEditor || Debug.isDebugBuild, Application.version);

        internal static SemVersion? GameVersionOf(bool devBuild, string version) =>
            !devBuild && SemVersion.TryParse(version, out SemVersion v) ? v : null;

        /// <summary>Whether a folder in <see cref="ModsRoot"/> is an installed mod's, by its name.</summary>
        internal static bool IsModFolder(string folder) => ModFolder.IsMatch(Path.GetFileName(folder));

        /// <summary>The mods installed in <see cref="ModsRoot"/>; a folder without a valid mod.json is logged and skipped.</summary>
        internal static List<InstalledMod> ReadInstalled() {
            var installed = new List<InstalledMod>();
            foreach (string folder in Directory.GetDirectories(ModsRoot).Where(IsModFolder)) {
                InstalledMod mod = ModArchive.ReadInstalled(folder);
                if (mod != null) {
                    installed.Add(mod);
                } else {
                    Logger.LogWarning("Mod folder {Folder} has no valid mod.json; skipped.", folder);
                }
            }
            return installed;
        }

        /// <summary>A corrupt state file counts as empty: every installed mod is then enabled.</summary>
        internal static List<ModStateEntry> ReadState() {
            try {
                List<ModStateEntry> state = ModStateFile.Read(StatePath).ToList();
                if (state.Any(e => e?.Id == null)) {
                    throw new InvalidDataException("an entry has no id");
                }
                return state;
            } catch (Exception e) {
                Logger.LogError("{Path} is unreadable ({Reason}); using no mod order.", StatePath, e.Message);
                return new List<ModStateEntry>();
            }
        }

        /// <summary>
        /// The manual install path: a new mod goes to the top of the list, enabled; a re-installed one
        /// keeps its place and its on/off state. The zip is deleted.
        /// </summary>
        private static void InstallFromInbox(string zip, List<ModStateEntry> state) {
            InstalledMod mod;
            try {
                mod = ModArchive.Install(zip, ModsRoot);
                File.Delete(zip);
            } catch (Exception e) {
                Logger.LogError("Mod {Zip} could not be installed: {Errors}", zip, e.Message);
                try {
                    File.Delete(zip + ".failed");
                    File.Move(zip, zip + ".failed");
                } catch (Exception moveError) {
                    Logger.LogError("Could not rename {Zip}: {Reason}", zip, moveError.Message);
                }
                return;
            }
            string id = mod.Manifest.Id;
            int at = state.FindIndex(e => e.Id == id);
            var entry = new ModStateEntry(id, mod.Manifest.Version.ToString(), at < 0 || state[at].Enabled);
            if (at < 0) {
                state.Insert(0, entry);
            } else {
                state[at] = entry;
            }
            Logger.LogInformation("Installed mod {Id} {Version} from {Zip}.", id, mod.Manifest.Version.ToString(), zip);
            // The mod is installed: an unsaved order costs this start's place and on/off state, not the mod.
            try {
                ModStateFile.Write(StatePath, state);
            } catch (Exception e) {
                Logger.LogWarning("Mod {Id} is installed, but the mod order and on/off state could not be saved to {Path}: {Reason}",
                    id, StatePath, e.Message);
            }
        }

        /// <summary>The content override folders, highest priority first: the dev folder when enabled, then the active mods.</summary>
        public static IReadOnlyList<string> ContentRoots() {
            var roots = new List<string>();
            if (BakResourceSettings.OverrideEnabled && Directory.Exists(BakResourceSettings.OverridePath)) {
                roots.Add(BakResourceSettings.OverridePath);
            }
            roots.AddRange(Current.Active.Select(m => m.Folder));
            return roots;
        }

        /// <summary>The installed language mods' folders in this mode.</summary>
        public static IReadOnlyList<string> LanguageRoots() => Current.Languages.Select(m => m.Folder).ToList();

        /// <summary>For tests only.</summary>
        public static void Reset() {
            _initialized = false;
            ModsRootOverride = null;
            OverrideResourceLocator.ClearAliasCache();
            World.Rendering.HdFrames.ClearCache();
            UI.BookSpreadBackdrop.ResetArt();
            Mode = default;
            Current = Empty;
        }
    }
}

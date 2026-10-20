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

        private static string StatePath => Path.Combine(ModsRoot, "mods.json");

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
                List<ModStateEntry> state = ReadState();
                foreach (string zip in Directory.GetFiles(ModsRoot, "*.zip")) {
                    InstallFromInbox(zip, state);
                }
                List<InstalledMod> installed = Directory.GetDirectories(ModsRoot)
                    .Where(d => ModFolder.IsMatch(Path.GetFileName(d)))
                    .Select(ModArchive.ReadInstalled)
                    .Where(m => m != null)
                    .ToList();
                SemVersion? game = !Application.isEditor && SemVersion.TryParse(Application.version, out SemVersion v) ? v : null;
                Current = ModResolver.Resolve(installed, state, Mode, game);
            } catch (Exception e) {
                Logger.LogError(e, "Mods could not be read from {Root}; starting without mods.", ModsRoot);
                Current = Empty;
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

        /// <summary>A corrupt state file counts as empty: every installed mod is then enabled.</summary>
        private static List<ModStateEntry> ReadState() {
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

        /// <summary>The manual install path: a new mod goes to the top of the list and its zip is deleted.</summary>
        private static void InstallFromInbox(string zip, List<ModStateEntry> state) {
            try {
                InstalledMod mod = ModArchive.Install(zip, ModsRoot);
                string id = mod.Manifest.Id;
                state.RemoveAll(e => e.Id == id);
                state.Insert(0, new ModStateEntry(id, mod.Manifest.Version.ToString(), true));
                ModStateFile.Write(StatePath, state);
                File.Delete(zip);
                Logger.LogInformation("Installed mod {Id} {Version} from {Zip}.", id, mod.Manifest.Version.ToString(), zip);
            } catch (Exception e) {
                Logger.LogError("Mod {Zip} could not be installed: {Errors}", zip, e.Message);
                try {
                    File.Delete(zip + ".failed");
                    File.Move(zip, zip + ".failed");
                } catch (Exception moveError) {
                    Logger.LogError("Could not rename {Zip}: {Reason}", zip, moveError.Message);
                }
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
            Mode = default;
            Current = Empty;
        }
    }
}

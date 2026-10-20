namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using GameData.Mods;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class OverrideResourceLocator : IResourceLocator {
        private readonly ILogger _logger;
        private readonly List<string> _roots = Roots().ToList();

        public OverrideResourceLocator() {
            _logger = LogManager.LoggerFactory.CreateLogger<OverrideResourceLocator>();
            var keys = new List<object>();
            foreach (string root in _roots) {
                foreach (string dir in Directory.GetDirectories(root)) {
                    string extension = Path.GetFileName(dir);
                    if (extension == "Lang") {
                        continue;
                    }
                    string[] files = extension == "BMX" ? Directory.GetDirectories(dir) : Directory.GetFiles(dir);
                    foreach (string file in files) {
                        string key = Path.GetFileNameWithoutExtension(file) + '.' + extension;
                        keys.Add(key);
                    }
                }
            }
            Keys = keys;
        }

        public IEnumerable<IResourceLocation> AllLocations { get; } = Array.Empty<IResourceLocation>();

        public bool Locate(object key, Type type, out IList<IResourceLocation> locations) {
            locations = new List<IResourceLocation>();

            if (type == null) {
                return false;
            }
            if (!TryMapKey(key.ToString(), out string directory, out string filename)) {
                return false;
            }
            foreach (string root in _roots) {
                string path = Find(root, directory, filename);
                if (path != null) {
                    locations.Add(new ResourceLocationBase(key.ToString(), path, nameof(OverrideResourceProvider), type));

                    return true;
                }
            }

            _logger.LogDebug("Override for {Type} '{Key}' not found under {Directory}", type, key, directory);

            return false;
        }

        /// <summary>
        /// The override folder and file name a resource key maps to: <c>AIR1.BMX#0</c> is
        /// <c>BMX/AIR1</c>, <c>0.png</c>; <c>DIALSTYL.DAT</c> is <c>DAT</c>, <c>DIALSTYL.json</c>.
        /// </summary>
        internal static bool TryMapKey(string key, out string directory, out string filename) {
            directory = null;
            filename = key;

            // Create a subdirectory path for sub-assets. So AIR1.BMX#0 becomes AIR1/0.BMX
            string dirName = null;
            if (filename.Contains('#')) {
                string[] parts = filename.Split('#');
                dirName = Path.GetFileNameWithoutExtension(parts[0]);
                filename = parts[1] + Path.GetExtension(parts[0]);
            }

            string extension = Path.GetExtension(filename);

            if (string.IsNullOrWhiteSpace(extension)) {
                return false;
            }

            // .SCR/.BMP extensions resolve to their .SCX/.BMX variants (see ResourceFilename).
            extension = ResourceFilename.NormalizeImageExtension(extension);

            string newExtension = extension.ToLowerInvariant() switch {
                ".scx" => ".png",
                ".bmx" => ".png",
                _ => ".json"
            };

            filename = Path.GetFileNameWithoutExtension(filename) + newExtension;
            directory = Path.Join(extension[1..].ToUpperInvariant(), dirName);
            return true;
        }

        /// <summary>
        /// Every override of <paramref name="directory"/>/<paramref name="filename"/> across the
        /// roots, <b>lowest priority first</b>: the order partial documents apply in.
        /// </summary>
        internal static IReadOnlyList<string> FindAll(string directory, string filename) =>
            Roots().Select(root => Find(root, directory, filename)).Where(path => path != null).Reverse().ToList();

        // Each root's aliases.json, read once: the roots are fixed until the next start.
        private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> AliasCache = new();

        /// <summary>Forget the cached <c>aliases.json</c> maps (<see cref="ModLayers.Reset"/>).</summary>
        internal static void ClearAliasCache() {
            lock (AliasCache) {
                AliasCache.Clear();
            }
        }

        /// <summary>The file in one root, or else the file that root's <c>aliases.json</c> maps it to.</summary>
        private static string Find(string root, string directory, string filename) {
            string path = FindIgnoringCase(root, directory, filename);
            if (path != null) {
                return path;
            }
            string extension = Path.GetExtension(filename);
            string alias = (directory.Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(filename)).TrimStart('/');
            IReadOnlyDictionary<string, string> aliases;
            lock (AliasCache) {
                if (!AliasCache.TryGetValue(root, out aliases)) {
                    aliases = ModAliases.Read(root);
                    AliasCache[root] = aliases;
                }
            }
            foreach (var entry in aliases) {
                if (string.Equals(entry.Key, alias, StringComparison.OrdinalIgnoreCase)) {
                    int slash = entry.Value.LastIndexOf('/');
                    return slash < 0 ? null
                        : FindIgnoringCase(root, entry.Value[..slash], entry.Value[(slash + 1)..] + extension);
                }
            }
            return null;
        }

        /// <summary>
        /// The folders searched, most specific first: the active language pack's, then
        /// <see cref="ModLayers.ContentRoots"/> (the dev folder when enabled, then the active mods).
        /// A pack is used whether or not mod overrides are.
        /// </summary>
        private static IEnumerable<string> Roots() {
            string pack = LanguagePacks.ActiveFolder();
            if (pack != null && Directory.Exists(pack)) {
                yield return pack;
            }
            foreach (string root in ModLayers.ContentRoots()) {
                yield return root;
            }
        }

        /// <summary>
        /// The file at <paramref name="directory"/>/<paramref name="filename"/> under
        /// <paramref name="root"/>, matched without regard to case: the game names its resources in
        /// either case (INTRO.TTM loads "credits.SCR"), and the archive's own lookup ignores it, so
        /// an override must too on a case-sensitive file system.
        /// </summary>
        private static string FindIgnoringCase(string root, string directory, string filename) {
            string exact = Path.Join(root, directory, filename);
            if (File.Exists(exact)) {
                return exact;
            }
            string dir = root;
            foreach (string part in directory.Split('/', '\\')) {
                if (part.Length == 0) {
                    continue;
                }
                dir = Directory.Exists(dir)
                    ? Directory.EnumerateDirectories(dir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), part, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (dir == null) {
                    return null;
                }
            }
            return Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), filename, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether there is any override folder to search.</summary>
        internal static bool HasAnyRoot() => Roots().Any();

        public string LocatorId => nameof(OverrideResourceLocator);

        public IEnumerable<object> Keys { get; }
    }
}
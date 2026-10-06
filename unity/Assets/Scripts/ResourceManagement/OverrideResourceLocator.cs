namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
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
            string filename = key.ToString();

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

            string directory = Path.Join(extension[1..].ToUpperInvariant(), dirName);
            foreach (string root in _roots) {
                string path = FindIgnoringCase(root, directory, filename);
                if (path != null) {
                    locations.Add(new ResourceLocationBase(key.ToString(), path, nameof(OverrideResourceProvider), type));

                    return true;
                }
            }

            _logger.LogDebug("Override for {Type} '{Key}' not found under {Directory}", type, key, directory);

            return false;
        }

        /// <summary>
        /// The folders searched, most specific first: the active language pack's, then the mod
        /// folder when overrides are on. A pack is used whether or not mod overrides are.
        /// </summary>
        private static IEnumerable<string> Roots() {
            string pack = LanguagePacks.ActiveFolder();
            if (pack != null && Directory.Exists(pack)) {
                yield return pack;
            }
            if (BakResourceSettings.OverrideEnabled && Directory.Exists(BakResourceSettings.OverridePath)) {
                yield return BakResourceSettings.OverridePath;
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
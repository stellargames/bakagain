namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public class OverrideResourceLocator : IResourceLocator {
        private readonly ILogger _logger;

        public OverrideResourceLocator() {
            _logger = LogManager.LoggerFactory.CreateLogger<OverrideResourceLocator>();
            var keys = new List<object>();
            if (Directory.Exists(BakResourceSettings.OverridePath)) {
                string[] dirs = Directory.GetDirectories(BakResourceSettings.OverridePath);
                foreach (string dir in dirs) {
                    string extension = Path.GetFileName(dir);
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
            string path = Path.Join(BakResourceSettings.OverridePath, directory, filename);

            if (File.Exists(path)) {
                var resourceLocation = new ResourceLocationBase(key.ToString(), path, nameof(OverrideResourceProvider), type);
                locations.Add(resourceLocation);

                return true;
            }

            _logger.LogDebug("Override for {Type} '{Key}' not found at '{Path}'", type, key, path);

            return false;
        }

        public string LocatorId => nameof(OverrideResourceLocator);

        public IEnumerable<object> Keys { get; }
    }
}
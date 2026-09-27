namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using ResourceExtraction;
    using System;
    using System.Collections.Generic;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using ILogger =  Microsoft.Extensions.Logging.ILogger;

    public class BakResourceLocator : IResourceLocator {
        private readonly IDictionary<string, (long, uint)> _dictionary;
        private readonly ILogger _logger;

        public BakResourceLocator() {
            _logger = LogManager.LoggerFactory.CreateLogger<BakResourceLocator>();
            IResourceProvider resourceProvider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            _dictionary = resourceProvider.GetDictionary();
            Keys = _dictionary.Keys;
        }
#if !ENABLE_JSON_CATALOG
        public IEnumerable<IResourceLocation> AllLocations { get; } = Array.Empty<IResourceLocation>();
#endif
        public bool Locate(object key, Type type, out IList<IResourceLocation> locations) {
            string keyString = key.ToString();
            string[] parts = keyString.Split('#');
            bool subResourceRequested = parts.Length > 1;
            string keyName = parts[0].ToUpperInvariant();
            string subResourceId = subResourceRequested ? parts[1] : null;

            // .SCR/.BMP filenames load their .SCX/.BMX variants (see ResourceFilename).
            keyName = ResourceFilename.NormalizeImageExtension(keyName);
            string id = subResourceRequested ? $"{keyName}#{subResourceId}" : keyName;

            locations = new List<IResourceLocation>();
            // Derived book-parchment variants (BOOK_EVEN.SCX / BOOK_ODD.SCX) are synthesized by
            // BakResourceProvider from BOOK.SCX, so they resolve whenever the source is present.
            bool isBookVariant = BookParchment.IsVariant(keyName) && _dictionary.ContainsKey(BookParchment.Source);
            // The chapter catalog (CHAPTERS.DAT) is likewise synthesized by BakResourceProvider (it
            // probes the archive for each chapter's parts — see ChapterCatalogBuilder); it has no
            // archive member, so resolve it here the same way as the book-parchment variants.
            bool isChapterCatalog = string.Equals(keyName,
                GameData.Resources.Data.ChapterCatalog.ResourceId, StringComparison.OrdinalIgnoreCase);
            // The dialog style table (DIALSTYL.DAT) is the same kind of thing: the original kept
            // the seven dialogTypeData rows in the executable's data segment, so there is no
            // archive member and GameData carries them as code (DialogStyleTable). Without this
            // clause the dictionary miss below would make Locate return false whenever no
            // override file is present — i.e. the resource would only exist when modded, and the
            // faithful default would be unreachable.
            bool isDialogStyleTable = string.Equals(keyName,
                GameData.Resources.Dialog.DialogStyleTable.ResourceId, StringComparison.OrdinalIgnoreCase);
            // NOTE: uistrings.json deliberately has NO clause here, unlike the dialog style table.
            // Its default is not a resource at all — UiStringLoader reads UiStringCatalog.Embedded
            // directly and only asks the resource layer for a mod's override, which resolves through
            // OverrideResourceLocator (registered first). BakResourceProvider cannot serve a
            // TextAsset in any case, so a clause here would only make this locator claim a key it
            // then fails to load.
            if (!isChapterCatalog && !isDialogStyleTable && !isBookVariant && !_dictionary.ContainsKey(keyName)) {
                // Check for standalone files in the game directory (e.g., STARTUP.GAM, save files)
                string filePath = System.IO.Path.Combine(BakResourceSettings.GamePath, keyName);
                if (!System.IO.File.Exists(filePath)) {
                    _logger.LogDebug("keyName '{KeyName}' not found in BaK resource dictionary or game directory", keyName);
                    return false;
                }
            }

            var location = new ResourceLocationBase(keyName, id, nameof(BakResourceProvider), type);
            locations.Add(location);

            return true;
        }

        public string LocatorId => nameof(BakResourceLocator);

        public IEnumerable<object> Keys { get; }
    }
}
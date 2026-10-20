namespace BakAgain.Core {
    using BakAgain.ResourceManagement;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using UnityEngine.AddressableAssets;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class ResourceManagementInitializer {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(ResourceManagementInitializer));

        public static void InitializeResourceManagement() {
            Logger.LogInformation("Initializing Resource Management...");
            ModLayers.Initialize();
            LanguagePacks.Reload(); // a language mod only exists now; the boot screen may have read the setting before
            Logger.LogInformation("BakResourceSettings.OverrideEnabled: {OverrideEnabled}", BakResourceSettings.OverrideEnabled);
            Logger.LogInformation("BakResourceSettings.OverridePath: '{OverridePath}'", BakResourceSettings.OverridePath);
            Logger.LogInformation("BakResourceSettings.GamePath: '{GamePath}'", BakResourceSettings.GamePath);

            Logger.LogInformation("Injecting custom resource providers...");

            // A language pack's folder and an active mod are override roots too, so they count even with the dev folder off.
            if (OverrideResourceLocator.HasAnyRoot()) {
                Logger.LogInformation("Adding OverrideResourceProvider and Locator.");
                Addressables.ResourceManager.ResourceProviders.AddOnce<OverrideResourceProvider>();
                AddressableExtensions.AddResourceLocatorOnce<OverrideResourceLocator>();
            } else if (BakResourceSettings.OverrideEnabled) {
                Logger.LogWarning("Override directory '{OverridePath}' not found. Skipping override provider.", BakResourceSettings.OverridePath);
            }

            Logger.LogInformation("Adding BakResourceProvider and Locator.");
            Addressables.ResourceManager.ResourceProviders.AddOnce<BakResourceProvider>();
            AddressableExtensions.AddResourceLocatorOnce<BakResourceLocator>();
            PutOverrideLocatorFirst();
            Logger.LogInformation("Resource Management Initialized.");
        }

        /// <summary>
        /// Addressables asks the locators in order and the first that resolves a key serves it, but
        /// <c>AddResourceLocator</c> appends: the catalog's locator would serve a built-in address
        /// (<c>Enhanced/Sky/moon_large</c>) before a mod's file. So the list is rebuilt with the
        /// override locator first and the others after it, in their order.
        /// </summary>
        private static void PutOverrideLocatorFirst() {
            List<IResourceLocator> locators = Addressables.ResourceLocators.ToList();
            IResourceLocator overrides = locators.OfType<OverrideResourceLocator>().FirstOrDefault();
            if (overrides == null || locators[0] == overrides) {
                return;
            }
            List<ResourceLocatorInfo> infos = locators.Select(l => Addressables.GetLocatorInfo(l)).ToList();
            Addressables.ClearResourceLocators();
            Addressables.AddResourceLocator(overrides);
            for (int i = 0; i < locators.Count; i++) {
                if (locators[i] != overrides) {
                    Addressables.AddResourceLocator(locators[i], infos[i]?.LocalHash, infos[i]?.CatalogLocation);
                }
            }
        }
    }
}
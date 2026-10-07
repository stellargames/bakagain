namespace BakAgain.Core {
    using BakAgain.ResourceManagement;
    using System.IO;
    using System.Text;
    using UnityEngine.AddressableAssets;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class ResourceManagementInitializer {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(ResourceManagementInitializer));

        public static void InitializeResourceManagement() {
            Logger.LogInformation("Initializing Resource Management...");
            Logger.LogInformation("BakResourceSettings.OverrideEnabled: {OverrideEnabled}", BakResourceSettings.OverrideEnabled);
            Logger.LogInformation("BakResourceSettings.OverridePath: '{OverridePath}'", BakResourceSettings.OverridePath);
            Logger.LogInformation("BakResourceSettings.GamePath: '{GamePath}'", BakResourceSettings.GamePath);

            Logger.LogInformation("Injecting custom resource providers...");

            // A language pack's folder is an override root too, so it counts even with mod overrides off.
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
            Logger.LogInformation("Resource Management Initialized.");
        }
    }
}
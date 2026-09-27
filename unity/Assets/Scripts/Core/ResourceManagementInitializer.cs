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
            // Register the CodePagesEncodingProvider to enable access to legacy encodings
            // This is crucial for supporting DosCodePage (e.g., 437) in builds.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Logger.LogInformation("BakResourceSettings.OverrideEnabled: {OverrideEnabled}", BakResourceSettings.OverrideEnabled);
            Logger.LogInformation("BakResourceSettings.OverridePath: '{OverridePath}'", BakResourceSettings.OverridePath);
            Logger.LogInformation("BakResourceSettings.GamePath: '{GamePath}'", BakResourceSettings.GamePath);

            Logger.LogInformation("Injecting custom resource providers...");

            if (BakResourceSettings.OverrideEnabled) {
                Logger.LogInformation("Override enabled. Checking directory: '{OverridePath}'", BakResourceSettings.OverridePath);
                if (Directory.Exists(BakResourceSettings.OverridePath)) {
                    Logger.LogInformation("Override directory exists. Adding OverrideResourceProvider and Locator.");
                    Addressables.ResourceManager.ResourceProviders.AddOnce<OverrideResourceProvider>();
                    AddressableExtensions.AddResourceLocatorOnce<OverrideResourceLocator>();
                } else {
                    Logger.LogWarning("Override directory '{OverridePath}' not found. Skipping override provider.", BakResourceSettings.OverridePath);
                }
            }

            Logger.LogInformation("Adding BakResourceProvider and Locator.");
            Addressables.ResourceManager.ResourceProviders.AddOnce<BakResourceProvider>();
            AddressableExtensions.AddResourceLocatorOnce<BakResourceLocator>();
            Logger.LogInformation("Resource Management Initialized.");
        }
    }
}
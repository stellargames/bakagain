namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine.AddressableAssets;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.ResourceProviders;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Extension methods for Addressables and ResourceManager
    /// </summary>
    public static class AddressableExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(AddressableExtensions));

        /// <summary>
        /// Adds a resource provider of type T to the collection if one isn't already present;
        /// no-op when a provider of type T already exists.
        /// </summary>
        /// <typeparam name="T">The type of resource provider to add</typeparam>
        /// <param name="providers">The resource provider collection</param>
        public static void AddOnce<T>(this IList<IResourceProvider> providers) where T : IResourceProvider, new() {
            // Check if provider of type T already exists
            IResourceProvider existingProvider = providers.FirstOrDefault(p => p is T);
            if (existingProvider != null) {
                return;
            }

            Logger.LogInformation("Create and add new provider");
            var provider = new T();
            providers.Add(provider);
        }

        /// <summary>
        /// Adds a resource locator of type T if it doesn't already exist
        /// </summary>
        /// <typeparam name="T">The type of resource locator to add</typeparam>
        /// <returns>The locator instance (either existing or newly created)</returns>
        public static T AddResourceLocatorOnce<T>() where T : IResourceLocator, new() {
            // Check if locator of type T already exists
            IResourceLocator existingLocator = Addressables.ResourceLocators.FirstOrDefault(l => l is T);
            if (existingLocator != null) {
                return (T)existingLocator;
            }

            // Create and add new locator
            var locator = new T();
            Addressables.AddResourceLocator(locator);

            return locator;
        }
    }
}
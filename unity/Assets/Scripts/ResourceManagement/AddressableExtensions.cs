namespace BakAgain.ResourceManagement {
    using BakAgain.Core;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine.AddressableAssets;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.ResourceManagement.ResourceProviders;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Extension methods for Addressables and ResourceManager
    /// </summary>
    public static class AddressableExtensions {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(AddressableExtensions));

        /// <summary>
        /// The handle's result, waiting for it only while it is still loading. Use it in place of
        /// <c>WaitForCompletion</c>.
        /// </summary>
        /// <remarks>
        /// Addressables 4.x's <c>WaitForCompletion</c> runs <c>ResourceManager.Update</c> even for a
        /// finished operation, and <c>Update</c> may not be re-entered: inside a provider's
        /// <c>Provide</c> or a load's Completed callback (both run inside <c>Update</c>) it throws
        /// "Reentering the Update method is not allowed" with nothing left to wait for (TASK-883).
        /// The game's own providers (<see cref="BakResourceProvider"/>, <see cref="OverrideResourceProvider"/>)
        /// complete inside <c>LoadAssetAsync</c>, so a load from the archive or a mod never waits here.
        /// <para>For the same reason a handle is awaited as <c>await handle.ToUniTask()</c>, never
        /// <c>await handle</c>: 4.x gave the handle its own <c>GetAwaiter</c>, which wins over UniTask's
        /// and resumes the caller inside Addressables' Completed callback (and drops the caller's
        /// reference on failure). On Android, where the catalog's loads are really asynchronous, that
        /// left the travel HUD without its party portraits.</para>
        /// </remarks>
        public static T ResultOrWait<T>(this AsyncOperationHandle<T> handle) =>
            handle.IsDone ? handle.Result : handle.WaitForCompletion();

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
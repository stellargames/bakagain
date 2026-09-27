using Cysharp.Threading.Tasks;

namespace BakAgain.ResourceManagement
{
    /// <summary>
    /// A service that provides a clean, testable, and memory-safe entry point
    /// into the Addressables system for loading game assets.
    /// </summary>
    public interface IResourceProviderService
    {
        /// <summary>
        /// Loads an asset asynchronously via its addressable key.
        /// </summary>
        /// <param name="key">The addressable key of the asset to load.</param>
        /// <param name="owner">The object that is requesting the asset. Used to track and release assets by scope.</param>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <returns>A UniTask that completes with the loaded asset, or null if loading fails.</returns>
        UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class;

        /// <summary>
        /// Releases all assets that were loaded by the specified owner.
        /// </summary>
        /// <param name="owner">The owner whose assets should be released.</param>
        void ReleaseAssets(object owner);
    }
}

using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using VContainer;

namespace BakAgain.ResourceManagement {
    using BakAgain.Core;

    /// <summary>
    /// An implementation of IResourceProviderService that uses the Unity Addressables system
    /// to load assets. It also handles the tracking and releasing of Addressable handles
    /// to prevent memory leaks.
    /// </summary>
    public class AddressableResourceProviderService : IResourceProviderService, IDisposable {
        private readonly ILogger<AddressableResourceProviderService> _logger;
        private readonly Dictionary<object, List<AsyncOperationHandle>> _handlesByOwner = new();

        [Inject]
        public AddressableResourceProviderService(ILogger<AddressableResourceProviderService> logger) {
            _logger = logger;
            _logger.LogInformation("AddressableResourceProviderService created.");
        }

        public async UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class {
            if (owner == null) {
                _logger.LogError("Asset loading requires a non-null owner for tracking.");

                return null;
            }

            // *** ASK THE LOCATORS FIRST. *** Addressables logs an InvalidKeyException to the console
            // for a key no locator knows, before the failed handle ever reaches us — and the sparse
            // per-id files (T####.DAT tile events, MONST#.DAT) are absent for most ids by design, so a
            // session's console filled with dozens of red "exceptions" that were all normal absence,
            // hiding the real ones. An unknown key is a null, as a failed load already was.
            if (!IsLocated(key, typeof(T))) {
                _logger.LogDebug("No resource at key: {Key}", key);
                return null;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);

            if (!_handlesByOwner.TryGetValue(owner, out List<AsyncOperationHandle> handleList)) {
                handleList = new List<AsyncOperationHandle>();
                _handlesByOwner[owner] = handleList;
            }
            handleList.Add(handle);

            await handle;

            if (handle.Status == AsyncOperationStatus.Succeeded) {
                _logger.LogDebug("Successfully loaded asset with key: {Key}", key);

                return handle.Result;
            }

            _logger.LogError("Failed to load asset with key: {Key}. Reason: {Exception}", key, handle.OperationException);

            return null;
        }

        private static bool IsLocated(object key, System.Type type) {
            foreach (UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator locator in Addressables.ResourceLocators) {
                if (locator.Locate(key, type, out _)) {
                    return true;
                }
            }
            return false;
        }

        public void ReleaseAssets(object owner) {
            if (owner == null) {
                _logger.LogWarning("Cannot release assets for a null owner.");

                return;
            }

            if (!_handlesByOwner.TryGetValue(owner, out List<AsyncOperationHandle> handlesToRelease)) {
                return;
            }

            _logger.LogDebug("Releasing {Count} assets for owner {Owner}", handlesToRelease.Count, owner.GetType().Name);
            foreach (AsyncOperationHandle handle in handlesToRelease) {
                Addressables.Release(handle);
            }

            _handlesByOwner.Remove(owner);
        }

        public void Dispose() {
            _logger.LogInformation("Disposing AddressableResourceProviderService and releasing all tracked handles.");
            foreach (object owner in new List<object>(_handlesByOwner.Keys)) {
                ReleaseAssets(owner);
            }
            _handlesByOwner.Clear();
        }
    }
}
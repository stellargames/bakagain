namespace BakAgain.CutScenes {
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;

    /// <summary>
    /// Session-scoped Addressables cache for cutscene/book/dialog assets that are
    /// reused across a play session (e.g. DIALOG.SCX, shared palettes). Retains the
    /// <see cref="AsyncOperationHandle"/> for every loaded asset so they can be
    /// released together via <see cref="Clear"/> / <see cref="Dispose"/> — call
    /// that at a session/chapter boundary where no cutscene is mid-play (clearing
    /// while assets are in use would free in-use textures).
    /// </summary>
    public class ResourceCache : IResourceCache, IDisposable {
        private readonly Dictionary<(Type, string), AsyncOperationHandle> _handles = new();

        public async UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
            // *** KEYED BY TYPE AS WELL AS NAME. *** One file loads as several types — the cutscene
            // player takes DIALOG.SCX as an IndexedTexture, the character sheet as a Sprite — and a
            // name-only key handed the second caller the first one's asset, which `as T` turned into
            // null: the temple healer's parchment vanished after any location scene had played.
            (Type, string) cacheKey = (typeof(T), key);
            if (_handles.TryGetValue(cacheKey, out AsyncOperationHandle cached)) {
                return cached.Result as T;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
            T asset = await handle.ToUniTask();
            _handles[cacheKey] = handle;
            return asset;
        }

        /// <summary>Release every cached Addressables handle and empty the cache.</summary>
        public void Clear() {
            foreach (AsyncOperationHandle handle in _handles.Values) {
                if (handle.IsValid()) {
                    Addressables.Release(handle);
                }
            }
            _handles.Clear();
        }

        public void Dispose() {
            Clear();
        }
    }
}

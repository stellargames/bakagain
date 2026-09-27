namespace BakAgain.Tests.Editor.Core.Fixtures {
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// In-memory <see cref="IResourceProviderService"/> for unit tests. Returns whatever
    /// the test pre-loaded with <see cref="Register{T}"/>; tracks owner refcounts so
    /// tests can assert ReleaseAssets was called.
    /// </summary>
    public sealed class FakeResourceProviderService : IResourceProviderService {
        private readonly Dictionary<string, object> _assets = new();
        private readonly Dictionary<string, Exception> _failures = new();
        private readonly Dictionary<object, int> _outstandingByOwner = new();

        public int LoadCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public void Register<T>(string key, T asset) where T : class {
            _assets[key] = asset;
        }

        /// <summary>Register a missing-resource entry: <see cref="LoadAssetAsync"/> returns null.</summary>
        public void RegisterMissing(string key) {
            _assets[key] = null;
        }

        /// <summary>Make the next load for <paramref name="key"/> throw.</summary>
        public void RegisterFailure(string key, Exception exception) {
            _failures[key] = exception;
        }

        public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class {
            LoadCalls++;
            string keyString = key?.ToString() ?? string.Empty;
            if (_failures.TryGetValue(keyString, out Exception failure)) {
                _failures.Remove(keyString);
                throw failure;
            }
            _outstandingByOwner.TryGetValue(owner, out int count);
            _outstandingByOwner[owner] = count + 1;
            _assets.TryGetValue(keyString, out object asset);
            return UniTask.FromResult(asset as T);
        }

        public void ReleaseAssets(object owner) {
            ReleaseCalls++;
            _outstandingByOwner.Remove(owner);
        }

        public int OutstandingForOwner(object owner) =>
            _outstandingByOwner.TryGetValue(owner, out int count) ? count : 0;
    }
}

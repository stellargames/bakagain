namespace BakAgain.ResourceManagement.Loaders {
    using BakAgain.Core;
    using Cysharp.Threading.Tasks;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations; 
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    public static class AddressableLoader {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(AddressableLoader));

        public static async UniTask<T> LoadAssetAsync<T>(string key) where T : class {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
            await handle;

            if (handle.Status == AsyncOperationStatus.Succeeded) {
                return handle.Result;
            }
            Logger.LogError("Failed to load addressable asset: {Address}", key);

            return null;
        }
    }
}
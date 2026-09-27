namespace BakAgain.CutScenes {
    using Cysharp.Threading.Tasks;

    public interface IResourceCache {
        UniTask<T> GetOrLoadAsync<T>(string key) where T : class;

        /// <summary>
        /// Release every cached Addressables handle and empty the cache. Call at a
        /// session/chapter boundary where no cutscene is mid-play.
        /// </summary>
        void Clear();
    }
}

namespace BakAgain.CutScenes {
    using Cysharp.Threading.Tasks;

    public interface IResourceCache {
        UniTask<T> GetOrLoadAsync<T>(string key) where T : class;

        /// <summary>
        /// Whether the game has a resource under this key, asked without loading it. Fakes have
        /// everything; the Addressables-backed caches ask the catalogue.
        /// </summary>
        bool Contains(string key) => true;

        /// <summary>
        /// Release every cached Addressables handle and empty the cache. Call at a
        /// session/chapter boundary where no cutscene is mid-play.
        /// </summary>
        void Clear();
    }
}

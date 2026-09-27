namespace BakAgain.World.Converters {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using UnityEngine;

    /// <summary>Loads a slot-bitmap image as a clamp-wrapped, point-filtered texture directly from its
    /// baked resource key ("Z##SLOT#.BMX#i") via the existing BMX sub-resource cache. The old runtime
    /// index resolution (concatenate the zone's slot BMX counts, resolve a global index) is gone — the
    /// extractor now bakes the key onto each face (see the 2026-07-16 design doc).</summary>
    public sealed class SlotBitmapTextureLoader {
        private readonly IResourceCache _cache;
        public SlotBitmapTextureLoader(IResourceCache cache) { _cache = cache; }

        public async UniTask<Texture2D> LoadAsync(string resourceKey) {
            var sprite = await _cache.GetOrLoadAsync<Sprite>(resourceKey);
            var tex = sprite.texture;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;
            return tex;
        }
    }
}

namespace BakAgain.ResourceManagement.Converters {
    using System;
    using System.IO;
    using Cysharp.Threading.Tasks;
    using GLTFast;
    using UnityEngine;

    /// <summary>
    /// Loads a .glb / .gltf file via glTFast and returns a <see cref="ModelVisual"/> containing a
    /// disabled template GameObject plus the <c>GltfImport</c> as the disposable resource. Returns
    /// <c>null</c> on any failure so the caller can fall back to the TBL path without crashing.
    /// On failure every path disposes the import; on success the import is returned for the loader
    /// to dispose at zone teardown (after the visuals are destroyed), because <c>GltfImport.Dispose</c>
    /// destroys the meshes/materials it owns and must not be called while clones are still rendering.
    /// </summary>
    public sealed class GltfModelConverter : IModelVisualConverter {
        public async UniTask<ModelVisual> BuildVisualAsync(string filePath) {
            GameObject holder = null;
            var gltf = new GltfImport();
            try {
                bool loaded = await gltf.LoadFile(filePath);
                if (!loaded) { gltf.Dispose(); return null; }

                holder = new GameObject(Path.GetFileNameWithoutExtension(filePath));
                bool ok = await gltf.InstantiateMainSceneAsync(holder.transform);
                if (!ok) { UnityEngine.Object.Destroy(holder); gltf.Dispose(); return null; }

                holder.SetActive(false);
                // gltf owns the instantiated meshes/materials; it is NOT disposed here (that destroys them).
                // The loader disposes it at zone teardown, after the visuals are destroyed.
                return new ModelVisual(holder, gltf);
            } catch (Exception e) {
                if (holder != null) UnityEngine.Object.Destroy(holder);
                gltf.Dispose();
                Debug.LogWarning($"glTF load failed for {filePath}: {e.Message}");
                return null;
            }
        }
    }
}

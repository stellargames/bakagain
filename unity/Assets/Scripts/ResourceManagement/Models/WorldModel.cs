namespace BakAgain.ResourceManagement.Models {
    using UnityEngine;

    /// <summary>
    /// A ready-to-instantiate world model: a disabled template GameObject (meshes + materials,
    /// already set up) plus engine-independent metadata. Produced by a converter in the resource
    /// layer (TBL today; .glb/.fbx/.obj later) and consumed by the world builder, which never
    /// knows the source. Instantiate <see cref="Visual"/> per placement (instances share the
    /// template's sharedMesh/sharedMaterials).
    /// <para>
    /// <see cref="Resources"/> carries any converter-owned assets (e.g. a glTFast
    /// <c>GltfImport</c>) that must be disposed after the visual is destroyed at zone teardown.
    /// It is <c>null</c> for TBL-built models (which own nothing beyond the template mesh).
    /// </para>
    /// </summary>
    public sealed class WorldModel {
        public GameObject Visual { get; }
        public WorldModelMetadata Metadata { get; }
        public System.IDisposable Resources { get; }

        public WorldModel(GameObject visual, WorldModelMetadata metadata, System.IDisposable resources = null) {
            Visual = visual;
            Metadata = metadata;
            Resources = resources;
        }
    }
}

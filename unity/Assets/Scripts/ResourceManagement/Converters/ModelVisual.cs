namespace BakAgain.ResourceManagement.Converters {
    using System;
    using UnityEngine;

    /// <summary>
    /// A converter's result: the disabled template <see cref="Visual"/> plus any converter-owned
    /// <see cref="Resources"/> that must be disposed when the model is torn down (e.g. a glTFast
    /// <c>GltfImport</c>, which owns the instantiated meshes/materials). <see cref="Resources"/> is
    /// null for converters that own nothing past instantiation.
    /// </summary>
    public sealed class ModelVisual {
        public GameObject Visual { get; }
        public IDisposable Resources { get; }
        public ModelVisual(GameObject visual, IDisposable resources) {
            Visual = visual;
            Resources = resources;
        }
    }
}

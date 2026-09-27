namespace BakAgain.ResourceManagement.Converters {
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Maps lowercase file extensions to their <see cref="IModelVisualConverter"/>. Task 6's
    /// override probe iterates <see cref="Extensions"/> to discover which formats are supported.
    /// </summary>
    public sealed class ModelConverterRegistry {
        private readonly Dictionary<string, IModelVisualConverter> _converters;

        public ModelConverterRegistry() {
            var gltf = new GltfModelConverter();
            _converters = new Dictionary<string, IModelVisualConverter>(StringComparer.Ordinal) {
                [".glb"]  = gltf,
                [".gltf"] = gltf,
            };
        }

        /// <summary>
        /// Look up the converter for <paramref name="extension"/>. The extension is normalized to
        /// lowercase before lookup, so <c>".GLB"</c> and <c>".glb"</c> both resolve.
        /// </summary>
        public bool TryGet(string extension, out IModelVisualConverter converter) {
            var key = extension?.ToLowerInvariant() ?? string.Empty;
            return _converters.TryGetValue(key, out converter);
        }

        /// <summary>
        /// Register (or replace) the converter for <paramref name="extension"/>. The extension is
        /// normalised to lowercase, so <c>".GLB"</c> and <c>".glb"</c> are the same key. Intended
        /// as a test seam and an extension point for future formats (fbx, obj, …).
        /// </summary>
        public void Register(string extension, IModelVisualConverter converter) {
            if (extension is null) throw new ArgumentNullException(nameof(extension));
            if (converter is null) throw new ArgumentNullException(nameof(converter));
            _converters[extension.ToLowerInvariant()] = converter;
        }

        /// <summary>All registered extensions (already lowercase).</summary>
        public IEnumerable<string> Extensions => _converters.Keys;
    }
}

namespace BakAgain.ResourceManagement.Converters {
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// Seam for converting a model file into a disabled template <see cref="ModelVisual"/> that the
    /// world builder can instantiate per-placement. Returns <c>null</c> on failure so the caller can
    /// fall back to the TBL path without crashing.
    /// </summary>
    public interface IModelVisualConverter {
        /// <summary>
        /// Build a disabled template + its disposable resources from the model file at
        /// <paramref name="filePath"/>. Returns <c>null</c> on any failure (caller falls back).
        /// </summary>
        UniTask<ModelVisual> BuildVisualAsync(string filePath);
    }
}

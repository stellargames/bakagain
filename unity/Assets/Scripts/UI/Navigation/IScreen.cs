namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// One visible surface managed by the <see cref="IScreenNavigator"/> stack. Implementations
    /// must be idempotent (Show on a shown screen / Hide on a hidden one are no-ops) — the
    /// navigator reconciles desired visibility after every stack mutation.
    /// </summary>
    public interface IScreen {
        /// <summary>Activate + build the surface (async prep may run before activation).</summary>
        UniTask ShowAsync();

        /// <summary>Deactivate + tear down the surface.</summary>
        UniTask HideAsync();
    }
}

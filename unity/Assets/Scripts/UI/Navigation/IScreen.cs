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

        /// <summary>
        /// <see cref="ShowAsync()"/>; with <paramref name="fade"/> false the surface's own fade-in is
        /// skipped too, for an unfaded push (TASK-887). Screens without one ignore it.
        /// </summary>
        UniTask ShowAsync(bool fade) => ShowAsync();

        /// <summary><see cref="HideAsync()"/>, its fade-out skipped when <paramref name="fade"/> is false.</summary>
        UniTask HideAsync(bool fade) => HideAsync();
    }
}

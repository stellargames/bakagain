namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// The single owner of "which screen is visible": a stack of <see cref="IScreen"/>s. The
    /// navigator makes the ONLY show/hide calls on screens — screens never activate/deactivate
    /// each other (no <c>Open(returnTo)</c>, no cross-screen references). One screen is visible at
    /// a time: every Push hides the current top before showing the new one.
    ///
    /// Operations are serialized (FIFO): a Pop issued while a Push is still showing runs after
    /// it, so the stack can never interleave half-finished transitions.
    ///
    /// See docs/superpowers/specs/2026-07-12-screen-navigation-architecture-design.md.
    /// </summary>
    public interface IScreenNavigator {
        /// <summary>Clear the stack (hiding everything) and show <paramref name="root"/> —
        /// a flow landing in a configuration (main menu, travel HUD).</summary>
        UniTask ResetTo(IScreen root);

        /// <summary>Hide the current top and show <paramref name="screen"/> on top. The current
        /// top stays on the stack (hidden) and re-shows on the matching <see cref="Pop"/>.</summary>
        /// <remarks>
        /// <b>This completes when the screen is SHOWN, not when it closes.</b> It is the fade,
        /// the stack push and the show — nothing waits for the player. Reading a screen's result
        /// on the line after awaiting it reads it a frame after it appeared, which silently broke
        /// three lock handlers; use <see cref="PushAndWaitAsync"/> when you need the "after".
        /// </remarks>
        UniTask Push(IScreen screen);

        /// <summary>
        /// Show <paramref name="screen"/> and complete once it is no longer the top of the stack.
        /// </summary>
        /// <remarks>
        /// What callers usually mean by "open this screen": the original's screen calls are
        /// blocking, and the code after them is the code that runs when the player is done. It
        /// returns as soon as the screen stops being the top, so a screen that pushes another over
        /// itself does not resume its opener early.
        /// </remarks>
        UniTask PushAndWaitAsync(IScreen screen);

        /// <summary>Hide the top screen and re-show the one beneath (iff it was hidden).</summary>
        UniTask Pop();

        /// <summary>Hide the top screen, drop it from the stack, and show
        /// <paramref name="screen"/> in its place.</summary>
        UniTask Replace(IScreen screen);

        /// <summary>Hide everything and empty the stack (leaving a configuration).</summary>
        UniTask Clear();

        /// <summary>The top screen, or null when the stack is empty.</summary>
        IScreen Current { get; }
    }
}

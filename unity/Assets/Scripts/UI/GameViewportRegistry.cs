namespace BakAgain.UI {
    using System;
    using UnityEngine;

    /// <summary>
    /// Mutable holder for the current <see cref="IGameViewport"/> rectangle.
    /// Producers (the active cutscene view, the in-game world camera, etc.)
    /// register a rect-provider here when they take over the game viewport;
    /// readers (DialogManager and any other UI Toolkit overlay) inject
    /// <see cref="IGameViewport"/> and call <see cref="ScreenRect"/> at
    /// render time.
    ///
    /// Existence rationale: keeping the read side (DialogManager) and the
    /// write side (CutSceneView, …) decoupled breaks the dependency cycle
    /// that arises when CutSceneView would otherwise be both an
    /// <see cref="IGameViewport"/> and a transitive consumer of dialog
    /// services. The registry has no dependencies of its own, so DI sees a
    /// clean DAG: producers → registry, consumers → registry.
    /// </summary>
    public class GameViewportRegistry : IGameViewport {
        private Func<Rect> _provider;

        /// <summary>
        /// Current viewport rectangle in screen pixels (Unity Screen-rect
        /// convention, origin bottom-left). Falls back to the full window
        /// when no producer has registered yet.
        /// </summary>
        public Rect ScreenRect => (_provider ?? DefaultProvider)();

        /// <summary>
        /// Install a rect-provider. Pass <c>null</c> to revert to the
        /// full-window default — producers should clear themselves when
        /// they tear down so a later reader doesn't see a stale rect from
        /// a destroyed component.
        /// </summary>
        public void SetProvider(Func<Rect> provider) {
            _provider = provider;
        }

        private static readonly Func<Rect> DefaultProvider =
            () => new Rect(0f, 0f, Screen.width, Screen.height);
    }
}

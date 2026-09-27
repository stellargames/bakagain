namespace BakAgain.UI.Navigation {
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// Fades the whole screen out before a transition and back in after it.
    /// </summary>
    /// <remarks>
    /// <b>The original fades whenever a hotspot replaced what was on screen</b> —
    /// <c>g_nSceneReloadPending</c> (WORLDLP.C:102-139) and <c>g_nMapReloadPending</c>
    /// (MAP.C:136-167). It fades out, repaints the whole frame — background, menu, world view,
    /// compass — and fades back in.
    ///
    /// <para><b>The repaint half is deliberately not ported.</b> Unity redraws every frame; there is
    /// no data reload behind those flags and no cache to invalidate. What remains is the fade, and
    /// the owner decided on 2026-09-03 that the remake has one (TASK-214).</para>
    ///
    /// <para><b>Why this hangs off the navigator rather than the hotspot dispatch.</b> A faithful
    /// fade is out, then change, then in — so the transition has to be awaitable. Both places that
    /// compute "the screen changed" are synchronous (<c>HotspotService.ActivateAtPartyPosition</c>
    /// and <c>SettleEncounter</c>), and a fade started from either would play AFTER the change it is
    /// supposed to cover. <see cref="IScreenNavigator" /> is async throughout and serialises every
    /// transition through one wrapper, so it is the only place the ordering is naturally right.</para>
    /// </remarks>
    public interface IScreenFade {
        /// <summary>Darken to opaque. Completes when the screen is fully covered.</summary>
        UniTask FadeOutAsync();

        /// <summary>Clear back to the game. Completes when nothing is covering it.</summary>
        UniTask FadeInAsync();
    }

    /// <summary>
    /// The no-fade implementation: transitions cut, exactly as they did before.
    /// </summary>
    /// <remarks>
    /// <b>The default, so wiring the seam changed nothing on its own.</b> A screen fade needs a
    /// rendering surface above every panel, which is a separate piece of work; until it exists the
    /// navigator calls this and the game cuts as it always has, rather than the seam being an
    /// invisible half-feature that looks wired.
    /// </remarks>
    public sealed class NullScreenFade : IScreenFade {
        public UniTask FadeOutAsync() => UniTask.CompletedTask;
        public UniTask FadeInAsync() => UniTask.CompletedTask;
    }
}

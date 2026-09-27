namespace BakAgain.World {
    using BakAgain.Graphics;
    using UnityEngine;

    /// <summary>
    /// Describes the in-game 3D viewport: the fixed rectangle (in canonical 1600×1200
    /// space) that the world camera renders into — i.e. the hole in the
    /// <c>frame.scr</c> chrome. Complements <see cref="UI.IGameViewport"/>: that one
    /// holds a screen-space rect for overlay (dialog) anchoring; this one is the
    /// authoritative source of the viewport geometry and owns the camera→RenderTexture
    /// sizing so the camera setup is testable without a live screen.
    ///
    /// <para>
    /// Rect is RE-verified (2026-05-31): <c>setupRenderView</c> (IDA <c>0x23d18</c>)
    /// installs it into the <c>renderView_*</c> edge globals, and it is carried as the
    /// four int16 in <c>START.DAT</c> that <c>LoadSTART.DAT</c> (@0x41620) puts into the render
    /// view descriptor. <c>REQ_MAIN.DAT</c>'s invisible <c>ClickArea</c> (ActionId 192) holds the
    /// same rect but is the mouse hit region — see <c>generated/START.json</c>,
    /// <c>generated/REQ/REQ_MAIN.json</c> and
    /// <c>docs/plans/2026-05-13-world-ui-next-steps.md</c> Phase 3.
    /// </para>
    /// </summary>
    public interface IWorldViewport {
        /// <summary>
        /// The viewport rectangle in canonical 1600×1200 coordinates (top-left origin):
        /// <c>x=65, y=66, w=1470, h=606</c> (the RE-verified VGA 13,11,294,101 scaled ×5/×6).
        /// Position UI Toolkit elements with this directly when the panel reference
        /// resolution is 1600×1200 (the panel scaler handles DPI).
        /// </summary>
        Area CanonicalRect { get; }

        /// <summary>
        /// Aspect ratio (width / height) of <see cref="CanonicalRect"/> in square pixels
        /// (1470 / 606 ≈ 2.426 — the true displayed aspect; the old non-square 294/101 ≈ 2.911
        /// ignored the 6:5 VGA pixel aspect). Use for the world camera so the rendered image
        /// matches the cutout without distortion.
        /// </summary>
        float ViewportAspect { get; }

        /// <summary>
        /// Maps <see cref="CanonicalRect"/> proportionally into <paramref name="stageScreenRect"/>
        /// — the <c>CanonicalStage</c>'s already-resolved on-screen rect (Unity screen-space
        /// pixels, origin bottom-left, matching <see cref="UI.IGameViewport"/>). The stage has
        /// already decided the fit (pillarboxed under <c>Contain</c>, full-window under
        /// <c>Fill</c>), so this method itself is fit-agnostic: it never re-derives Contain math,
        /// it only rescales the RE-verified viewport rect into whatever box the stage occupies.
        /// Consumers registered through <see cref="UI.GameViewportRegistry"/> anchor to the world
        /// view, not the whole screen. Mirrors the canonical top-left origin to Unity's
        /// bottom-left.
        /// </summary>
        Rect ToScreenRect(Rect stageScreenRect);

        /// <summary>
        /// Pixel size for the world RenderTexture given the stage's current on-screen rect — the
        /// on-screen size of <see cref="ToScreenRect"/>, rounded and clamped to ≥1 so
        /// the texture is crisp at the current resolution. Recompute whenever the stage's screen
        /// rect changes (the RenderTexture should be reallocated when this changes).
        /// </summary>
        Vector2Int RenderTextureSize(Rect stageScreenRect);
    }
}

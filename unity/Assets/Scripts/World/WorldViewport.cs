namespace BakAgain.World {
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using GameData.Resources.Config;
    using ResourceExtraction;
    using UnityEngine;

    /// <summary>
    /// Plain (non-MonoBehaviour) implementation of <see cref="IWorldViewport"/>, registered
    /// as a DI singleton. All geometry is pure math against the RE-verified viewport rect
    /// (in canonical 1600×1200 space), so it is unit-testable without a live screen or scene.
    ///
    /// <para>
    /// The rect comes from <b>START.DAT</b>, read here rather than typed in.
    /// <c>LoadSTART.DAT</c> (ovr129 @0x41620) reads four int16 straight into the render view
    /// descriptor that <c>setupRenderView</c> (@0x23d18) clips to. <c>REQ_MAIN.DAT</c>'s invisible
    /// <c>ClickArea</c> (ActionId 192) carries the same rectangle, and this class used to cite it as
    /// the source — but that one is the MOUSE hit region. They agree, which is precisely why the
    /// wrong attribution survived. See <see cref="StartData"/>.
    /// </para>
    ///
    /// <para>
    /// It is constant across normal gameplay — not per-zone data. (The underlying
    /// <c>renderView_*</c> globals are a shared clip rect the DOS engine also re-points for
    /// sub-region draws via <c>set_screen_dimensions</c>/<c>restore_screen_dimensions</c>, but
    /// the world-view value itself does not change — see Phase 3 notes.)
    /// </para>
    /// </summary>
    public sealed class WorldViewport : IWorldViewport {
        /// <summary>Reference screen width the viewport rect is expressed in (canonical 1600×1200).</summary>
        public const int CanonicalWidth = Canonical.Width;

        /// <summary>Reference screen height the viewport rect is expressed in (canonical 1600×1200).</summary>
        public const int CanonicalHeight = Canonical.Height;

        // Read once, on first use rather than in the constructor: this is a DI singleton built while
        // BakResourceSettings.GamePath may still be unset (GameInitializationService prompts for it),
        // and nothing can ask where the 3D view is before there is a game to draw.
        private StartData _start;

        private StartData Start => _start ??= LoadStart();

        private static StartData LoadStart() {
            // Synchronous archive read, the same shape BakResourceLocator uses. The async
            // IResourceProviderService is not usable here — CanonicalRect is a plain property that
            // hit-testing and layout call during a frame.
            IResourceProvider provider =
                ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            return provider.GetResource<StartData>("START.DAT");
        }

        private int ViewportX => Start.ViewportX;
        private int ViewportY => Start.ViewportY;
        private int ViewportWidth => Start.ViewportWidth;
        private int ViewportHeight => Start.ViewportHeight;

        public Area CanonicalRect => new Area(ViewportX, ViewportY, ViewportWidth, ViewportHeight);

        public float ViewportAspect => (float)ViewportWidth / ViewportHeight;

        public int FocalLength => Start.FocalLength;

        public Rect ToScreenRect(Rect stageScreenRect) {
            // The stage has already resolved the fit (pillarboxed under Contain, full-window under
            // Fill). Mapping the RE-verified viewport rect proportionally into whatever box the
            // stage actually occupies is therefore fit-agnostic — and deletes the duplicate
            // Contain math this method used to carry.
            //
            // That deletion is NOT a pure no-op under Contain, and the one case where it differs is
            // a deliberate correction rather than a regression. The old math was
            // min(w/1600, h/1200) with a centring offset on BOTH axes — a letterbox as well as a
            // pillarbox. CanonicalStage never letterboxes: the shared PanelSettings match-HEIGHT
            // scaling pins the panel's logical height at 1200, so a Contain stage always fills the
            // window vertically and simply overflows horizontally on a window narrower than 4:3
            // (CanonicalStage.cs:26-31 — "windows narrower than the frame's aspect crop the stage
            // equally on both sides"). The old min() therefore invented a vertical letterbox that
            // was never on screen, and hit-tested against it. Above 4:3 the two agree exactly (the
            // faithfulness fence in WorldViewportTests pins that); below 4:3 they part company —
            // e.g. at 1280x1024 the old formula gave left 52 / width 1176 / bottom 454.4 /
            // height 484.8, this one gives 12.8 / 1254.4 / 450.56 / 517.12, and it is the new
            // numbers that match where the viewport is actually drawn. Nothing visible moves; a
            // latent hit-test bug at sub-4:3 aspects is fixed. Pinned by
            // ToScreenRect_ContainStageBelowFourThree_*.
            float sx = stageScreenRect.width / CanonicalWidth;
            float sy = stageScreenRect.height / CanonicalHeight;
            float width = ViewportWidth * sx;
            float height = ViewportHeight * sy;
            float left = stageScreenRect.x + ViewportX * sx;
            // Canonical y is measured from the top; Unity screen rects are bottom-left origin.
            float bottom = stageScreenRect.y + stageScreenRect.height - (ViewportY + ViewportHeight) * sy;
            return new Rect(left, bottom, width, height);
        }

        public Vector2Int RenderTextureSize(Rect stageScreenRect) {
            Rect r = ToScreenRect(stageScreenRect);
            return new Vector2Int(
                Mathf.Max(1, Mathf.RoundToInt(r.width)),
                Mathf.Max(1, Mathf.RoundToInt(r.height)));
        }
    }
}

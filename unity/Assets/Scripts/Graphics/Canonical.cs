namespace BakAgain.Graphics {
    /// <summary>
    /// The canonical coordinate spaces — single source of truth for every
    /// runtime consumer (stage, cutscene buffers, viewport, book layout).
    ///
    /// <para>
    /// All engine-independent data (extracted JSON, plugin-DLL models, mod
    /// overrides) carries coordinates in these square-pixel spaces; the
    /// conversion from the original DOS spaces happens once, in the extractor
    /// (<c>DotNetProjects/ResourceExtraction/Imaging/AspectCorrection.cs</c> —
    /// keep these factors in sync with it). Unity code never sees VGA/EGA
    /// pixels.
    /// </para>
    ///
    /// <list type="bullet">
    /// <item>VGA 320×200 → 1600×1200: ×5 horizontal, ×6 vertical. The unequal
    /// factors bake in the original's 6:5 non-square pixel aspect, so the
    /// canonical space is square-pixel and 4:3.</item>
    /// <item>EGA 640×350 (books) → 1280×960: ×2 horizontal, ×96/35 vertical
    /// (non-integral — derive from the frame sizes where needed).</item>
    /// </list>
    ///
    /// <para>
    /// <b>THIS IS NOT THE SHIPPING RESOLUTION, AND NOTHING AT RUNTIME MAY RELY
    /// ON IT.</b> The port targets <b>1920×1080</b>. 1600×1200 is only the space
    /// the extractor emits resources in, so it is the natural size of a
    /// <i>faithful</i> resource's <see cref="GameData.Resources.Layout.DesignFrame"/>
    /// — which is why an unmodded faithful screen pillarboxes at 16:9, and that
    /// is the intended presentation, not a defect to correct.
    /// </para>
    ///
    /// <para>
    /// A shipped mod supplies an alternative <b>responsive</b> UI, whose frames
    /// are whatever that mod declares. So runtime code takes its dimensions from
    /// the frame it was handed (<see cref="BakAgain.UI.CanonicalStage"/> already
    /// does) or from the resolved panel, never from these constants. Their only
    /// legitimate runtime uses are converting extracted VGA/EGA coordinates and
    /// serving as the last-resort fallback when a resource declares no frame at
    /// all. Reaching for <c>Canonical.Width</c> because you need "the screen
    /// width" is the mistake this paragraph exists to stop — a test or renderer
    /// written that way passes at 4:3 and is wrong everywhere the game ships.
    /// </para>
    /// </summary>
    public static class Canonical {
        /// <summary>Canonical frame width (VGA 320 × 5).</summary>
        public const int Width = 1600;

        /// <summary>Canonical frame height (VGA 200 × 6).</summary>
        public const int Height = 1200;

        /// <summary>Horizontal canonical px per VGA px.</summary>
        public const int VgaScaleX = 5;

        /// <summary>Vertical canonical px per VGA px (carries the ×1.2 pixel-aspect stretch).</summary>
        public const int VgaScaleY = 6;

        /// <summary>Canonical book-space width (EGA 640 × 2).</summary>
        public const int BookWidth = 1280;

        /// <summary>Canonical book-space height (EGA 350 × 96/35).</summary>
        public const int BookHeight = 960;

        /// <summary>Horizontal canonical px per EGA px (book space).</summary>
        public const int EgaScaleX = 2;
    }
}

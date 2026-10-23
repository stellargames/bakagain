namespace BakAgain.Core {
    /// <summary>The separately switchable parts of Enhanced mode (spec 2026-10-08).</summary>
    public enum EnhancedFeature {
        FullScreenTravel,
        MouseLook,
        PortraitRings,

        /// <summary>The game font drawn in Alegreya Medium instead of GAME.FNT; bound when the fonts are built, so at launch.</summary>
        BookFont,

        /// <summary>A book page turns with a curl instead of the original's cut (forward only).</summary>
        PageTurn,

        /// <summary>The inventory full-window, with examine and give made explicit (spec 2026-10-13).</summary>
        Inventory,

        /// <summary>The sky dome — sun, moons, stars, clouds — and the mountains hazed toward it (spec 2026-10-15).</summary>
        Sky,

        /// <summary>The sun light, its tint on the world and the time-of-day grading; brightness stays the original's.</summary>
        Lighting,

        /// <summary>The cast screen full-window in the original's materials: the ring a stone on a marble
        /// bench, the school figure a dim laser, the known spells holograms in it (TASK-878).</summary>
        Casting,

        /// <summary>Forward and back glide while held instead of stepping, in the original's Small
        /// steps with the same time per distance and the same per-step rules (TASK-966).</summary>
        ContinuousMovement,
    }
}

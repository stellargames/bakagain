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
    }
}

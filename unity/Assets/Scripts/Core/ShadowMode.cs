namespace BakAgain.Core {
    /// <summary>Enhanced shadows (spec 2026-10-15-enhanced-shadows-design, Q4): none, a soft blob under
    /// each sprite and model, or the sun's shadows with blobs where the sun casts none.</summary>
    public enum ShadowMode {
        Off,
        Blobs,
        Sun,
    }
}

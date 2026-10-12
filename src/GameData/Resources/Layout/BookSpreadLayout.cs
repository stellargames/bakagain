namespace GameData.Resources.Layout;

/// <summary>
/// Tuning for the open book drawn around a full-screen parchment
/// (docs/superpowers/specs/2026-10-11-book-spread-pillars-design.md in the private workspace).
/// Fractions of the shown page's height, so the book scales with the stage. The defaults are the
/// shipped values.
/// </summary>
public sealed class BookSpreadLayout
{
    /// <summary>How far the facing page sits below the shown one, so their torn edges do not mirror each other.</summary>
    public float FacingPageDrop { get; set; } = 0.012f;

    /// <summary>The width of the binding's shade, centred on the spine.</summary>
    public float ShadeWidth { get; set; } = 0.12f;

    /// <summary>The shade's strength: 0 is none, 1 is the crease map as drawn.</summary>
    public float ShadeOpacity { get; set; } = 0.85f;

    /// <summary>How far the shadow of a loose sheet on the table (the full map) reaches past its edges.</summary>
    public float SheetShadowWidth { get; set; } = 0.03f;

    /// <summary>How far that shadow sits below the sheet, as if lit from above and behind.</summary>
    public float SheetShadowDrop { get; set; } = 0.008f;

    /// <summary>The sheet shadow's strength at the sheet's edge.</summary>
    public float SheetShadowOpacity { get; set; } = 0.6f;
}

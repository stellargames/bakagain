namespace GameData.Resources.Layout;

/// <summary>
/// Sizing for Enhanced mode's book font (Alegreya Medium in place of GAME.FNT). The default is the
/// shipped value.
/// </summary>
public sealed class EnhancedFontLayout
{
    /// <summary>
    /// The font's em as a multiple of the game's dialog line pitch. The book font wraps with its own
    /// widths; surveyed 2026-10-09 over every narrative and spoken DDX entry in English and German,
    /// 0.757 (em 50 at the 66 px pitch) is the largest size at which no entry wraps to more lines
    /// than the original does in GAME.FNT, so no page or box gets longer.
    /// </summary>
    public float EmPerLinePitch { get; set; } = 0.757f;
}

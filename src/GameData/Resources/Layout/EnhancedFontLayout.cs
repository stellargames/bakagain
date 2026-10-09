namespace GameData.Resources.Layout;

/// <summary>
/// Sizing for Enhanced mode's book font (Alegreya Medium in place of GAME.FNT). The default is the
/// shipped value.
/// </summary>
public sealed class EnhancedFontLayout
{
    /// <summary>
    /// The font's em as a multiple of the game's dialog line pitch. Measured: at 1.04 the original
    /// Gorath dialog wraps line for line as it does in GAME.FNT (pitch 44 px against an em of 45.6 px).
    /// </summary>
    public float EmPerLinePitch { get; set; } = 1.04f;
}

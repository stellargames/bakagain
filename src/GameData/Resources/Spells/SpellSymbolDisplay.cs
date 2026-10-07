namespace GameData.Resources.Spells;

/// <summary>
/// How a school's spell symbols are drawn onto the casting ring — <c>UI_drawSpellSymbols</c>
/// (ovr173 @0x69252).
///
/// <para>Companion to <see cref="CastRingLayout"/>, which places the ring itself.</para>
/// </summary>
public static class SpellSymbolDisplay {
    /// <summary>
    /// <b>A spell symbol is a character in a font, not a bitmap.</b>
    /// </summary>
    /// <remarks>
    /// The routine selects a dedicated spell font and draws each symbol as a one-character string.
    /// The <c>FontGlyph</c> the extracted <c>SYMBOL&lt;n&gt;.DAT</c> carries per node is that
    /// character. A port that goes looking for a sprite sheet for these will not find one — and the
    /// distinction is not cosmetic, because the glyph's measured width is what centres it.
    /// </remarks>
    public static bool SymbolsAreFontGlyphs => true;

    /// <summary>
    /// <b>THE ROUTINE'S COLOUR ARGUMENTS NEVER REACH THE SCREEN.</b>
    /// </summary>
    /// <remarks>
    /// Established 2026-08-19 by reading the blitter rather than the caller. SPELL.FNT declares
    /// glyph format 3 in its header — a byte per pixel — and <c>drawGlyphClipped</c> (0x15c48)
    /// assigns <c>textColor = thatByte</c> for every byte of 5 or more before drawing it. The font's
    /// ink bytes are 0, 6, 35, 108 and 110, so <i>every</i> pixel it draws overwrites the colour the
    /// caller chose, and none of them fall in the 1..4 range that would be remapped instead.
    ///
    /// <para>So the fade-in produces a DELAY and not a fade — the seven passes still wait seven
    /// ticks each (<see cref="FadePasses"/>, <see cref="FadePassTicks"/>), and the symbols still
    /// appear gradually in the sense of arriving late, but they never change colour. The routine
    /// brightens each pass by the colour step, rests at <c>base + 12 * step</c>, and shimmers the
    /// selected symbol through eight pens from 208, one every four ticks — and none of it shows: the
    /// selected symbol draws exactly like its neighbours.</para>
    ///
    /// <para>Those colour rules are not modelled. A font with pens below 5 — a mod's, or a different
    /// symbol font — would obey them, but a port must not build a highlight out of them and call it
    /// faithful: for the shipped data there is no colour highlight on the casting ring to
    /// reproduce.</para>
    /// </remarks>
    public static bool ColourAppliesToTheShippedSymbolFont => false;

    /// <summary>
    /// <b>Only spells the caster can actually cast are drawn.</b>
    /// </summary>
    /// <remarks>
    /// Each symbol is gated on the same castable test the rest of the screen uses, so the ring shows
    /// this caster's repertoire rather than the school's. Drawing them all and greying the
    /// unavailable ones would be a different screen from the one the game shows.
    /// </remarks>
    public static bool OnlyCastableSymbolsAreDrawn => true;

    /// <summary>The line box the vertical centring halves, in original pixels.</summary>
    /// <remarks>
    /// <b>A HARD-CODED 10, not the glyph's own height.</b> <c>cspell_menu_animate_hilite</c> sets
    /// <c>iHeight = 10</c> once and subtracts <c>iHeight &gt;&gt; 1</c> from every symbol's Y, so
    /// every glyph is lifted by the SAME five pixels whatever its size — while the X offset really
    /// is half the measured width. The two axes are centred differently and it looks like an
    /// oversight in the original; reproducing it is the difference between the game's ring and a
    /// tidier one.
    ///
    /// <para><b>SPELL.FNT's own height is 9</b> — read from its header in the shipped archive,
    /// format 0xFD, 96 glyphs from character 0. So centring vertically on the glyph would lift a
    /// symbol by 4.5 rather than the fixed 5, leaving it half an original pixel low. Small, but the
    /// fixed lift is not derivable from the font at all: change the font and the two rules diverge
    /// by however much its height differs from ten.</para>
    /// </remarks>
    public const int LineBox = 10;

    /// <summary>Canonical-space vertical scale — VGA x6 down.</summary>
    /// <remarks>
    /// Converting here rather than at the call site is the house rule, the same one
    /// <see cref="Dialog.DialogButtonRow.CanonicalScaleY"/> states: it keeps the 320x200 space out
    /// of the UI layer.
    /// </remarks>
    public const int CanonicalScaleY = 6;

    /// <summary>Half the line box, in canonical pixels — what a renderer passes to
    /// <see cref="GlyphOrigin"/>.</summary>
    public const int HalfLineBoxCanonical = LineBox / 2 * CanonicalScaleY;

    /// <summary>
    /// Where a symbol's glyph is drawn, given its node position and the glyph's measured width.
    /// </summary>
    /// <param name="nodeX">The node's X, canonical.</param>
    /// <param name="nodeY">The node's Y, canonical.</param>
    /// <param name="glyphWidth">The measured width of the glyph, canonical.</param>
    /// <param name="halfLineBox">
    /// Half <see cref="LineBox"/> in the caller's units — canonical callers pass the scaled value,
    /// since the original's 5 is in its own pixels.
    /// </param>
    /// <remarks>
    /// <b>The stored position is the symbol's centre, not its corner.</b> The original subtracts half
    /// the measured text width and half the line box before drawing. Treating the node position as a
    /// top-left corner shifts every glyph right and down by half its own size — subtly wrong in a way
    /// that looks like a bad font rather than a bad offset.
    /// </remarks>
    public static (int X, int Y) GlyphOrigin(int nodeX, int nodeY, int glyphWidth, int halfLineBox) =>
        (nodeX - (glyphWidth / 2), nodeY - halfLineBox);

    /// <summary>Passes the fade-in runs.</summary>
    public const int FadePasses = 7;

    /// <summary>Timer ticks each pass waits before the next.</summary>
    public const int FadePassTicks = 7;

    /// <summary>
    /// <b>The spell font is selected for the draw and the game font restored on the way out.</b>
    /// </summary>
    /// <remarks>
    /// Anything drawing text after this routine gets the normal font back. A port that leaves the
    /// spell font active finds the next label rendered in spell glyphs.
    /// </remarks>
    public static bool RestoresTheGameFont => true;

    /// <summary>
    /// Whether the fade runs at all.
    /// </summary>
    /// <remarks>
    /// <b>A colour step of zero skips the animation entirely</b> — the loop's own exit test — which is
    /// how the screen redraws symbols without replaying the fade. So the same routine is both "fade
    /// in" and "just draw", chosen by that argument.
    /// </remarks>
    public static bool FadeRuns(int colourStep) => colourStep != 0;
}

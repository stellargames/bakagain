namespace GameData.Resources.Spells;

/// <summary>
/// The cast screen's opening reveal — <c>cspell_cast_menu_open_transition</c> /
/// <c>cspell_gfx_wipe_horizontal_split</c> (CSPELL.C:2081-2131).
/// </summary>
/// <remarks>
/// The panel is drawn off screen and copied over the world view one strip pair per timer tick,
/// from the centre outward: a band that widens by <see cref="StepVga"/> on each side until it
/// reaches the rect's left edge. Everything outside the rect (the party bar) is on screen at once.
/// </remarks>
public static class CastOpenWipe {
    // The rect the transition copies: gfx_present_dispatch(0xd, 0xb, 0x128, 0x66).
    public const int XVga = 0xd;
    public const int YVga = 0xb;
    public const int WidthVga = 0x128;
    public const int HeightVga = 0x66;

    /// <summary><c>step = w / 0x4c</c>, integer division: 3 VGA px.</summary>
    public const int StepVga = WidthVga / 0x4c;

    /// <summary>The game timer: <c>timer_install(0xd)</c>, 13 times the BIOS 18.2065 Hz.</summary>
    public const double TicksPerSecond = 18.2065 * 13;

    private const int ScaleX = 5;
    private const int ScaleY = 6;

    public static int CentreVga => XVga + (WidthVga >> 1);

    /// <summary>Ticks until the band reaches the left edge: the <c>while (left &gt;= x)</c> loop.</summary>
    public static int StepCount => (CentreVga - StepVga - XVga) / StepVga + 1;

    /// <summary>The revealed band after <paramref name="step"/> ticks (1-based), canonical x and
    /// width; the last step and beyond reveal the whole rect.</summary>
    public static (int X, int Width) RevealedBand(int step) {
        if (step >= StepCount) {
            return (XVga * ScaleX, WidthVga * ScaleX);
        }
        int left = CentreVga - StepVga * step;
        return (left * ScaleX, 2 * (CentreVga - left) * ScaleX);
    }

    /// <summary>The whole rect in canonical units.</summary>
    public static (int X, int Y, int Width, int Height) CanonicalRect =>
        (XVga * ScaleX, YVga * ScaleY, WidthVga * ScaleX, HeightVga * ScaleY);
}

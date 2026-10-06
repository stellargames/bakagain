namespace GameData.Resources.Menu;

/// <summary>
/// The main menu's V key: a version strip over the menu until the next click or key —
/// <c>mainmenu_save_show_ctrd_modal</c> (MAINMENU.C:1371-1393), reached from <c>case 0x2f</c>.
/// </summary>
/// <remarks>
/// <c>uiwidget_panel_draw_inset(0x6e, 0x36, 100, 0xc, 0xa9)</c>, then the version text centred at
/// x 160, y 0x38 in pen 0xae over a pen-0xaa shadow one pixel lower. It also blits CRET.BMX
/// centred at y 100, but the shipped game has no such file (the original logs "File 'CRET.BMX' not
/// found" and draws nothing), so there is no picture to port. Measured 2026-10-05, TASK-804.
/// </remarks>
public static class VersionBanner {
    /// <summary>The scancode that raises it.</summary>
    public const int Key = 0x2f;

    /// <summary>The strip, canonical 1600x1200: VGA (0x6e, 0x36) 100x12, scaled x5 / x6.</summary>
    public const int X = 0x6e * 5, Y = 0x36 * 6, Width = 100 * 5, Height = 0xc * 6;

    /// <summary>Fill and top edge; left edge; right edge; bottom edge (uiwidget_panel_draw_inset).</summary>
    public const int FillPen = 0xa9, LeftPen = 0xa9 + 2, RightPen = 0xa9 + 5, BottomPen = 0xa9 + 6;

    /// <summary>The text and its shadow (uiwidget_draw_text_shadowed_dflt).</summary>
    public const int TextPen = 0xae, ShadowPen = 0xaa;

    /// <summary>The palette the main menu is drawn in.</summary>
    public const string Palette = "OPTIONS.PAL";
}

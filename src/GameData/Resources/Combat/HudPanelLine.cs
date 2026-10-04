namespace GameData.Resources.Combat;

/// <summary>
/// One piece of text on the combat HUD's parchment, placed in canonical units (TASK-765).
/// </summary>
/// <remarks>
/// <b>The panels differ only in what they put on the parchment</b> — the parchment, the pen and the
/// placement are the same for all three (<c>combat_arena_draw_tgt_info_panel</c>,
/// <c>combat_arena_hud_melee_panel</c>, <c>combat_actor_draw_stats_panel</c>). Expressing the
/// content as positioned lines keeps the view free of any per-panel knowledge, so a new panel is a
/// model rather than a second renderer.
/// </remarks>
public readonly struct HudPanelLine {
    /// <param name="x">The original's x, in its 320x200 pixels — the panels are ported as the
    /// original computes them, and placed in canonical units here.</param>
    /// <param name="y">The original's y, in its pixels.</param>
    public HudPanelLine(string text, int x, int y, HudPanelAlign align = HudPanelAlign.Left) {
        Text = text;
        X = x * 5;
        Y = y * 6;
        Align = align;
    }

    /// <summary>The text drawn.</summary>
    public string Text { get; }

    /// <summary>Horizontal anchor in canonical units — what <see cref="Align"/> refers to.</summary>
    public int X { get; }

    /// <summary>Top of the glyph cell in canonical units.</summary>
    public int Y { get; }

    /// <summary>Which edge (or centre) <see cref="X"/> names.</summary>
    public HudPanelAlign Align { get; }
}

/// <summary>
/// How a line is placed against its x — the original's <c>font_draw_text_ds</c> conventions.
/// </summary>
/// <remarks>
/// <b>Right alignment is a real case, not a convenience.</b> The melee panel's swing column is
/// positioned as <c>x = 0x73 - textWidth; x += 0x3f</c>, which is a right edge at 0xb2 — the column
/// grows leftward as the word gets longer.
/// </remarks>
public enum HudPanelAlign {
    /// <summary>x is the left edge.</summary>
    Left,

    /// <summary>x is the centre — the original's <c>x -= width / 2</c>.</summary>
    Centre,

    /// <summary>x is the right edge — the original's <c>x -= width</c>.</summary>
    Right,
}

/// <summary>A horizontal rule drawn on the parchment, one original pixel tall, in canonical units.</summary>
/// <remarks>
/// Only the melee panel has any: two of them, one row apart in palette pens 2 and 3, which together
/// read as a bevelled underline rather than as two lines.
/// </remarks>
public readonly struct HudPanelRule {
    /// <param name="x">The original's x, in its pixels; likewise y and width.</param>
    public HudPanelRule(int x, int y, int width, int pen) {
        X = x * 5;
        Y = y * 6;
        Width = width * 5;
        Pen = pen;
    }

    /// <summary>One original pixel row.</summary>
    public int Height => 6;

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    /// <summary>Palette pen — the original's <c>bGfx_outline_color</c>.</summary>
    public int Pen { get; }
}

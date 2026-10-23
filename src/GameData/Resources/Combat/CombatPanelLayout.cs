namespace GameData.Resources.Combat;

using GameData.Resources.Layout;

/// <summary>
/// The fixed columns of the combat HUD's text panels, in design-frame (1600x1200) px — the x each
/// label and value is drawn at (TASK-825).
/// </summary>
/// <remarks>
/// <b>The defaults are the original's</b>, the immediate operands of the draw routines times five
/// (TASK-765). A translation is meant to fit them (owner, 2026-10-06; the overflow report names every
/// row that does not). <b>Moving a column is the fallback</b> for a language that cannot: a pack
/// carries <see cref="ResourceId"/> (<c>JSON/combatpanels.json</c>) naming only the columns it moves,
/// and the rest keep these values.
///
/// <para>Only the columns: the rows, the parchment and the assessment's wrap (which decides how
/// many rows the CD build shows) are not a translator's to move.</para>
/// </remarks>
public sealed class CombatPanelLayout {
    /// <summary>The override document's key: <c>JSON/combatpanels.json</c> in a pack or mod folder.</summary>
    public const string ResourceId = "combatpanels.json";

    /// <summary>The columns in effect: the shipped ones, or a pack's, installed with the UI strings.</summary>
    public static CombatPanelLayout Current { get; set; } = new();

    /// <summary>Left edge of the stats panel's labels (<c>combat_actor_draw_stats_panel</c>, x 0x4e).</summary>
    public LayoutLength StatsLabelX { get; set; } = Column(0x4e);

    /// <summary>Left edge of the stats panel's values, x 0x85.</summary>
    /// <remarks><b>0x85, not the target panel's 0x99.</b> The two panels put their values in
    /// different columns; sharing one would move this one 20 original px right.</remarks>
    public LayoutLength StatsValueX { get; set; } = Column(0x85);

    /// <summary>Left edge of the shoot and spell target panels' labels, x 0x4e. The no-target
    /// quarrel count sits 5 original px right of it.</summary>
    public LayoutLength TargetLabelX { get; set; } = Column(0x4e);

    /// <summary>Left edge of the target panels' values, x 0x99.</summary>
    public LayoutLength TargetValueX { get; set; } = Column(0x99);

    /// <summary>Left edge of the melee panel's thrust column (heading, values, button and the rule
    /// under the heading), x 0x53.</summary>
    public LayoutLength MeleeThrustX { get; set; } = Column(0x53);

    /// <summary>x the melee row labels ("Damage", "Accuracy") centre on, x 0x82.</summary>
    public LayoutLength MeleeRowLabelCentreX { get; set; } = Column(0x82);

    /// <summary>Right edge the swing heading and button grow leftward from: <c>0x73 + 0x3f</c>.</summary>
    public LayoutLength MeleeSwingRightX { get; set; } = Column(0xb2);

    /// <summary>x the swing damage value centres on, x 0xad.</summary>
    /// <remarks><b>The two swing values do not share a centre</b>: the accuracy arm subtracts a
    /// further 0x17 from a 0xbc origin, eight pixels left of the damage. Reproduced, not tidied.</remarks>
    public LayoutLength MeleeSwingDamageCentreX { get; set; } = Column(0xad);

    /// <inheritdoc cref="MeleeSwingDamageCentreX"/>
    public LayoutLength MeleeSwingAccuracyCentreX { get; set; } = Column(0xbc - 0x17);

    /// <summary>Distance from an assessment label to its number (CBENC.C, 50 original px).</summary>
    public LayoutLength AssessmentValueOffsetX { get; set; } = Column(50);

    private static LayoutLength Column(int originalX) => LayoutLength.Px(originalX * OriginalPixel.Width);
}

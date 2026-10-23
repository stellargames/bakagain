namespace GameData.Resources.Combat;

using System.Collections.Generic;
using GameData.Resources.Layout;

/// <summary>
/// The melee attack preview — <c>combat_arena_hud_melee_panel</c> (canassa COMBAT.C:1240).
/// </summary>
/// <remarks>
/// <b>It is not the idle HUD and it is not the melee menu's panel.</b> The turn loop draws it under
/// <c>stateA == 0</c> alone (COMBAT.C:2566), and the loop <i>opens</i> at <c>stateA = 1</c> — 0 is
/// written in exactly one place, the arm where the cursor is over a reachable enemy and a click
/// would attack it. So this is the melee counterpart of
/// <see cref="ShootTargetPanel"/>: the numbers for the attack the next click would make, against
/// the enemy under the cursor. (The idle panel in state 1 is the actor stats panel, a different
/// one.)
///
/// <para><b>Its bottom row is the only place the game tells the player about the two buttons.</b>
/// "Left" sits under the thrust column and "Right" under the swing column, with no values — they
/// are labels for the mouse buttons, not for anything above them.</para>
///
/// <para><b>THE NUMBERS ARE AN ESTIMATE, NOT THE ROLL.</b> Accuracy here is a bare
/// <c>meleeAccuracy + weaponAccuracy</c>: no class affinity, no condition scaling, no blessing, and
/// <b>no subtraction of the target's defence</b>, all of which <see cref="CombatFormulas.MeleeHitChance"/>
/// applies. Damage likewise skips the weapon's condition scaling that
/// <see cref="CombatFormulas.MeleeDamage"/> applies. The panel therefore reads high against a
/// defended target and high again for a worn weapon — reproduce it as shipped rather than
/// "correcting" it to the real chance, which would make the readout disagree with the original for
/// every character in the game.</para>
/// </remarks>
public static class MeleeStatsPanel {
    /// <summary>Top of the column headings.</summary>
    public const int HeadingY = 0x86;

    /// <summary>Width of the bevelled rule under the thrust heading.</summary>
    public const int RuleWidth = 0x5e;

    /// <summary>Rows between the heading and the damage row.</summary>
    public const int DamageRowOffset = 0xf;

    /// <summary>Rows between the damage row and the accuracy row.</summary>
    public const int AccuracyRowOffset = 0xa;

    /// <summary>Rows between the accuracy row and the button row.</summary>
    public const int ButtonRowOffset = 0xc;

    /// <summary>Column headings.</summary>
    public static string ThrustHeading => Text.UiStrings.Get("base:uistring:combat.weapon_table_thrust"); // TASK-775: the EXE's own entry, so a pack translates it

    /// <inheritdoc cref="ThrustHeading"/>
    public static string SwingHeading => Text.UiStrings.Get("base:uistring:combat.weapon_table_swing"); // TASK-775: the EXE's own entry, so a pack translates it

    /// <summary>Row labels.</summary>
    public static string DamageLabel => Text.UiStrings.Get("base:uistring:combat.weapon_table_damage"); // TASK-775: the EXE's own entry, so a pack translates it

    /// <inheritdoc cref="DamageLabel"/>
    public static string AccuracyLabel => Text.UiStrings.Get("base:uistring:combat.weapon_table_accuracy"); // TASK-775: the EXE's own entry, so a pack translates it

    /// <summary>The mouse-button labels on the bottom row.</summary>
    public static string ThrustButton => Text.UiStrings.Get("base:uistring:combat.weapon_table_left"); // TASK-775: the EXE's own entry, so a pack translates it

    /// <inheritdoc cref="ThrustButton"/>
    public static string SwingButton => Text.UiStrings.Get("base:uistring:combat.weapon_table_right"); // TASK-775: the EXE's own entry, so a pack translates it

    /// <summary>Palette pens of the two rows of the bevelled rule, in draw order.</summary>
    public static readonly int[] RulePens = { 2, 3 };

    /// <summary>Damage never reads below this.</summary>
    public const int MinDamage = 1;

    /// <summary>
    /// <b>The whole swing column is hidden unless a swing is possible.</b>
    /// </summary>
    /// <remarks>
    /// The same two conditions the right-click arm tests — the target orthogonally adjacent, and the
    /// attacker's combined health+stamina above 1 — so the column is absent exactly when the button
    /// would refuse. The heading, both values and the "Right" label all go; the thrust column stays.
    ///
    /// <para>The gates read <c>stat_actor_get(actor, 0x10, <b>0</b>)</c> here against
    /// <c>(actor, 0x10, <b>4</b>)</c> in the click arm. <b>They cannot disagree</b>: mode 4 only
    /// skips the health-ratio scaling, and that ratio is 0 for Health and Stamina
    /// (<c>StatEngine</c>'s <c>HealthRatio</c> table), which is what stat 0x10 sums.</para>
    /// </remarks>
    public static bool ShowsSwingColumn(bool targetOrthogonallyAdjacent, int healthStaminaPool) =>
        targetOrthogonallyAdjacent
        && CombatActionDispatch.HasReservesFor(
            CombatActionDispatch.MeleeAttack.Swing, healthStaminaPool);

    /// <summary>
    /// The accuracy the panel prints — <b>not the hit chance</b>. See the type remarks.
    /// </summary>
    public static int AccuracyShown(int meleeAccuracy, int weaponAccuracy) {
        int shown = meleeAccuracy + weaponAccuracy;
        return shown < CombatFormulas.MinHitChance ? CombatFormulas.MinHitChance : shown;
    }

    /// <summary>
    /// The damage the panel prints — <b>not the damage roll</b>. See the type remarks.
    /// </summary>
    public static int DamageShown(int weaponBase, int strength, int enchantmentBonus) {
        int shown = weaponBase + strength + enchantmentBonus;
        return shown < MinDamage ? MinDamage : shown;
    }

    /// <summary>What the panel puts on the parchment.</summary>
    /// <param name="showSwing">From <see cref="ShowsSwingColumn"/>.</param>
    /// <param name="layout">The columns; <see cref="CombatPanelLayout.Current"/> when null.</param>
    public static IReadOnlyList<HudPanelLine> Lines(
        bool showSwing, int thrustDamage, int thrustAccuracy, int swingDamage, int swingAccuracy,
        CombatPanelLayout? layout = null) {
        layout ??= CombatPanelLayout.Current;
        LayoutLength thrustX = layout.MeleeThrustX, swingRightX = layout.MeleeSwingRightX;
        int damageY = HeadingY + DamageRowOffset;
        int accuracyY = damageY + AccuracyRowOffset;
        int buttonY = accuracyY + ButtonRowOffset;

        var lines = new List<HudPanelLine> {
            new HudPanelLine(ThrustHeading, thrustX, HeadingY),
            new HudPanelLine(DamageLabel, layout.MeleeRowLabelCentreX, damageY, HudPanelAlign.Centre),
            new HudPanelLine(thrustDamage.ToString(), thrustX, damageY),
            new HudPanelLine(AccuracyLabel, layout.MeleeRowLabelCentreX, accuracyY, HudPanelAlign.Centre),
            new HudPanelLine(
                Text.UiTemplates.Percent(thrustAccuracy), thrustX, accuracyY),
            new HudPanelLine(ThrustButton, thrustX, buttonY),
        };
        if (!showSwing) {
            return lines;
        }

        lines.Add(new HudPanelLine(SwingHeading, swingRightX, HeadingY, HudPanelAlign.Right));
        lines.Add(new HudPanelLine(
            swingDamage.ToString(), layout.MeleeSwingDamageCentreX, damageY, HudPanelAlign.Centre));
        lines.Add(new HudPanelLine(Text.UiTemplates.Percent(swingAccuracy),
            layout.MeleeSwingAccuracyCentreX, accuracyY, HudPanelAlign.Centre));
        lines.Add(new HudPanelLine(SwingButton, swingRightX, buttonY, HudPanelAlign.Right));
        return lines;
    }

    /// <summary>The bevelled underline below the thrust heading. Always drawn, both rows.</summary>
    public static IReadOnlyList<HudPanelRule> Rules(CombatPanelLayout? layout = null) {
        layout ??= CombatPanelLayout.Current;
        var rules = new List<HudPanelRule>();
        for (var row = 0; row < RulePens.Length; row++) {
            rules.Add(new HudPanelRule(
                layout.MeleeThrustX, HeadingY + 0xa + row, RuleWidth, RulePens[row]));
        }
        return rules;
    }
}

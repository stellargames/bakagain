namespace GameData.Resources.Combat;

using System.Collections.Generic;

/// <summary>
/// The parchment while a combat spell waits for its target — <c>combat_arena_draw_tgt_info_hud</c>
/// (COMBAT.C:1092-1149): "Choose a target", the spell's name, and for a damage spell over a living
/// enemy its accuracy and damage. Same plaque and pens as <see cref="ShootTargetPanel"/>.
/// </summary>
public static class SpellTargetPanel {
    /// <summary>The one damage-kind spell the panel never rates: <c>param_2 != 0x2c</c>.</summary>
    public const int UnratedSpellId = 0x2c;

    /// <summary>Whether the accuracy and damage lines appear: a living encounter actor under the
    /// cursor, a kind-0 spell, and not spell 0x2c.</summary>
    public static bool ShowsTargetStats(bool liveEnemy, int spellKind, int spellId) =>
        liveEnemy && spellKind == 0 && spellId != UnratedSpellId;

    public static IReadOnlyList<HudPanelLine> Lines(string? spellName, bool showStats, int accuracy, int damage) {
        int y = ShootTargetPanel.PromptY;
        var lines = new List<HudPanelLine> {
            new HudPanelLine(ShootTargetPanel.Prompt, ShootTargetPanel.CentreX, y, HudPanelAlign.Centre),
        };
        if (!string.IsNullOrEmpty(spellName)) {
            y += ShootTargetPanel.LineStep;
            lines.Add(new HudPanelLine(spellName, ShootTargetPanel.CentreX, y, HudPanelAlign.Centre));
        }
        y += ShootTargetPanel.StatsGap;
        if (!showStats) {
            return lines;
        }
        // The spell panel prints the chance as computed: no 2% floor, unlike the shot's.
        lines.Add(new HudPanelLine(ShootTargetPanel.AccuracyLabel, ShootTargetPanel.LabelX, y));
        lines.Add(new HudPanelLine(accuracy + ShootTargetPanel.PercentSign, ShootTargetPanel.ValueX, y));
        y += ShootTargetPanel.LineStep;
        lines.Add(new HudPanelLine(ShootTargetPanel.DamageLabel, ShootTargetPanel.LabelX, y));
        lines.Add(new HudPanelLine(damage.ToString(), ShootTargetPanel.ValueX, y));
        return lines;
    }
}

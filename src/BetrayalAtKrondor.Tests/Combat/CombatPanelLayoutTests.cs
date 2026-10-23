namespace BetrayalAtKrondor.Tests.Combat;

using System.Collections.Generic;
using System.Linq;
using GameData.Resources.Combat;
using GameData.Resources.Layout;
using Xunit;

/// <summary>
/// The combat panels' fixed columns (TASK-825): the original's, unless a language pack moves them.
/// </summary>
public class CombatPanelLayoutTests {
    private static int X(IReadOnlyList<HudPanelLine> lines, string text) => lines.Single(l => l.Text == text).X;

    [Fact]
    public void WithNoOverride_EveryColumnIsTheOriginals() {
        // Canonical x = the original's x * 5. Pinned as literals: a control that reads the same
        // default it checks would pass whatever the default became.
        IReadOnlyList<HudPanelLine> stats = ActorStatsPanel.Lines("Locklear", new[] { 41, 33, 7, 25 });
        Assert.Equal(0x4e * 5, X(stats, "Health:"));
        Assert.Equal(0x85 * 5, X(stats, "41"));

        IReadOnlyList<HudPanelLine> shoot = ShootTargetPanel.Lines(
            ShootTargetPanel.ForTarget(new[] { "Quarrel" }, rawHitChance: 37, damage: 12));
        Assert.Equal(0x4e * 5, X(shoot, ShootTargetPanel.AccuracyLabel));
        Assert.Equal(0x99 * 5, X(shoot, "37%"));
        Assert.Equal(0x99 * 5, X(shoot, "12"));
        IReadOnlyList<HudPanelLine> noTarget = ShootTargetPanel.Lines(
            ShootTargetPanel.WithoutTarget(new[] { "Quarrel" }, quarrelsRemaining: 7));
        Assert.Equal((0x4e + 5) * 5, noTarget.Last().X);

        IReadOnlyList<HudPanelLine> spell = SpellTargetPanel.Lines("Flamecast", showStats: true, 9, 20);
        Assert.Equal(0x4e * 5, X(spell, SpellTargetPanel.AccuracyLabel));
        Assert.Equal(0x99 * 5, X(spell, "9%"));

        IReadOnlyList<HudPanelLine> melee = MeleeStatsPanel.Lines(true, 11, 22, 33, 44);
        Assert.Equal(0x53 * 5, X(melee, MeleeStatsPanel.ThrustHeading));
        Assert.Equal(0x53 * 5, X(melee, "11"));
        Assert.Equal(0x82 * 5, X(melee, MeleeStatsPanel.DamageLabel));
        Assert.Equal(0xb2 * 5, X(melee, MeleeStatsPanel.SwingHeading));
        Assert.Equal(0xad * 5, X(melee, "33"));
        Assert.Equal(0xa5 * 5, X(melee, "44%"));
        Assert.All(MeleeStatsPanel.Rules(), r => Assert.Equal(0x53 * 5, r.X));

        // The assessment's number sits 50 original px right of its label, whichever column.
        Assert.Equal(0x46 * 5 + 250, CombatAssessment.ValueLine("40", 0x46, 0x44).X);
        Assert.Equal((0x46 + 100) * 5 + 250, CombatAssessment.ValueLine("40", 0x46 + 100, 0x44).X);
    }

    [Fact]
    public void APackThatMovesAColumnMovesEveryLineInIt_AndNothingElse() {
        var moved = new CombatPanelLayout {
            StatsValueX = LayoutLength.Px(700),
            TargetLabelX = LayoutLength.Px(360),
            TargetValueX = LayoutLength.Px(800),
            MeleeThrustX = LayoutLength.Px(400),
            MeleeRowLabelCentreX = LayoutLength.Px(660),
            MeleeSwingRightX = LayoutLength.Px(900),
            MeleeSwingDamageCentreX = LayoutLength.Px(870),
            MeleeSwingAccuracyCentreX = LayoutLength.Px(830),
            AssessmentValueOffsetX = LayoutLength.Px(300),
        };

        IReadOnlyList<HudPanelLine> stats = ActorStatsPanel.Lines("Locklear", new[] { 41, 33, 7, 25 }, moved);
        Assert.Equal(700, X(stats, "41"));
        Assert.Equal(0x4e * 5, X(stats, "Health:"));   // not overridden: the original's

        IReadOnlyList<HudPanelLine> shoot = ShootTargetPanel.Lines(
            ShootTargetPanel.ForTarget(new[] { "Quarrel" }, rawHitChance: 37, damage: 12), moved);
        Assert.Equal(360, X(shoot, ShootTargetPanel.AccuracyLabel));
        Assert.Equal(800, X(shoot, "37%"));
        IReadOnlyList<HudPanelLine> noTarget = ShootTargetPanel.Lines(
            ShootTargetPanel.WithoutTarget(new[] { "Quarrel" }, quarrelsRemaining: 7), moved);
        Assert.Equal(360 + 25, noTarget.Last().X);   // the count keeps its 5 px indent off the label column

        IReadOnlyList<HudPanelLine> spell = SpellTargetPanel.Lines("Flamecast", showStats: true, 9, 20, moved);
        Assert.Equal(360, X(spell, SpellTargetPanel.AccuracyLabel));
        Assert.Equal(800, X(spell, "20"));

        IReadOnlyList<HudPanelLine> melee = MeleeStatsPanel.Lines(true, 11, 22, 33, 44, moved);
        Assert.Equal(400, X(melee, "11"));
        Assert.Equal(660, X(melee, MeleeStatsPanel.AccuracyLabel));
        Assert.Equal(900, X(melee, MeleeStatsPanel.SwingButton));
        Assert.Equal(870, X(melee, "33"));
        Assert.Equal(830, X(melee, "44%"));
        Assert.All(MeleeStatsPanel.Rules(moved), r => Assert.Equal(400, r.X));

        Assert.Equal(0x46 * 5 + 300, CombatAssessment.ValueLine("40", 0x46, 0x44, moved).X);
    }
}

namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>combat_arena_draw_tgt_info_hud, COMBAT.C:1092-1149 (TASK-821).</summary>
public class SpellTargetPanelTests {
    [Fact]
    public void ThePromptAndTheSpellNameAlwaysShow() {
        var lines = SpellTargetPanel.Lines("Flamecast", showStats: false, 0, 0);
        Assert.Equal(2, lines.Count);
        Assert.Equal("Choose a target", lines[0].Text);
        Assert.Equal(0x84 * 6, lines[0].Y);
        Assert.Equal("Flamecast", lines[1].Text);
        Assert.Equal((0x84 + 10) * 6, lines[1].Y);
    }

    [Fact]
    public void ADamageSpellOverAnEnemyRatesItBelowTheName() {
        var lines = SpellTargetPanel.Lines("Flamecast", showStats: true, 1, 20);
        Assert.Equal(6, lines.Count);
        Assert.Equal("Accuracy:", lines[2].Text);
        Assert.Equal((0x84 + 10 + 12) * 6, lines[2].Y);
        Assert.Equal("1%", lines[3].Text);   // no 2% floor on the spell panel
        Assert.Equal("Damage:", lines[4].Text);
        Assert.Equal("20", lines[5].Text);
        Assert.Equal((0x84 + 10 + 12 + 10) * 6, lines[5].Y);
    }

    [Theory]
    [InlineData(true, 0, 4, true)]
    [InlineData(false, 0, 4, false)]
    [InlineData(true, 1, 4, false)]
    [InlineData(true, 0, 0x2c, false)]
    public void StatsNeedALiveEnemyAKindZeroSpellAndNot0x2c(bool enemy, int kind, int id, bool expected) {
        Assert.Equal(expected, SpellTargetPanel.ShowsTargetStats(enemy, kind, id));
    }
}

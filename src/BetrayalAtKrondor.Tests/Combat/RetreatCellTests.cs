namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>
/// TASK-1150: <c>combataiturn_pick_tile_or_attack</c> (CBTAITRN.C:32-87) scores every free cell on
/// the board whose trial walk goes through, by the nearest foe's distance AT that cell, and the
/// creature then walks its speed toward the winner. It does not limit itself to cells it can reach.
/// </summary>
public class RetreatCellTests {
    private static int NearestTo((int X, int Y)[] foes, int x, int y) {
        int best = int.MaxValue;
        foreach ((int fx, int fy) in foes) {
            best = System.Math.Min(best, CombatGrid.ChebyshevDistance(x, y, fx, fy));
        }
        return best;
    }

    [Fact]
    public void TheWholeBoardIsScored_NotOnlyTheCellsInReach() {
        // One foe at (4,1), the creature beside it at (4,2) with speed 4. Every cell on row 12 is 11
        // from the foe, the farthest anywhere; scanning x 7..0, y 12..0 meets (7,12) first, and with
        // no tie roll under 0x33 nothing displaces it. A search held to Chebyshev 4 would stop at row 6.
        var grid = new CombatGrid();
        var actor = new Combatant { X = 4, Y = 2, Speed = 4 };
        (int X, int Y)[] foes = { (4, 1) };

        (int x, int y) = MonsterSpellcasting.PickRetreatCell(grid, actor, actor.Speed,
            (cx, cy) => NearestTo(foes, cx, cy), (cx, cy) => cx == 4 && cy == 1, _ => 99);

        Assert.Equal((7, 12), (x, y));
    }

    [Fact]
    public void AnEqualCellTakesOverOnARollUnder0x33() {
        var grid = new CombatGrid();
        var actor = new Combatant { X = 4, Y = 2, Speed = 4 };
        (int X, int Y)[] foes = { (4, 1) };

        (int x, int y) = MonsterSpellcasting.PickRetreatCell(grid, actor, actor.Speed,
            (cx, cy) => NearestTo(foes, cx, cy), (cx, cy) => cx == 4 && cy == 1, _ => 0);

        Assert.Equal((0, 12), (x, y));   // every later tie on row 12 displaced the one before
    }
}

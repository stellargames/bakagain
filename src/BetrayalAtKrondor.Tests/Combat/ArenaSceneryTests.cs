namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>Scenery walling its arena cell — combatgrid_build_combatant_list (CMBTGRID.C:390).</summary>
public class ArenaSceneryTests {
    private const int Cell = 300;
    private const int Tree = 5;
    private const int Bag = 41;       // listed, does not block
    private const int StoneSlab = 29;

    // The world offset (heading 0) of a cell's centre.
    private static ArenaScenery.Placement At(int kind, int x, int y) =>
        new(kind, (x * Cell) + (Cell / 2) - 0x4b0, (y * Cell) + (Cell / 2) + 0xc80);

    [Fact]
    public void ATreeInTheBackRowsWallsItsCell() {
        var grid = new CombatGrid();

        var walled = ArenaScenery.Wall(grid, new[] { At(Tree, 6, 11) }, 0, 0, 0, Cell);

        Assert.Equal(new[] { (6, 11) }, walled);
        Assert.True(grid.IsBlocked(6, 11));
    }

    [Fact]
    public void ATreeNearTheCameraIsLeftOut_ButAStoneSlabIsNot() {
        var grid = new CombatGrid();

        ArenaScenery.Wall(grid, new[] { At(Tree, 3, 3), At(StoneSlab, 4, 4) }, 0, 0, 0, Cell);

        Assert.False(grid.IsBlocked(3, 3));
        Assert.True(grid.IsBlocked(4, 4));
    }

    [Fact]
    public void TheFirstObjectToClaimACellKeepsIt() {
        // A bag earlier in the file takes the cell's table slot, so the tree after it walls nothing.
        var grid = new CombatGrid();

        ArenaScenery.Wall(grid, new[] { At(Bag, 6, 11), At(Tree, 6, 11) }, 0, 0, 0, Cell);

        Assert.False(grid.IsBlocked(6, 11));
    }

    [Fact]
    public void TheWalkStopsWhenTheTableIsFull() {
        var grid = new CombatGrid();
        var objects = new System.Collections.Generic.List<ArenaScenery.Placement>();
        for (var x = 0; x < 8; x++) {
            objects.Add(At(Bag, x, 12));
            objects.Add(At(Bag, x, 11));
        }
        objects.Add(At(Tree, 3, 10)); // the 17th, after 15 bags

        ArenaScenery.Wall(grid, objects, 0, 0, 0, Cell);

        Assert.False(grid.IsBlocked(3, 10));
    }

    [Fact]
    public void TheOffsetIsCutTo16Bits() {
        // 65536 units away lands on the same cell as 0 away.
        Assert.Equal(ArenaScenery.WorldToCell(0, 5000, 0, Cell),
            ArenaScenery.WorldToCell(65536, 5000, 0, Cell));
    }

    [Fact]
    public void AQuarterTurnSwapsTheAxes() {
        // The inverse of the sweep's own rotation (ProximityMath.Rotate): at a quarter turn, ahead
        // is world -x.
        Assert.NotNull(ArenaScenery.WorldToCell(0, 5000, 0, Cell));
        Assert.Equal(ArenaScenery.WorldToCell(0, 5000, 0, Cell),
            ArenaScenery.WorldToCell(-5000, 0, 0x4000, Cell));
    }

    // --- what the fight still draws (PROXSCAN.C:326-331 + CMBTGRID.C:409-418), TASK-790 ---

    private static bool Hidden(ArenaScenery.Placement p, params ArenaScenery.Placement[] partyTile) =>
        ArenaScenery.HiddenInFight(p, ArenaScenery.Kept(partyTile, 0, 0, 0, Cell), 0, 0, 0, Cell);

    [Fact]
    public void ATreeInTheWedgeNearestTheCameraIsGone() {
        ArenaScenery.Placement tree = At(Tree, 3, 8);
        Assert.True(ArenaScenery.InNearWedge(3, 8));

        Assert.True(Hidden(tree, tree), "the original deletes it: not in the backdrop, not in the table");
    }

    [Fact]
    public void ATreeTheTableKeepsIsStillDrawn() {
        ArenaScenery.Placement tree = At(Tree, 6, 11);

        Assert.False(Hidden(tree, tree));
    }

    [Fact]
    public void ScenerySomeOtherTileOwnsIsGoneFromTheArena() {
        // Only the party's own tile is re-added; a tree on the grid from the next tile is not.
        Assert.True(Hidden(At(Tree, 6, 11)));
    }

    [Fact]
    public void ScenerySoCloseItIsOffTheGridIsGoneToo() {
        var underfoot = new ArenaScenery.Placement(Tree, 0, 1000);
        Assert.Null(ArenaScenery.WorldToCell(underfoot.X, underfoot.Y, 0, Cell));

        Assert.True(Hidden(underfoot, underfoot), "within 4000 units");
        Assert.False(Hidden(new ArenaScenery.Placement(Tree, 20000, 20000)), "far off the grid stays");
    }

    [Fact]
    public void UnlistedKindsAreNotTouched() {
        // Only combatgrid_obj_kind_prox_actv's kinds leave the backdrop.
        Assert.False(Hidden(At(1, 3, 8)));
    }
}

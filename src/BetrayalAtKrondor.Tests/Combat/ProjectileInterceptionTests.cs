namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// <see cref="ProjectileInterception"/> — crystal ground stops a shot (CACTOR.C:1114-1130; forum p=60).
/// </summary>
public class ProjectileInterceptionTests {
    private const int Crystal = CrystalChain.FirstCrystalId;

    // Two crystals at (3,2) and (3,6): the run between them is column 3, rows 3..5.
    private static TrapPuzzle TwoPoles() =>
        TrapPuzzleBuilder.Build(new List<(int, int, int)> { (Crystal, 3, 2), (Crystal, 3, 6) });

    [Fact]
    public void AShotAcrossTheRunBetweenTwoPolesIsSwallowed() {
        // A blaster at (0,4) firing east at a party member at (6,4) crosses (3,4).
        TrapPuzzle puzzle = TwoPoles();

        var outcome = ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((0, 4), (6, 4)), ProjectileInterception.CollapsingShape);

        Assert.Equal(ProjectileInterception.Fate.Zapped, outcome.Fate);
        Assert.Equal((3, 4), (outcome.X, outcome.Y));
        Assert.Equal(new[] { (3, 3), (3, 4), (3, 5) }, outcome.LitRun.Where(c => c.Y is >= 3 and <= 5).OrderBy(c => c.Y).ToArray());
        Assert.Equal(CombatTerrain.Crystal, puzzle.Grid.TerrainAt(3, 4));   // the run is still there
    }

    [Fact]
    public void AShotThatCrossesNoRunReachesItsTarget() {
        var outcome = ProjectileInterception.Fly(TwoPoles(),
            ProjectileInterception.StraightPath((0, 1), (6, 1)), ProjectileInterception.CollapsingShape);

        Assert.Equal(ProjectileInterception.Fate.Reaches, outcome.Fate);
    }

    [Fact]
    public void AFlamecastShotIntoAPoleCollapsesItsRun() {
        // Down column 3 from a blaster at (3,0): the first crystal cell is the pole at (3,2).
        TrapPuzzle puzzle = TwoPoles();

        var outcome = ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((3, 0), (3, 4)), ProjectileInterception.CollapsingShape);

        Assert.Equal(ProjectileInterception.Fate.Collapsed, outcome.Fate);
        Assert.NotEqual(CombatTerrain.Crystal, puzzle.Grid.TerrainAt(3, 4));
    }

    [Fact]
    public void AnyOtherShotIntoAPoleIsStoppedAndLeavesTheRun() {
        TrapPuzzle puzzle = TwoPoles();

        var outcome = ProjectileInterception.Fly(puzzle,
            ProjectileInterception.StraightPath((3, 0), (3, 4)), shape: 3);

        Assert.Equal(ProjectileInterception.Fate.Stopped, outcome.Fate);
        Assert.Equal(CombatTerrain.Crystal, puzzle.Grid.TerrainAt(3, 4));
    }

    [Fact]
    public void TheStraightPathSkipsTheShooterAndEndsOnTheTarget() {
        Assert.Equal(new[] { (1, 4), (2, 4), (3, 4) }, ProjectileInterception.StraightPath((0, 4), (3, 4)).ToArray());
        Assert.Equal(new[] { (2, 1), (2, 0) }, ProjectileInterception.StraightPath((2, 2), (2, 0)).ToArray());
    }
}

namespace BetrayalAtKrondor.Tests.Combat;

using System.Collections.Generic;
using GameData.Resources.Combat;
using Xunit;

/// <summary>
/// Enhanced combat's shortest routes (owner, 2026-10-29: "do shortest path"): a breadth-first search over
/// the walker's own step rules, beside the original's greedy walk, which Classic keeps.
/// </summary>
public class CombatShortestPathTests {
    // The owner's case: the foe's side nearest the attacker is taken, so the original's approach goes
    // round to the far side; the free side beside the attacker is two steps away.
    private static (CombatGrid Grid, Combatant Attacker) OwnersCase() {
        var grid = new CombatGrid();
        grid.SetOccupied(1, 3, true);   // the foe
        grid.SetOccupied(1, 2, true);   // its neighbour nearest the attacker, taken
        grid.SetOccupied(2, 1, true);   // the attacker
        return (grid, new Combatant { X = 2, Y = 1, Speed = 5 });
    }

    [Fact]
    public void TheApproachTakesTheShortestRoute_WhereTheOriginalsGreedyWalkGoesRound() {
        var (grid, attacker) = OwnersCase();

        List<(int X, int Y)>? shortest = CombatWalk.ShortestApproach(grid, 2, 1, 1, 3, attacker.Speed + 1);
        Assert.Equal(new List<(int, int)> { (2, 2), (2, 3) }, shortest);

        // Classic keeps the original's route: the -x side, four steps round (CACTOR.C:1666-1706).
        var trace = new List<(int X, int Y)>();
        CombatWalk.MeleeApproach(grid, attacker, 1, 3, attacker.Speed, probe: true, trace: trace);
        Assert.Equal(new List<(int, int)> { (2, 1), (2, 2), (2, 3), (1, 4), (0, 3) }, trace);
    }

    // Mobile3 review I1: where the original's greedy walk is already shortest, the route IS the original's
    // (diagonal first, as CombatMovement.Step steps); Enhanced differs only where greedy detours.
    [Theory]
    [InlineData(1, 1, 4, 2)]
    [InlineData(4, 8, 1, 3)]
    [InlineData(0, 0, 7, 12)]
    [InlineData(6, 2, 2, 2)]
    [InlineData(3, 9, 3, 1)]
    public void InOpenGroundTheRouteIsTheOriginalsOwn(int fx, int fy, int tx, int ty) {
        AssertSameAsTheOriginal(new CombatGrid(), fx, fy, tx, ty);
    }

    [Fact]
    public void AroundAWallTheGreedyWalkHandles_TheRouteIsTheOriginalsOwn() {
        var grid = new CombatGrid();
        grid.SetTerrain(3, 3, CombatTerrain.Wall);
        AssertSameAsTheOriginal(grid, 1, 1, 5, 5);   // the wall sits on the diagonal; Step slides round it
        AssertSameAsTheOriginal(grid, 3, 1, 3, 6);   // straight down into it
    }

    private static void AssertSameAsTheOriginal(CombatGrid grid, int fx, int fy, int tx, int ty) {
        var walker = new Combatant { X = fx, Y = fy };
        var trace = new List<(int X, int Y)>();
        CombatWalk.WalkResult greedy = CombatWalk.Walk(grid, walker, tx, ty, 20, probe: true, trace: trace);
        Assert.True(greedy.Arrived);
        trace.RemoveAt(0);   // the start
        List<(int X, int Y)>? shortest = CombatWalk.ShortestPath(grid, fx, fy, tx, ty, 20);
        Assert.Equal(trace.Count, shortest!.Count);   // the original is already shortest here
        Assert.Equal(trace, shortest);
    }

    [Fact]
    public void TiesGoToTheOriginalsTryOrder_TheSideNearestTheAttackerFirst() {
        var grid = new CombatGrid();
        grid.SetOccupied(3, 5, true);
        // From (3,2) the -y side (3,4) is nearest and two steps away; nothing else is as short.
        Assert.Equal((3, 4), CombatWalk.ShortestApproach(grid, 3, 2, 3, 5, 6)![^1]);
        // From (0,5) the -x side (2,5) is two steps; the +y/-y sides are three.
        Assert.Equal((2, 5), CombatWalk.ShortestApproach(grid, 0, 5, 3, 5, 6)![^1]);
    }

    [Fact]
    public void NoRouteWithinTheSteps_OrNoneAtAll_IsNull() {
        var grid = new CombatGrid();
        Assert.Null(CombatWalk.ShortestPath(grid, 0, 0, 6, 0, 5));   // six steps away
        Assert.NotNull(CombatWalk.ShortestPath(grid, 0, 0, 5, 0, 5));
        for (int x = 0; x < CombatGrid.Width; x++) {
            grid.SetTerrain(x, 3, CombatTerrain.Wall);              // a wall right across the board
        }
        Assert.Null(CombatWalk.ShortestPath(grid, 2, 1, 2, 5, 20));
    }

    [Fact]
    public void ADiagonalDoesNotSqueezeBetweenTwoCrystals() {
        var grid = new CombatGrid();
        grid.SetTerrain(1, 0, CombatTerrain.Crystal);
        grid.SetTerrain(0, 1, CombatTerrain.Crystal);
        List<(int X, int Y)>? path = CombatWalk.ShortestPath(grid, 0, 0, 1, 1, 5);
        Assert.NotNull(path);
        Assert.Equal(2, path!.Count);   // through a crystal, not diagonally between them (CombatMovement.Step)
    }

    [Fact]
    public void ATrapOnTheShortestRouteFires_AsWalkingOverItWould() {
        var (grid, attacker) = OwnersCase();
        grid.SetTileEffect(2, 2, CombatTerrain.Trap, 40);
        var hit = new List<CombatWalk.Hazard>();
        CombatWalk.WalkResult walk = CombatWalk.WalkPath(grid, attacker,
            CombatWalk.ShortestApproach(grid, 2, 1, 1, 3, 6)!, attacker.Speed, onHazard: (_, h) => hit.Add(h));
        Assert.True(walk.Arrived);
        Assert.Equal((2, 3), (attacker.X, attacker.Y));
        Assert.Single(hit);
        Assert.Equal(CombatWalk.HazardKind.TileTrap, hit[0].Kind);
        Assert.Equal(40, hit[0].Damage);
        Assert.Equal(CombatTerrain.Open, grid.TerrainAt(2, 2));   // spent, as the walker spends it
    }
}

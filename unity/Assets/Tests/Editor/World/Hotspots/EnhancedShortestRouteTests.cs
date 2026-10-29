namespace BakAgain.Tests.Editor.World.Hotspots {
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Combat;
    using BakAgain.World.Hotspots;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// Enhanced combat walks the party by the shortest route (owner, 2026-10-29: "do shortest path"); a
    /// Classic turn keeps the original's greedy walk. One route function feeds the reach marks, the
    /// forecast trail, the Thrust targets and the walk.
    /// </summary>
    public class EnhancedShortestRouteTests {
        // The owner's case: foe A at (1,3), its side nearest the hero taken by foe B at (1,2), the hero at (2,1).
        private static (HotspotService S, Combatant Hero, Combatant A) OwnersCase(bool enhanced) {
            var (s, hero) = EnhancedBoardMarksTests.PartyTurn(enemies: 2, roll: 0);
            EnhancedBoardMarksTests.Place(s, hero, (2, 1), 5, (0, 1, 3), (1, 1, 2));
            s.Combat.ShortestRoutes = () => enhanced;
            return (s, hero, s.Combat.Encounter.Enemies[0]);
        }

        [Test]
        public void AThrustTakesTheShortestRoute_ClassicKeepsTheOriginals() {
            var (s, hero, a) = OwnersCase(enhanced: true);
            CollectionAssert.AreEqual(new[] { (2, 1), (2, 2), (2, 3) }, s.Combat.ForecastThrust(hero, a).ToArray(),
                "Enhanced: two steps to the free side beside the hero");
            Assert.AreEqual(CombatRuntime.MeleeClick.Struck, s.Combat.ResolveMeleeClick(hero, a, CombatActionDispatch.MeleeAttack.Thrust));
            Assert.AreEqual((2, 3), (hero.X, hero.Y), "and the walk goes there");

            var (c, classic, ca) = OwnersCase(enhanced: false);
            CollectionAssert.AreEqual(new[] { (2, 1), (2, 2), (2, 3), (1, 4), (0, 3) },
                c.Combat.ForecastThrust(classic, ca).ToArray(), "Classic: the original's way round");
            c.Combat.ResolveMeleeClick(classic, ca, CombatActionDispatch.MeleeAttack.Thrust);
            Assert.AreEqual((0, 3), (classic.X, classic.Y));
        }

        [Test]
        public void ATrapOnTheShortestRouteFires() {
            var (s, hero, a) = OwnersCase(enhanced: true);
            s.Combat.Grid.SetTileEffect(2, 2, CombatTerrain.Trap, 7);
            int pool = hero.Health + hero.Stamina;
            s.Combat.ResolveMeleeClick(hero, a, CombatActionDispatch.MeleeAttack.Thrust);
            Assert.AreEqual(CombatTerrain.Open, s.Combat.Grid.TerrainAt(2, 2), "the trap on the route went off");
            Assert.Less(hero.Health + hero.Stamina, pool, "and hurt the walker");
        }

        [Test]
        public void AFoeWithNoSideInReach_IsRefused_NothingSpent() {
            var (s, hero, a) = OwnersCase(enhanced: true);
            foreach ((int x, int y) in new[] { (0, 3), (2, 3), (1, 4) }) {
                s.Combat.Grid.SetTerrain(x, y, CombatTerrain.Wall);   // every free side walled
            }
            Assert.IsNull(s.Combat.ForecastThrust(hero, a), "no route: out of reach");
            Assert.AreEqual(CombatRuntime.MeleeClick.Refused,
                s.Combat.ResolveMeleeClick(hero, a, CombatActionDispatch.MeleeAttack.Thrust));
            Assert.AreEqual((2, 1), (hero.X, hero.Y));
            Assert.IsTrue(hero.CanAct(strict: true), "the turn is not spent");
            Assert.IsFalse(s.EnhancedBoardMarks().ThrustTargets.Contains(0), "and the board does not offer it");
        }

        [Test]
        public void EveryReachableCellIsWalkedTo_ByTheRouteTheTrailShows() {
            var (s, hero, _) = OwnersCase(enhanced: true);
            var reach = s.EnhancedBoardMarks().Reach.ToList();
            Assert.Greater(reach.Count, 10);
            foreach ((int x, int y) in reach) {
                IReadOnlyList<(int X, int Y)> trail = s.Combat.ForecastWalk(hero, x, y);
                Assert.IsNotNull(trail, $"({x},{y}) is marked reachable, so it has a trail");
                Assert.AreEqual((x, y), trail[^1]);
                Assert.LessOrEqual(trail.Count - 1, hero.Speed, "within the move");
                for (int i = 1; i < trail.Count; i++) {
                    Assert.AreEqual(1, CombatGrid.ChebyshevDistance(trail[i - 1].X, trail[i - 1].Y, trail[i].X, trail[i].Y),
                        "one cell a step");
                }
            }
            (int fx, int fy) = reach.OrderByDescending(r => CombatGrid.ChebyshevDistance(2, 1, r.Col, r.Row)).First();
            IReadOnlyList<(int X, int Y)> shown = s.Combat.ForecastWalk(hero, fx, fy);
            Assert.IsTrue(s.Combat.MoveToTile(hero, fx, fy, _ => 0));
            Assert.AreEqual((fx, fy), (hero.X, hero.Y), "the walk arrives where the trail ends");
            CollectionAssert.AreEqual(shown.ToArray(), hero.WalkedCells.ToArray(), "along the cells it showed");
        }
    }
}

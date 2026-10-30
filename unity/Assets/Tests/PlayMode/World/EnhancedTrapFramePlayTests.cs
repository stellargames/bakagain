namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using System.Linq;
    using BakAgain.World.Converters;
    using BakAgain.World.Encounters;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>The trap board's rim, dimmed surroundings and goal tint, and the off-board flash (TASK-972).</summary>
    public class EnhancedTrapFramePlayTests {
        private const int Cell = 300;
        private bool _enhanced, _combatOn;
        private GameObject _root;

        [SetUp]
        public void Remember() {
            _enhanced = BakAgain.Core.GameOptions.Enhanced;
            _combatOn = BakAgain.Core.GameOptions.GetFeature(BakAgain.Core.EnhancedFeature.Combat);
            BakAgain.Core.GameOptions.Enhanced = true;
        }

        [TearDown]
        public void Restore() {
            BakAgain.Core.GameOptions.Enhanced = _enhanced;
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, _combatOn);
            if (_root != null) { Object.DestroyImmediate(_root); }
        }

        // The party at the origin facing +Y, so the board frame is the world's (÷ WorldScale).
        private static Vector3? CentreOf(int column, int row) {
            (int across, int away) = CombatArenaPlacement.CellOffset(column, row, Cell);
            return BakCoordinateConverter.ConvertPosition(across, away, 0);
        }

        private ArenaTrapFrame Frame(bool trap, bool underground, CombatGrid grid) {
            _root = new GameObject("arena");
            // The arena root holds the whole fight; the frame names only its own parts (seen live: it
            // listed every combatant and prop).
            new GameObject("Encounter locky #1").transform.SetParent(_root.transform, false);
            var frame = _root.AddComponent<ArenaTrapFrame>();
            frame.Bind(trap, ArenaOrbit.BoardCorners(Cell, underground),
                trap ? EnhancedBoardBounds.GoalCells(grid, underground) : new System.Collections.Generic.List<(int, int)>(),
                CentreOf, new EnhancedCombatLayout(), Cell / BakCoordinateConverter.WorldScale);
            return frame;
        }

        private static CombatGrid TrapGrid(bool underground) {
            var grid = new CombatGrid(underground);
            grid.SetTerrain(0, 5, CombatTerrain.Exit);
            grid.SetTerrain(3, 5, CombatTerrain.Wall);   // on the exit row: not a goal cell
            return grid;
        }

        [Test]
        public void ATrapDrawsTheRimTheDimAndTheGoal() {
            CombatGrid grid = TrapGrid(underground: false);
            ArenaTrapFrame frame = Frame(trap: true, underground: false, grid);

            CollectionAssert.IsSubsetOf(new[] { "TrapRim", "TrapOutsideDim", "TrapGoal" }, frame.DrawnKinds().ToList());
            CollectionAssert.DoesNotContain(frame.DrawnKinds().ToList(), "EdgeFlash", "no flash until a tap asks for one");
            Assert.AreEqual(EnhancedBoardBounds.GoalCells(grid, false).Count, _root.transform.Find("TrapGoal").childCount,
                "one quad per standable goal cell");

            // The far wall spans the board only: past the far corners the side walls already dim, and a second
            // layer drew a hard seam across the scenery at the far rim's height (review, fix round 1).
            float far = _root.transform.Find("TrapOutsideDim/Far").GetComponent<MeshRenderer>().bounds.size.x;
            float rim = _root.transform.Find("TrapRim").GetComponent<MeshRenderer>().bounds.size.x;
            Assert.AreEqual(rim, far, 1e-3f, "the far wall is as wide as the rim, no wider");
        }

        // Final review: at its yaw limit the trap camera stands outside a side wall (measured 7.4 cells out on
        // SAVE02), which hid that wall and left the ground and scenery on that side undimmed.
        [UnityTest]
        public IEnumerator ACameraOutsideASideWallDimsThatSideWithTheStripInstead() {
            _root = new GameObject("arena");
            var camGo = new GameObject("WorldCamera");
            try {
                Camera cam = camGo.AddComponent<Camera>();
                var corners = ArenaOrbit.BoardCorners(Cell, false);
                float leftWall = (float)(corners[0].X - (new EnhancedCombatLayout().TrapRimCells * Cell)) / BakCoordinateConverter.WorldScale;
                cam.transform.position = new Vector3(leftWall - 15f, 30f, -20f);
                var frame = _root.AddComponent<ArenaTrapFrame>();
                frame.Bind(true, corners, EnhancedBoardBounds.GoalCells(TrapGrid(false), false), CentreOf,
                    new EnhancedCombatLayout(), Cell / BakCoordinateConverter.WorldScale, () => cam);
                yield return null;
                bool On(string part) => _root.transform.Find("TrapOutsideDim/" + part).GetComponent<MeshRenderer>().enabled;
                Assert.IsFalse(On("Left"), "the rim's left wall faces away from the camera");
                Assert.IsTrue(On("LeftFloor") && On("LeftOuter") && On("FarLeft"), "the left side is dimmed by the strip instead");
                Assert.IsTrue(On("Right") && On("Far"));
                Assert.IsFalse(On("RightFloor") || On("RightOuter") || On("FarRight"), "the right side keeps its rim wall alone");
                Bounds strip = _root.transform.Find("TrapOutsideDim/LeftFloor").GetComponent<MeshRenderer>().bounds;
                Assert.AreEqual(leftWall, strip.max.x, 1e-3f, "the strip meets the rim, no overlap with the board");

                cam.transform.position = new Vector3(0f, 30f, -20f);   // back behind the board's middle
                yield return null;
                Assert.IsTrue(On("Left"));
                Assert.IsFalse(On("LeftFloor") || On("LeftOuter") || On("FarLeft"), "inside, the rim wall alone: nothing dimmed twice");
            } finally {
                Object.Destroy(camGo);
            }
        }

        [UnityTest]
        public IEnumerator ACombatEncounterDrawsNothingUntilFlashed() {
            ArenaTrapFrame frame = Frame(trap: false, underground: false, null);
            CollectionAssert.IsEmpty(frame.DrawnKinds().ToList());

            frame.Flash();
            CollectionAssert.AreEqual(new[] { "EdgeFlash" }, frame.DrawnKinds().ToList());

            yield return new WaitForSeconds(new EnhancedCombatLayout().EdgeFlashSeconds + 0.1f);
            CollectionAssert.IsEmpty(frame.DrawnKinds().ToList(), "the outline is gone once the flash has run");
        }

        [Test]
        public void AClassicTurnHasNoFrame() {
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, false);
            var world = new BakAgain.World.WorldRuntime(null,
                new BakAgain.Core.GameSession { PositionX = 50000, PositionY = 60000 }, null, null, null, null, null);
            _root = new GameObject("arena");
            Assert.IsNull(world.AttachTrapFrame(_root));
            Assert.IsNull(_root.GetComponent<ArenaTrapFrame>());
        }

        [Test]
        public void TheUndergroundRimFollowsThePlayableRows() {
            Frame(trap: true, underground: true, TrapGrid(underground: true));
            Transform rim = _root.transform.Find("TrapRim");
            float farthest = rim.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => rim.TransformPoint(v).z);

            var layout = new EnhancedCombatLayout();
            float playableFar = (float)ArenaOrbit.BoardCorners(Cell, underground: true)[3].Y / BakCoordinateConverter.WorldScale;
            float rimWidth = layout.TrapRimCells * Cell / BakCoordinateConverter.WorldScale;
            Assert.AreEqual(playableFar + rimWidth, farthest, 1e-3f,
                "the rim stands just past row 6's far edge, not round the blocked rows");
            Assert.Less(farthest, (float)ArenaOrbit.BoardCorners(Cell, underground: false)[3].Y / BakCoordinateConverter.WorldScale);
        }

        // ---- the tap path: an off-board tap flashes and does nothing else ----

        private GameObject _screenGo;

        private (BakAgain.UI.InGame.InGameScreen Screen, BakAgain.World.Hotspots.HotspotService S,
            GameData.Resources.Combat.Combatant Hero, BakAgain.UI.Combat.Enhanced.EnhancedCombatController C, int[] Flashes)
            Tap(bool underground, System.Func<Vector2, ((int, int)?, (int, bool)?, bool)> probe) {
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, true);
            var (s, hero) = BakAgain.Tests.Editor.World.Hotspots.EnhancedBoardMarksTests.PartyTurn(
                underground: underground, roll: 0, arenaBusy: () => true,
                dialogs: new BakAgain.Tests.PlayMode.UI.Inventory.NoOpDialogs());
            BakAgain.Tests.Editor.World.Hotspots.EnhancedBoardMarksTests.Place(s, hero, (4, 5), 5, (0, 5, 5));
            var c = new BakAgain.UI.Combat.Enhanced.EnhancedCombatController(s, s.Combat, null, new EnhancedCombatLayout(),
                null, false, null, () => 0f);
            _screenGo = new GameObject("tapScreen");
            _screenGo.SetActive(false);
            var screen = _screenGo.AddComponent<BakAgain.UI.InGame.InGameScreen>();
            screen.SetInCombatPredicate(() => true);
            screen.CombatController = c;
            var flashes = new int[1];
            screen.SetBoardEdgeFlashSeam(() => flashes[0]++, () => underground);
            screen.SetBoardProbeForTest(v => probe(v));
            return (screen, s, hero, c, flashes);
        }

        [TearDown]
        public void DropScreen() { if (_screenGo != null) { Object.DestroyImmediate(_screenGo); } }

        [Test]
        public void ATapOffTheBoardFlashesAndDoesNothingElse() {
            var (screen, s, hero, c, flashes) = Tap(false, v => (null, null, true));
            var at = (hero.X, hero.Y);
            Assert.IsTrue(screen.EnhancedCombatClick(isPrimary: true, Vector2.zero));
            Assert.AreEqual(1, flashes[0]);
            Assert.AreEqual(at, (hero.X, hero.Y), "nobody moved");
            Assert.IsTrue(hero.CanAct(strict: true), "the turn is not spent");
        }

        [Test]
        public void ATapOnTheSkyKeepsToday() {
            var (screen, _, _, _, flashes) = Tap(false, v => (null, null, false));
            screen.EnhancedCombatClick(isPrimary: true, Vector2.zero);
            Assert.AreEqual(0, flashes[0]);
        }

        [Test]
        public void ATapOnAWallInsideTheBoardDoesNotFlash() {
            var (screen, s, _, _, flashes) = Tap(false, v => ((2, 2), null, true));
            s.Combat.Grid.SetTerrain(2, 2, CombatTerrain.Wall);
            screen.EnhancedCombatClick(isPrimary: true, Vector2.zero);
            Assert.AreEqual(0, flashes[0]);
        }

        [Test]
        public void ATapOnAFoeAtTheEdgeActsAsBefore() {
            var (screen, s, hero, _, flashes) = Tap(false, v => ((5, 5), (0, false), true));
            int pool = s.Combat.Encounter.Enemies[0].Health + s.Combat.Encounter.Enemies[0].Stamina;
            Assert.IsTrue(screen.EnhancedCombatClick(isPrimary: true, Vector2.zero));
            Assert.AreEqual(0, flashes[0]);
            Assert.Less(s.Combat.Encounter.Enemies[0].Health + s.Combat.Encounter.Enemies[0].Stamina, pool, "the thrust landed");
        }

        [Test]
        public void AnUndergroundTapOnABlockedRowFlashes() {
            var (screen, _, _, _, flashes) = Tap(true, v => ((3, CombatGrid.UndergroundPlayableRows), null, true));
            screen.EnhancedCombatClick(isPrimary: true, Vector2.zero);
            Assert.AreEqual(1, flashes[0]);
        }
    }
}

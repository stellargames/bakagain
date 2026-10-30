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
            grid.SetTerrain(3, 6, CombatTerrain.Wall);
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
    }
}

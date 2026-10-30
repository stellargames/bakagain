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

    /// <summary>The trap board's rim and dimmed surroundings (TASK-972).</summary>
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

        private const int ExitRow = 5;

        // The corners as the runtime hands them over: the board up to the exit row.
        private static (double X, double Y)[] Corners(bool underground) => ArenaOrbit.BoardCorners(Cell, underground, ExitRow);

        private ArenaTrapFrame Frame(bool underground) {
            _root = new GameObject("arena");
            // The arena root holds the whole fight; the frame names only its own parts (seen live: it
            // listed every combatant and prop).
            new GameObject("Encounter locky #1").transform.SetParent(_root.transform, false);
            var frame = _root.AddComponent<ArenaTrapFrame>();
            frame.Bind(Corners(underground), CentreOf, new EnhancedCombatLayout(), Cell / BakCoordinateConverter.WorldScale);
            return frame;
        }

        private static float FarthestZ(Transform part) =>
            part.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => part.TransformPoint(v).z);

        [Test]
        public void ATrapDrawsTheRimAndTheDimOpenPastTheExitRow() {
            ArenaTrapFrame frame = Frame(underground: false);
            CollectionAssert.AreEquivalent(new[] { "TrapRim", "TrapOutsideDim" }, frame.DrawnKinds().ToList());

            // Owner, round 6: no back wall, and the sides stop at the exit row's far edge.
            Assert.IsNull(_root.transform.Find("TrapOutsideDim/Far"), "no far dim wall");
            float exitEdge = (float)Corners(false)[2].Y / BakCoordinateConverter.WorldScale;
            Assert.AreEqual(exitEdge, FarthestZ(_root.transform.Find("TrapRim")), 1e-3f, "the rim ends at the exit row: no far strip");
            Assert.Greater(FarthestZ(_root.transform.Find("TrapOutsideDim/Left")), exitEdge + 100f,
                "the side dim runs on past the exit: ending there left a hard edge in the sky");
        }

        // Final review: at its yaw limit the trap camera stands outside a side wall (measured 7.4 cells out on
        // SAVE02), which hid that wall and left the ground and scenery on that side undimmed.
        [UnityTest]
        public IEnumerator ACameraOutsideASideWallDimsThatSideWithTheStripInstead() {
            _root = new GameObject("arena");
            var camGo = new GameObject("WorldCamera");
            try {
                Camera cam = camGo.AddComponent<Camera>();
                var corners = Corners(false);
                float leftWall = (float)(corners[0].X - (new EnhancedCombatLayout().TrapRimCells * Cell)) / BakCoordinateConverter.WorldScale;
                cam.transform.position = new Vector3(leftWall - 15f, 30f, -20f);
                var frame = _root.AddComponent<ArenaTrapFrame>();
                frame.Bind(corners, CentreOf, new EnhancedCombatLayout(), Cell / BakCoordinateConverter.WorldScale, () => cam);
                yield return null;
                bool On(string part) => _root.transform.Find("TrapOutsideDim/" + part).GetComponent<MeshRenderer>().enabled;
                Assert.IsFalse(On("Left"), "the rim's left wall faces away from the camera");
                Assert.IsTrue(On("LeftFloor") && On("LeftOuter"), "the left side is dimmed by the strip instead");
                Assert.IsTrue(On("Right"));
                Assert.IsFalse(On("RightFloor") || On("RightOuter"), "the right side keeps its rim wall alone");
                Bounds strip = _root.transform.Find("TrapOutsideDim/LeftFloor").GetComponent<MeshRenderer>().bounds;
                Assert.AreEqual(leftWall, strip.max.x, 1e-3f, "the strip meets the rim, no overlap with the board");

                cam.transform.position = new Vector3(0f, 30f, -20f);   // back behind the board's middle
                yield return null;
                Assert.IsTrue(On("Left"));
                Assert.IsFalse(On("LeftFloor") || On("LeftOuter"), "inside, the rim wall alone: nothing dimmed twice");
            } finally {
                Object.Destroy(camGo);
            }
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
        public void TheUndergroundRimStaysOnThePlayableRows() {
            // An underground exit row past the playable rows is clamped to the last playable row.
            _root = new GameObject("arena");
            _root.AddComponent<ArenaTrapFrame>().Bind(ArenaOrbit.BoardCorners(Cell, true, 12), CentreOf,
                new EnhancedCombatLayout(), Cell / BakCoordinateConverter.WorldScale);
            Transform rim = _root.transform.Find("TrapRim");
            float playableFar = (float)ArenaOrbit.BoardCorners(Cell, underground: true)[3].Y / BakCoordinateConverter.WorldScale;
            Assert.AreEqual(playableFar, FarthestZ(rim), 1e-3f, "the rim stops at row 6's far edge, not round the blocked rows");
        }
    }
}

namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Encounters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A walking actor keeps its sprite's recorded pose current — TASK-237's follow-on.
    /// </summary>
    /// <remarks>
    /// <b><see cref="DirectionalSprite"/> picks its frame from a RECORDED world position and facing,
    /// not from the transform</b> (the transform is a local offset under the zone root, so reading
    /// it back is only correct while that root sits at the origin). Those three fields were set once
    /// by <c>Bind</c> and never again, so every roaming actor chose its octant against its SPAWN
    /// point for the whole session — an error that grows with each step, and one that hides a
    /// road-follower's turn at a bend entirely.
    ///
    /// <para>The bug was invisible while road-followers could not move at all, which is why it
    /// surfaced with the road step rather than before it.</para>
    /// </remarks>
    public class RoamingActorSpritePoseTests {
        private GameObject _go;

        [SetUp]
        public void SetUp() {
            _go = new GameObject("roaming actor under test");
            _go.AddComponent<DirectionalSprite>();
            _go.AddComponent<RoamingActor>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private RoamingActor Bound(RoamingMovement.Pattern pattern, long x, long y, short facing,
            long[] waypointX = null, long[] waypointY = null) {
            RoamingActor actor = _go.GetComponent<RoamingActor>();
            actor.Bind(new EncounterActorPlacement.Placed(
                rosterSlot: 0, creatureNumber: 1, worldX: x, worldY: y, facing: facing,
                roams: true, pattern: pattern,
                waypointX: waypointX ?? new long[] { long.MaxValue },
                waypointY: waypointY ?? new long[] { long.MaxValue }));
            return actor;
        }

        /// <summary>
        /// <b>Moving is not walking.</b>
        /// </summary>
        /// <remarks>
        /// <c>EncounterActorPose.Advance</c> has modelled this gait since the pose work and nothing
        /// ever called it, so roaming actors turned to face the camera correctly and slid along with
        /// their legs frozen on the first frame of the group. The step is the right driver because
        /// roaming actors only advance when the PARTY moves — <c>WorldRuntime</c> accumulates party
        /// distance and issues whole ticks — so the gait follows the world's pace rather than a
        /// frame rate, and needs no clock of its own.
        /// </remarks>
        [Test]
        public void STEPPINGAdvancesTheGaitFrame_notJustThePosition() {
            RoamingActor actor = Bound(RoamingMovement.Pattern.BackAndForth, 0, 0, 0);
            DirectionalSprite sprite = _go.GetComponent<DirectionalSprite>();
            Assert.AreEqual(0, sprite.GaitFrame, "it starts on the group's first frame");

            actor.Step();
            Assert.AreEqual(1, sprite.GaitFrame);

            actor.Step();
            Assert.AreEqual(2, sprite.GaitFrame);
        }

        /// <summary>And it ping-pongs rather than wrapping — 0,1,2,1,0,1,2 …</summary>
        /// <remarks>
        /// Walked over four steps rather than asserted on the model, because the wiring is what is
        /// under test: a port that reset to 0 at the end would still pass a test of
        /// <c>Advance</c> alone while snapping the leg back on screen.
        /// </remarks>
        [Test]
        public void TheGaitTURNSROUNDAtTheEndRatherThanWrappingToZero() {
            RoamingActor actor = Bound(RoamingMovement.Pattern.BackAndForth, 0, 0, 0);
            DirectionalSprite sprite = _go.GetComponent<DirectionalSprite>();

            var seen = new System.Collections.Generic.List<int>();
            for (var step = 0; step < 5; step++) {
                actor.Step();
                seen.Add(sprite.GaitFrame);
            }

            CollectionAssert.AreEqual(new[] { 1, 2, 1, 0, 1 }, seen,
                "0,1,2,1,0,1 — the middle frame is passed through twice per cycle");
        }

        /// <summary>A pattern that does not move does not walk on the spot.</summary>
        [Test]
        public void AStationaryActorsGaitNeverAdvances() {
            RoamingActor actor = Bound(RoamingMovement.Pattern.Stationary, 0, 0, 0);
            DirectionalSprite sprite = _go.GetComponent<DirectionalSprite>();

            actor.Step();
            actor.Step();

            Assert.AreEqual(0, sprite.GaitFrame);
        }

        [Test]
        public void StepPushesTheNewPositionIntoTheSprite() {
            RoamingActor actor = Bound(RoamingMovement.Pattern.BackAndForth, 0, 0, 0);
            DirectionalSprite sprite = _go.GetComponent<DirectionalSprite>();

            actor.Step();

            Assert.AreEqual(RoamingMovement.StepDistance, sprite.RecordedPose.Y,
                "heading 0 walks along +Y, and the sprite has to know it moved");
            Assert.AreEqual(actor.Pose.Y, sprite.RecordedPose.Y);
        }

        [Test]
        public void ATURNReachesTheSpriteToo_notJustAMOVE() {
            // *** The half a position-only fix would miss. *** An about-face at a waypoint changes
            // which frame the actor should show without changing where it is by much; leaving the
            // facing stale means the turn simply never appears.
            RoamingActor actor = Bound(RoamingMovement.Pattern.BackAndForth, 0, 0, 0);
            DirectionalSprite sprite = _go.GetComponent<DirectionalSprite>();
            Assert.AreEqual(0, sprite.RecordedPose.Facing);

            // The waypoint is exactly one step ahead, so this step arrives and about-faces.
            actor = Bound(RoamingMovement.Pattern.BackAndForth, 0, 0, 0,
                waypointX: new long[] { 0 },
                waypointY: new long[] { RoamingMovement.StepDistance });
            actor.Step();

            Assert.AreEqual(unchecked((short)RoamingMovement.HalfTurn), sprite.RecordedPose.Facing);
            Assert.AreEqual(actor.Pose.Heading, unchecked((ushort)sprite.RecordedPose.Facing));
        }

        [Test]
        public void AStationaryActorNeverTouchesTheSprite() {
            // Pattern 0 returns before the tick, so the spawn pose Bind recorded stands — which is
            // correct for it and is why the update belongs on the moving path, not in LateUpdate.
            RoamingActor actor = Bound(RoamingMovement.Pattern.Stationary, 1234, 5678, 0x40);
            DirectionalSprite sprite = _go.GetComponent<DirectionalSprite>();

            actor.Step();

            Assert.AreEqual(0, sprite.RecordedPose.X, "Bind never sets the sprite; only Step does");
        }
    }
}

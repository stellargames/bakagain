namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Tests.TestSupport;
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using Cysharp.Threading.Tasks;
    using BakAgain.Tests.Editor.World;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    /// <summary>
    /// The rope swing across a pit — <c>worldcross_hotspot_use_rope</c>'s two loops.
    /// </summary>
    /// <remarks>
    /// <b>Everything here was modelled, tested and unconsumed until now</b> — <c>StepUnits</c>,
    /// <c>SagHeightAt</c> and <c>SwingSoundId</c> all existed while the handler called
    /// <c>PlaceAt</c>, so a player saw a teleport. These cover the wiring, which is the half a pure
    /// test of the curve cannot reach.
    ///
    /// <para><b>PlayMode and <c>[UnityTest]</c>, not <c>[Test]</c>:</b> the traverse yields a frame
    /// per step, so a synchronous test would leave the task pending for ever.</para>
    /// </remarks>
    public class PitSwingMovementTests {
        private const int PitX = 4000;
        private const int PitY = 4000;
        private const int Span = PitRopeCrossing.CrossingSpan;

        private sealed class Rig {
            public PartyMovement Movement;
            public GameSession Session;
            public readonly List<int> Sounds = new();
        }

        private static MovementData Table() => new MovementData("MOVEMENT.DAT") {
            StepDistances = new[] { 100, 800, 1600 },
            TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
            SecondsPerStep = new[] { 60, 120, 240 },
        };

        private static Rig Build(int x, int y) {
            var rig = new Rig {
                Session = new GameSession { PositionX = x, PositionY = y, Rotation = 0 },
            };
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = StepSize.Medium;
            prefs.Current.TurnSize = TurnSize.Large;
            // No collision: the traverse writes its own heights and only re-grounds at the end,
            // which a null collision skips. What is under test is the path, not the ground.
            rig.Movement = new PartyMovement(rig.Session, Table(), prefs, camera: null,
                cameraHeightZ: 230, cameraPitch: 0, collision: null,
                playSfx: id => rig.Sounds.Add(id), underground: true);
            return rig;
        }

        /// <summary>Crossing along Y from the +Y side, which is the shape the handler builds.</summary>
        private static UniTask CrossFromPlusY(Rig rig) =>
            rig.Movement.SwingAcrossAsync(crossingAxisIsY: true, PitX, PitY,
                startAcross: PitY + Span, landingAcross: PitY - Span);

        [UnityTest]
        public IEnumerator TheLATERALCoordinateIsSnappedOntoThePit() => UniTask.ToCoroutine(async () => {
            // *** The fidelity gap that was there BEFORE any animation. *** The offer only needs the
            // party beside the hook, so they may be up to LateralBand off-centre when they click.
            // The original assigns the pit's own coordinate; PlaceAt kept the party's, which put
            // them down parallel to the rope rather than on it.
            Rig rig = Build(PitX + 250, PitY + Span);

            await CrossFromPlusY(rig);

            Assert.AreEqual(PitX, rig.Session.PositionX);
        });

        [UnityTest]
        public IEnumerator ThePartyLandsOnTheFarLip() => UniTask.ToCoroutine(async () => {
            Rig rig = Build(PitX, PitY + Span);

            await CrossFromPlusY(rig);

            Assert.AreEqual(PitY - Span, rig.Session.PositionY);
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ThePartyIsTURNEDToFaceTheCrossing() => UniTask.ToCoroutine(async () => {
            Rig rig = Build(PitX, PitY + Span);
            rig.Session.Rotation = 0x2000;   // facing anywhere; the swing does not care

            await CrossFromPlusY(rig);

            Assert.AreEqual(unchecked((short)0x8000), rig.Session.Rotation,
                "crossing toward -Y, and the heading is not a choice");
        });

        [UnityTest]
        public IEnumerator TheSWINGCUESoundsExactlyOnce() => UniTask.ToCoroutine(async () => {
            // At the exact centre, not on entering the sag band — firing on the boundary plays it
            // twice, once per side.
            Rig rig = Build(PitX, PitY + Span);

            await CrossFromPlusY(rig);

            Assert.AreEqual(1, rig.Sounds.FindAll(id => id == PitRopeCrossing.SwingSoundId).Count);
        });

        [UnityTest]
        public IEnumerator TheEyeDIPSBelowTheLipAndComesBackUp() => UniTask.ToCoroutine(async () => {
            // The sag is NEGATIVE: the anchor sits below the lip, so the party hangs under it.
            // A reading that treats "deepest" as the largest value inverts the whole curve.
            Rig rig = Build(PitX, PitY + Span);
            var lowest = int.MaxValue;
            var atTheLip = int.MinValue;

            UniTask crossing = CrossFromPlusY(rig);
            while (rig.Movement.IsCrossing) {
                // The flat frames outside the sag band ARE the lip height; reading the session
                // before the swing would read a PositionZ nothing has written yet.
                if (System.Math.Abs(rig.Session.PositionY - PitY) >= PitRopeCrossing.SagRadius) {
                    atTheLip = System.Math.Max(atTheLip, rig.Session.PositionZ);
                }
                lowest = System.Math.Min(lowest, rig.Session.PositionZ);
                await UniTask.Yield();
            }
            await crossing;

            Assert.Less(lowest, atTheLip, "the party dips into the chasm");
            Assert.AreEqual(PitRopeCrossing.SagHeightAt(0), lowest,
                "and the deepest point is the centre of the modelled curve");
        });

        [UnityTest]
        public IEnumerator TheAPPROACHIsFlat_onlyTheCrossingSags() => UniTask.ToCoroutine(async () => {
            // Two loops, not one. The party walks to the near lip on level ground; folding the
            // loops together would sag the approach as well.
            // NOT read from the session before the swing: nothing has written PositionZ yet, so it
            // is 0 there while the eye the traverse carries is the camera height. The claim is that
            // the approach is LEVEL, so the approach's own first frame is the reference.
            Rig rig = Build(PitX, PitY + Span + 400);
            var heightsBeforeTheLip = new List<int>();

            UniTask crossing = rig.Movement.SwingAcrossAsync(crossingAxisIsY: true, PitX, PitY,
                startAcross: PitY + Span, landingAcross: PitY - Span);
            while (rig.Movement.IsCrossing) {
                if (rig.Session.PositionY > PitY + Span) {
                    heightsBeforeTheLip.Add(rig.Session.PositionZ);
                }
                await UniTask.Yield();
            }
            await crossing;

            CollectionAssert.IsNotEmpty(heightsBeforeTheLip, "the approach really is walked");
            foreach (int z in heightsBeforeTheLip) {
                Assert.AreEqual(heightsBeforeTheLip[0], z,
                    "the approach never leaves the walking eye height");
            }
            Assert.Greater(heightsBeforeTheLip[0], PitRopeCrossing.SagHeightAt(0),
                "and it is above the point the rope dips to");
        });

        [UnityTest]
        public IEnumerator ThePlayerCannotWALKOutOfTheSwing() => UniTask.ToCoroutine(async () => {
            // The original runs its loop inside the click behind a busy cursor, so there is no frame
            // in which input reaches the world. Animating across real frames re-opens that door.
            Rig rig = Build(PitX, PitY + Span);

            UniTask crossing = CrossFromPlusY(rig);
            await UniTask.Yield();
            int x = rig.Session.PositionX;
            rig.Movement.MoveForward();
            rig.Movement.TurnLeft();
            short rotation = rig.Session.Rotation;

            // *** THE AWAIT IS IN A finally BECAUSE THE ASSERTS COME FIRST. *** This is the only
            // test in the class that reads its result before the crossing has ended, so a failing
            // assertion here would throw past a trailing await and leave the traverse running into
            // whichever test the runner starts next — where it yields against a rig that test does
            // not own. That is the shape behind the intermittent NullReferenceException this class
            // reported twice on 2026-09-08 (TASK-382), in two different tests, each time green on
            // the next run: the victim is not the leaker.
            try {
                Assert.AreEqual(x, rig.Session.PositionX, "a step during the swing is refused");
                Assert.AreEqual(unchecked((short)0x8000), rotation, "and so is a turn");
            } finally {
                await crossing;
            }
        });
    }
}

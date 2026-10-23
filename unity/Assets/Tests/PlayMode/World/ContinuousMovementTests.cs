namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Tests.Editor.World;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.World;
    using BakAgain.World.Collision;
    using BakAgain.World.Converters;
    using GameData.Resources.Config;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Enhanced continuous walking (TASK-966): the party glides in the original's Small steps, and
    /// time, the hotspot pass, the Stealth cell tick and collision stay the original's per-step rules
    /// (WORLDLP.C:190-198, WORLDMOV.C:87-155 and :775-795, HOTSPOT.C:474/729).
    /// </summary>
    public class ContinuousMovementTests {
        private const float Cadence = ClassicMovementDriver.RepeatStepsPerSecond;
        private const float Frame = 1f / 60f;

        // The shipped MOVEMENT.DAT.
        private static MovementData Table() => new MovementData("MOVEMENT.DAT") {
            StepDistances = new[] { 400, 800, 1600 },
            TurnAngles = new[] { 1024, 2048, 4096 },
            SecondsPerStep = new[] { 60, 120, 240 },
        };

        private sealed class Rig {
            public GameSession Session;
            public PartyMovement Movement;
            public GameClock Clock;
            public Camera Camera;
            public readonly List<(int X, int Y)> Passes = new();
            public readonly List<int> Sounds = new();
            public System.Func<int, int, bool> Pass = (_, _) => true;
        }

        // Open ground by default: the hotspot pass runs only with collision data, as in the game.
        private static Rig Build(StepSize step, bool underground = false, ProximityWorld world = null,
            bool camera = false) {
            world ??= WorldWith();
            var rig = new Rig { Session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 } };
            rig.Clock = new GameClock(rig.Session);
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = step;
            prefs.Current.TurnSize = TurnSize.Medium;
            if (camera) {
                rig.Camera = new GameObject("cam").AddComponent<Camera>();
            }
            rig.Movement = new PartyMovement(rig.Session, Table(), prefs, rig.Camera, cameraHeightZ: 0, cameraPitch: 0,
                collision: world,
                hotspotPass: (x, y) => { rig.Passes.Add((x, y)); return rig.Pass(x, y); },
                playSfx: id => rig.Sounds.Add(id), underground: underground, clock: rig.Clock);
            return rig;
        }

        private static void Glide(Rig rig, int direction, int frames) {
            for (var i = 0; i < frames; i++) {
                rig.Movement.Glide(direction, Frame, Cadence);
            }
        }

        // Glides forward until the party has gone `distance` north, and lets the camera arrive.
        private static void GlideNorth(Rig rig, int distance) {
            for (var i = 0; i < 10000 && rig.Session.PositionY < distance; i++) {
                rig.Movement.Glide(1, Frame, Cadence);
            }
            Glide(rig, 0, 60);
        }

        // ---- time per distance --------------------------------------------------------------------

        [TestCase(StepSize.Small, false)]
        [TestCase(StepSize.Medium, false)]
        [TestCase(StepSize.Large, false)]
        [TestCase(StepSize.Large, true)]
        [TestCase(StepSize.Medium, true)]
        public void TheSameDistanceCostsTheSameTimeAsClassicSteps(StepSize step, bool underground) {
            var classic = Build(step, underground);
            for (var i = 0; i < 8; i++) {
                classic.Movement.MoveForward();
            }
            int distance = classic.Session.PositionY;

            var glide = Build(step, underground);
            GlideNorth(glide, distance);

            Assert.AreEqual(distance, glide.Session.PositionY, "the glide ends on the same step point");
            Assert.AreEqual(classic.Session.GameTimeIn2Seconds, glide.Session.GameTimeIn2Seconds,
                "game time over the same distance");
            Assert.Greater(glide.Session.GameTimeIn2Seconds, 0);
        }

        [Test]
        public void TheGlideMovesInSmallStepsWhateverThePreference() {
            var rig = Build(StepSize.Large);
            GlideNorth(rig, 3200);
            CollectionAssert.AreEqual(
                new[] { (0, 400), (0, 800), (0, 1200), (0, 1600), (0, 2000), (0, 2400), (0, 2800), (0, 3200) },
                rig.Passes, "one hotspot pass per Small step, in order — the original's Small-step rule");
        }

        [Test]
        public void UndergroundTheGlideStepIsQuartered() {
            var rig = Build(StepSize.Large, underground: true);
            GlideNorth(rig, 400);
            CollectionAssert.AreEqual(new[] { (0, 100), (0, 200), (0, 300), (0, 400) }, rig.Passes);
        }

        // ---- the Stealth cell tick ------------------------------------------------------------------

        [TestCase(StepSize.Small)]
        [TestCase(StepSize.Large)]
        public void TheCellTickIsRaisedOncePer1600UnitsAsInClassic(StepSize step) {
            var rig = Build(step);
            rig.Session.TileBoundaryCrossed = 0;
            var raisedAt = new List<int>();
            for (var i = 0; i < 10000 && rig.Session.PositionY < 6400; i++) {
                int before = rig.Session.PositionY;
                rig.Movement.Glide(1, Frame, Cadence);
                Assert.LessOrEqual(rig.Session.PositionY - before, 400, "one step a frame at this speed");
                if (rig.Session.PositionY != before && rig.Session.TileBoundaryCrossed != 0) {
                    raisedAt.Add(rig.Session.PositionY);
                }
            }
            CollectionAssert.AreEqual(new[] { 1600, 3200, 4800, 6400 }, raisedAt);
            Assert.AreEqual(0, rig.Session.SubTileStepCount, "the saved counter ends where Classic's would");
        }

        // ---- triggers --------------------------------------------------------------------------------

        [Test]
        public void ATriggerTurnsTheStepBackAndTheGlideStopsOnTheLastPoint() {
            var rig = Build(StepSize.Small, camera: true);
            rig.Pass = (_, y) => y < 3200;   // a trigger rect from y 3200
            Glide(rig, 1, 120);
            Glide(rig, 0, 30);

            Assert.AreEqual(2800, rig.Session.PositionY, "the party stays before the trigger, as a turned-back step");
            Assert.IsFalse(rig.Movement.IsGliding);
            Assert.AreEqual(BakCoordinateConverter.ConvertPosition(0, 2800, 0), rig.Camera.transform.position);
            int fired = rig.Passes.FindAll(p => p.Y == 3200).Count;
            Assert.LessOrEqual(fired, 2 * Cadence + 1, "a held walk retries at the held cadence, not every frame");
            Object.DestroyImmediate(rig.Camera.gameObject);
        }

        // ---- collision -------------------------------------------------------------------------------

        [Test]
        public void ABlockedGlideStopsOnTheLastWalkablePointAndBumpsAtTheHeldCadence() {
            // Rock covering y -3500..-2500; walking backward (south) is refused at the first probe into
            // it, with no pivot rescue (WORLDMOV.C:140 sweeps only for mode 1, forward).
            var rig = Build(StepSize.Small, world: WorldWith((Rock, 0, -3000, 500)), camera: true);
            Glide(rig, -1, 120);   // 2 s held
            Glide(rig, 0, 30);

            Assert.AreEqual(-2400, rig.Session.PositionY, "stopped on the last step point before the rock");
            Assert.AreEqual(0, rig.Session.PositionX);
            Assert.AreEqual(BakCoordinateConverter.ConvertPosition(0, -2400, 0), rig.Camera.transform.position,
                "the camera stops there too, not inside the rock");
            Assert.That(rig.Sounds.Count, Is.InRange(1, (int)(2 * Cadence) + 1), "bump at the held walk's rate");
            Object.DestroyImmediate(rig.Camera.gameObject);
        }

        // ---- the camera and saves --------------------------------------------------------------------

        [Test]
        public void MidGlideThePartyIsOnAStepPointAndTheCameraBetween() {
            var rig = Build(StepSize.Small, camera: true);
            rig.Movement.Glide(1, Frame, Cadence);
            Assert.IsTrue(rig.Movement.IsGliding);
            Assert.AreEqual(400, rig.Session.PositionY, "the saved position is a real step point");
            float z = rig.Camera.transform.position.z * BakCoordinateConverter.WorldScale;
            Assert.That(z, Is.GreaterThan(0f).And.LessThan(400f), "the camera is on its way there");
            Object.DestroyImmediate(rig.Camera.gameObject);
        }

        [Test]
        public void TheGroundSpeedIsTheHeldWalkAtThePreference() {
            var small = Build(StepSize.Small);
            Glide(small, 1, 120);
            var large = Build(StepSize.Large);
            Glide(large, 1, 120);
            // 2 s at 8.84 steps a second: ~7071 units at Small, four times that at Large.
            Assert.That(small.Session.PositionY, Is.InRange(2 * Cadence * 400 - 400, 2 * Cadence * 400 + 400));
            Assert.That(large.Session.PositionY, Is.InRange(2 * Cadence * 1600 - 400, 2 * Cadence * 1600 + 400));
        }

        // ---- road-follow ---------------------------------------------------------------------------

        [Test]
        public void RoadFollowGlidesAlongTheRoadLattice() {
            var rig = Build(StepSize.Large, world: WorldWith(
                (Road, Centre, Centre, 700), (Road, Centre, Centre + Cell, 700),
                (Road, Centre, Centre + 2 * Cell, 700), (Road, Centre, Centre + 3 * Cell, 700)));
            rig.Session.PositionX = Centre;
            rig.Session.PositionY = Centre;
            Assert.IsTrue(rig.Movement.TryEngageTravel());
            GlideNorth(rig, Centre + 2 * Cell);

            Assert.AreEqual(Centre, rig.Session.PositionX);
            Assert.AreEqual(Centre + 2 * Cell, rig.Session.PositionY);
            Assert.IsTrue(rig.Movement.IsTravelling);
            Assert.AreEqual(8, rig.Passes.Count, "Small travel steps along the road");
        }

        // ---- the driver --------------------------------------------------------------------------------

        private static (ClassicMovementDriver, Rig, FakeGameplayInput) Driver(bool continuous, StepSize step) {
            var rig = Build(step);
            var input = new FakeGameplayInput();
            var driver = new ClassicMovementDriver(null, rig.Movement, input, new FakePointer(), () => Frame,
                continuous: () => continuous);
            return (driver, rig, input);
        }

        [Test]
        public void Classic_AHeldForwardStillStepsOnceThenWaits() {
            // The control: continuous walking off is the faithful driver, a whole preference step at once.
            var (driver, rig, input) = Driver(continuous: false, StepSize.Large);
            input.Move = new Vector2(0, 1);
            for (var i = 0; i < 20; i++) {   // inside the 0.38 s dead time
                driver.Tick(active: true);
            }
            Assert.AreEqual(1600, rig.Session.PositionY);
            Assert.AreEqual(120, rig.Session.GameTimeIn2Seconds);
        }

        [Test]
        public void Continuous_AHeldForwardGlidesWithoutTheDeadTime() {
            var (driver, rig, input) = Driver(continuous: true, StepSize.Small);
            input.Move = new Vector2(0, 1);
            for (var i = 0; i < 20; i++) {
                driver.Tick(active: true);
            }
            Assert.That(rig.Session.PositionY, Is.InRange(800, 1600), "a third of a second at 3536 units/s, no dead time");
        }

        [Test]
        public void Continuous_AOneFrameTapStillCoversAPreferenceStep() {
            var (driver, rig, input) = Driver(continuous: true, StepSize.Large);
            input.Move = new Vector2(0, 1);
            driver.Tick(active: true);
            input.Move = Vector2.zero;
            for (var i = 0; i < 60; i++) {
                driver.Tick(active: true);
            }
            Assert.AreEqual(1600, rig.Session.PositionY, "a tap walks a Classic tap's ground");
            Assert.AreEqual(120, rig.Session.GameTimeIn2Seconds, "for a Classic step's time");
            Assert.IsFalse(rig.Movement.IsGliding);
        }

        [Test]
        public void Continuous_TurningIsUnchanged() {
            var (driver, rig, input) = Driver(continuous: true, StepSize.Small);
            input.Move = new Vector2(-1, 0);
            driver.Tick(active: true);
            Assert.AreEqual((short)2048, rig.Session.Rotation, "a turn is still the Classic turn");
            Assert.AreEqual(0, rig.Session.PositionY);
        }

        [Test]
        public void Continuous_AnArrowClickGlidesInsteadOfStepping() {
            var (driver, rig, _) = Driver(continuous: true, StepSize.Large);
            Assert.IsTrue(driver.TapGlide(72));
            for (var i = 0; i < 60; i++) {
                driver.Tick(active: true);
            }
            Assert.AreEqual(1600, rig.Session.PositionY);

            var (classic, _, _) = Driver(continuous: false, StepSize.Large);
            Assert.IsFalse(classic.TapGlide(72), "Classic: the click steps as before");
        }

        // ---- a minimal collision world (PartyMovementCollisionTests' shapes) ------------------------

        private const int Ground = 0;
        private const int Road = 1;
        private const int Rock = 3;
        private const int Cell = 1600;
        private const int Centre = 800;

        private static GidRegion Square(int half) {
            var region = new GidRegion();
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = 125, AnchorX = (short)-half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 125, Dy = 0, AnchorX = (short)half, AnchorY = (short)half });
            region.Subedges.Add(new GidSubedge { Dx = 0, Dy = -125, AnchorX = (short)half, AnchorY = (short)-half });
            region.Subedges.Add(new GidSubedge { Dx = -125, Dy = 0, AnchorX = (short)-half, AnchorY = (short)-half });
            return region;
        }

        private static ZoneTableEntry Entry(int kind, int half) {
            var entry = new ZoneTableEntry {
                Dat = new TableDatInfo { EntityType = (WorldEntityType)(byte)kind },
                Gid = new TableGidInfo { XRadius = (short)half, YRadius = (short)half },
            };
            entry.Gid.Regions.Add(Square(half));
            return entry;
        }

        private static ProximityWorld WorldWith(params (int kind, int x, int y, int half)[] features) {
            var filter = new FilterData("FILTER.DAT");
            for (int level = 0; level < FilterData.DetailLevelCount; level++) {
                var block = new DetailLevelFilter { Level = level };
                for (int k = 0; k < FilterData.EntityTypeCount; k++) {
                    block.DrawDistances[k] = 1;
                }
                block.DrawDistances[7] = -1;
                filter.DetailLevels.Add(block);
            }
            var records = new List<ProximityRecord> {
                // The ground tile sits 12000 away so a nearby feature out-ranks it, as in the shipped data
                // (vislist_sort; PartyMovementCollisionTests).
                new ProximityRecord(Entry(Ground, 16000), 0, 12000, 0, 0),
            };
            foreach (var (kind, x, y, half) in features) {
                records.Add(new ProximityRecord(Entry(kind, half), x, y, 0, 0));
            }
            return new ProximityWorld(records, filter);
        }
    }
}

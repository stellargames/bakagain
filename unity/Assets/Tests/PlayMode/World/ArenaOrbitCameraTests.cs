namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.World.Encounters;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>Enhanced combat's orbit camera (TASK-972): the gesture, the wheel, the pose, the Classic control.</summary>
    public class ArenaOrbitCameraTests {
        private static readonly Vector2 World = new Vector2(800, 400);
        private FakePointer _pointer;
        private CombatCameraInput _input;
        private float _dx, _dy, _zoom;
        private int _orbits, _zooms;

        [SetUp]
        public void SetUp() {
            _pointer = new FakePointer();
            _input = new CombatCameraInput(_pointer, new EnhancedTravelLayout(), new EnhancedCombatLayout());
            _dx = _dy = _zoom = 0f;
            _orbits = _zooms = 0;
            _pointer.ScreenPosition = World;
        }

        private void Tick(bool enabled = true, bool onWorld = true) =>
            _input.Tick(enabled, _ => onWorld, (x, y) => { _dx += x; _dy += y; _orbits++; }, f => { _zoom = f; _zooms++; });

        private void RightDrag(float pixels) {
            _pointer.Secondary.SetDown(true, pressedThisFrame: true);
            Tick();
            _pointer.Delta = new Vector2(pixels, 0);
            Tick();
            _pointer.Delta = Vector2.zero;
            _pointer.Secondary.SetDown(false, releasedThisFrame: true);
            Tick();
        }

        [Test]
        public void InEnhancedCombat_ADragOnTheWorldOrbits_ATapDoesNot() {
            // Past the 8 px threshold, from a press the world took (a combatant is a world press).
            RightDrag(9f);
            Assert.AreEqual(1, _orbits);
            Assert.AreEqual(9f, _dx, 1e-4f, "the drag reaches the orbit in pixels");
            Assert.IsTrue(_input.ConsumeClick(), "the release of a drag is not also a click");

            _orbits = 0;
            RightDrag(7f);
            Assert.AreEqual(0, _orbits, "under the threshold is a tap");
            Assert.IsFalse(_input.ConsumeClick());
        }

        [Test]
        public void ADragThatStartsOnTheHud_DoesNotOrbit() {
            _pointer.Secondary.SetDown(true, pressedThisFrame: true);
            Tick(onWorld: false);
            _pointer.Delta = new Vector2(50, 0);
            Tick(onWorld: false);
            Assert.AreEqual(0, _orbits);
        }

        [Test]
        public void TheWheelOverTheBoardZooms() {
            _pointer.Scroll = new Vector2(0, 120);
            Tick();
            Assert.AreEqual(1, _zooms);
            Assert.Less(_zoom, 1f, "wheel up comes closer");

            _pointer.Scroll = new Vector2(0, -120);
            Tick();
            Assert.Greater(_zoom, 1f);

            _zooms = 0;
            Tick(onWorld: false);
            Assert.AreEqual(0, _zooms, "the wheel over the HUD does not zoom the board");
        }

        // ---- the real path: InGameScreen gesture -> WorldRuntime -> camera ----

        private const int PartyX = 50000, PartyY = 60000, Cell = 300;
        private bool _enhanced, _combatOn;
        private GameObject _camGo, _hudGo;

        private BakAgain.World.WorldRuntime NewWorld(bool combat, out Camera cam) {
            _enhanced = BakAgain.Core.GameOptions.Enhanced;
            _combatOn = BakAgain.Core.GameOptions.GetFeature(BakAgain.Core.EnhancedFeature.Combat);
            BakAgain.Core.GameOptions.Enhanced = true;
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, combat);
            _camGo = new GameObject("arenaCam");
            cam = _camGo.AddComponent<Camera>();
            var session = new BakAgain.Core.GameSession { PositionX = PartyX, PositionY = PartyY, Rotation = 0 };
            var world = new BakAgain.World.WorldRuntime(null, session, null, null, null, null, null);
            world.SetArenaCameraForTest(cam, new GameData.Resources.Config.StartData("START") {
                CombatGridCellSize = Cell, CombatCameraHeightAboveGround = 800, CombatCameraHeightUnderground = 1024 },
                underground: false);
            return world;
        }

        [TearDown]
        public void RestoreOptions() {
            BakAgain.Core.GameOptions.Enhanced = _enhanced;
            BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, _combatOn);
            if (_camGo != null) { Object.DestroyImmediate(_camGo); }
            if (_hudGo != null) { Object.DestroyImmediate(_hudGo); }
        }

        private InGameScreen NewScreen() {
            _hudGo = new GameObject("hud");
            _hudGo.SetActive(false);
            var hud = _hudGo.AddComponent<InGameScreen>();
            hud.SetInCombatPredicate(() => true);
            hud.SetPointerForTest(_pointer);
            return hud;
        }

        private void DragWith(FakePointer.Button button, InGameScreen hud, BakAgain.World.WorldRuntime world, float pixels) {
            button.SetDown(true, pressedThisFrame: true);
            hud.TickCombatCamera(world, _ => true);
            _pointer.Delta = new Vector2(pixels, 0);
            hud.TickCombatCamera(world, _ => true);
            _pointer.Delta = Vector2.zero;
            button.SetDown(false, releasedThisFrame: true);
            hud.TickCombatCamera(world, _ => true);
        }

        private static Vector3 BoardCentreInUnity() {
            var c = ArenaOrbit.BoardCentre(Cell, underground: false);
            return BakAgain.World.Converters.BakCoordinateConverter.ConvertPosition(
                PartyX + (int)System.Math.Round(c.X), PartyY + (int)System.Math.Round(c.Y), 0);
        }

        private static float Horizontal(Vector3 p, Vector3 c) => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(c.x, c.z));

        [Test]
        public void ClassicCombatCameraUnchanged() {
            BakAgain.World.WorldRuntime world = NewWorld(combat: false, out Camera cam);
            world.PointCameraAtArena();

            Assert.IsFalse(world.ArenaUsesOrbit);
            Assert.IsFalse(world.HasArenaOrbit, "Classic combat creates no orbit");
            Vector3 fixedEye = BakAgain.World.Converters.BakCoordinateConverter.ConvertPosition(PartyX, PartyY, 800);
            Assert.AreEqual(0f, Vector3.Distance(fixedEye, cam.transform.position), 1e-3f, "the original's fixed pose");

            Quaternion rot = cam.transform.rotation;
            world.OrbitArena(200, 40);
            world.ZoomArena(0.5f);
            Assert.AreEqual(0f, Vector3.Distance(fixedEye, cam.transform.position), 1e-3f, "orbit and zoom are no-ops");
            Assert.AreEqual(0f, Quaternion.Angle(rot, cam.transform.rotation), 1e-3f);

            // The gesture reads nothing either: a drag and a wheel with Combat off move nothing.
            InGameScreen hud = NewScreen();
            _pointer.Scroll = new Vector2(0, 120);
            DragWith(_pointer.Secondary, hud, world, 40f);
            DragWith(_pointer.Primary, hud, world, 40f);
            Assert.AreEqual(0f, Vector3.Distance(fixedEye, cam.transform.position), 1e-3f);
        }

        [Test]
        public void InEnhancedCombat_AnInGameScreenDragMovesTheRealCameraAroundTheCentre_ATapDoesNot() {
            BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
            world.PointCameraAtArena();
            Assert.IsTrue(world.HasArenaOrbit);
            InGameScreen hud = NewScreen();
            Vector3 centre = BoardCentreInUnity();
            Vector3 start = cam.transform.position;
            float horizontal = Horizontal(start, centre), height = start.y;

            DragWith(_pointer.Secondary, hud, world, 7f);
            Assert.AreEqual(0f, Vector3.Distance(start, cam.transform.position), 1e-4f, "7 px is a tap");

            DragWith(_pointer.Secondary, hud, world, 60f);
            Vector3 moved = cam.transform.position;
            Assert.Greater(Vector3.Distance(start, moved), 0.1f, "a drag past the threshold moves the camera");
            // The radius itself follows the whole-board fit, which a new yaw moves (review I3): the pitch is what
            // a sideways drag must keep.
            Assert.AreEqual(height / horizontal, moved.y / Horizontal(moved, centre), 1e-3f,
                $"around the centre, pitch unchanged by a sideways drag (start {start} moved {moved} centre {centre})");
            Vector3 seen = cam.WorldToViewportPoint(new Vector3(centre.x, 0, centre.z));
            var layout = new EnhancedCombatLayout();
            Assert.AreEqual(0.5f, seen.x, 1e-3f, "still looking at the centre");
            Assert.AreEqual((layout.BandHeight + 1f) / 2f, seen.y, 1e-3f, "drawn at the middle of the space above the band");

            float before = Vector3.Distance(moved, new Vector3(centre.x, 0, centre.z));
            _pointer.Scroll = new Vector2(0, 120);
            hud.TickCombatCamera(world, _ => true);
            Assert.Less(Vector3.Distance(cam.transform.position, new Vector3(centre.x, 0, centre.z)), before, "wheel comes closer");
        }

        [Test]
        public void InEnhancedCombat_ALeftMouseDragOrbitsToo_ALeftClickStaysAClick() {
            BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
            world.PointCameraAtArena();
            InGameScreen hud = NewScreen();
            Vector3 start = cam.transform.position;

            DragWith(_pointer.Primary, hud, world, 5f);
            Assert.AreEqual(0f, Vector3.Distance(start, cam.transform.position), 1e-4f);
            Assert.IsFalse(_input.ConsumeClick(), "unrelated input untouched");

            DragWith(_pointer.Primary, hud, world, 40f);
            Assert.Greater(Vector3.Distance(start, cam.transform.position), 0.1f, "a left drag orbits");
        }

        [Test]
        public void ALeftDragOrbits_ItsReleaseIsNotAClick_ATapIs() {
            var tap = new CombatCameraInput(_pointer, new EnhancedTravelLayout(), new EnhancedCombatLayout());
            _pointer.Primary.SetDown(true, pressedThisFrame: true);
            tap.Tick(true, _ => true, (x, y) => _orbits++, f => { });
            _pointer.Delta = new Vector2(3, 0);
            tap.Tick(true, _ => true, (x, y) => _orbits++, f => { });
            _pointer.Delta = Vector2.zero;
            _pointer.Primary.SetDown(false, releasedThisFrame: true);
            tap.Tick(true, _ => true, (x, y) => _orbits++, f => { });
            Assert.AreEqual(0, _orbits);
            Assert.IsFalse(tap.ConsumeClick(), "a left click under the threshold is still a click");

            _pointer.Primary.SetDown(true, pressedThisFrame: true);
            tap.Tick(true, _ => true, (x, y) => _orbits++, f => { });
            _pointer.Delta = new Vector2(30, 0);
            tap.Tick(true, _ => true, (x, y) => _orbits++, f => { });
            _pointer.Delta = Vector2.zero;
            _pointer.Primary.SetDown(false, releasedThisFrame: true);
            tap.Tick(true, _ => true, (x, y) => _orbits++, f => { });
            Assert.AreEqual(1, _orbits);
            Assert.IsTrue(tap.ConsumeClick(), "the release of a left drag is not a click");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TheFarthestZoomFramesTheWholeBoardInTheSpaceTheHudLeaves(bool phone) {
            bool? keep = InGameScreen.PhoneHudOverride;
            InGameScreen.PhoneHudOverride = phone;
            try {
                BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
                world.PointCameraAtArena();
                var layout = new EnhancedCombatLayout();
                double opened = world.ArenaOrbitForTest.Distance;
                // Zoomed all the way out, the whole board just fits: no zoom goes farther (owner, 2026-10-28).
                world.ZoomArena(1000f);
                double whole = world.ArenaOrbitForTest.Distance;
                Assert.AreEqual(phone ? layout.PhoneStartZoom : 1f, opened / whole, 1e-6,
                    "the desktop opens at the whole board, the phone closer");
                AssertTheWholeBoardJustFits(cam, layout, phone, "at the opening pose");
                var orbit = world.ArenaOrbitForTest;
                Assert.AreEqual(layout.StartPitchDegrees, orbit.PitchDegrees, 1e-6, "opens at the layout's pitch");
            } finally {
                InGameScreen.PhoneHudOverride = keep;
            }
        }

        // Review I3: the whole-board fit moves with the yaw and the pitch (at 45 degrees the board is about
        // 15 cells both ways), so it is refitted after each input and the farthest zoom frames it again.
        [TestCase(false)]
        [TestCase(true)]
        public void AfterATurnOrADragTheFarthestZoomFramesTheWholeBoardAgain(bool phone) {
            bool? keep = InGameScreen.PhoneHudOverride;
            InGameScreen.PhoneHudOverride = phone;
            try {
                BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
                world.PointCameraAtArena();
                var layout = new EnhancedCombatLayout();
                world.TurnArenaQuarter(1);
                world.ZoomArena(1000f);
                AssertTheWholeBoardJustFits(cam, layout, phone, "a quarter turn");
                world.OrbitArena(-45f / layout.OrbitDegreesPerPixel, 40f);   // 45 degrees round, and steeper
                world.ZoomArena(1000f);
                AssertTheWholeBoardJustFits(cam, layout, phone, "at 45 degrees and a steeper pitch");
            } finally {
                InGameScreen.PhoneHudOverride = keep;
            }
        }

        [Test]
        public void ZoomedIn_ADragKeepsTheCameraDistance() {
            // Re-review R2: only the limits follow the refitted whole-board distance; a close view stays close.
            BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
            world.PointCameraAtArena();
            var layout = new EnhancedCombatLayout();
            world.ZoomArena(0.4f);
            double close = world.ArenaOrbitForTest.Distance;
            world.OrbitArena(-45f / layout.OrbitDegreesPerPixel, 0f);
            Assert.AreEqual(close, world.ArenaOrbitForTest.Distance, close * 1e-6, "no drift in or out");
            world.ZoomArena(1000f);
            AssertTheWholeBoardJustFits(cam, layout, false, "zoomed back out at 45 degrees");
        }

        private static void AssertTheWholeBoardJustFits(Camera cam, EnhancedCombatLayout layout, bool phone, string pose) {
            float bottom = phone ? layout.PhoneBoardBottom : layout.BandHeight, top = phone ? layout.PhoneBoardTop : 1f;
            float slack = 1f;
            foreach (var c in ArenaOrbit.BoardCorners(Cell, underground: false)) {
                Vector3 v = cam.WorldToViewportPoint(BakAgain.World.Converters.BakCoordinateConverter.ConvertPosition(
                    PartyX + (int)System.Math.Round(c.X), PartyY + (int)System.Math.Round(c.Y), 0));
                Assert.Greater(v.z, 0f, pose);
                Assert.That(v.x, Is.InRange(layout.BoardFitMarginX - 2e-3f, 1f - layout.BoardFitMarginX + 2e-3f), $"{pose}: corner {c} across");
                Assert.That(v.y, Is.InRange(bottom, top), $"{pose}: corner {c} clear of the HUD");
                slack = Mathf.Min(slack, Mathf.Min(Mathf.Min(v.x - layout.BoardFitMarginX, 1f - layout.BoardFitMarginX - v.x),
                    Mathf.Min(v.y - bottom - layout.BoardFitMarginY, top - layout.BoardFitMarginY - v.y)));
            }
            Assert.AreEqual(0f, slack, 0.005f, $"{pose}: as close as the board fits, one corner on its margin");
        }

        [Test]
        public void TheCameraSitsOnTheOrbitAndLooksAtTheCentre() {
            var go = new GameObject("cam");
            try {
                var cam = go.AddComponent<Camera>();
                var orbit = ArenaOrbit.From(new EnhancedCombatLayout(), (0, 1500), 0, 0, 600);
                var rig = new ArenaOrbitCamera();
                rig.Bind(cam, orbit, 10000, 20000, 0);
                rig.Apply();
                Vector3 centre = new Vector3(10000 / 100f, 0, (20000 + 1500) / 100f);
                Vector3 toCentre = (centre - cam.transform.position).normalized;
                Assert.AreEqual(1f, Vector3.Dot(cam.transform.forward, toCentre), 1e-3f);
                Assert.AreEqual(Mathf.Sqrt(1500 * 1500 + 600 * 600) / 100f,
                    Vector3.Distance(cam.transform.position, centre), 0.02f);

                orbit.TurnQuarter(1);
                rig.Apply();
                Assert.AreEqual(1f, Vector3.Dot(cam.transform.forward, (centre - cam.transform.position).normalized), 1e-3f);
            } finally {
                Object.DestroyImmediate(go);
            }
        }

        // Owner's phone round 4 (2026-10-30): "the camera appears to be jerking around the enemy turns". A monster
        // already in view leaves the camera still; one out of view is glided to before it acts (Enhanced only).
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator AMonsterOutOfViewIsGlidedTo_OneInViewLeavesTheCameraStill() {
            bool? keep = InGameScreen.PhoneHudOverride;
            InGameScreen.PhoneHudOverride = true;
            try {
                BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
                world.PointCameraAtArena();
                world.ZoomArena(0.01f);   // the closest view: most of the board is off screen
                var orbit = world.ArenaOrbitForTest;
                var near = new GameData.Resources.Combat.Combatant { X = 3, Y = 6 };   // the board's middle
                var far = new GameData.Resources.Combat.Combatant { X = 0, Y = 12 };   // its far corner
                (double X, double Y) before = orbit.Centre;
                var still = world.FrameEnemyForTest(near);
                Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, still.Status, "in view: done at once");
                Assert.AreEqual(before, orbit.Centre, "in view: the camera does not move");

                var glide = world.FrameEnemyForTest(far);
                Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending, glide.Status, "out of view: it waits for the glide");
                float until = Time.realtimeSinceStartup + 3f;
                while (glide.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending && Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, glide.Status);
                (int ax, int ay) = GameData.Resources.Combat.CombatArenaPlacement.CellOffset(far.X, far.Y, Cell);
                Assert.AreEqual(ax, orbit.Centre.X, 1, "the closest view centres on the monster");
                Assert.AreEqual(ay, orbit.Centre.Y, 1);
            } finally {
                InGameScreen.PhoneHudOverride = keep;
            }
        }

        // Review I2: the next hero already in view leaves the camera still; only one out of view is glided to.
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator AHeroAlreadyInViewCausesNoCameraMove() {
            bool? keep = InGameScreen.PhoneHudOverride;
            InGameScreen.PhoneHudOverride = true;
            try {
                BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
                world.PointCameraAtArena();
                world.ZoomArena(0.01f);
                var orbit = world.ArenaOrbitForTest;
                world.FocusHeroForTest(new GameData.Resources.Combat.Combatant { X = 3, Y = 6 });   // the fight's first: framed at once
                (double X, double Y) framed = orbit.Centre;
                world.FocusHeroForTest(new GameData.Resources.Combat.Combatant { X = 4, Y = 6 });   // the next cell, in view
                for (int i = 0; i < 10; i++) {
                    yield return null;
                }
                Assert.AreEqual(framed, orbit.Centre, "a hero in view: no glide");

                world.FocusHeroForTest(new GameData.Resources.Combat.Combatant { X = 0, Y = 12 });  // the far corner
                float until = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.AreNotEqual(framed, orbit.Centre, "a hero out of view: the control, it glides");
            } finally {
                InGameScreen.PhoneHudOverride = keep;
            }
        }

        // Review M1: at the whole-board zoom the centre ignores the focus, so a glide would move nothing: no wait.
        [Test]
        public void AtTheWholeBoardZoomAMonsterCostsNoGlide() {
            BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
            world.PointCameraAtArena();
            world.ZoomArena(1000f);
            var orbit = world.ArenaOrbitForTest;
            (double X, double Y) before = orbit.Centre;
            var task = world.FrameEnemyForTest(new GameData.Resources.Combat.Combatant { X = 0, Y = 12 });
            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, task.Status, "no wait");
            Assert.AreEqual(before, orbit.Centre);
        }

        [Test]
        public void ClassicNeverMovesTheCameraForAMonster() {
            BakAgain.World.WorldRuntime world = NewWorld(combat: false, out Camera cam);
            world.PointCameraAtArena();
            Vector3 eye = cam.transform.position;
            var task = world.FrameEnemyForTest(new GameData.Resources.Combat.Combatant { X = 0, Y = 12 });
            Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded, task.Status);
            Assert.AreEqual(0f, Vector3.Distance(eye, cam.transform.position), 1e-3f);
        }

        // The original waits for a swing and a fall (combat_actor_anim_play wait 1, CACTOR.C:1895); the arena
        // counts them as work, so the next turn cannot start and rebuild the sprite under them.
        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ASwingHoldsTheArenaUntilItEnds() {
            BakAgain.World.WorldRuntime world = NewWorld(combat: false, out _);
            var swing = new Cysharp.Threading.Tasks.UniTaskCompletionSource();
            world.HoldArenaWhile(swing.Task);
            Assert.AreEqual(1, world.ArenaWorkForTest, "busy while it plays");
            swing.TrySetResult();
            yield return null;
            Assert.AreEqual(0, world.ArenaWorkForTest, "idle once it has played");
        }
    }
}

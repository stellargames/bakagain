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
            Assert.AreEqual(horizontal, Horizontal(moved, centre), 0.05f, $"around the centre: the radius stays (start {start} moved {moved} centre {centre})");
            Assert.AreEqual(height, moved.y, 0.05f, "pitch unchanged by a sideways drag");
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
        public void TheOpeningPoseFramesTheWholeBoardInTheSpaceTheHudLeaves(bool phone) {
            bool? keep = InGameScreen.PhoneHudOverride;
            InGameScreen.PhoneHudOverride = phone;
            try {
                BakAgain.World.WorldRuntime world = NewWorld(combat: true, out Camera cam);
                world.PointCameraAtArena();
                var layout = new EnhancedCombatLayout();
                float bottom = phone ? layout.PhoneBoardBottom : layout.BandHeight, top = phone ? layout.PhoneBoardTop : 1f;
                float slack = 1f;
                foreach (var c in ArenaOrbit.BoardCorners(Cell, underground: false)) {
                    Vector3 v = cam.WorldToViewportPoint(BakAgain.World.Converters.BakCoordinateConverter.ConvertPosition(
                        PartyX + (int)System.Math.Round(c.X), PartyY + (int)System.Math.Round(c.Y), 0));
                    Assert.Greater(v.z, 0f);
                    Assert.That(v.x, Is.InRange(layout.BoardFitMarginX - 1e-3f, 1f - layout.BoardFitMarginX + 1e-3f), $"corner {c} across");
                    Assert.That(v.y, Is.InRange(bottom, top), $"corner {c} clear of the HUD");
                    slack = Mathf.Min(slack, Mathf.Min(Mathf.Min(v.x - layout.BoardFitMarginX, 1f - layout.BoardFitMarginX - v.x),
                        Mathf.Min(v.y - bottom - layout.BoardFitMarginY, top - layout.BoardFitMarginY - v.y)));
                }
                Assert.AreEqual(0f, slack, 0.005f, "as close as the board fits: one corner sits on its margin");
                var orbit = world.ArenaOrbitForTest;
                Assert.AreEqual(layout.StartPitchDegrees, orbit.PitchDegrees, 1e-6, "opens at the layout's pitch");
            } finally {
                InGameScreen.PhoneHudOverride = keep;
            }
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
    }
}

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

        [Test]
        public void ClassicCombatCameraUnchanged() {
            bool enhanced = BakAgain.Core.GameOptions.Enhanced;
            bool combat = BakAgain.Core.GameOptions.GetFeature(BakAgain.Core.EnhancedFeature.Combat);
            try {
                BakAgain.Core.GameOptions.Enhanced = true;
                BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, false);
                Assert.IsFalse(BakAgain.World.WorldRuntime.ArenaUsesOrbit, "Classic combat keeps the original's fixed camera");
                BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, true);
                Assert.IsTrue(BakAgain.World.WorldRuntime.ArenaUsesOrbit);
            } finally {
                BakAgain.Core.GameOptions.Enhanced = enhanced;
                BakAgain.Core.GameOptions.SetFeature(BakAgain.Core.EnhancedFeature.Combat, combat);
            }

            // The gestures are only read while the caller says Enhanced combat is active.
            _pointer.Scroll = new Vector2(0, 120);
            _pointer.Secondary.SetDown(true, pressedThisFrame: true);
            _input.Tick(false, _ => true, (x, y) => _orbits++, f => _zooms++);
            _pointer.Delta = new Vector2(40, 0);
            _input.Tick(false, _ => true, (x, y) => _orbits++, f => _zooms++);
            Assert.AreEqual(0, _orbits + _zooms);
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

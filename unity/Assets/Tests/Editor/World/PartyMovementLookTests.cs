namespace BakAgain.Tests.Editor.World {
    using BakAgain.Core;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using GameData.Resources.Config;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>Enhanced free look: LookTo sets the real heading; LookPitch only tilts the view.</summary>
    public class PartyMovementLookTests {
        private GameObject _cam;

        [TearDown]
        public void TearDown() {
            if (_cam != null) Object.DestroyImmediate(_cam);
        }

        private (PartyMovement, GameSession, Camera) Build() {
            var session = new GameSession();
            var prefs = new FakePreferencesService();
            prefs.Current.TurnSize = TurnSize.Medium;
            _cam = new GameObject("cam");
            Camera cam = _cam.AddComponent<Camera>();
            var movement = new PartyMovement(session, new MovementData("MOVEMENT.DAT") {
                StepDistances = new[] { 100, 200, 400 }, TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                SecondsPerStep = new[] { 60, 120, 240 },
            }, prefs, cam, cameraHeightZ: 0, cameraPitch: 0);
            return (movement, session, cam);
        }

        [Test]
        public void LookToSetsTheRealHeadingAndTheCameraFollows() {
            var (movement, session, cam) = Build();
            movement.LookTo(12345);
            Assert.AreEqual(12345, (ushort)session.Rotation);
            Quaternion expected = BakCoordinateConverter.ConvertRotation(0, 0, 12345);
            Assert.Less(Quaternion.Angle(expected, cam.transform.rotation), 0.01f);
        }

        [Test]
        public void LookPitchTiltsTheCameraButIsNeverStored() {
            var (movement, session, cam) = Build();
            var before = (session.Rotation, session.PositionX, session.PositionY, session.PositionZ);
            movement.LookPitch = 1000;
            movement.SyncToCamera();
            Assert.Greater(Quaternion.Angle(BakCoordinateConverter.ConvertRotation(0, 0, 0), cam.transform.rotation), 1f);
            Assert.AreEqual(before, (session.Rotation, session.PositionX, session.PositionY, session.PositionZ),
                "pitch has no session field to leak into; the pose is untouched");
        }

        private const float Units = 65536f / 360f;

        [Test]
        public void ApplyLookClampsThePitchBothWays() {
            var (movement, _, _) = Build();
            movement.ApplyLook(0f, 50f, 30f);
            Assert.AreEqual((short)(30f * Units), movement.LookPitch, "held at +limit");
            movement.ApplyLook(0f, -10f, 30f);
            Assert.AreEqual((short)(20f * Units), movement.LookPitch, "and comes back from it");
            movement.ApplyLook(0f, -500f, 30f);
            Assert.AreEqual((short)(-30f * Units), movement.LookPitch, "held at -limit");
        }

        [Test]
        public void ApplyLookTurnsTheHeadingThroughLookToRightIsPositive() {
            var (movement, session, cam) = Build();
            session.Rotation = 0x4000;
            movement.ApplyLook(10f, 0f, 30f);
            ushort expected = unchecked((ushort)(0x4000 - Mathf.RoundToInt(10f * Units)));
            Assert.AreEqual(expected, (ushort)session.Rotation);
            Assert.Less(Quaternion.Angle(BakCoordinateConverter.ConvertRotation(0, 0, expected), cam.transform.rotation), 0.01f,
                "the camera follows, as LookTo makes it");
        }

        [Test]
        public void LevelLookZeroesThePitchAndResyncsTheCamera() {
            var (movement, session, cam) = Build();
            session.Rotation = 0x2000;
            movement.ApplyLook(0f, 20f, 30f);
            Assert.Greater(Quaternion.Angle(BakCoordinateConverter.ConvertRotation(0, 0, 0x2000), cam.transform.rotation), 1f);

            movement.LevelLook();

            Assert.AreEqual(0, movement.LookPitch);
            Assert.Less(Quaternion.Angle(BakCoordinateConverter.ConvertRotation(0, 0, 0x2000), cam.transform.rotation), 0.01f,
                "the view is level again at once, not at the next move");
        }

        [Test]
        public void LevelLookIsANoOpWhenAlreadyLevel() {
            var (movement, _, cam) = Build();
            Quaternion marker = Quaternion.Euler(1f, 2f, 3f);
            cam.transform.rotation = marker;
            movement.LevelLook();
            Assert.Less(Quaternion.Angle(marker, cam.transform.rotation), 0.01f, "nothing written");
        }
    }
}

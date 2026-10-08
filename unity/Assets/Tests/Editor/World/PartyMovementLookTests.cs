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
    }
}

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

        private (PartyMovement, GameSession, Camera) Build(TurnSize turnSize = TurnSize.Medium) {
            var session = new GameSession();
            var prefs = new FakePreferencesService();
            prefs.Current.TurnSize = turnSize;
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
        public void ThePitchSlowsTheFurtherItLooksAndNeverReachesTheMax() {
            float a = PartyMovement.EasePitch(0f, 10f, 89f);
            Assert.AreEqual(10f, a, 0.5f, "near level a drag pitches about one for one");
            float b = PartyMovement.EasePitch(60f, 10f, 89f) - 60f;
            Assert.Less(b, 6f, "at 60 degrees the same drag pitches much less");
            Assert.Greater(b, 0f);
            Assert.Less(PartyMovement.EasePitch(0f, 10000f, 89f), 89f, "no drag reaches the max");
            Assert.Greater(PartyMovement.EasePitch(0f, -10000f, 89f), -89f, "either way");
            Assert.AreEqual(25f, PartyMovement.EasePitch(PartyMovement.EasePitch(25f, 70f, 89f), -70f, 89f), 0.01f,
                "dragging back undoes it");
        }

        [Test]
        public void ApplyLookHasNoHardStop() {
            var (movement, _, _) = Build();
            movement.ApplyLook(0f, 50f, 89f);
            short first = movement.LookPitch;
            Assert.Greater(first, (short)(30f * Units), "past the old 30 degree stop");
            movement.ApplyLook(0f, 50f, 89f);
            Assert.Greater(movement.LookPitch, first, "a further drag still looks further up");
            Assert.Less(movement.LookPitch - first, first, "but by less than the first");
            movement.ApplyLook(0f, -500f, 89f);
            Assert.Less(movement.LookPitch, 0);
            Assert.Greater(movement.LookPitch, (short)(-89f * Units));
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

        [TestCase(TurnSize.Small, 0.5f)]
        [TestCase(TurnSize.Medium, 1f)]
        [TestCase(TurnSize.Large, 2f)]
        public void TheTurnSizePreferenceScalesTheLook(TurnSize turnSize, float scale) {
            var (movement, session, _) = Build(turnSize);
            session.Rotation = 0x4000;
            movement.ApplyLook(10f, 4f, 89f);
            Assert.AreEqual(unchecked((ushort)(0x4000 - Mathf.RoundToInt(10f * scale * Units))), (ushort)session.Rotation);
            Assert.AreEqual(PartyMovement.EasePitch(0f, 4f * scale, 89f) * Units, movement.LookPitch, 1f);
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

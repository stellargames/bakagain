namespace BakAgain.Tests.Editor.World {
    using BakAgain.Core;
    using BakAgain.World;
    using GameData.Resources.Config;
    using NUnit.Framework;
    using UnityEngine;

    public class PartyMovementTests {
        // MOVEMENT.DAT-shaped table with distinct per-preset values so the test can tell which preset was used.
        private static MovementData Table() => new MovementData("MOVEMENT.DAT") {
            StepDistances = new[] { 100, 200, 400 },     // Small/Medium/Large
            TurnAngles    = new[] { 0x1000, 0x2000, 0x4000 },
            SecondsPerStep = new[] { 60, 120, 240 },
        };

        private static (PartyMovement pm, GameSession session, FakePreferencesService prefs, GameObject cam)
            Build(StepSize step = StepSize.Medium, TurnSize turn = TurnSize.Medium) {
            var session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 };
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = step;
            prefs.Current.TurnSize = turn;
            var cam = new GameObject("cam").AddComponent<Camera>();
            var pm = new PartyMovement(session, Table(), prefs, cam, cameraHeightZ: 0, cameraPitch: 0);
            return (pm, session, prefs, cam.gameObject);
        }

        [Test]
        public void MoveForward_UsesStepDistanceForPreferenceStepSize() {
            var (pm, session, _, cam) = Build(step: StepSize.Large); // StepDistances[Large] = 400
            pm.MoveForward();
            // Heading 0: StepDelta(0, 400) = (dx=0, dy=400) -> PositionY += 400.
            Assert.AreEqual(0, session.PositionX);
            Assert.AreEqual(400, session.PositionY);
            Object.DestroyImmediate(cam);
        }

        // The original's dialog is modal, so a sub-action-12 request is consumed after the whole
        // conversation (WORLDLP.C:185). Between two of our pages the travel screen owns input for a
        // frame; the pass must wait, or Navon's fight opens on the first page of his confrontation.
        [Test]
        public void ARequestedHotspotPassWaitsForTheConversationToEnd() {
            var session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 };
            var cam = new GameObject("cam").AddComponent<Camera>();
            int passes = 0;
            var pm = new PartyMovement(session, Table(), new FakePreferencesService(), cam,
                cameraHeightZ: 0, cameraPitch: 0, hotspotPass: (x, y) => { passes++; return true; });
            session.HotspotPassRequested = true;
            session.DialogsPlaying = 1;

            pm.RunRequestedHotspotPass();
            Assert.AreEqual(0, passes, "ran between two pages of a conversation");
            Assert.IsTrue(session.HotspotPassRequested, "and the request must survive to run later");

            session.DialogsPlaying = 0;
            pm.RunRequestedHotspotPass();
            Assert.AreEqual(1, passes, "control: with the conversation over it runs");
            Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void AnAutomaticTurnSwingsAStridePerFrameTheShortWayAndLandsExactly() {
            // worldmove_animate_hdg_tgt (WORLDMOV.C:161), TASK-443: one stride per rendered frame.
            Assert.AreEqual((ushort)0x2000, PartyMovement.SwingStep(0x0000, 0x5000, 0x2000));
            Assert.AreEqual((ushort)0xE000, PartyMovement.SwingStep(0x0000, 0xC000, 0x2000), "the short way round");
            Assert.IsNull(PartyMovement.SwingStep(0x4000, 0x5000, 0x2000), "within a stride it lands");
            Assert.IsNull(PartyMovement.SwingStep(0x5000, 0x5000, 0x2000));
        }

        [Test]
        public void TurnLeft_UsesTurnAngleForPreferenceTurnSize() {
            var (pm, session, _, cam) = Build(turn: TurnSize.Large); // TurnAngles[Large] = 0x4000
            pm.TurnLeft();
            // TurnLeft -> Turn(0, 0x4000, left:false) = 0x4000 = 16384.
            Assert.AreEqual((short)16384, session.Rotation);
            Object.DestroyImmediate(cam);
        }

        [Test]
        public void Movement_ReadsPreferencesLive_NotCachedAtConstruction() {
            var (pm, session, prefs, cam) = Build(step: StepSize.Small); // 100
            pm.MoveForward();                       // +100  -> PositionY = 100
            prefs.Current.StepSize = StepSize.Large; // 400 (no re-construction)
            pm.MoveForward();                       // +400  -> PositionY = 500
            Assert.AreEqual(500, session.PositionY);
            Object.DestroyImmediate(cam);
        }
    }
}

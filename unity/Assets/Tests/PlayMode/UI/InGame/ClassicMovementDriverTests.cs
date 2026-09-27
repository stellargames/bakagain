namespace BakAgain.Tests.PlayMode.UI.InGame {
    using BakAgain.Core;
    using BakAgain.Tests.Editor.World;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.World;
    using GameData.Resources.Config;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Locks ClassicMovementDriver's axis->action interpretation of IGameplayInput.Move: this is
    /// the driver's own convention (Up=+y -> MoveForward), independent of whether the real device
    /// composite agrees — that binding fact is verified live against the running Editor separately.
    /// PartyMovement is sealed/non-virtual, so — mirroring PartyMovementTests — we assert via the
    /// observable GameSession effect rather than a call-count spy.
    /// </summary>
    public class ClassicMovementDriverTests {
        // MOVEMENT.DAT-shaped table with distinct per-preset values (mirrors PartyMovementTests.Table).
        private static MovementData Table() => new MovementData("MOVEMENT.DAT") {
            StepDistances = new[] { 100, 200, 400 },     // Small/Medium/Large
            TurnAngles    = new[] { 0x1000, 0x2000, 0x4000 },
            SecondsPerStep = new[] { 60, 120, 240 },
        };

        /// <summary>The dead time in the seconds the driver counts it in: 90 ticks of the engine's
        /// 236.7 Hz timer (int8timerInterrupt; the rate lives in DialogTextSpeed) = 0.380 s.</summary>
        private const float DeadTimeSeconds =
            (float)(0x5a / GameData.Resources.Config.DialogTextSpeed.TicksPerSecond);

        /// <summary>Ticks at 60 fps that land strictly INSIDE the dead time (22).</summary>
        private const int InsideDeadTimeAt60 = (int)(DeadTimeSeconds * 60f);

        private static (ClassicMovementDriver driver, GameSession session, GameObject cam) Build(
            IGameplayInput gameplay, IPointer pointer, System.Func<float> deltaSeconds = null) {
            var session = new GameSession { PositionX = 0, PositionY = 0, Rotation = 0 };
            // *** PIN THE PRESET, DON'T INHERIT IT. *** These assertions are about the driver's
            // axis convention, so the preset has to be stated here or the test silently tracks
            // whatever Preferences' field initializers happen to be. It did: those initializers
            // are the shipped DEFAULT.DAT, byte 0 of that file is 2 = Large, and correcting the
            // long-standing Medium on 2026-09-10 moved this expectation from 200 to 400 for a
            // reason that has nothing to do with what the test checks.
            var prefs = new FakePreferencesService();
            prefs.Current.StepSize = StepSize.Medium;
            prefs.Current.TurnSize = TurnSize.Medium;
            var cam = new GameObject("cam").AddComponent<Camera>();
            var movement = new PartyMovement(session, Table(), prefs, cam, cameraHeightZ: 0, cameraPitch: 0);
            var driver = new ClassicMovementDriver(document: null, movement, gameplay, pointer,
                deltaSeconds ?? (() => 1f / 60f));
            return (driver, session, cam.gameObject);
        }

        [Test]
        public void Tick_MovesForward_WhenGameplayMoveIsUp_AndActive() {
            var gameplay = new FakeGameplayInput { Move = new Vector2(0, 1) };
            var (driver, session, cam) = Build(gameplay, new FakePointer());

            driver.Tick(active: true);

            // Heading 0: StepDelta(0, +200) -> PositionX unchanged, PositionY += 200 (Medium preset).
            Assert.AreEqual(0, session.PositionX);
            Assert.AreEqual(200, session.PositionY);
            Assert.AreEqual(0, session.Rotation);
            Object.DestroyImmediate(cam);
        }

        [Test]
        public void Tick_TurnsLeft_WhenGameplayMoveIsLeft_AndActive() {
            var gameplay = new FakeGameplayInput { Move = new Vector2(-1, 0) };
            var (driver, session, cam) = Build(gameplay, new FakePointer());

            driver.Tick(active: true);

            // TurnLeft -> Turn(angle=0x2000, left:false) rotation delta = +0x2000 (Medium preset).
            Assert.AreEqual((short)0x2000, session.Rotation);
            Assert.AreEqual(0, session.PositionX);
            Assert.AreEqual(0, session.PositionY);
            Object.DestroyImmediate(cam);
        }

        [Test]
        public void Tick_NoOp_WhenInactive_EvenWithMoveHeld() {
            var gameplay = new FakeGameplayInput { Move = new Vector2(0, 1) };
            var (driver, session, cam) = Build(gameplay, new FakePointer());

            driver.Tick(active: false);

            Assert.AreEqual(0, session.PositionX);
            Assert.AreEqual(0, session.PositionY);
            Assert.AreEqual(0, session.Rotation);
            Object.DestroyImmediate(cam);
        }
    
        // --- the hold curve (TASK-431) -----------------------------------------------------------

        [Test]
        public void AHeldDirection_StepsOnce_ThenDoesNothingForTheDeadTime() {
            // world3d_main_loop arms g_nFrameTickCountdown = 0x5a when the arrow is left focused and
            // only tests it against zero afterwards, and int8timerInterrupt decrements it at
            // 236.7 Hz. So: one step, then 0.38 s of nothing.
            var gameplay = new FakeGameplayInput { Move = new Vector2(0, 1) };
            var (driver, session, cam) = Build(gameplay, new FakePointer());

            driver.Tick(active: true);
            Assert.AreEqual(200, session.PositionY, "the first tick steps at once");

            for (var frame = 0; frame < InsideDeadTimeAt60; frame++) {   // 0.367 s, inside it
                driver.Tick(active: true);
            }

            Assert.AreEqual(200, session.PositionY,
                "a held direction must not move again inside the dead time — a 0.14 s throttle "
                + "repeated here, a quarter of a second early");
            Object.DestroyImmediate(cam);
        }

        // The dead time is WALL CLOCK, not frames: the original counts ticks of a 236.7 Hz timer,
        // so it is the same 0.38 s on any machine. Counting 90 Unity frames instead made it 1.5 s
        // at 60 fps and 0.63 s on a 144 Hz monitor.
        [Test]
        public void TheDeadTimeIsSeconds_NotFrames() {
            var gameplay = new FakeGameplayInput { Move = new Vector2(0, 1) };
            var (driver, session, cam) = Build(gameplay, new FakePointer(), () => 1f / 240f);

            driver.Tick(active: true);
            for (var frame = 0; frame < 0x5a; frame++) {   // ninety FRAMES: only 0.375 s at 240 fps
                driver.Tick(active: true);
            }
            Assert.AreEqual(200, session.PositionY, "ninety frames is not the dead time");

            for (var frame = 0; frame < 4; frame++) {      // past 0.380 s
                driver.Tick(active: true);
            }
            Assert.Greater(session.PositionY, 200, "0.38 s of wall clock is");
            Object.DestroyImmediate(cam);
        }

        /// <summary>Steps taken by holding forward for <paramref name="seconds"/> after the dead time.</summary>
        private static int StepsPastTheDeadTime(float fps, float seconds = 1f) {
            var gameplay = new FakeGameplayInput { Move = new Vector2(0, 1) };
            var (driver, session, cam) = Build(gameplay, new FakePointer(), () => 1f / fps);
            driver.Tick(active: true);                                   // the immediate step
            for (var frame = 0; frame < (int)(DeadTimeSeconds * fps) + 2; frame++) {
                driver.Tick(active: true);                               // burn the 0.38 s
            }
            int before = session.PositionY;
            for (var frame = 0; frame < (int)(fps * seconds); frame++) { // the held wall clock
                driver.Tick(active: true);
            }
            int steps = (session.PositionY - before) / 200;
            Object.DestroyImmediate(cam);
            return steps;
        }

        // Past the dead time a held direction repeats at a WALL-CLOCK rate — the owner's decision on
        // TASK-431 (2026-09-14). The original repeats once per uncapped loop iteration, which on the
        // emulator measured 8.84 steps a second; stepping once per rendered frame strode ~57/s.
        [Test]
        public void PastTheDeadTime_ItStepsAtAFixedRate_notEveryFrame() {
            int steps = StepsPastTheDeadTime(60f);

            Assert.That(steps, Is.InRange(8, 9),
                "one second of holding repeats at ~8.84 steps, not sixty");
        }

        // Ten seconds, not one: 8.84 steps a second straddles a whole number, so a one-second window
        // counts 8 or 9 depending on where the first repeat falls against the frames. That is
        // quantisation, not frame-rate dependence — the driver carries the leftover time on every step.
        [Test]
        public void TheRepeatRateIsTheSame_atAnyFrameRate() {
            int at60 = StepsPastTheDeadTime(60f, seconds: 10f);
            int at240 = StepsPastTheDeadTime(240f, seconds: 10f);

            Assert.That(at60, Is.InRange(87, 89), "ten seconds at 60 fps is ~88 steps");
            Assert.That(at240, Is.InRange(87, 89), "and at 240 fps the same");
            Assert.That(System.Math.Abs(at60 - at240), Is.LessThanOrEqualTo(1),
                "a faster monitor must not walk the party faster");
        }

        [Test]
        public void ChangingDirection_StepsAtOnce_AndRearmsTheDeadTime() {
            var gameplay = new FakeGameplayInput { Move = new Vector2(0, 1) };
            var (driver, session, cam) = Build(gameplay, new FakePointer());

            driver.Tick(active: true);
            for (var frame = 0; frame < InsideDeadTimeAt60 + 7; frame++) {
                driver.Tick(active: true);                   // repeating by now
            }

            gameplay.Move = new Vector2(-1, 0);              // a different direction
            driver.Tick(active: true);
            short afterTurn = session.Rotation;
            Assert.AreEqual((short)0x2000, afterTurn, "a new direction acts at once");

            driver.Tick(active: true);
            Assert.AreEqual(afterTurn, session.Rotation,
                "and re-arms the dead time rather than continuing the previous repeat");
            Object.DestroyImmediate(cam);
        }
    }
}

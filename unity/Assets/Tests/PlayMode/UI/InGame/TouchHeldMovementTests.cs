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
    /// A finger held on a touch pad or a compass arrow, reported through UI Toolkit pointer events
    /// (TouchInputState.HeldTouchAction), drives ClassicMovementDriver without the polled pointer —
    /// which never saw a held finger on the owner's phone (2026-09-30).
    /// </summary>
    public class TouchHeldMovementTests {
        private const float DeadTime = (float)(0x5a / DialogTextSpeed.TicksPerSecond);
        private TouchInputState _saved;
        private TouchInputState _state;
        private GameObject _cam;

        [SetUp]
        public void SetUp() {
            _saved = TouchInputState.Instance;
            _state = new TouchInputState();
            TouchInputState.Instance = _state;
        }

        [TearDown]
        public void TearDown() {
            TouchInputState.Instance = _saved;
            if (_cam != null) Object.DestroyImmediate(_cam);
        }

        private (ClassicMovementDriver, GameSession) Build(System.Func<float> dt) {
            var session = new GameSession();
            var prefs = new FakePreferencesService();
            prefs.Current.TurnSize = TurnSize.Medium;
            _cam = new GameObject("cam");
            var movement = new PartyMovement(session, new MovementData("MOVEMENT.DAT") {
                StepDistances = new[] { 100, 200, 400 }, TurnAngles = new[] { 0x1000, 0x2000, 0x4000 },
                SecondsPerStep = new[] { 60, 120, 240 },
            }, prefs, _cam.AddComponent<Camera>(), cameraHeightZ: 0, cameraPitch: 0);
            // A pointer that never reports a press: the phone's polled path.
            var dead = new FakePointer { IsPresent = false, CanPointOverride = true };
            return (new ClassicMovementDriver(null, movement, new FakeGameplayInput(), dead, dt), session);
        }

        [Test]
        public void AHeldPadTurnsAtOnceWithoutThePolledPointer() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: false);
            driver.Tick(active: true);
            Assert.AreEqual((short)-0x2000, session.Rotation);
        }

        [Test]
        public void AHeldReqArrowLeavesTheFirstStepToItsClickThenRepeats() {
            float dt = 1f / 60f;
            var (driver, session) = Build(() => dt);
            _state.PressHold(77, reqArrow: true);
            driver.Tick(active: true);
            Assert.AreEqual(0, session.Rotation, "the arrow's own click (on release) takes the first step");
            dt = DeadTime + 0.01f;
            driver.Tick(active: true);
            Assert.AreEqual((short)-0x2000, session.Rotation, "past the dead time the hold repeats");
            Assert.IsTrue(_state.SwallowNextArrowClick, "and the release click must not add a step");
        }

        [Test]
        public void ReleasingStops() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: false);
            driver.Tick(active: true);
            _state.ReleaseHold(77);
            driver.Tick(active: true);
            Assert.AreEqual((short)-0x2000, session.Rotation, "one step, then nothing");
            Assert.AreEqual(-1, _state.HeldTouchAction);
        }

        /// <summary>
        /// Found on the emulator (2026-10-02): a quick tap on a pad, pressed and released before the
        /// next frame, never turned the party. The driver reads the held pad once per frame, so a
        /// press that ended in between was never seen. A tap is still a step.
        /// </summary>
        [Test]
        public void ATapReleasedBeforeTheNextFrameStillTakesOneStep() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: false);
            _state.ReleaseHold(77);
            driver.Tick(active: true);
            Assert.AreEqual((short)-0x2000, session.Rotation, "the tap's one step");
            driver.Tick(active: true);
            Assert.AreEqual((short)-0x2000, session.Rotation, "and only one");
        }

        [Test]
        public void AHeldPadSeenByTheDriverLeavesNoExtraStepOnRelease() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: false);
            driver.Tick(active: true);
            _state.ReleaseHold(77);
            driver.Tick(active: true);
            driver.Tick(active: true);
            Assert.AreEqual((short)-0x2000, session.Rotation, "the latched tap was the held step");
        }

        [Test]
        public void AQuickTapOnAReqArrowIsLeftToItsClick() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: true);
            _state.ReleaseHold(77);
            driver.Tick(active: true);
            Assert.AreEqual(0, session.Rotation, "the arrow's own click takes a tap's step");
        }

        [Test]
        public void ATapWhileTheWorldIsNotRunningIsNoStepLater() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: false);
            _state.ReleaseHold(77);
            driver.Tick(active: false);
            driver.Tick(active: true);
            Assert.AreEqual(0, session.Rotation);
        }

        [Test]
        public void AFightEdgeDropsALatchedTap() {
            var (driver, session) = Build(() => 1f / 60f);
            _state.PressHold(77, reqArrow: false);
            _state.ReleaseHold(77);
            _state.ForgetCombatPreview();
            driver.Tick(active: true);
            Assert.AreEqual(0, session.Rotation);
        }
    }
}

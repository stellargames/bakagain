namespace BakAgain.Tests.PlayMode.CutScenes {
    using BakAgain.CutScenes;
    using NUnit.Framework;

    /// <summary>
    /// The pacing arithmetic behind every cutscene frame — the tenth slice of the engine.
    /// </summary>
    /// <remarks>
    /// <b>None of this was reachable before.</b> It sat inline in
    /// <c>CutsceneFrameProcessor.ProcessFrameRuntimeAsync</c>, between a real clock and a UniTask
    /// delay, so the only way to exercise it was to play a cutscene and watch. Three rules, each of
    /// which a port gets wrong in a way that looks like "the timing feels a bit off" rather than
    /// like a bug.
    /// </remarks>
    public class CutsceneFramePacingTests {
        [Test]
        public void AZeroDurationFrameSTILLHoldsForOneTick() {
            // *** THE PLUS ONE. *** A frame is a frame, not an instant. Dropping it makes every
            // zero-duration frame flash past AND shortens every other frame by a tick, which reads
            // as the whole cutscene running slightly fast.
            Assert.AreEqual(CutsceneTiming.FrameDurationSeconds,
                CutsceneTiming.FrameHoldSeconds(0), 1e-6f);
            Assert.AreEqual(CutsceneTiming.FrameDurationSeconds * 4,
                CutsceneTiming.FrameHoldSeconds(3), 1e-6f);
        }

        [Test]
        public void TheHoldIsABudget_soProcessingIsSubtracted() {
            // A frame that took three ticks to build waits for the REMAINDER, not its full duration
            // on top. Adding instead of subtracting stretches the scene, and the error accumulates
            // over the whole cutscene rather than staying in the one heavy frame.
            float hold = CutsceneTiming.FrameHoldSeconds(9);          // ten ticks
            float spent = CutsceneTiming.FrameDurationSeconds * 3;

            Assert.AreEqual(CutsceneTiming.FrameDurationSeconds * 7,
                CutsceneTiming.WaitAfterProcessing(hold, spent), 1e-5f);
        }

        [Test]
        public void AnOverrunningFrameYieldsRatherThanWaiting() {
            // *** THIS ONCE ASSERTED THE VALUE WAS NEGATIVE. *** It is now clamped to zero, because
            // the rule moved into GameData.Resources.Animation.FrameBudget, which the original's
            // scheduler shape says never produces a negative wait — it finds the frame due and
            // moves on. The SIGN was never the behaviour: the only consumer is HoldsAtAll, and the
            // value is never used as a delay once it fails that test. So this asserts what the
            // caller actually depends on.
            float hold = CutsceneTiming.FrameHoldSeconds(0);
            float wait = CutsceneTiming.WaitAfterProcessing(hold, hold * 3);

            Assert.IsFalse(CutsceneTiming.HoldsAtAll(wait),
                "the caller must treat this as 'yield once', never as a delay");
            Assert.GreaterOrEqual(wait, 0f, "and never hands UniTask.Delay a negative span");
            Assert.IsFalse(CutsceneTiming.HoldsAtAll(0f), "exactly spent is also not a hold");
        }

        [Test]
        public void ThePaletteCycleDeadlineACCUMULATES_itDoesNotRestartFromNow() {
            // *** THE DRIFT BUG THIS PREVENTS. *** `next = elapsed + step` pushes the deadline out
            // by however late the yield woke up, every step — so the shimmer runs slow by the frame
            // loop's jitter and drifts further the longer the hold. Adding to the DEADLINE keeps a
            // fixed cadence and lets a late wake-up catch up.
            double first = CutsceneTiming.PaletteCycleStepSeconds;
            double second = CutsceneTiming.NextCycleStep(first);
            double third = CutsceneTiming.NextCycleStep(second);

            Assert.AreEqual(CutsceneTiming.PaletteCycleStepSeconds * 2, second, 1e-6);
            Assert.AreEqual(CutsceneTiming.PaletteCycleStepSeconds * 3, third, 1e-6);
        }

        [Test]
        public void ThePaletteCycleRunsOnTheSAMEClockAsTheFrames() {
            // It once ran on 1/18.2s while frames were held at 1/60s. Two incommensurable rates
            // meant a shimmer that should occupy a whole number of frames drifted against the frame
            // it belonged to, by more the longer the hold.
            Assert.AreEqual(CutsceneTiming.FrameDurationSeconds * CutsceneTiming.PaletteCycleStepTicks,
                CutsceneTiming.PaletteCycleStepSeconds, 1e-6f);
        }
    }
}

namespace BakAgain.CutScenes {
    /// <summary>
    /// Shared cutscene timing constants. Single source of truth for the original
    /// engine's frame cadence, previously duplicated across CutsceneFrameProcessor
    /// and the Fade in/out extensions.
    /// </summary>
    public static class CutsceneTiming {
        /// <summary>
        /// Duration of one original-game logical frame, in seconds — one tick of the engine's
        /// single animation clock.
        /// </summary>
        /// <remarks>
        /// <b>Was a hardcoded 60Hz; the real rate is about 59.17Hz and is recovered rather than
        /// assumed</b> — see <see cref="GameData.Resources.Animation.GameTick"/> for the derivation
        /// from the timer the boot path installs. Close enough that the guess survived a long time,
        /// and wrong enough that anything counting ticks against a deadline drifted.
        ///
        /// <para>Everything timed in ticks must come off THIS value, so the durations stay
        /// commensurable — the original has exactly one counter and everything animated polls it.
        /// </para>
        /// </remarks>
        public static readonly float FrameDurationSeconds = (float)GameData.Resources.Animation.GameTick.SecondsPerTick;

        /// <summary>
        /// How long a palette fade of <paramref name="paletteWrites"/> writes should take.
        /// </summary>
        /// <remarks>
        /// <b>This factor is a CALIBRATION, not a recovered fact.</b> The original's fade loop has no
        /// wait in it — it writes the DAC as fast as the machine allows and never presents a frame
        /// per step — so the data says how many writes a fade is made of
        /// (<see cref="GameData.Resources.Animation.FadeRamp.PaletteWrites"/>) and nothing at all
        /// about how long that took. Mapping writes onto our fixed 60Hz clock is therefore a
        /// judgement about how the fade should FEEL.
        ///
        /// <para>The half-rate here is what the inlined tables in FadeIn/FadeOut already implied
        /// (they listed 4/16/32/64/160/320 against write counts of 8/32/64/128/320/640), so this
        /// preserves the current look exactly rather than changing it while removing the
        /// duplication. If fades are ever judged too quick or too slow, this one number is the knob —
        /// and it belongs with the other tuned constants (TASK-30), not with the extracted data.</para>
        /// </remarks>
        public static float FadeDurationSeconds(int paletteWrites) =>
            paletteWrites * FadeWritesToFrames * FrameDurationSeconds;

        /// <summary>Frames per palette write. Tuned, not derived — see <see cref="FadeDurationSeconds"/>.</summary>
        public const float FadeWritesToFrames = 0.5f;

        // ---------------------------------------------------------------- frame pacing

        /// <summary>
        /// How long a frame occupies the screen, from its script duration.
        /// </summary>
        /// <remarks>
        /// <b>THE PLUS ONE IS LOAD-BEARING.</b> A frame whose script duration is 0 still holds for
        /// one tick — it is a frame, not an instant — so the shortest possible hold is one
        /// <see cref="FrameDurationSeconds"/> and not zero. Dropping it makes every
        /// zero-duration frame flash past and shortens every other frame by a tick, which reads as
        /// a cutscene running slightly fast rather than as a bug in one place.
        /// </remarks>
        public static float FrameHoldSeconds(int framesDuration) =>
            (framesDuration + 1) * FrameDurationSeconds;

        /// <summary>
        /// What is left of a frame's hold once the work of drawing it is paid for.
        /// </summary>
        /// <returns>
        /// Seconds still to wait; <b>zero when the frame overran</b>, which the caller must treat
        /// as "yield once and move on" rather than as a delay of no time.
        /// </returns>
        /// <remarks>
        /// <b>The processing time is SUBTRACTED, not added to.</b> The hold is the frame's total
        /// budget, so a frame that took three ticks to build waits for the remainder rather than
        /// waiting its full duration on top — otherwise a heavy frame stretches the scene and the
        /// error accumulates over the whole cutscene rather than being absorbed.
        ///
        /// <para><b>The rule itself lives in <see cref="GameData.Resources.Animation.FrameBudget"/>
        /// now.</b> Both sides had it — that one in ticks, this one in seconds — and a rule this
        /// easy to get backwards should not be written twice. This is the seconds-facing wrapper.
        /// </para>
        ///
        /// <para>Overrun used to come back NEGATIVE and now comes back zero, which changes nothing:
        /// the only consumer is <see cref="HoldsAtAll"/>, and the value is never used as a delay
        /// once it fails that test — <c>CutsceneFrameProcessor</c> yields once instead.</para>
        /// </remarks>
        public static float WaitAfterProcessing(float holdSeconds, double processingSeconds) =>
            (float)GameData.Resources.Animation.FrameBudget.RemainingWait(
                holdSeconds, processingSeconds);

        /// <summary>Whether there is any of the hold left to wait out.</summary>
        /// <remarks>
        /// The complement of <see cref="GameData.Resources.Animation.FrameBudget.Overran"/>, kept
        /// as a positive test because that is how the frame loop reads.
        /// </remarks>
        public static bool HoldsAtAll(float waitSeconds) => waitSeconds > 0;

        /// <summary>
        /// Ticks between palette-cycle advances during a hold.
        /// </summary>
        /// <remarks>
        /// <b>One entry per timer tick is sourced; this count is ours.</b> The script stores an
        /// <c>abs(Step)</c> and nothing ever reads it back, so the interval is not in the data. It
        /// is the one remaining knob and belongs with the other tuned constants (TASK-30).
        ///
        /// <para>It must come off <see cref="FrameDurationSeconds"/> like everything else. This
        /// once ran on a second, different clock — 1/18.2s against frames held at 1/60s — and two
        /// incommensurable rates meant a shimmer that should occupy a whole number of frames
        /// drifted against the frame it belonged to, by more the longer the hold.</para>
        /// </remarks>
        public const int PaletteCycleStepTicks = 1;

        /// <inheritdoc cref="PaletteCycleStepTicks"/>
        public static readonly float PaletteCycleStepSeconds =
            PaletteCycleStepTicks * FrameDurationSeconds;

        /// <summary>
        /// When the next palette-cycle advance is due, given the one just serviced.
        /// </summary>
        /// <remarks>
        /// <b>IT ACCUMULATES; IT DOES NOT RESTART FROM NOW.</b> Writing
        /// <c>next = elapsed + step</c> instead would push the deadline out by however late the
        /// yield woke up, every single step — so the shimmer would run slow by the frame loop's
        /// jitter and drift further the longer the hold. Adding the step to the DEADLINE keeps the
        /// cadence fixed and lets a late wake-up catch up.
        /// </remarks>
        public static double NextCycleStep(double currentDeadline) =>
            currentDeadline + PaletteCycleStepSeconds;
    }
}

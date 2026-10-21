namespace BetrayalAtKrondor.Tests.Data;

using GameData.Resources.Config;
using Xunit;

/// <summary>
/// How long a dialog holds itself up, from the text-speed preference
/// (<c>dialog_input_wait_timed</c>, DIALOG.C:224-240).
/// </summary>
public class DialogTextSpeedTests {
    [Fact]
    public void SlowMeansWaitForThePlayer_NotAVeryLongTimeout() {
        // The engine sets an infinite deadline at speed 0 and only leaves on input.
        Assert.Null(DialogTextSpeed.AutoDismissSeconds(500, TextSpeed.Slow));
    }

    [Fact]
    public void FasterSettingsHoldTheTextForLess() {
        double? medium = DialogTextSpeed.AutoDismissSeconds(100, TextSpeed.Medium);
        double? fast = DialogTextSpeed.AutoDismissSeconds(100, TextSpeed.Fast);

        Assert.NotNull(medium);
        Assert.NotNull(fast);
        Assert.True(fast < medium);
    }

    [Fact]
    public void MoreTextHoldsForLonger() {
        double? shortLine = DialogTextSpeed.AutoDismissSeconds(20, TextSpeed.Medium);
        double? paragraph = DialogTextSpeed.AutoDismissSeconds(400, TextSpeed.Medium);

        Assert.True(paragraph > shortLine);
    }

    [Theory]
    [InlineData(0, TextSpeed.Medium, 112)]   // (0*12 + 150) * 75 / 100
    [InlineData(100, TextSpeed.Medium, 1012)] // (100*12 + 150) * 75 / 100
    [InlineData(100, TextSpeed.Fast, 675)]    // (100*12 + 150) * 50 / 100
    [InlineData(100, TextSpeed.Slow, 1350)]   // scale 100 — the raw reading time
    public void TheTickFormulaIsTheEnginesOwn(int characters, TextSpeed speed, long expected) {
        Assert.Equal(expected, DialogTextSpeed.AutoDismissTicks(characters, speed));
    }

    [Fact]
    public void TheDeadlineCountsTheSixtyHertzTimer_NotTheInterruptRate() {
        // The deadline is `+ g_timer_ticks` (DIALOG.C:218, :236), and g_timer_ticks is bumped by
        // syslowio_timer_tick_isr, registered with interval 4 (BOOT.C:212) — every fourth IRQ0, so
        // 59.17 Hz (GameTick), not 236.7. Measured 2026-10-21 in the original (TASK-912): Timer2
        // advanced 900 while the BIOS tick advanced 277, i.e. 13/4 per BIOS tick.
        double medium = DialogTextSpeed.AutoDismissSeconds(100, TextSpeed.Medium)!.Value;

        Assert.Equal(1012 / GameData.Resources.Animation.GameTick.TicksPerSecond, medium, 6);
    }

    [Fact]
    public void EvenAnEmptyLineGetsAMoment() {
        // The +150 floor: a two-word answer must not vanish the instant it appears.
        Assert.True(DialogTextSpeed.AutoDismissSeconds(0, TextSpeed.Fast) > 0.2);
    }

    [Fact]
    public void NegativeLengthsAreTreatedAsEmpty() {
        Assert.Equal(
            DialogTextSpeed.AutoDismissTicks(0, TextSpeed.Fast),
            DialogTextSpeed.AutoDismissTicks(-5, TextSpeed.Fast));
    }

    [Fact]
    public void ARecordCarrying0x20NeverTimesOut() {
        // `while (deadline > g_timer_ticks || (flags & 0x20))` (DIALOG.C:238): with 0x20 the
        // deadline never ends the wait, whatever the text speed.
        Assert.Null(DialogTextSpeed.AutoDismissSeconds(100, TextSpeed.Fast,
            GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard));
        Assert.NotNull(DialogTextSpeed.AutoDismissSeconds(100, TextSpeed.Fast, 0));
    }

    [Fact]
    public void AnAutoAdvanceRecordClosesItselfEvenAtSlow() {
        // DIALOG.C:211-220: under flags & 0x40 the deadline is set before the text-speed test, so
        // it runs at Slow too, divided by chapter / 5 + 3. Measured in the original (TASK-912):
        // dial_z27:1203 drew 213 characters in chapter 1 and closed with no input after 902 ticks.
        double? seconds = DialogTextSpeed.AutoDismissSeconds(213, TextSpeed.Slow,
            GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer, chapter: 1);

        Assert.NotNull(seconds);
        Assert.Equal(902 / GameData.Resources.Animation.GameTick.TicksPerSecond, seconds!.Value, 6);
    }

    [Fact]
    public void AnAutoAdvanceRecordGetsBrieferAsTheChaptersGoOn() {
        // `/ (nChapter / 5 + 3)`: chapters 5-9 divide by 4.
        double early = DialogTextSpeed.AutoDismissSeconds(213, TextSpeed.Slow,
            GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer, chapter: 4)!.Value;
        double late = DialogTextSpeed.AutoDismissSeconds(213, TextSpeed.Slow,
            GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer, chapter: 5)!.Value;

        Assert.Equal(902 / GameData.Resources.Animation.GameTick.TicksPerSecond, early, 6);
        Assert.Equal(676 / GameData.Resources.Animation.GameTick.TicksPerSecond, late, 6);
    }

    [Fact]
    public void AutoAdvanceIsDecidedBefore0x20() {
        // The 0x40 branch returns before the `while (... || (flags & 0x20))` loop is reached.
        Assert.NotNull(DialogTextSpeed.AutoDismissSeconds(100, TextSpeed.Slow,
            GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer
            | GameData.Resources.Dialog.DialogEntryFlags.KeepAcceptingKeyboard, chapter: 1));
    }

    [Fact]
    public void OnlyTheDrawnCharactersCount() {
        // g_wTextWrapXAccum sums `lines[fl].end - lines[fl].start` (TEXTWRAP.C:145): tabs, line
        // breaks and markup are not drawn. Read live in the original after dial_z27:1203: 213,
        // where the extracted text is 220 long (two tabs, one line break, `<i/>`).
        const string text = "\tThe trip was <i/>dull.\n\tWith only the occasional cold draft to chill their bones as they moved through the pass, it was a relief to finally feel the more temperate winds typical to the southern reaches of the Kingdom...";

        Assert.Equal(220, text.Length);
        Assert.Equal(213, DialogTextSpeed.DrawnCharacters(text));
    }
}

namespace GameData.Resources.Config;

/// <summary>
/// How long a dialog panel stays up before dismissing itself, from the player's
/// <see cref="TextSpeed"/> preference — <c>dialog_input_wait_timed</c> (DIALOG.C:224-240).
/// </summary>
public static class DialogTextSpeed {
    /// <summary>
    /// <c>g_abTextSpeedScale</c>: the per-setting percentage applied to the base reading time.
    /// </summary>
    private static readonly int[] ScalePercent = { 100, 75, 50 };

    /// <summary>
    /// The engine's INTERRUPT rate, <b>~236.7 Hz</b>: <c>InitializeGameHardware</c> calls
    /// <c>InitializeTimers(13)</c> (KRONDOR.EXE 0x41a20), which programs the PIT with a count of
    /// <c>0xFFFF / 13</c> = 5041, i.e. 1193182 / 5041. Countdowns decremented by the ISR itself
    /// (<c>g_nFrameTickCountdown</c>: the arrow repeat, the music fade, the picklock pause) are in
    /// these units. <b>A dialog's deadline is NOT</b> — see <see cref="AutoDismissSeconds"/>.
    /// </summary>
    public const double TicksPerSecond = 1193182.0 / (0xFFFF / 13);

    /// <summary>
    /// Reading time for <paramref name="characters"/> of text, in engine ticks:
    /// <c>(chars * 12 + 150) * scale / 100</c>. The 150 is a floor so a two-word line still
    /// registers.
    /// </summary>
    public static long AutoDismissTicks(int characters, TextSpeed speed) {
        if (characters < 0) {
            characters = 0;
        }
        int scale = ScalePercent[(int)speed >= 0 && (int)speed < ScalePercent.Length ? (int)speed : 0];
        return ((long)characters * 12 + 150) * scale / 100;
    }

    /// <summary>
    /// How long the panel should wait before dismissing itself, or <c>null</c> when it waits for
    /// the player.
    /// </summary>
    /// <remarks>
    /// <para><b>The deadline counts <c>g_timer_ticks</c>, the 59.17 Hz clock</b> (<see cref="Animation.GameTick"/>):
    /// <c>deadline += g_timer_ticks</c> (DIALOG.C:218, :236), and that counter is bumped by
    /// <c>syslowio_timer_tick_isr</c>, registered with interval 4 (BOOT.C:212) — every fourth
    /// interrupt, not every one. Measured in the original (TASK-912): Timer2 @0x3d8d4 advanced 900
    /// while the BIOS tick advanced 277, i.e. 13/4 per BIOS tick. Read at the interrupt rate, every
    /// timed panel closed four times too soon.</para>
    ///
    /// <para><b><c>AutoAdvanceTimer</c> (0x40) closes itself at every text speed.</b> DIALOG.C:211-220
    /// sets its deadline before the text-speed test, divided by <c>chapter / 5 + 3</c>, and returns at
    /// once; the caller waits out the rest (<c>dialog_input_wait_with_cooldown</c>, e.g. HOTSPOT.C:886
    /// after a zone crossing). It is decided before the 0x20 loop, so 0x20 does not hold it. Measured:
    /// dial_z27:1203 ("The trip was dull...", 213 characters drawn, chapter 1) closed with no input
    /// after 902 ticks at text speed 0.</para>
    ///
    /// <para>Otherwise: <c>null</c> at <see cref="TextSpeed.Slow"/>, which is "wait for the player" —
    /// the engine sets an infinite deadline there (DIALOG.C:227-231) — and for a record carrying 0x20
    /// (<c>while (deadline &gt; g_timer_ticks || (flags &amp; 0x20))</c>, DIALOG.C:238).</para>
    ///
    /// <para>Not modelled: the engine's flag-8 dialogs, which use the timeout even at Slow and
    /// divide it by <c>chapter / 5 + 2</c>.</para>
    /// </remarks>
    public static double? AutoDismissSeconds(int characters, TextSpeed speed,
        Dialog.DialogEntryFlags recordFlags = 0, int chapter = 0) {
        if ((recordFlags & Dialog.DialogEntryFlags.AutoAdvanceTimer) != 0) {
            long ticks = AutoDismissTicks(characters, speed) / ((Math.Max(chapter, 0) / 5) + 3);
            return ticks / Animation.GameTick.TicksPerSecond;
        }
        if ((recordFlags & Dialog.DialogEntryFlags.KeepAcceptingKeyboard) != 0) {
            return null;
        }
        if (speed == TextSpeed.Slow) {
            return null;
        }
        return AutoDismissTicks(characters, speed) / Animation.GameTick.TicksPerSecond;
    }

    /// <summary>
    /// The characters the engine counts as read: <c>g_wTextWrapXAccum</c> sums
    /// <c>lines[fl].end - lines[fl].start</c> (TEXTWRAP.C:145), so tabs, line breaks and markup tags
    /// are not counted. Read live in the original after dial_z27:1203: 213, for an extracted text of
    /// 220 (two tabs, one line break, <c>&lt;i/&gt;</c>).
    /// </summary>
    /// <remarks>Speaker tokens (<c>@5</c>) are counted as written, not as the name they become.</remarks>
    public static int DrawnCharacters(string text) {
        if (text == null) {
            return 0;
        }
        int count = 0;
        bool inTag = false;
        foreach (char c in text) {
            if (c == '<') {
                inTag = true;
            } else if (c == '>' && inTag) {
                inTag = false;
            } else if (!inTag && c != '\t' && c != '\n' && c != '\r') {
                count++;
            }
        }
        return count;
    }
}

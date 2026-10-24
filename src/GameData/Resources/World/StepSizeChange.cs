namespace GameData.Resources.World;

/// <summary>
/// Whether a change in the player's step-size setting triggers the roaming-encounter reset —
/// <c>worldmove_rgn_chap_trans_apply</c> (WORLDMOV.C:65).
/// </summary>
/// <remarks>
/// <b>The function's name says chapter transition and it has nothing to do with chapters.</b> The two
/// values it compares are resolved step distances, read from <c>movement.dat</c> by
/// <c>engine_prefs-&gt;step_speed</c> (WORLDMOV.C:30) — the player's Preferences step-size. The name
/// fits nothing in the body and is treated as unreliable, like other canassa names here.
///
/// <para><b>The asymmetry is the whole rule and it is easy to miss:</b> the stored baseline is
/// updated on <i>any</i> change, but the sweep runs only on an <i>increase</i>. So lowering the
/// setting silently re-baselines, and raising it back to a value already seen <b>does</b> fire again.
/// A port that only stored the baseline when it acted would suppress that second reset.</para>
///
/// <para>The grid-stride half of the same routine follows the identical shape, calling
/// <c>worldmove_plr_hdg_align_grid()</c> instead — see <see cref="AlignsHeading"/>.</para>
/// </remarks>
public static class StepSizeChange {
    /// <summary>Whether the roaming-encounter reset runs for this change.</summary>
    /// <param name="lastSeen">The stored value — <c>nLastSeenStepSpeed</c>.</param>
    /// <param name="current">The distance the current preference resolves to.</param>
    public static bool ResetsRoamers(int lastSeen, int current) => current > lastSeen;

    /// <summary>Whether the party's heading is re-aligned for this grid-stride change.</summary>
    /// <remarks>Same rule, different action — kept named rather than folded into one predicate so a
    /// reader is not left deducing that the two arms are identical.</remarks>
    public static bool AlignsHeading(int lastSeen, int current) => current > lastSeen;

    /// <summary>
    /// The baseline to store after handling a change.
    /// </summary>
    /// <remarks>
    /// <b>Always the current value, whether or not anything fired.</b> This exists as a named
    /// function purely so the asymmetry cannot be lost in an <c>if</c> — the original assigns it
    /// outside the increase check, and reproducing that by accident is unlikely.
    /// </remarks>
    public static int NewBaseline(int lastSeen, int current) => current;

    /// <summary>
    /// The heading snapped to the nearest multiple of the turn angle, a half rounding up —
    /// <c>worldmove_align_to_grid_stride</c> (WORLDMOV.C:438-460), which
    /// <c>worldmove_plr_hdg_align_grid</c> runs on the party's yaw when the turn angle grows.
    /// </summary>
    /// <remarks>A heading of 0xF800 at stride 0x1000 rounds up to 0x10000, which wraps to north.</remarks>
    public static ushort AlignedHeading(ushort yaw, int stride) {
        if (stride <= 0) {
            return yaw;
        }
        long candidate = 0;
        int steps = (int)(0x10000L / stride) + 1;
        for (var i = 0; i < steps; i++) {
            if (candidate == yaw) {
                return yaw;
            }
            if (yaw < candidate) {
                long snapped = yaw < candidate - (stride >> 1) ? candidate - stride : candidate;
                return (ushort)snapped;
            }
            candidate += stride;
        }
        return yaw;
    }
}

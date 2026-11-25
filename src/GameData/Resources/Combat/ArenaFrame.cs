namespace GameData.Resources.Combat;

/// <summary>
/// The clock combat creature animation runs on: the original's ARENA FRAME, not its timer tick.
/// </summary>
/// <remarks>
/// <b>One animation step per drawn arena frame.</b> <c>combat_actor_anim_step</c> (CACTOR.C:835)
/// increments each actor's tick counter and advances on <c>tick_counter % loop_period</c>, and it is
/// called from <c>world_render_actor_at_tile</c> (WORLDHIT.C:450) — once per actor per render of the
/// arena. The attack and death runs wait in <c>combat_actor_anim_wait_rndr_loop</c> (CACTOR.C:1821),
/// which renders the arena until the run is done, so they step on the same clock. The loop has no
/// timer wait: it is as fast as the machine draws.
///
/// <para><b>Measured, 2026-10-04 (TASK-768):</b> the t739 ambush in Spice86, idle turn, 60 s of
/// screenshots: Owyn's and the left bandit's idle advanced 1.34 and 1.39 times per second; with the
/// idle's mean delay of 11.5 steps (<c>RND2(8) + 8</c>) that is ~15.7 arena frames per second. The
/// port had stepped on the 59.17 Hz timer and ran the idle ~4x fast.</para>
///
/// <para><b>Re-measured, 2026-10-05 (TASK-770), in EMULATED time:</b> the 2026-10-04 figure timed
/// screenshots against the wall clock, and every MCP screenshot pauses the emulator, so it ran
/// slow. Stamping each frame with the BIOS tick (0040:006C) instead, the same ambush gave 1.56-1.71
/// idle changes per second for the three actors whose boxes overlap nobody — 17.9-19.7 arena
/// frames per second. An enemy thrust in that fight changed picture every 3 ticks or less (4 frames
/// at 18.5/s is 3.9 ticks). The rate is the emulated machine's drawing speed (the loop has no timer
/// wait), so it is a property of Spice86's configured CPU as much as of the game.</para>
///
/// <para><b>Set to 50/3 (owner, 2026-11-25, TASK-1136).</b> A player's DOSBox clip at 15,000 cycles
/// (forum p88) took 0.71 s from a moredhel's first fall picture to lying; 18.5 gave 0.65 s, and the
/// owner found combat "a bit nervous". At 50/3 the same run takes 0.72 s (3 x 4 frames). A pace
/// choice within the range real machines draw at, not a measurement.</para>
/// </remarks>
public static class ArenaFrame {
    /// <summary>Arena frames per second (see the remarks: chosen near the original's measured pace).</summary>
    public const double PerSecond = 50.0 / 3;

    /// <summary>The player's combat speed (a port setting): <see cref="PerSecond"/> times 0.75, 1, 1.5 or 2.</summary>
    public static double Rate(CombatSpeed speed) => PerSecond * speed switch {
        CombatSpeed.Slow => 0.75,
        CombatSpeed.Fast => 1.5,
        CombatSpeed.Ultra => 2.0,
        _ => 1.0,
    };
}

/// <summary>How fast combat animates (TASK-1136, owner 2026-11-25). Normal is <see cref="ArenaFrame.PerSecond"/>.</summary>
public enum CombatSpeed { Slow, Normal, Fast, Ultra }

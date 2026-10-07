namespace GameData.Resources.Combat;

/// <summary>
/// One step of a creature's bitmap animation — <c>advanceCreatureAnimationFrame</c> @0x5d37a, the
/// walk cycle and the idle gait.
/// </summary>
/// <remarks>
/// <b>This is the half of the animation that changes WHICH FRAME is shown.</b> The other half —
/// which column, i.e. the facing — is <c>DirectionalSprite</c>'s. They are independent, and the
/// task that owns them has said so since before either was written.
///
/// <para><b>AWAITING ITS FEATURE (TASK-103).</b> Nothing steps a combatant's animation yet.</para>
///
/// <para><b>NOT the same animation as <see cref="World.EncounterActorPose"/>'s gait, and the two
/// must not be folded together.</b> That one is <c>rgnenc_render_object</c> (RGNENC.C) — the
/// ROAMING WORLD actor's billboard, a fixed three-frame cycle that ping-pongs at BOTH ends for ever.
/// This one is the COMBAT creature's <c>creatueBitmapAnim</c>, whose range is authored per
/// animation and which bounces at the top and then RESTARTS rather than bouncing at the bottom.
/// Same idea, different subsystems, different rules.</para>
///
/// <para><b>They do agree on one thing, and the agreement is worth something:</b> this function
/// mirrors above facing 4, and <c>EncounterActorPose.SpriteColumn</c> mirrors at
/// <c>octant &gt;= 5</c>. Two independent RE passes over two different renderers landing on the same
/// split is real corroboration that the sheets hold five columns.</para>
///
/// <para>Traced whole on 2026-09-01. The function's own IDA comment had said the reverse path and
/// the exact <c>isComplete</c> condition were never read (only the first ~75 of 161 instructions),
/// and both turned out to carry rules a port would not have guessed.</para>
/// </remarks>
public static class CreatureAnimationStep {
    /// <summary>
    /// <b>The gait rate is RE-ROLLED AT EVERY FRAME, and only for slot 0.</b>
    /// </summary>
    /// <remarks>
    /// After each advance, slot 0 sets <c>frameDelay = 8 + random(0..7)</c> — a fresh value every
    /// frame, not once per animation. So the idle/walk gait is deliberately irregular, and a port
    /// with a fixed delay produces a metronome the original never has.
    ///
    /// <para>Non-zero slots keep whatever delay they were given, which is what makes an attack or a
    /// death animation play at its authored speed while the creature's own idle breathes.</para>
    /// </remarks>
    public const int GaitDelayMinimum = 8;

    /// <summary>The delay slot 0 takes for its next frame.</summary>
    public static int NextGaitDelay(int roll) => (roll & 7) + GaitDelayMinimum;

    /// <summary>
    /// Whether this step advances a frame at all — <c>tickCounter % frameDelay == 0</c>.
    /// </summary>
    /// <remarks>
    /// <b>A modulo, not a countdown</b>, and the counter resets to <b>1</b> rather than 0 on an
    /// advance. Every other tick just increments it. Reproduced exactly because the two differ on
    /// the first tick after an advance, which is where a gait visibly stutters.
    /// </remarks>
    public static bool Advances(int tickCounter, int frameDelay) =>
        frameDelay != 0 && tickCounter % frameDelay == 0;

    /// <summary>The counter value after a step that advanced.</summary>
    public const int TickCounterAfterAdvance = 1;
}

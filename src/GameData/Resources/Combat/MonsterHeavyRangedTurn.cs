namespace GameData.Resources.Combat;

/// <summary>
/// <c>combataiact_ranged_attack</c> (canassa CBTAIACT.C:235) — seventh of the nine AI action
/// routines, and the hardest-hitting one in the file.
///
/// <para>Distinct from <see cref="MonsterTurnRoutines.ChooseRangedTurn"/>, which is
/// <c>combataiact_ranged_attack_TURN</c>: canassa gives two routines near-identical names for very
/// different attacks.</para>
/// </summary>
public static class MonsterHeavyRangedTurn {
    /// <summary>
    /// <b>The creature restores its Strength to full at the START OF EVERY TURN.</b>
    /// </summary>
    /// <remarks>
    /// The routine opens with <c>actor->stats[3].base = actor->stats[3].max</c> — stat 3 is Strength
    /// — before it even looks for a target. So draining this creature's Strength is pointless: it
    /// undoes the damage itself each turn, unconditionally, whether or not it goes on to attack.
    ///
    /// <para>It is the first statement of the routine, easy to skim past as bookkeeping, and a port
    /// that drops it makes the creature meaningfully weaker over a long fight.</para>
    ///
    /// <para><b>Deliberately callerless.</b> CombatRuntime restores the strength at the head of the 0x36 turn; this pins the rule.</para>
    /// </remarks>
    public static bool RestoresStrengthEachTurn => true;

    /// <summary>The stat restored — Strength.</summary>
    public const ActorAttribute RestoredAttribute = ActorAttribute.Strength;

    /// <summary>Damage range — <c>RNDR(0x2d, 0x4a)</c>.</summary>
    /// <remarks>
    /// <b>45-73, the largest in the file</b> — roughly triple the 15-33 of the melee routine and an
    /// order above the 4-7 weak tier elsewhere.
    /// </remarks>
    public static readonly (int Min, int Max) Damage = (0x2d, 0x4a);

    /// <summary>The hit-tint ramp — the damage call's fourth argument, <c>markActorHit</c>'s remap
    /// (a plain blow passes 1; see HitReaction). Named "knockback" by canassa; it moves nobody.</summary>
    public const int Knockback = 4;

    /// <summary>What flies — <c>world_rndr_ranged_attack_anim(.., 4, 100, -1)</c>, COMBAT.TBL <c>spell3</c>
    /// (CBTAIACT.C:246).</summary>
    public const int ProjectileShape = 4;

    /// <summary>The spit's spark burst — <c>g_nVfxParticleColor = 0xe8</c>,
    /// <c>worldfx_combat_damage_ptcl_burst(target, 0x23)</c> (CBTAIACT.C:249-251), no tint meanwhile.</summary>
    public const int BurstColour = 0xe8;

    /// <summary>See <see cref="BurstColour"/>.</summary>
    public const int BurstSpread = 0x23;

    /// <summary>Damage flags.</summary>
    public const int DamageFlags = 0x200;

    /// <summary>The impact cue — <c>audio_play(0x15)</c>. The routine has no launch cue.</summary>
    public const int ImpactCue = 0x15;

    /// <summary>Whether the routine attacks, rather than deferring to pathing.</summary>
    public static bool Attacks(bool hasLineOfSight, int distance) => hasLineOfSight && distance > 1;
}

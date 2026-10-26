namespace GameData.Resources.Combat;

/// <summary>
/// Which COMBAT.TBL entry is drawn flying from an attacker to its target.
/// </summary>
/// <remarks>
/// The original passes an effect-sprite id to the flight routine at four call sites, and every one
/// of the four ids lands on an aptly named COMBAT.TBL entry — checked against the shipped table,
/// which is what turned these from "an integer in an instruction encoding" into a mapping worth
/// keeping: 1 = <c>arrow</c>, 2 = <c>spell</c>, 3 = <c>jack</c>, 20 = <c>rock</c>, 50 = <c>spell5</c>, 60 = <c>sling</c>.
/// <para>The two spells that get their own sprite are picked by id, not by any property of the
/// spell record — the original branches on the spell number.</para>
/// </remarks>
public static class CombatEffectSprite {
    /// <summary>A thrown rock — COMBAT.TBL <c>rock</c>, quarrel kind 8; the one shape that arcs.</summary>
    public const int Rock = 20;

    /// <summary>A crossbow quarrel — COMBAT.TBL <c>arrow</c>.</summary>
    public const int Arrow = 1;

    /// <summary>Creature 0x1a's shot — COMBAT.TBL <c>sling</c>.</summary>
    public const int Sling = 0x3c;

    /// <summary>The ground shadow under the arrow — COMBAT.TBL <c>ashadow</c>.</summary>
    public const int ArrowShadow = 0xe;

    /// <summary>The ground shadow under any other flying shape — COMBAT.TBL <c>flat</c>.</summary>
    public const int FlatShadow = 6;

    /// <summary>No shadow.</summary>
    public const int NoShadow = -1;

    /// <summary>What a flying shape casts on the ground — <c>world_render_combat_grid_sprite</c>
    /// (WORLDHIT.C:743-760): <c>ashadow</c> under the arrow, <c>flat</c> under the rest, nothing under
    /// shape 4, whose growing whirl already sits on the ground.</summary>
    public static int ShadowFor(int shape) => shape == 4 ? NoShadow : shape == Arrow ? ArrowShadow : FlatShadow;

    /// <summary>What a shot flies — <c>combataiturn_ranged_attack</c> (CBTAITRN.C:121-139), the party's
    /// Shoot (COMBAT.C:1493) and every monster shooter alike: the rock for quarrel kind 8, else creature
    /// 0x1a's sling, else the arrow.</summary>
    public static int ForShot(int quarrelKind, int creatureType) =>
        quarrelKind == RangedShotSound.ThrownRockKind ? Rock
        : creatureType == 0x1a ? Sling
        : Arrow;

    /// <summary>Flamecast's own sprite — COMBAT.TBL <c>spell</c>.</summary>
    public const int Flamecast = 2;

    /// <summary>Bane of Black Slayers' own sprite — COMBAT.TBL <c>jack</c>.</summary>
    public const int BaneOfBlackSlayers = 3;

    /// <summary>The remaining projectile spell — COMBAT.TBL <c>spell5</c>.</summary>
    /// <remarks>
    /// <b>"Every other spell" was wrong, and the number of users is ONE.</b> The projectile routine
    /// is only reached for spells whose <c>AnimationEffectType</c> is
    /// <see cref="ProjectileAnimationType"/>, and the shipped table has exactly three of those —
    /// Flamecast, Bane of Black Slayers and <b>The Fetters of Rime</b>. The first two have bespoke
    /// sprites, so this default belongs to Fetters of Rime alone.
    /// </remarks>
    public const int GenericSpell = 50;

    /// <summary>The <c>AnimationEffectType</c> whose arm flies a projectile at all.</summary>
    /// <remarks>
    /// <b>Most spells fly nothing.</b> <c>Spell_RunAnimationEffect</c> @0x6782f switches on
    /// <c>spellData.animationEffectType</c> across 20 cases and only case 3 calls
    /// <c>Spell_ApplyHitWithProjectile</c> — case 0 fades the palette, case 2 flashes it on the
    /// target, and so on. Arming a projectile for every targeted cast gives a whistling missile to
    /// Candle Glow.
    /// </remarks>
    public const int ProjectileAnimationType = 3;

    /// <summary>Whether a spell flies a projectile.</summary>
    public static bool FliesProjectile(int animationEffectType) =>
        animationEffectType == ProjectileAnimationType;

    private const int FlamecastSpellId = 4;
    private const int BaneOfBlackSlayersSpellId = 9;

    /// <summary>The effect sprite a cast of <paramref name="spellId"/> flies.</summary>
    public static int ForSpell(int spellId) => spellId switch {
        FlamecastSpellId => Flamecast,
        BaneOfBlackSlayersSpellId => BaneOfBlackSlayers,
        _ => GenericSpell,
    };
}

namespace GameData.Resources.Combat;

/// <summary>
/// The arena's animation slots a run is played in (<c>combat_actor_anim_play</c>'s sprite id). Each
/// creature's COMBAT.TBL entry carries one mesh per slot (its RuntimeFlagsIndex), whose faces are the
/// slot's pictures.
/// </summary>
public static class CombatAnimSlot {
    /// <summary>The melee swing — combat_actor_play_anim_sprite3, CACTOR.C:1952-1955.</summary>
    public const int Swing = 3;

    /// <summary>The melee thrust — combat_actor_play_anim_sprite4, CACTOR.C:1947-1950.</summary>
    public const int Thrust = 4;

    /// <summary>A ranged kind's wind-up — combat_actor_play_ranged_windup, CACTOR.C:1967-1971.</summary>
    public const int Windup = 8;

    /// <summary>A cast's swing — combat_actor_play_melee_swing, CACTOR.C:1973-1977.</summary>
    public const int CastSwing = 9;
}

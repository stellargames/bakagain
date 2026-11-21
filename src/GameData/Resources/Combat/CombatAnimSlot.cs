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
    /// <summary>The crossbow pose before a shot (combat_actor_anim7_field2_var, CACTOR.C:1903-1910).</summary>
    public const int Crossbow = 7;

    public const int Windup = 8;

    /// <summary>A cast's swing — combat_actor_play_melee_swing, CACTOR.C:1973-1977.</summary>
    public const int CastSwing = 9;

    /// <summary>
    /// Which of the creature's three bitmaps a slot draws from (COMBAT.TBL's SpriteKeys index):
    /// slots 0-1 the first, 3-6 the second, 7-9 the third (combat_actor_rsrc_load_3values,
    /// CACTOR.C:577-596).
    /// </summary>
    public static int BitmapOf(int slot) => slot >= 7 ? 2 : slot >= 3 ? 1 : 0;
}

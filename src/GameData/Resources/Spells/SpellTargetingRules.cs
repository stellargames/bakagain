namespace GameData.Resources.Spells;

/// <summary>
/// What the player is allowed to aim a spell at — the targeting switch inside
/// <c>combat_arena_disp_spell_action</c> (ovr168 @0x62360), which decides whether the cell under the
/// cursor is a legal target for the selected spell.
///
/// <para><b>The targeting type does two jobs, and they agree.</b> <see cref="SpellCastTail"/> found
/// it selecting the delivery at the end of a cast; here it is the cursor's aiming rule at the start
/// of one. Every group lines up: the types that deliver no damage are the types that aim at ground
/// rather than at anybody, and the type that routes to the heal is the type that demands a named
/// character.</para>
/// </summary>
public static class SpellTargetingRules {
    /// <summary>What a targeting type requires under the cursor.</summary>
    public enum Aim {
        /// <summary>An actor that is still in the fight.</summary>
        LivingActor,

        /// <summary>
        /// A named character — a party member rather than a creature. Types 2 and 3 reject actor
        /// number zero, which every monster carries.
        /// </summary>
        NamedCharacter,

        /// <summary>An empty, unblocked cell with no crystal on it.</summary>
        ClearGround,

        /// <summary>
        /// A dead actor — type 7, which only Final Rest carries. The cursor tests <c>CAF_DEAD</c>
        /// (COMBAT.C:2202-2207), so a Grief-frozen enemy (incapacitated, not dead) is refused.
        /// </summary>
        DownedActor,

        /// <summary>A cell holding a red or green crystal (<c>CrystalChain.IsCrystalElement</c>).</summary>
        Crystal,
    }

    /// <summary>
    /// The aiming rule for a targeting type.
    /// </summary>
    /// <remarks>
    /// Six branches over nine types: 0 and 1/4 want a living actor, 2/3 want a named character, 5/6
    /// want clear ground, 7 wants a downed actor and 8 wants a crystal. The grouping is not in
    /// numeric order and cannot be guessed from the numbers.
    /// </remarks>
    public static Aim AimOf(int targetingType) {
        switch (targetingType) {
            case 2:
            case 3:
                return Aim.NamedCharacter;
            case 5:
            case 6:
                return Aim.ClearGround;
            case 7:
                return Aim.DownedActor;
            case 8:
                return Aim.Crystal;
            default:
                return Aim.LivingActor;
        }
    }

    /// <summary>
    /// Whether clear ground accepts this cell.
    /// </summary>
    /// <param name="blocked">The cell is impassable.</param>
    /// <param name="hasCrystal">The cell carries a trap crystal.</param>
    public static bool GroundIsTargetable(bool blocked, bool hasCrystal) => !blocked && !hasCrystal;

    /// <summary>
    /// The cursor bounds the check accepts, which are one wider than the nominal grid.
    /// </summary>
    /// <remarks>
    /// The guard is <c>0 &lt;= x &lt;= 8</c> and <c>0 &lt;= y &lt;= 13</c>, against a combat grid
    /// addressed as 8 columns by 13 rows — so it admits one column and one row past the last valid
    /// index. Recorded rather than modelled: whether that is slack in the check or a grid that is
    /// really nine by fourteen has not been established here, and the cell lookups that follow would
    /// decide it.
    /// <para><b>Deliberately callerless.</b> A recorded fact; the port's targeting tests CombatGrid.InBounds.</para>
    /// </remarks>
    public static bool CursorBoundsAreOneWiderThanTheGrid => true;

    // ---------------------------------------------------------------- committing the cast
    // combat_arena_resolve_menu_action @0x626ca, case 4.

    /// <summary>
    /// <b>The ground-aimed types reach the dispatcher with no target at all.</b>
    /// </summary>
    /// <remarks>
    /// Types 5 and 6 are handed to <c>Cast_Spell</c> with a null target actor outright, and type 8
    /// gets there by the empty-cell branch — so all three of the types that aim at floor rather than
    /// at anybody arrive untargeted.
    ///
    /// <para>That is the other half of <see cref="SpellCostModifiers.DiscardsTarget"/>, which
    /// records the dispatcher nulling type 8's target on the way in. For 5 and 6 there was never a
    /// target to null: the UI simply never supplies one.</para>
    /// </remarks>
    public static bool CastsWithoutATarget(int targetingType) =>
        AimOf(targetingType) == Aim.ClearGround || AimOf(targetingType) == Aim.Crystal;

    /// <summary>
    /// <b>Casting ends the turn.</b>
    /// </summary>
    /// <remarks>
    /// Every path that reaches <c>Cast_Spell</c> clears the caster's ready bit immediately
    /// afterwards, the same bit the move and melee actions clear. There is no cast-and-then-move.
    /// <para><b>Deliberately callerless.</b> HotspotService clears the caster's Ready flag after a cast resolves.</para>
    /// </remarks>
    public static bool CastingEndsTheTurn => true;

    /// <summary>
    /// Whether a click commits the cast.
    /// </summary>
    /// <param name="mouseY">Screen Y of the click.</param>
    /// <param name="cursorDistance">The cursor's grid distance, or <see cref="OffGridDistance"/>.</param>
    /// <remarks>
    /// Two independent rejections before any spell rule is consulted: a click at or below
    /// <see cref="FieldBottomY"/> is in the menu bar rather than the field, and a distance of
    /// <see cref="OffGridDistance"/> is the sentinel for a cursor that is not over a cell. Both leave
    /// the action pending rather than cancelling it.
    ///
    /// <para><b>Deliberately callerless.</b> The port's menu is a separate UI layer that consumes its own clicks and its pick returns a cell or nothing, so both DOS-space gates hold by construction; see CombatTargetSelection.ResolveOnField.</para>
    /// </remarks>
    public static bool ClickCommitsTheCast(int mouseY, int cursorDistance) =>
        mouseY < FieldBottomY && cursorDistance != OffGridDistance;

    /// <summary>Screen Y at which the combat field gives way to the menu bar.</summary>
    public const int FieldBottomY = 0x8C;

    /// <summary>The distance value standing for "the cursor is not over a grid cell".</summary>
    public const int OffGridDistance = 1000;
}

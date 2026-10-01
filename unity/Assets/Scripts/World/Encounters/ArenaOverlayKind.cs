namespace BakAgain.World.Encounters {
    /// <summary>
    /// What one line of the arena overlay is, since the three are drawn by the same component but
    /// mean different things to the player.
    /// </summary>
    public enum ArenaOverlayKind {
        /// <summary>A cell boundary of the 8x13 tactical grid — <c>drawCombatGridOutline</c>.</summary>
        Grid = 0,

        /// <summary>A crystal chain link — <c>crystalChain_drawLinks</c>. Shares the grid's flag.</summary>
        Link = 1,

        /// <summary>
        /// An edge of the ring under the acting combatant. <b>Not behind the G toggle</b>: it is the
        /// only thing on screen that says where the character whose turn it is stands.
        /// </summary>
        ActingCell = 2,

        /// <summary>
        /// An edge of the ring under the enemy the cursor is on, while a click would attack them.
        /// </summary>
        /// <remarks>
        /// <b>The original draws it yellow while the acting cell stays green</b>, and it comes up at
        /// the same moment the Thrust/Swing prompt replaces the stat panel — so the outline and the
        /// prompt are one signal, not two. Ours is raised from the same predicate that prints the
        /// melee preview for exactly that reason (TASK-586).
        ///
        /// <para>Not behind the G toggle either: it answers "will this click hit", which a player
        /// needs whether or not they have asked for the grid.</para>
        /// </remarks>
        TargetCell = 3,

        /// <summary>
        /// The cursor on a cell the acting combatant can walk to — the original's COMBAT.TBL marker
        /// kind 1 (pen 226), chosen by <c>combatgrid_cursor_tile_movable</c> (COMBAT.C:2324-2326).
        /// </summary>
        MoveCell = 4,

        /// <summary>
        /// A cell the touch aids' cursor or selection is on that is neither a target nor movable. The
        /// original's own "none" marker (kind 0xffff) is drawn for the mouse cursor too; here it is
        /// only shown for touch, where there is no cursor to say which cell is meant.
        /// </summary>
        NoneCell = 5,
    }
}

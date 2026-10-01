namespace BakAgain.UI.InGame {
    using System;
    using BakAgain.UI.InputCore;
    using UnityEngine;

    /// <summary>
    /// The side bar's Thrust and Swing (spec 2026-09-29-android-touch-aids-design.md in the private
    /// workspace): an attack on the combatant the combat cursor previews.
    /// </summary>
    /// <remarks>
    /// The cursor stores the previewed cell's screen point in
    /// <see cref="TouchInputState.CombatHoverScreenPoint"/>, which WorldInteractionController's hover
    /// pick reads on touch — so the ring and the Thrust/Swing preview are the original's own. The
    /// attack goes through the mouse's click path: Thrust is the left click, Swing the right.
    /// </remarks>
    public sealed class CombatTouchTargeting {
        private readonly TouchInputState _state;
        private readonly Func<Vector2, (int RosterSlot, bool PartyMember)?> _targetAt;
        private readonly Action<int, bool, bool> _attack;

        /// <param name="targetAt">The combatant under a screen point (Input System coords), or null.</param>
        /// <param name="attack">(roster slot, party member, isPrimary): the mouse click's own dispatch.</param>
        public CombatTouchTargeting(TouchInputState state, Func<Vector2, (int RosterSlot, bool PartyMember)?> targetAt,
            Action<int, bool, bool> attack) {
            _state = state;
            _targetAt = targetAt;
            _attack = attack;
        }

        public void Melee(bool thrust) {
            if (!_state.CombatHoverScreenPoint.HasValue) {
                return;
            }
            (int RosterSlot, bool PartyMember)? target = _targetAt(_state.CombatHoverScreenPoint.Value);
            if (!target.HasValue) {
                return;
            }
            _state.CombatHoverScreenPoint = null;
            _attack(target.Value.RosterSlot, target.Value.PartyMember, thrust);
        }
    }
}

namespace BakAgain.UI.InGame {
    using System;
    using BakAgain.UI.InputCore;
    using UnityEngine;

    /// <summary>
    /// Touch combat targeting (spec 2026-09-29-android-touch-aids-design.md in the private workspace).
    /// </summary>
    /// <remarks>
    /// <para><b>C1, select then confirm:</b> a tap SELECTS a target by storing its screen point in
    /// <see cref="TouchInputState.CombatHoverScreenPoint"/>, which WorldInteractionController's hover
    /// pick reads on touch — so the target ring and the Thrust/Swing preview are the original's own,
    /// driven exactly as a mouse hover drives them. A second tap on the same target, or the Thrust /
    /// Swing button, attacks through the mouse's click path (left = Thrust, right = Swing).</para>
    ///
    /// <para><b>C2, finger as hover:</b> the hover point sits above the finger while it is down, and
    /// lifting it thrusts.</para>
    /// </remarks>
    public sealed class CombatTouchTargeting {
        private readonly TouchInputState _state;
        private readonly Func<Vector2, (int RosterSlot, bool PartyMember)?> _targetAt;
        private readonly Action<int, bool, bool> _attack;
        private readonly float _snap;

        /// <param name="targetAt">The combatant under a screen point (Input System coords), or null.</param>
        /// <param name="attack">(roster slot, party member, isPrimary): the mouse click's own dispatch.</param>
        public CombatTouchTargeting(TouchInputState state, Func<Vector2, (int RosterSlot, bool PartyMember)?> targetAt,
            Action<int, bool, bool> attack, float snapRadiusPixels) {
            _state = state;
            _targetAt = targetAt;
            _attack = attack;
            _snap = snapRadiusPixels;
        }

        public void Tap(Vector2 screenPoint) {
            Vector2? hit = Snap(screenPoint);
            if (!hit.HasValue) {
                Clear();
                return;
            }
            (int, bool)? tapped = _targetAt(hit.Value);
            (int, bool)? selected = _state.CombatHoverScreenPoint.HasValue
                ? _targetAt(_state.CombatHoverScreenPoint.Value)
                : null;
            if (selected.HasValue && selected.Equals(tapped)) {
                Melee(thrust: true);
                return;
            }
            _state.CombatHoverScreenPoint = hit;
        }

        /// <summary>Attacks the selected target: Thrust is the original's left click, Swing its right.</summary>
        public void Melee(bool thrust) {
            if (!_state.CombatHoverScreenPoint.HasValue) {
                return;
            }
            (int RosterSlot, bool PartyMember)? target = _targetAt(_state.CombatHoverScreenPoint.Value);
            if (!target.HasValue) {
                return;
            }
            Clear();
            _attack(target.Value.RosterSlot, target.Value.PartyMember, thrust);
        }

        public void Clear() => _state.CombatHoverScreenPoint = null;

        // ponytail: screen-space ring sampling (8 directions at two radii). A world-space nearest-cell
        // search belongs here if the ring ever misses a target a player plainly tapped next to.
        private Vector2? Snap(Vector2 p) {
            if (_targetAt(p).HasValue) {
                return p;
            }
            foreach (float r in new[] { _snap * 0.5f, _snap }) {
                for (int i = 0; i < 8; i++) {
                    float a = i * Mathf.PI / 4f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (_targetAt(q).HasValue) {
                        return q;
                    }
                }
            }
            return null;
        }
    }
}

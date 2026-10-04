namespace BakAgain.World.Encounters {
    using GameData.Resources.Animation;
    using GameData.Resources.Combat;
    using System;
    using UnityEngine;

    /// <summary>
    /// Breathes the live combatants in an arena — <c>combat_actor_anim0_if_not_dead</c> @0x5ee02.
    /// </summary>
    /// <remarks>
    /// <b>The idle IS the walk cycle, played in place.</b> Slot 0 asks for <c>framesPerDir 3</c>,
    /// which is the same three frames the walk uses, and there is no separate idle art — bitmap 0
    /// holds five facings of three walk frames and nothing else for a living creature. A standing
    /// creature marching on the spot looks odd written down and is what the original does.
    ///
    /// <para><b>The delay is re-rolled after every advance</b>, to 8..15 ticks
    /// (<see cref="CreatureAnimationStep.NextGaitDelay"/>). A fixed delay produces a metronome the
    /// original never has, and the irregularity is the whole character of the animation.</para>
    ///
    /// <para><b>State lives on the combatant, not here.</b> The arena is destroyed and rebuilt on
    /// every combat redraw, so a counter on this component would restart the gait each turn — the
    /// same reason <c>Combatant.GaitFrame</c> exists.</para>
    ///
    /// <para><b>Downed actors are skipped</b>, which is the original's
    /// <c>combatStatus_incapacitated</c> guard and what keeps a corpse still after its collapse.</para>
    /// </remarks>
    public sealed class ArenaIdleAnimator : MonoBehaviour {
        private Func<int, bool, Combatant> _lookup;
        private Func<int, int> _rnd;
        private double _carried;

        /// <summary>Bind the seam that turns a marker back into its combatant.</summary>
        public void Bind(Func<int, bool, Combatant> lookup, Func<int, int> rnd) {
            _lookup = lookup;
            _rnd = rnd ?? (n => UnityEngine.Random.Range(0, n));
        }

        private void Update() {
            if (_lookup == null) {
                return;
            }

            // Whole ARENA FRAMES only: the original steps the gait once per drawn arena frame
            // (ArenaFrame, ~15.7/s), not per timer tick — stepping on the 59.17 Hz tick ran the idle
            // ~4x fast (TASK-768). A frame rate that outruns it must not step more than once.
            _carried += Time.deltaTime * ArenaFrame.PerSecond;
            var ticks = (int)_carried;
            if (ticks <= 0) {
                return;
            }
            _carried -= ticks;

            foreach (ArenaCombatant marker in GetComponentsInChildren<ArenaCombatant>()) {
                Combatant c = _lookup(marker.RosterSlot, marker.PartyMember);
                if (c == null || c.IsDead) {
                    continue;
                }
                var sprite = marker.GetComponent<DirectionalSprite>();
                if (sprite == null) {
                    continue;
                }
                for (var i = 0; i < ticks; i++) {
                    c.AnimTick++;
                    if (!CreatureAnimationStep.Advances(c.AnimTick, c.AnimDelay)) {
                        continue;
                    }
                    sprite.AdvanceGait();
                    c.GaitFrame = sprite.GaitFrame;
                    c.GaitAdvancing = sprite.GaitAdvancing;
                    c.AnimTick = CreatureAnimationStep.TickCounterAfterAdvance;
                    c.AnimDelay = CreatureAnimationStep.NextGaitDelay(_rnd(int.MaxValue));
                }
            }
        }
    }
}

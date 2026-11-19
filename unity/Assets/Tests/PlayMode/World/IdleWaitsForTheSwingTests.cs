namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.World.Encounters;
    using GameData.Resources.Combat;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// The idle gait does not step an attacker in mid-swing: the swing owns its picture until it
    /// ends. The port stepped it, so a standing frame could flash through the swing and the swing
    /// then restored a stale one (TASK-1100 D6).
    /// </summary>
    public class IdleWaitsForTheSwingTests {
        [UnityTest]
        public IEnumerator ASwingingCombatantIsNotSteppedByTheIdle() {
            var arena = new GameObject("arena");
            try {
                var c = new Combatant { AnimDelay = 3 };
                var idle = arena.AddComponent<ArenaIdleAnimator>();
                idle.Bind((slot, party) => c, n => 0);
                var actor = new GameObject("actor");
                actor.transform.SetParent(arena.transform);
                actor.AddComponent<ArenaCombatant>().RosterSlot = 0;
                var sprite = actor.AddComponent<DirectionalSprite>();
                var swing = actor.AddComponent<AttackSwing>();

                swing.Running = true;
                int before = c.AnimTick;
                float until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.AreEqual(before, c.AnimTick, "the idle does not tick a swinging combatant");

                swing.Running = false;
                until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.AreNotEqual(before, c.AnimTick, "control: the idle ticks it once the swing is over");
            } finally {
                Object.DestroyImmediate(arena);
            }
        }

        /// <summary>
        /// The swing's last picture holds until the turn is handed on: a non-0 animation slot ends on
        /// its end frame and stays (CACTOR.C:862-864), released only when the next turn redraws the
        /// attacker (COMBAT.C:1434-1439). The port snapped back to standing as the swing ended, before
        /// the victim fell (TASK-1100 D4). The idle must not step it either.
        /// </summary>
        [UnityTest]
        public IEnumerator AHeldSwingIsNotSteppedByTheIdle() {
            var arena = new GameObject("arena");
            try {
                var c = new Combatant { AnimDelay = 3 };
                var idle = arena.AddComponent<ArenaIdleAnimator>();
                idle.Bind((slot, party) => c, n => 0);
                var actor = new GameObject("actor");
                actor.transform.SetParent(arena.transform);
                actor.AddComponent<ArenaCombatant>().RosterSlot = 0;
                actor.AddComponent<DirectionalSprite>();
                var swing = actor.AddComponent<AttackSwing>();

                swing.Holding = true;
                int before = c.AnimTick;
                float until = Time.realtimeSinceStartup + 1.5f;
                while (Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.AreEqual(before, c.AnimTick, "the last attack picture holds");
            } finally {
                Object.DestroyImmediate(arena);
            }
        }
    }
}

namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.World.Encounters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Combat;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Winds of Eortis flies its whirlwind to the victim's OLD cell and only then walks it back —
    /// <c>world_rndr_ranged_attack_anim</c> before <c>cspell_actor_walk_steps</c> (CSPELL.C:653-668).
    /// The rules move the victim at once, so the arena's slide has to wait for the flight (TASK-117).
    /// </summary>
    public class WhirlwindHoldsTheMoversTests {
        private static SpellVfx Vfx() => new SpellVfx(_ => null, () => null, () => null, () => null,
            (_, _) => UniTask.FromResult<GameObject>(null), () => Quaternion.identity, _ => null);

        [UnityTest]
        public IEnumerator AWhirlwindHoldsTheMoversUntilItHasFlown() {
            SpellVfx vfx = Vfx();
            vfx.Enqueue(new SpellVisual(SpellVisualKind.WhirlwindFlight), new Combatant(), new Combatant());
            Assert.IsTrue(vfx.HoldsMovers, "the slide must wait while the whirlwind is queued");
            for (var i = 0; i < 10 && vfx.HoldsMovers; i++) {
                yield return null;
            }
            Assert.IsFalse(vfx.HoldsMovers, "and let go once it has played");
        }

        [Test]
        public void OtherVisualsDoNotHoldTheMovers() {
            SpellVfx vfx = Vfx();
            vfx.Enqueue(new SpellVisual(SpellVisualKind.StormFlash), new Combatant(), new Combatant());
            Assert.IsFalse(vfx.HoldsMovers);
        }
    }
}

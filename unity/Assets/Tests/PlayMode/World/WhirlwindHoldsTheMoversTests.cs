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
        // Every test lets its queue drain and clears the world flash: a visual left playing (the
        // storm sets the flash global) washed out the rendering tests that ran after this class.
        [TearDown]
        public void ClearTheFlash() => SpellVfx.SetWorldFlash(Color.clear, 0f);

        private static IEnumerator Drain(SpellVfx vfx, int played) {
            for (var i = 0; i < 600 && vfx.Played < played; i++) {
                yield return null;
            }
        }

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

        [UnityTest]
        public IEnumerator EvilSeeksHopsHoldTheBoardToo() {
            // cspell_invoke_effect plays Evil Seek's whole chain before the board shows its damage;
            // the redraw floated "50"/"40" from the first frame (TASK-117).
            SpellVfx vfx = Vfx();
            vfx.Enqueue(new SpellVisual(SpellVisualKind.HopBurst), new Combatant(), new Combatant());
            Assert.IsTrue(vfx.HoldsMovers);
            yield return Drain(vfx, 1);
        }

        [UnityTest]
        public IEnumerator FinalRestsSinkHoldsTheBoard() {
            // cspell_invoke_effect sinks the body BEFORE the post-animation arm takes it off the grid
            // (CSPELL.C:1459-1475). Our rules remove it at once, so a redraw that did not wait dropped
            // the sprite before the sink could move it: the body just vanished (TASK-117).
            SpellVfx vfx = Vfx();
            vfx.Enqueue(new SpellVisual(SpellVisualKind.Sink), new Combatant(), new Combatant());
            Assert.IsTrue(vfx.HoldsMovers);
            yield return Drain(vfx, 1);
        }

        [UnityTest]
        public IEnumerator OtherVisualsDoNotHoldTheMovers() {
            SpellVfx vfx = Vfx();
            vfx.Enqueue(new SpellVisual(SpellVisualKind.Rebound), new Combatant(), new Combatant());
            Assert.IsFalse(vfx.HoldsMovers);
            yield return Drain(vfx, 1);
        }
    }
}

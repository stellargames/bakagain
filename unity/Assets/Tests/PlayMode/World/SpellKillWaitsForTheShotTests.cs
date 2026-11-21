namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.World.Encounters;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// A body falls after the blow, and a spell's blow lands when its shot does. The original plays
    /// the projectile and the spell's effect blocking, then applies the damage that plays the fall
    /// (CSPELL.C; world_rndr_ranged_attack_anim before apply_damage). The port held the fall for a
    /// melee swing only, so a spell kill fell while the bolt was still flying (TASK-1107 D7).
    /// </summary>
    public class SpellKillWaitsForTheShotTests {
        [UnityTest]
        public IEnumerator AFlyingShotHoldsTheFallBack() {
            var shot = new GameObject("shot");
            try {
                Assert.IsFalse(DeathCollapse.HeldBack, "control: nothing in flight");
                shot.AddComponent<ProjectileFlight>().Play(Vector3.zero, Vector3.forward, seconds: 0.2f);

                Assert.IsTrue(DeathCollapse.HeldBack, "the fall waits while the shot flies");

                float until = Time.realtimeSinceStartup + 0.6f;
                while (Time.realtimeSinceStartup < until) {
                    yield return null;
                }
                Assert.IsFalse(DeathCollapse.HeldBack, "and goes once it has landed");
            } finally {
                Object.DestroyImmediate(shot);
            }
        }

        /// <summary>
        /// A shot armed but not yet flying (its shooter is still posing, or its sprite is building)
        /// holds the fall too: a killing crossbow shot dropped its target in the gap between the pose
        /// and the bolt (TASK-1124 #9, seen live 2026-11-21).
        /// </summary>
        [Test]
        public void AShotArmedButNotYetFlyingHoldsTheFallBack() {
            Assert.IsFalse(DeathCollapse.HeldBack, "control");
            ProjectileFlight.Arm();
            try {
                Assert.IsTrue(DeathCollapse.HeldBack);
            } finally {
                ProjectileFlight.Release();
            }
            Assert.IsFalse(DeathCollapse.HeldBack);
        }
    }
}

namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Encounters;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// world_rndr_ranged_attack_anim yaws the projectile toward its target (WORLDHIT.C:654) and draws a
    /// ground shadow under it (world_render_combat_grid_sprite, WORLDHIT.C:743-760). The port's arrow
    /// flew at identity rotation, end-on to the arena camera, and without a shadow (TASK-1008).
    /// </summary>
    public class ProjectileHeadingTests {
        [Test]
        public void TheProjectileFacesItsTargetAndItsShadowRunsOnTheGround() {
            var shot = new GameObject("shot");
            var shadow = new GameObject("shadow");
            try {
                var from = new Vector3(0f, 2f, 0f);
                var to = new Vector3(4f, 2f, 3f);

                shot.AddComponent<ProjectileFlight>().Play(from, to, shadow: shadow.transform, groundY: 0f);

                Assert.That(Vector3.Angle(shot.transform.forward, new Vector3(4f, 0f, 3f)), Is.LessThan(0.5f));
                Assert.AreEqual(new Vector3(0f, 0f, 0f), shadow.transform.localPosition);
                Assert.That(Vector3.Angle(shadow.transform.forward, new Vector3(4f, 0f, 3f)), Is.LessThan(0.5f));
            } finally {
                Object.DestroyImmediate(shot);
                Object.DestroyImmediate(shadow);
            }
        }
    }
}

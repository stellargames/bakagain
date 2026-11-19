namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Encounters;
    using NUnit.Framework;

    /// <summary>
    /// The thrust is its own run: anim slot 4, two frames per facing, starting at the facing
    /// (combat_actor_play_anim_sprite4, CACTOR.C:1947-1950; frame base :1873-1883), where the
    /// swing is slot 3, four frames from twice the facing (:1952-1955). The port played the swing
    /// for both, so every walking attack ran twice as long and showed other pictures (TASK-1095).
    /// </summary>
    public class AttackRunTests {
        [TestCase(0, 0, 4)]
        [TestCase(2, 4, 4)]
        [TestCase(4, 8, 4)]
        [TestCase(6, 4, 4)]
        public void TheSwingRunsFourFramesFromTwiceTheFacing(int octant, int start, int frames) =>
            Assert.AreEqual((start, frames), AttackSwing.RunFor(octant, thrust: false));

        [TestCase(0, 0, 2)]
        [TestCase(2, 2, 2)]
        [TestCase(3, 2, 2)]
        [TestCase(4, 4, 2)]
        [TestCase(6, 2, 2)]
        public void TheThrustRunsTwoFramesFromTheFacing(int octant, int start, int frames) =>
            Assert.AreEqual((start, frames), AttackSwing.RunFor(octant, thrust: true));
    }
}

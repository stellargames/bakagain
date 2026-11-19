namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.World.Encounters;
    using NUnit.Framework;

    /// <summary>
    /// The death run shows the sprite's four death pictures and holds the last: sprite1 is four
    /// frames (CACTOR.C:1957-1960) and a non-0 slot ends on end_frame and stays (CACTOR.C:862-864).
    /// The port played last-3..last-1 and left the body one picture short (TASK-1100 D7).
    /// </summary>
    public class DeathCollapseFramesTests {
        [Test]
        public void TheRunEndsOnTheLastPicture() {
            CollectionAssert.AreEqual(new[] { 17, 18, 19, 20 }, DeathCollapse.FramesFor(20));
        }
    }
}

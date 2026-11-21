namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Encounters;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// One spell that kills several plays their falls one after another: each death blocks until its
    /// four frames have played (COMBAT.C:226; CACTOR.C:1957-1959, 1896-1897). The port started every
    /// fall on the same redraw (TASK-1125 #2).
    /// </summary>
    public class FallsPlayInTurnTests {
        [Test]
        public void EachFallWaitsForTheOneBeforeIt() {
            var a = new GameObject("first").AddComponent<DeathCollapse>();
            var b = new GameObject("second").AddComponent<DeathCollapse>();
            try {
                DeathCollapse.EnterLine(a);
                DeathCollapse.EnterLine(b);
                Assert.IsTrue(DeathCollapse.IsFirstInLine(a));
                Assert.IsFalse(DeathCollapse.IsFirstInLine(b), "the second waits");

                DeathCollapse.LeaveLine(a);
                Assert.IsTrue(DeathCollapse.IsFirstInLine(b), "and goes once the first has fallen");
            } finally {
                DeathCollapse.LeaveLine(a);
                DeathCollapse.LeaveLine(b);
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        [Test]
        public void AFallWhoseBodyIsGoneDoesNotHoldTheLine() {
            var a = new GameObject("cleared").AddComponent<DeathCollapse>();
            var b = new GameObject("next").AddComponent<DeathCollapse>();
            try {
                DeathCollapse.EnterLine(a);
                DeathCollapse.EnterLine(b);
                Object.DestroyImmediate(a.gameObject);

                Assert.IsTrue(DeathCollapse.IsFirstInLine(b), "a redraw that cleared the arena frees the line");
            } finally {
                DeathCollapse.LeaveLine(b);
                Object.DestroyImmediate(b.gameObject);
            }
        }
    }
}

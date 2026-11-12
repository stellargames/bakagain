namespace BakAgain.Tests.Editor.World {
    using BakAgain.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// TASK-1056 (owner: tilt only): standing next to a chest or mound in the Enhanced travel view, the
    /// thing sank out of the bottom of the view and had to be stepped back from before it could be
    /// clicked. The view tilts down just enough to keep the foot of a clickable thing ahead in sight.
    /// </summary>
    public class NearObjectTiltTests {
        private static readonly Vector3 Eye = new(0f, 2f, 0f);
        private const float HalfV = 20f, HalfH = 35f, BasePitchDown = 0f;

        private static float Tilt(Vector3 foot, float reach = 30f) =>
            NearObjectTilt.DegreesFor(Eye, Vector3.forward, BasePitchDown, HalfV, HalfH,
                new[] { (foot, reach) });

        [Test]
        public void AChestAtTheFeet_TiltsTheViewUntilItsFootIsInSight() {
            var foot = new Vector3(0f, 0f, 2f);   // 45 degrees down, below a 20-degree half view
            float tilt = Tilt(foot);
            Assert.Greater(tilt, 0f);
            float footDown = Mathf.Atan2(Eye.y - foot.y, foot.z) * Mathf.Rad2Deg;
            Assert.LessOrEqual(footDown, BasePitchDown + tilt + HalfV - NearObjectTilt.MarginDegrees + 0.01f,
                "the foot is inside the view, with the margin");
        }

        [Test]
        public void NothingInReach_InSightAlready_OrBehind_LeavesTheViewLevel() {
            Assert.AreEqual(0f, Tilt(new Vector3(0f, 0f, 2f), reach: 1f), "out of click reach");
            Assert.AreEqual(0f, Tilt(new Vector3(0f, 0f, 20f)), "far enough ahead to be in view");
            Assert.AreEqual(0f, Tilt(new Vector3(0f, 0f, -2f)), "behind the party");
            Assert.AreEqual(0f, Tilt(new Vector3(5f, 0f, 1f)), "off to the side, outside the view");
        }

        [Test]
        public void TheTiltIsCapped() {
            Assert.AreEqual(NearObjectTilt.MaxDegrees, Tilt(new Vector3(0f, 0f, 0.2f)), 1e-4f);
        }
    }
}

namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.ResourceManagement.Loaders;
    using NUnit.Framework;

    /// <summary>
    /// A button a screen has deliberately cleared must not grow a face under the pointer.
    /// </summary>
    /// <remarks>
    /// The hover highlight is the button's icon one brighter, and -1 is the sentinel for "no icon" —
    /// so the arithmetic put -1 + 1 = 0 on screen, icon 0 being a real sprite. The picklock screen's
    /// Use stone is bare by the original's own rule (INVENTOR.C leaves it so when neither of its two
    /// meanings applies) and grew a small arrow on hover (TASK-585).
    /// </remarks>
    [TestFixture]
    public class HoverIconStaysBareTests {
        [Test]
        public void TheSentinelDoesNotBecomeIconZero() {
            // The whole defect in one line: 0 is a real sprite, so +1 on the sentinel paints one.
            Assert.That(UserInterfaceLoader.HoverIcon(-1), Is.LessThan(0));
        }

        [TestCase(0, 1)]
        [TestCase(34, 35)]
        [TestCase(0x67, 0x68)]
        public void ARealIconStillBrightensByOne(int baseIcon, int expected) {
            // The control: the guard must not flatten the highlight everywhere else. Icon 0 is a
            // real base in its own right, which is exactly why the sentinel could not simply be
            // treated as "falsy".
            Assert.That(UserInterfaceLoader.HoverIcon(baseIcon), Is.EqualTo(expected));
        }
    }
}

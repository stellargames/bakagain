namespace BakAgain.Tests.World {
    using BakAgain.World;
    using NUnit.Framework;

    public class MovementMathTests {
        [Test]
        public void StepDelta_Heading0_MovesForwardOnY() {
            var (dx, dy) = MovementMath.StepDelta(0, 1600);
            Assert.AreEqual(0, dx);       // no lateral drift facing forward
            Assert.AreEqual(1600, dy);    // +Y = forward in BaK space
        }

        [Test]
        public void StepDelta_QuarterTurn_MovesOnX() {
            // heading 16384 = 90°
            var (dx, dy) = MovementMath.StepDelta(16384, 1600);
            Assert.AreEqual(-1600, dx);
            Assert.AreEqual(0, dy);
        }

        [Test]
        public void StepDelta_Magnitude_EqualsStep() {
            var (dx, dy) = MovementMath.StepDelta(12345, 800);
            double mag = System.Math.Sqrt((double)dx * dx + (double)dy * dy);
            Assert.That(mag, Is.EqualTo(800).Within(1.5)); // rounding tolerance
        }

        [Test]
        public void Turn_Right_AddsAngle() {
            Assert.AreEqual(1024, MovementMath.Turn(0, 1024, left: false));
        }

        [Test]
        public void Turn_Left_WrapsBelowZero() {
            Assert.AreEqual(64512, MovementMath.Turn(0, 1024, left: true)); // 65536-1024
        }

        [Test]
        public void Turn_Right_WrapsAbove65535() {
            Assert.AreEqual(0, MovementMath.Turn(64512, 1024, left: false)); // 65536 → 0
        }
    }
}

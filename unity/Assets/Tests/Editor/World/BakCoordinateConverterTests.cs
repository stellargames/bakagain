namespace BakAgain.Tests.Editor.World {
    using BakAgain.World.Converters;
    using NUnit.Framework;
    using UnityEngine;

    public class BakCoordinateConverterTests {
        [Test]
        public void ConvertPosition_ZeroPosition_ReturnsZero() {
            var result = BakCoordinateConverter.ConvertPosition(0, 0, 0);
            Assert.AreEqual(Vector3.zero, result);
        }

        [Test]
        public void ConvertPosition_SwapsYZ() {
            // BaK: X=1000, Y=2000, Z=3000 (Y is forward in BaK)
            // Unity: X=1000/100=10, Y=3000/100=30 (BaK Z→Unity Y), Z=2000/100=20 (BaK Y→Unity Z)
            var result = BakCoordinateConverter.ConvertPosition(1000, 2000, 3000);
            Assert.AreEqual(new Vector3(10f, 30f, 20f), result);
        }

        [Test]
        public void ConvertPosition_Uint_HandlesLargeValues() {
            // WLD positions are uint. Tile center at 32000,32000,0
            var result = BakCoordinateConverter.ConvertPosition(32000u, 32000u, 0u);
            Assert.AreEqual(new Vector3(320f, 0f, 320f), result);
        }

        [Test]
        public void ConvertRotation_ZeroRotation_ReturnsIdentity() {
            // Must be EXACTLY identity: the old 65535-a formula turned raw 0 into 359.9945°,
            // tilting every unrotated world object by -0.006° on all three axes.
            var result = BakCoordinateConverter.ConvertRotation(0, 0, 0);
            Assert.AreEqual(Quaternion.identity, result);
        }

        [Test]
        public void ConvertRotation_90DegreeYaw() {
            // BaK angles are negated (Y↔Z reflection): degrees = (65536 - bakAngle) / 65536 * 360.
            // For a 90° Unity yaw, BaK rot.Z = 65536 - 16384 = 49152 (0xC000).
            var result = BakCoordinateConverter.ConvertRotation(0, 0, 49152);
            Assert.AreEqual(90f, result.eulerAngles.y, 0.01f);
        }

        [Test]
        public void ConvertVertexPosition_Short_ConvertsCorrectly() {
            // TBL vertices use short (signed 16-bit)
            var result = BakCoordinateConverter.ConvertVertexPosition(-100, 200, 300);
            Assert.AreEqual(new Vector3(-1f, 3f, 2f), result);
        }

        [Test]
        public void BakAngleToDegrees_IsInverted() {
            // Angles are negated into [0, 360): degrees = (65536 - bakAngle) % 65536 / 65536 * 360.
            // Raw 0 maps to exactly 0°; one raw step below zero (0xFFFF) maps to one step (0.0055°).
            Assert.AreEqual(0f, BakCoordinateConverter.BakAngleToDegrees(0), 0f);
            Assert.AreEqual(360f / 65536f, BakCoordinateConverter.BakAngleToDegrees(0xFFFF), 1e-4f);
            Assert.AreEqual(90f, BakCoordinateConverter.BakAngleToDegrees(0xC000), 1e-4f);
        }

        [Test]
        public void BakAngleToDegrees_HalfCircle() {
            Assert.AreEqual(180f, BakCoordinateConverter.BakAngleToDegrees(0x8000), 1e-4f);
        }
    }
}

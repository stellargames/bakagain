namespace BakAgain.World.Converters {
    using UnityEngine;

    /// <summary>
    /// Converts BaK world coordinates and rotations to Unity equivalents.
    /// BaK: X=right, Y=forward, Z=up. Tile size=64000 units. gWorldScale=100.
    /// Unity: X=right, Y=up, Z=forward.
    /// Conversion: Unity.X = BaK.X/100, Unity.Y = BaK.Z/100, Unity.Z = BaK.Y/100.
    /// BaK angles: ushort where 65536 = 360 degrees (full circle).
    /// </summary>
    public static class BakCoordinateConverter {
        public const float WorldScale = 100f;
        public const float TileSizeBak = GameData.Resources.World.WorldTileCache.TileWorldSize;

        /// <summary>Convert BaK world position (uint, from WLD) to Unity Vector3.</summary>
        public static Vector3 ConvertPosition(uint bakX, uint bakY, uint bakZ) {
            return new Vector3(
                bakX / WorldScale,
                bakZ / WorldScale,
                bakY / WorldScale
            );
        }

        /// <summary>Convert BaK position (int/short, from TBL vertices) to Unity Vector3.</summary>
        public static Vector3 ConvertPosition(int bakX, int bakY, int bakZ) {
            return new Vector3(
                bakX / WorldScale,
                bakZ / WorldScale,
                bakY / WorldScale
            );
        }

        /// <summary>Convert BaK ushort angles to Unity Quaternion.</summary>
        public static Quaternion ConvertRotation(ushort bakRotX, ushort bakRotY, ushort bakRotZ) {
            float pitch = BakAngleToDegrees(bakRotX);  // BaK rot.X → Unity euler.X
            float roll = BakAngleToDegrees(bakRotY);   // BaK rot.Y → Unity euler.Z
            float yaw = BakAngleToDegrees(bakRotZ);    // BaK rot.Z → Unity euler.Y
            return Quaternion.Euler(pitch, yaw, roll);
        }

        /// <summary>Convert BaK angle (ushort, 65536=360°) to degrees, in [0, 360).
        /// The Y↔Z axis swap is a reflection, which flips every rotation sense — so the BaK angle
        /// is negated: 65536 − a (mod 65536). NOT 65535 − a: that off-by-one (one 65536th of a
        /// circle, 0.0055°) rotated every unrotated world object by −0.006° on all axes.</summary>
        public static float BakAngleToDegrees(ushort bakAngle) {
            return (65536 - bakAngle) % 65536 / 65536f * 360f;
        }

        /// <summary>Unity XZ (÷WorldScale from BaK X/Y) → BaK fine X/Y coords — the inverse of
        /// ConvertPosition's XZ mapping. Used by world-interaction container lookups.</summary>
        public static (int X, int Y) ToBakXY(Vector3 unityPos) {
            return ((int)Mathf.Round(unityPos.x * WorldScale), (int)Mathf.Round(unityPos.z * WorldScale));
        }
    }
}

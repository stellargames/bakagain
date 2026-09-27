namespace BakAgain.World.Collision {
    using System;
    using GameData.Resources.World;

    /// <summary>
    /// The arithmetic primitives of the original proximity ("collision") system, ported 1:1 from
    /// <c>WORLD/ZONE/PROXIM.C</c> (<c>proximity_polygon_contains</c>, <c>proximity_interp_height</c>,
    /// <c>proximity_vec2_long_rotate_q14</c>) and <c>R3D/CORE/DISTDIR.ASM</c>
    /// (<c>distdir_octagonal_distance</c>). IDA: <c>testCollisionInWorldItemList</c> @0x29b52.
    /// See docs/specs/collision-system.md §2.3 / §2.4 / §1.
    ///
    /// <para>All of it is integer maths on the values the extractor already surfaces
    /// (<see cref="GidSubedge"/> = the original's <c>ProximityVertex</c>, <see cref="GidRegion"/> =
    /// <c>ProximityPolygon</c>, <see cref="GidSlopePlane"/> = the polygon's reference vertex), so it
    /// stays free of UnityEngine and is unit-testable on its own.</para>
    /// </summary>
    public static class ProximityMath {
        /// <summary>Fixed-point fraction the original's sin/cos tables use (Q14).</summary>
        private const int Q14 = 1 << 14;

        /// <summary>
        /// Is <paramref name="px"/>/<paramref name="py"/> (model space) inside this convex region?
        /// Every edge must report <c>side &lt;= 0</c>. The sign shortcut is the original's: when the
        /// signs of both deltas agree with the edge normal's the result is forced to ±1 rather than
        /// multiplied out, so only the genuinely ambiguous case pays for the 32-bit dot product.
        /// </summary>
        public static bool RegionContains(GidRegion region, int px, int py) {
            var edges = region.Subedges;
            int count = edges.Count;
            if (count == 0) {
                return false;
            }

            for (int i = 0; i < count; i++) {
                GidSubedge v = edges[i];
                long side = 0;
                int cy = v.Dy;
                // The original holds the deltas in 16-bit ints; keep the truncation so a wrap here
                // behaves the same way it did in the DOS build.
                int dy = (short)(py - v.AnchorY);
                if (cy != 0 && dy != 0) {
                    bool agreeY = 0 < cy == 0 < dy;
                    int cx = v.Dx;
                    int dx = (short)(px - v.AnchorX);
                    if (cx != 0 && dx != 0) {
                        bool agreeX = 0 < cx == 0 < dx;
                        side = agreeX == agreeY
                            ? agreeX ? 1 : -1
                            : (long)cy * dy + (long)cx * dx;
                    } else {
                        side = agreeY ? 1 : -1;
                    }
                } else {
                    int cx = v.Dx;
                    int dx = (short)(px - v.AnchorX);
                    if (cx != 0 && dx != 0) {
                        side = 0 < cx == 0 < dx ? 1 : -1;
                    }
                }

                if (side > 0) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Ground height inside a region, in the model's own units. Flat regions are just their
        /// <see cref="GidRegion.BaseElevation"/>; sloped ones add a linear gradient measured from the
        /// region's reference vertex (<see cref="GidRegion.Slope"/>).
        /// </summary>
        public static int RegionHeight(GidRegion region, int px, int py) {
            GidSlopePlane slope = region.Slope;
            int accum = 0;
            if (slope != null) {
                if (slope.A != 0) {
                    accum = slope.A * (short)(slope.AnchorX - px);
                }
                if (slope.B != 0) {
                    accum += slope.B * (short)(slope.AnchorY - py);
                }
            }

            // `accum * (unsigned long)shift` wrapped mod 2^32 in the original before the arithmetic
            // shift; reproduce that rather than widening to 64-bit.
            int height = accum != 0
                ? unchecked((int)((uint)accum * region.SlopeShift)) >> 12
                : 0;
            return region.BaseElevation + height;
        }

        /// <summary>
        /// The engine's 2-D distance approximation: <c>max(|dx|,|dy|) + 3*min/8</c>. Octagonal, not
        /// Euclidean, and deliberately ignores z.
        /// </summary>
        public static long OctagonalDistance(long dx, long dy) {
            long ax = dx < 0 ? -dx : dx;
            long ay = dy < 0 ? -dy : dy;
            long max = ax >= ay ? ax : ay;
            long min = ax >= ay ? ay : ax;
            return max + (3 * min >> 3);
        }

        /// <summary>
        /// Rotate a model-space vector by <paramref name="angle"/> (BaK angle space, 65536 = 360°)
        /// in Q14 fixed point, matching <c>proximity_vec2_long_rotate_q14</c>. Angle 0 is a no-op,
        /// exactly as in the original — which is why unrotated records cost nothing.
        /// </summary>
        public static (int x, int y) Rotate(int x, int y, int angle) {
            if (angle == 0) {
                return (x, y);
            }

            double theta = angle / 65536.0 * 2.0 * Math.PI;
            long cos = (long)Math.Round(Math.Cos(theta) * Q14);
            long sin = (long)Math.Round(Math.Sin(theta) * Q14);
            long xp = (x * cos + y * -sin) >> 14;
            long yp = (x * sin + y * cos) >> 14;
            return ((int)xp, (int)yp);
        }
    }
}

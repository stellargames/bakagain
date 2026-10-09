namespace BakAgain.Tests.Editor.World {
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// Pins the cross-entity paint order derived from DrawPriority ("8=ground/base, 7=road/path,
    /// 6=river, 0=non-terrain object; higher = painted first/under"). This is the only mechanism
    /// ordering one GameObject against another — the shaders' _PaintBias ranks faces WITHIN a
    /// single model and cannot separate an entity from the terrain beneath it.
    /// </summary>
    public class LayeredTerrainMaterialTests {
        private static WorldEntityRenderContext NewContext() =>
            WorldEntityRenderContext.Create(new Color[256], new ClassicRenderProfile());

        private static Material MatFor(WorldEntityRenderContext ctx, byte drawPriority) =>
            WorldMeshObjects.GetLayeredTerrainMaterial(TerrainPen.Ground, drawPriority, ctx);

        [Test]
        public void Paint_order_follows_draw_priority_descending() {
            var ctx = NewContext();

            int ground = MatFor(ctx, 8).renderQueue;
            int road = MatFor(ctx, 7).renderQueue;
            int river = MatFor(ctx, 6).renderQueue;
            int obj = MatFor(ctx, 0).renderQueue;

            Assert.Less(ground, road, "road paints over ground");
            Assert.Less(road, river, "river paints over road");
            // The bug this fixes: DrawPriority 0 fell into the same layer as ground with no depth
            // bias, so geometry coplanar with the terrain under it had nothing breaking the tie —
            // the waterfall base, which shares exact planes at Y=0/12/24 with its neighbours.
            Assert.Less(river, obj, "DrawPriority 0 paints over all terrain");

            ctx.Dispose();
        }

        [Test]
        public void Terrain_overlays_carry_both_depth_bias_terms() {
            var ctx = NewContext();

            var groundMat = MatFor(ctx, 8);
            Assert.AreEqual(0f, groundMat.GetFloat("_OffsetUnits"), "base layer takes no bias");
            Assert.AreEqual(0f, groundMat.GetFloat("_OffsetFactor"), "base layer takes no bias");
            // _OffsetFactor scales with the polygon's depth slope and is what separates large
            // near-coplanar planes at grazing angles (a river sheet over ground). Removing it made
            // rivers z-fight the ground immediately — verified in-game 2026-07-20. Do not drop it.
            foreach (byte dp in new byte[] { 7, 6 }) {
                var m = MatFor(ctx, dp);
                Assert.Less(m.GetFloat("_OffsetUnits"), 0f, $"DrawPriority {dp}: constant bias toward the camera");
                Assert.Less(m.GetFloat("_OffsetFactor"), 0f, $"DrawPriority {dp}: slope-scaled term is required");
            }

            ctx.Dispose();
        }

        [Test]
        public void Objects_take_the_constant_bias_but_no_slope_scaled_term() {
            // A slope-scaled offset is per POLYGON: a wall seen edge-on has a huge depth slope and is
            // pulled far forward while the roof above it is not, so the gable pierced the roof
            // (the "tear in the roof", 2026-10-09). Objects keep the constant term, which moves a
            // whole model uniformly and still puts DrawPriority 0 over the river layer.
            var ctx = NewContext();

            var objMat = MatFor(ctx, 0);
            var riverMat = MatFor(ctx, 6);
            Assert.AreEqual(0f, objMat.GetFloat("_OffsetFactor"), "no slope-scaled term on objects");
            Assert.Less(objMat.GetFloat("_OffsetUnits"), riverMat.GetFloat("_OffsetUnits"),
                "objects still bias toward the camera past the river layer");

            ctx.Dispose();
        }
    }
}

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
        public void Overlay_layers_carry_both_depth_bias_terms() {
            var ctx = NewContext();

            var groundMat = MatFor(ctx, 8);
            var objMat = MatFor(ctx, 0);

            Assert.AreEqual(0f, groundMat.GetFloat("_OffsetUnits"), "base layer takes no bias");
            Assert.Less(objMat.GetFloat("_OffsetUnits"), 0f, "constant bias toward the camera");
            // _OffsetFactor scales with the polygon's depth slope and is what separates large
            // near-coplanar planes at grazing angles (a river sheet over ground). Removing it made
            // rivers z-fight the ground immediately — verified in-game 2026-07-20. Do not drop it.
            Assert.Less(objMat.GetFloat("_OffsetFactor"), 0f, "slope-scaled term is required");

            ctx.Dispose();
        }
    }
}

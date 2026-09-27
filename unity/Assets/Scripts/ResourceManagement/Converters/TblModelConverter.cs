namespace BakAgain.ResourceManagement.Converters {
    using BakAgain.ResourceManagement.Models;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.World;
    using UnityEngine;

    /// <summary>
    /// Builds the visual <see cref="GameObject"/> for a static polygon entity from its TBL data: a
    /// disabled template (meshes + Classic materials, all baked) at the
    /// origin. The world builder instantiates the template per placement. Metadata is assembled
    /// separately via <see cref="WorldModelMetadata.FromEntry"/> so that Phase 2's glTF path can
    /// supply a different visual while reusing the same metadata construction.
    /// </summary>
    public static class TblModelConverter {
        public static async UniTask<GameObject> BuildVisualAsync(ZoneTableEntry entry, WorldEntityRenderContext ctx) {
            var dat = entry.Dat;
            // Depth-sorted models currently render identically to terrain (plain per-pen z-buffered
            // emit) — the depth-sort branch is kept as the hook where a painter's-order-faithful
            // coplanar fix will plug in. Texturing is a per-face property either way, so landscape
            // models (depth-sorted) keep their ground textures.
            var meshData = dat.IsDepthSorted
                ? TblMeshConverter.ConvertDepthSortedEntity(dat, ctx.Palette, entry.Name, ctx.MapPalette)
                : TblMeshConverter.ConvertTerrainEntity(dat, ctx.Palette, ctx.MapPalette);
            var mesh = TblMeshConverter.CreateTerrainMesh(meshData, entry.Name);
            ctx.TrackMesh(mesh);
            // Preload slot-bitmap textures (async) between the sync mesh conversion and the sync
            // material assignment, so BuildTerrain can pull each texture from ctx.SlotTextures.
            await ctx.EnsureSlotTexturesAsync(meshData.Keys);
            GameObject visual = WorldMeshObjects.BuildTerrain(
                entry.Name, mesh, meshData.Keys, dat.DrawPriority, ctx, parent: null);
            visual.SetActive(false); // template; instantiated per placement
            return visual;
        }
    }
}

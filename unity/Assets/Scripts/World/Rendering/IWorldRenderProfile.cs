namespace BakAgain.World.Rendering {
    using BakAgain.World.Converters;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Rendering;

    public enum WorldRenderMode { Classic, Enhanced }

    /// <summary>
    /// Provides all mode-specific rendering configuration for the world scene.
    /// Created once at zone load based on the player's selected mode.
    /// </summary>
    public interface IWorldRenderProfile {
        WorldRenderMode Mode { get; }

        // Note: the material factories take no fog arguments. Fog is applied by URP per fragment
        // (ComputeFogFactor/MixFog) from RenderSettings, configured once via ConfigureFog — not
        // through per-material uniforms.

        /// <summary>Create per-pen materials for terrain sub-meshes from pre-built pen textures.</summary>
        Dictionary<TerrainPen, Material> CreateTerrainMaterials(
            Dictionary<TerrainPen, Texture2D> penTextures);

        /// <summary>Create material for polygon entity meshes.</summary>
        Material CreatePolygonMaterial();

        /// <summary>Create material for a slot-bitmap textured world-model quad (one bitmap stretched
        /// across the quad, modulated by vertex colour).</summary>
        Material CreateSlotMaterial(Texture2D bitmap);

        /// <summary>Create material for a billboard sprite.</summary>
        Material CreateSpriteMaterial(Texture2D spriteTexture);

        /// <summary>Configure the sky for this zone.</summary>
        void ConfigureSky(GameObject skyContainer, Texture2D horizonTexture);

        /// <summary>Configure the world fog out to <paramref name="fogMaxDistance"/>, and the sprites'
        /// own haze from <paramref name="spriteFogStart"/> to <paramref name="spriteFogEnd"/> (Unity
        /// units) — the original fades only sprites with distance (TASK-434).</summary>
        void ConfigureFog(Color fogColor, float fogMaxDistance, float spriteFogStart, float spriteFogEnd);

        /// <summary>Configure lighting.</summary>
        void ConfigureLighting(GameObject environmentContainer, Color ambientColor);
    }
}

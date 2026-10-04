namespace BakAgain.World.Rendering {
    using BakAgain.World.Converters;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Rendering;

    public class ClassicRenderProfile : IWorldRenderProfile {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int TexScaleId = Shader.PropertyToID("_TexScale");
        private static readonly int ColorBlendId = Shader.PropertyToID("_ColorBlend");
        private static readonly int PaintBiasId = Shader.PropertyToID("_PaintBias");
        /// <summary>Depth bias per paint-order rank for the coplanar tie-break on depth-sorted models.
        /// The shader applies it as NDC offset = rank × bias / w², i.e. a constant eye-space depth
        /// offset of bias/nearClip world units per rank (near 0.1 → 5e-4 u = 0.05 BaK units). Sized for
        /// the worst real plane group: a timbered gable wall carries ~30 coplanar faces, so worst-case
        /// cross-surface bias is ~1.5 BaK units — below any real inter-surface gap (an earlier 100×
        /// larger value let high-rank gable faces bleed over the adjacent roof edge), while the per-rank
        /// nudge stays ≥ ~6 float-depth ulps out to fog range (~640 u) on a D32F reversed-Z buffer, so
        /// coplanar ties still resolve. (On a D24 fixed-point buffer the tie-break quantizes away beyond
        /// ~30 u — acceptable; desktop targets use D32F.) Shared by both render profiles.</summary>
        public const float CoplanarPaintBias = 5e-5f;

        private readonly Shader _terrainShader;
        private readonly Shader _polygonShader;
        private readonly Shader _spriteShader;
        private readonly Shader _slotShader;

        public WorldRenderMode Mode => WorldRenderMode.Classic;

        public ClassicRenderProfile() {
            _terrainShader = Shader.Find("BakAgain/ClassicTerrain");
            _polygonShader = Shader.Find("BakAgain/ClassicPolygon");
            _spriteShader = Shader.Find("BakAgain/ClassicSprite");
            _slotShader = Shader.Find("BakAgain/ClassicTexturedPolygon");
        }

        public Dictionary<TerrainPen, Material> CreateTerrainMaterials(
            Dictionary<TerrainPen, Texture2D> penTextures) {
            var materials = new Dictionary<TerrainPen, Material>();

            // FlatFill pen — uses ClassicPolygon (vertex color only, no texture)
            materials[TerrainPen.FlatFill] = CreatePolygonMaterial();

            foreach (var kvp in penTextures) {
                var mat = new Material(_terrainShader);
                mat.SetTexture(MainTexId, kvp.Value);
                mat.SetFloat(TexScaleId, GetTexScale(kvp.Key));
                mat.SetFloat(ColorBlendId, GetColorBlend(kvp.Key));
                // Paint-order tie-break for pen-textured faces on depth-sorted models (fall1's
                // River/Waterfall stream). Inert on terrain tiles (their UV1 ranks are all 0).
                mat.SetFloat(PaintBiasId, CoplanarPaintBias);
                mat.name = $"Terrain_{kvp.Key}";
                materials[kvp.Key] = mat;
            }

            return materials;
        }

        /// <summary>World-space tile size per pen (larger = coarser tiling).</summary>
        private static float GetTexScale(TerrainPen pen) {
            switch (pen) {
                case TerrainPen.River:    return 8f;
                case TerrainPen.Waterfall:return 6f;
                case TerrainPen.Road:     return 12f;
                case TerrainPen.Path:     return 14f;
                case TerrainPen.Dirt:     return 12f;
                case TerrainPen.Horizon1: return 40f;
                case TerrainPen.Horizon2: return 40f;
                default:                  return 100f; // Ground, GroundLod — large tiles = chunky/pixelated like the original
            }
        }

        /// <summary>How much vertex color tints the texture (0 = texture only, 1 = full multiply).</summary>
        private static float GetColorBlend(TerrainPen pen) {
            switch (pen) {
                case TerrainPen.Horizon1: return 0.0f;
                case TerrainPen.Horizon2: return 0.0f;
                default:                  return 0.3f;
            }
        }

        public Material CreatePolygonMaterial() {
            var mat = new Material(_polygonShader);
            mat.SetFloat(PaintBiasId, CoplanarPaintBias);
            return mat;
        }

        public Material CreateSlotMaterial(Texture2D bitmap) {
            var mat = new Material(_slotShader);
            mat.SetTexture(MainTexId, bitmap);
            mat.SetFloat(PaintBiasId, CoplanarPaintBias);
            mat.name = $"SlotMat_{bitmap.name}";
            return mat;
        }

        public Material CreateSpriteMaterial(Texture2D spriteTexture) {
            var mat = new Material(_spriteShader);
            mat.SetTexture(MainTexId, spriteTexture);
            return mat;
        }

        public void ConfigureSky(GameObject skyContainer, Texture2D horizonTexture) {
            // HorizonRenderer is added by ZoneSceneBuilder; this just disables Unity skybox
            RenderSettings.skybox = null;
        }

        public void ConfigureFog(Color fogColor, float fogMaxDistance, float spriteFogStart,
            float spriteFogEnd) {
            // Distance-based Unity (URP) fog. Replaces the old per-vertex shader fog, whose small
            // _FogMaxDistance saturated across whole objects and tinted their entire surface.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogMaxDistance * 0.5f;
            RenderSettings.fogEndDistance = fogMaxDistance;
            // Sprites fade on the zone's own range; terrain and polygons keep the wide fog above,
            // because the original's distance remap reaches only sprites.
            Shader.SetGlobalFloat(SpriteFogStartId, spriteFogStart);
            Shader.SetGlobalFloat(SpriteFogEndId, spriteFogEnd);
            ApplySpriteNearFade();
        }

        /// <summary>The sprite near fade, in Unity units — <see cref="GameData.Resources.World.SpriteNearFade"/>.</summary>
        internal static void ApplySpriteNearFade() {
            Shader.SetGlobalFloat(SpriteNearHideId,
                GameData.Resources.World.SpriteNearFade.HiddenWithin / BakAgain.World.Converters.BakCoordinateConverter.WorldScale);
            Shader.SetGlobalFloat(SpriteNearShowId,
                GameData.Resources.World.SpriteNearFade.ShownFrom / BakAgain.World.Converters.BakCoordinateConverter.WorldScale);
        }

        internal static readonly int SpriteNearHideId = Shader.PropertyToID("_BakSpriteNearHide");
        internal static readonly int SpriteNearShowId = Shader.PropertyToID("_BakSpriteNearShow");

        internal static readonly int SpriteFogStartId = Shader.PropertyToID("_BakSpriteFogStart");
        internal static readonly int SpriteFogEndId = Shader.PropertyToID("_BakSpriteFogEnd");

        public void ConfigureLighting(GameObject environmentContainer, Color ambientColor) {
            // Classic: ambient light only, no directional
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
        }
    }
}

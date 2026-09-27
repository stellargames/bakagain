namespace BakAgain.World.Rendering {
    using BakAgain.World.Converters;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// Enhanced render profile using URP Lit materials, Unity skybox, and Unity fog.
    /// Terrain uses standard URP Lit with per-pen textures.
    /// Other entities use standard URP Lit with vertex colors.
    /// </summary>
    public class EnhancedRenderProfile : IWorldRenderProfile {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        public WorldRenderMode Mode => WorldRenderMode.Enhanced;

        private readonly Material _defaultSkybox;

        public EnhancedRenderProfile() {
            // Load default enhanced skybox (shipped with project)
            _defaultSkybox = Resources.Load<Material>("World/Enhanced/Sky/DefaultSkybox");
        }

        public Dictionary<TerrainPen, Material> CreateTerrainMaterials(
            Dictionary<TerrainPen, Texture2D> penTextures) {
            var materials = new Dictionary<TerrainPen, Material>();
            materials[TerrainPen.FlatFill] = CreatePolygonMaterial();
            foreach (var kvp in penTextures) {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetTexture(BaseMap, kvp.Value);
                mat.name = $"EnhTerrain_{kvp.Key}";
                materials[kvp.Key] = mat;
            }
            return materials;
        }

        public Material CreatePolygonMaterial() {
            // Use the Classic vertex-colour polygon shader: stock URP/Lit ignores mesh COLOR, which
            // dropped the per-face vertex colours (the grey-blob house bug). ClassicPolygon reads COLOR.
            var mat = new Material(Shader.Find("BakAgain/ClassicPolygon"));
            mat.SetFloat(Shader.PropertyToID("_PaintBias"), ClassicRenderProfile.CoplanarPaintBias);
            return mat;
        }

        public Material CreateSlotMaterial(Texture2D bitmap) {
            var mat = new Material(Shader.Find("BakAgain/ClassicTexturedPolygon"));
            mat.SetTexture(MainTex, bitmap);
            mat.SetFloat(Shader.PropertyToID("_PaintBias"), ClassicRenderProfile.CoplanarPaintBias);
            mat.name = $"EnhSlotMat_{bitmap.name}";
            return mat;
        }

        public Material CreateSpriteMaterial(Texture2D spriteTexture) {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            mat.SetTexture(BaseMap, spriteTexture);
            mat.SetFloat(Cutoff, 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            return mat;
        }

        public void ConfigureSky(GameObject skyContainer, Texture2D horizonTexture) {
            // Enhanced mode uses Unity skybox, no horizon quad
            if (_defaultSkybox != null)
                RenderSettings.skybox = _defaultSkybox;
        }

        public void ConfigureFog(Color fogColor, float fogMaxDistance, float spriteFogStart,
            float spriteFogEnd) {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogMaxDistance * 0.3f;
            RenderSettings.fogEndDistance = fogMaxDistance;
        }

        public void ConfigureLighting(GameObject environmentContainer, Color ambientColor) {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor * 0.5f;

            // Create directional light for sun
            var lightGo = new GameObject("DirectionalLight");
            lightGo.transform.SetParent(environmentContainer.transform);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.9f);
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
        }
    }
}

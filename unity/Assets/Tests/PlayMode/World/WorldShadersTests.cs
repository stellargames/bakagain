namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The world's materials follow the Enhanced master switch live: Classic shaders while it is off,
    /// their Enhanced twins while it is on, swapped in place (owner, 2026-10-19).
    /// </summary>
    public class WorldShadersTests {
        private EnhancedPrefsStash _stash;
        private Material _material;
        private Material _copy;
        private Texture2D _texture;

        [SetUp]
        public void SetUp() => _stash = EnhancedPrefsStash.Take();

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_material);
            Object.DestroyImmediate(_copy);
            Object.DestroyImmediate(_texture);
            _stash.Restore();
            WorldShaders.Follow(GameOptions.Enhanced);
        }

        [Test]
        public void AMaterialIsMadeOnTheCurrentModesShader() {
            GameOptions.Enhanced = false;
            _material = WorldShaders.Create(WorldShaders.Surface.Sprite);
            Assert.AreEqual("BakAgain/ClassicSprite", _material.shader.name);

            GameOptions.Enhanced = true;
            _copy = WorldShaders.Create(WorldShaders.Surface.Sprite);
            Assert.AreEqual("BakAgain/EnhancedSprite", _copy.shader.name);
        }

        [Test]
        public void TheSwitchSwapsLiveMaterialsAndKeepsWhatTheyCarry() {
            GameOptions.Enhanced = false;
            _texture = new Texture2D(2, 2);
            _material = WorldShaders.Create(WorldShaders.Surface.Terrain);
            _material.SetTexture("_MainTex", _texture);
            _material.SetFloat("_TexScale", 12f);
            // A layered terrain copy with its own queue (WorldMeshObjects.GetLayeredTerrainMaterial).
            _copy = WorldShaders.Track(new Material(_material) { renderQueue = 2003 });

            WorldShaders.Follow(true);

            Assert.AreEqual("BakAgain/EnhancedTerrain", _material.shader.name);
            Assert.AreEqual("BakAgain/EnhancedTerrain", _copy.shader.name);
            Assert.AreSame(_texture, _material.GetTexture("_MainTex"));
            Assert.AreEqual(12f, _material.GetFloat("_TexScale"));
            Assert.AreEqual(2003, _copy.renderQueue);

            WorldShaders.Follow(false);

            Assert.AreEqual("BakAgain/ClassicTerrain", _material.shader.name);
            Assert.AreEqual(2003, _copy.renderQueue);
        }

        [Test]
        public void EveryEnhancedTwinIsAlwaysIncludedInBuilds() {
            // Shader.Find only finds a shader in a player build when it is referenced or always included.
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var list = new UnityEditor.SerializedObject(settings).FindProperty("m_AlwaysIncludedShaders");
            var included = new System.Collections.Generic.HashSet<Object>();
            for (int i = 0; i < list.arraySize; i++) included.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (WorldShaders.Surface s in System.Enum.GetValues(typeof(WorldShaders.Surface))) {
                foreach (string mode in new[] { "Classic", "Enhanced" }) {
                    Assert.IsTrue(included.Contains(Shader.Find($"BakAgain/{mode}{s}")), $"{mode}{s}");
                }
            }
        }
    }
}

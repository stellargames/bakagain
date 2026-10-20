namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Dialog;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// A portrait is requested as <c>ACT001A.BMX#0@INVENTOR.PAL</c> (the host palette it is drawn
    /// against, <see cref="GameData.PaletteMapping.WithHostPalette"/>). A mod's replacement is one
    /// PNG whatever the host, so the suffix must not stop the lookup.
    /// </summary>
    public class HostPaletteOverrideTests {
        private TempOverrideDirectory _overrides;

        [SetUp]
        public void SetUp() => _overrides = new TempOverrideDirectory();

        [TearDown]
        public void TearDown() => _overrides.Dispose();

        [Test]
        public void Locate_HostPaletteKey_FindsOverride() {
            string file = _overrides.Write(Path.Combine("BMX", "ACTOR1"), "0.png", "png");
            string key = GameData.PaletteMapping.WithHostPalette("ACTOR1.BMX#0", "OPTIONS");

            bool found = new OverrideResourceLocator().Locate(key, typeof(Sprite), out IList<IResourceLocation> locations);

            Assert.IsTrue(found, key);
            Assert.AreEqual(file, locations[0].InternalId);
            Assert.AreEqual(key, locations[0].PrimaryKey, "the location keeps the key that was asked for");
        }

        /// <summary>The character sheet's own path: its portrait key, loaded through Addressables and the provider.</summary>
        [UnityTest, RequiresShippedGameData]
        public IEnumerator CharacterSheetPortrait_IsTheModsPicture() => UniTask.ToCoroutine(async () => {
            string bitmap = ActorFaceCache.BitmapNameFor(1, CharacterSheetLayout.PortraitIsAlternate);
            var png = new Texture2D(3, 5);
            byte[] bytes = png.EncodeToPNG();
            Object.DestroyImmediate(png);
            File.WriteAllBytes(_overrides.Write(Path.Combine("BMX", Path.GetFileNameWithoutExtension(bitmap)), "0.png", ""), bytes);
            await Addressables.InitializeAsync().ToUniTask();
            var cache = new ResourceCache();
            using (new IsolatedResourceLocators()) {
                ResourceManagementInitializer.InitializeResourceManagement();
                var face = new VisualElement();
                try {
                    Assert.IsTrue(await ActorFaceView.ApplyAsync(face, 1, cache, CharacterSheetLayout.PortraitIsAlternate,
                        sizeToSprite: true, hostPalette: CharacterSheetLayout.ScreenPalette));
                    Assert.AreEqual((3f, 5f), (face.style.width.value.value, face.style.height.value.value), "the mod's 3x5 picture");
                } finally {
                    cache.Dispose();
                }
            }
        });
    }
}

namespace BakAgain.Tests.Editor.ResourceManagement {
    using BakAgain.Audio;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.TestTools;
    using Object = UnityEngine.Object;

    /// <summary>
    /// The built-in Enhanced and audio assets load through Addressables, so a mod's
    /// <c>Resources/&lt;address&gt;.&lt;ext&gt;</c> replaces them (mod system spec, built-in assets).
    /// Each test registers the locators the way the game does, through
    /// <see cref="ResourceManagementInitializer"/>, on top of the catalog's.
    /// </summary>
    [RequiresShippedGameData]
    public class BuiltInAssetOverrideTests {
        private TempOverrideDirectory _dev;
        private EnhancedPrefsStash _enhanced;
        private IsolatedResourceLocators _locators;

        [SetUp]
        public void SetUp() {
            _dev = new TempOverrideDirectory();
            _enhanced = EnhancedPrefsStash.Take();
            GameOptions.Enhanced = true;
            Addressables.InitializeAsync().WaitForCompletion(); // the catalog's locator, as at boot
            _locators = new IsolatedResourceLocators();
        }

        [TearDown]
        public void TearDown() {
            _locators.Dispose();
            _enhanced.Restore();
            _dev.Dispose();
        }

        /// <summary>An active mod holding <paramref name="file"/>; the resource system then starts as the game does.</summary>
        private void StartWithMod(string file, byte[] bytes) {
            string mod = Path.Combine(ModLayers.ModsRoot, "sky-pack");
            string path = Path.Combine(mod, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(Path.Combine(mod, "mod.json"), "{\"schema\":1,\"id\":\"sky-pack\",\"name\":\"Sky\",\"version\":\"1.0.0\"}");
            File.WriteAllBytes(path, bytes);
            ResourceManagementInitializer.InitializeResourceManagement();
        }

        private static T Load<T>(string address) where T : Object {
            return Addressables.LoadAssetAsync<T>(address).WaitForCompletion();
        }

        [TestCase("Enhanced/Sky/moon_large", typeof(Texture2D))]
        [TestCase("Enhanced/Stone/marble", typeof(Texture2D))]
        [TestCase("Fonts/Alegreya/Alegreya-Medium", typeof(Font))]
        [TestCase("BookSpread/table", typeof(Texture2D))]
        [TestCase("Audio/GeneralUser-GS", typeof(TextAsset))]
        public void BuiltInAddress_LoadsWithoutMods(string address, Type type) {
            ResourceManagementInitializer.InitializeResourceManagement();

            Object loaded = type == typeof(Font) ? Load<Font>(address)
                : type == typeof(TextAsset) ? Load<TextAsset>(address)
                : Load<Texture2D>(address);

            Assert.IsInstanceOf(type, loaded, address);
        }

        [Test]
        public void ModFile_OverridesBuiltInTexture() {
            var png = new Texture2D(3, 5);
            byte[] bytes = png.EncodeToPNG();
            Object.DestroyImmediate(png);
            StartWithMod(Path.Combine("Resources", "Enhanced", "Sky", "moon_large.png"), bytes);

            Texture2D moon = Load<Texture2D>("Enhanced/Sky/moon_large");

            Assert.IsNotNull(moon);
            Assert.AreEqual((3, 5), (moon.width, moon.height), "the mod's PNG, not the built-in moon");
        }

        [TestCase("Enhanced/Sky/clouds_cumulus")]
        [TestCase("Enhanced/Stone/marble")]
        public void ModTexture_SamplesLikeTheBuiltIn(string address) {
            var handle = Addressables.LoadAssetAsync<Texture2D>(address);
            Texture2D builtIn = handle.WaitForCompletion();
            var png = new Texture2D(4, 4);
            byte[] bytes = png.EncodeToPNG();
            Object.DestroyImmediate(png);
            StartWithMod(Path.Combine("Resources", address + ".png"), bytes);

            Texture2D mod = Load<Texture2D>(address);

            Assert.AreEqual(4, mod.width, "the mod's texture");
            Assert.AreEqual((builtIn.wrapModeU, builtIn.wrapModeV), (mod.wrapModeU, mod.wrapModeV), "wrap");
            Assert.AreEqual(builtIn.filterMode, mod.filterMode, "filter");
            Assert.AreEqual(builtIn.anisoLevel, mod.anisoLevel, "aniso");
            Assert.AreEqual(builtIn.isDataSRGB, mod.isDataSRGB, "colour space");
            Assert.Greater(mod.mipmapCount, 1, "mipmapped like the built-in");
            Addressables.Release(handle);
        }

#if UNITY_EDITOR
        /// <summary>The sampling a replacement gets is the built-in's import settings, for every built-in texture.</summary>
        [Test]
        public void BuiltInSamplingOf_MatchesEveryBuiltInImporter() {
            // t:Texture2D also finds a font's atlas: only texture files have a TextureImporter.
            UnityEditor.TextureImporter[] importers = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/BuiltIn" })
                .Select(guid => UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)) as UnityEditor.TextureImporter)
                .Where(importer => importer != null).ToArray();
            Assert.AreEqual(22, importers.Length, "the built-in textures");
            foreach (UnityEditor.TextureImporter importer in importers) {
                string path = importer.assetPath;
                string address = Path.ChangeExtension(path["Assets/BuiltIn/".Length..], null);
                (TextureWrapMode wrap, bool linear) = OverrideResourceProvider.BuiltInSamplingOf(address);
                Assert.AreEqual((FilterMode.Bilinear, true, 1, wrap, !linear),
                    (importer.filterMode, importer.mipmapEnabled, importer.anisoLevel, importer.wrapMode, importer.sRGBTexture), address);
            }
        }
#endif

        [Test]
        public void ModFile_OverridesSoundFontBytes() {
            byte[] bank = { 0x52, 0x49, 0x46, 0x46, 0, 1, 2, 3 };
            StartWithMod(Path.Combine("Resources", "Audio", "GeneralUser-GS.sf2"), bank);

            TextAsset loaded = Load<TextAsset>("Audio/GeneralUser-GS");

            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(bank, loaded.bytes);
        }

        private static readonly byte[] NotAFile = System.Text.Encoding.ASCII.GetBytes("not an image or a sound bank");

        /// <summary>Runs <paramref name="body"/> and returns the log messages containing <paramref name="mark"/>;
        /// Addressables' own logs of the failed load are not under test.</summary>
        private static async UniTask<List<string>> Logged(string mark, Func<UniTask> body) {
            var logged = new List<string>();
            void Collect(string message, string stack, LogType type) {
                if (message.Contains(mark)) logged.Add(message);
            }
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += Collect;
            try {
                await body();
            } finally {
                Application.logMessageReceived -= Collect;
                LogAssert.ignoreFailingMessages = false;
            }
            return logged;
        }

        [UnityTest]
        public IEnumerator BrokenStoneMod_PreloadCompletesAndDrawsNothingForIt() => UniTask.ToCoroutine(async () => {
            var loaded = (Dictionary<string, Texture2D>)typeof(EnhancedArt)
                .GetField("Loaded", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var saved = new Dictionary<string, Texture2D>(loaded);
            loaded.Clear();
            try {
                StartWithMod(Path.Combine("Resources", "Enhanced", "Stone", "marble.png"), NotAFile);

                List<string> errors = await Logged("Enhanced art", EnhancedArt.PreloadAsync);

                // Once per preload; a focused run's scene boots the game, whose own preload can see the mod too.
                Assert.IsNotEmpty(errors);
                Assert.IsTrue(errors.All(e => e.Contains("'Enhanced/Stone/marble'")), string.Join("\n", errors));
                Assert.IsNull(EnhancedArt.Texture("marble"), "the broken one is drawn as nothing");
                Assert.IsNotNull(EnhancedArt.Texture("slab"), "the others still load");
                Assert.IsNotNull(EnhancedArt.Texture("parchment"));
            } finally {
                loaded.Clear();
                foreach (var kv in saved) loaded[kv.Key] = kv.Value;
            }
        });

        [UnityTest]
        public IEnumerator CorruptSoundFontMod_LogsOnceAndPlaysWithoutMusic() => UniTask.ToCoroutine(async () => {
            FieldInfo bank = typeof(MidiSynthPlayer).GetField("_soundFont", BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo unusable = typeof(MidiSynthPlayer).GetField("_soundFontUnusable", BindingFlags.NonPublic | BindingFlags.Static);
            object savedBank = bank.GetValue(null);
            bank.SetValue(null, null);
            unusable.SetValue(null, false);
            var host = new GameObject("MidiSynthPlayerUnderTest", typeof(AudioSource));
            try {
                StartWithMod(Path.Combine("Resources", "Audio", "GeneralUser-GS.sf2"), NotAFile);
                // The smallest Standard MIDI File: a header and one empty track.
                byte[] smf = {
                    0x4D, 0x54, 0x68, 0x64, 0, 0, 0, 6, 0, 0, 0, 1, 0, 0x60,
                    0x4D, 0x54, 0x72, 0x6B, 0, 0, 0, 4, 0, 0xFF, 0x2F, 0,
                };

                List<string> errors = await Logged("Sound bank", async () => {
                    await MidiSynthPlayer.PreloadSoundFontAsync();
                    MidiSynthPlayer player = host.AddComponent<MidiSynthPlayer>();
                    for (int song = 0; song < 2; song++) {
                        Assert.DoesNotThrow(() => player.Load(smf));
                        Assert.DoesNotThrow(() => player.Play(loop: false));
                        Assert.IsFalse(player.IsPlaying);
                    }
                });

                Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            } finally {
                Object.DestroyImmediate(host);
                bank.SetValue(null, savedBank);
                unusable.SetValue(null, false);
            }
        });

        [Test]
        public void NoResourcesLoadLeft() {
            string scripts = Path.Combine(Application.dataPath, "Scripts");
            string[] users = Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains("Resources.Load"))
                .Where(f => !f.Contains("SimpleFileBrowser"))
                .ToArray();

            CollectionAssert.IsEmpty(users, "a Resources.Load bypasses the mod layers: load through Addressables");
        }
    }
}

namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.ResourceManagement;
    using BakAgain.Tests.Editor.Core.Fixtures;
    using BakAgain.Tests.TestSupport;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Rendering;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Image;
    using GameData.Resources.World;
    using NUnit.Framework;
    using ResourceExtraction;
    using ResourceExtraction.Extractors;
    using System;
    using System.Collections;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Object = UnityEngine.Object;

    /// <summary>
    /// A mod's HD picture replaces a world billboard's texture, never its size: the billboard is still
    /// sized and anchored from the native frame (mod system spec 6, phase 1).
    /// </summary>
    [RequiresShippedGameData]
    public class HdFrameBillboardTests {
        [UnityTest]
        public IEnumerator HdFrame_BillboardKeepsNativeSize() => UniTask.ToCoroutine(async () => {
            var provider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            var table = provider.GetResource<ZoneTable>("Z01.TBL");
            ZoneTableExtractor.StampTextureKeys(table, "Z01.TBL", provider);
            ZoneTableEntry entry = table.Entries.First(e => e.Dat != null
                && TblMeshConverter.FindSpriteBFace(e.Dat) is { TextureBitmap: { Length: > 0 } }
                && WorldEntityClassifier.Classify(e.Dat.IsUnbounded, e.Dat.IsDepthSorted, e.Dat.EntityType,
                    TblMeshConverter.HasPolygonGeometry(e.Dat), true) == WorldEntityKind.Sprite);
            string key = TblMeshConverter.FindSpriteBFace(entry.Dat).TextureBitmap;   // Z01SLOT<n>.BMX#<i>
            int hash = key.IndexOf('#');
            string setName = key[..hash];
            int index = int.Parse(key[(hash + 1)..]);
            var set = provider.GetResource<ImageSet>(setName);
            var resources = new FakeResourceProviderService();
            resources.Register(setName, set);

            using var mods = new TempOverrideDirectory();
            HdFrames.ClearCache();
            var root = new GameObject("HdFrameBillboardTest");
            try {
                (Bounds plain, Texture plainTex) = await BuildAsync(entry, resources, root.transform);

                var hd = new Texture2D(set.Images[index].Width * 2, set.Images[index].Height * 2, TextureFormat.RGBA32, false);
                var px = new Color32[hd.width * hd.height];
                Array.Fill(px, new Color32(255, 0, 255, 255));
                hd.SetPixels32(px);
                string dir = Path.Combine(mods.Root, "BMX", Path.GetFileNameWithoutExtension(setName));
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"{index}.png"), hd.EncodeToPNG());
                Object.DestroyImmediate(hd);
                HdFrames.ClearCache();   // the plain build cached "no HD frame" for this key

                (Bounds withHd, Texture hdTex) = await BuildAsync(entry, resources, root.transform);

                Assert.AreEqual(set.Images[index].Width * 2, hdTex.width, "the billboard draws the mod's picture");
                Assert.AreNotSame(plainTex, hdTex);
                Assert.That(Vector3.Distance(plain.size, withHd.size), Is.LessThan(1e-4f), $"{plain} vs {withHd}");
                Assert.That(Vector3.Distance(plain.center, withHd.center), Is.LessThan(1e-4f), $"{plain} vs {withHd}");
            } finally {
                Object.DestroyImmediate(root);
                HdFrames.ClearCache();
            }
        });

        private static async UniTask<(Bounds, Texture)> BuildAsync(ZoneTableEntry entry,
            FakeResourceProviderService resources, Transform parent) {
            var ctx = WorldEntityRenderContext.Create(new Color[256], new WorldRenderProfile());
            ctx.ResourceProvider = resources;
            try {
                GameObject go = await WorldEntityBuilder.Build(entry, entry.Index, Vector3.zero, Quaternion.identity,
                    parent, parent, ctx, null);
                var renderer = go.GetComponent<MeshRenderer>();
                return (renderer.bounds, renderer.sharedMaterial.mainTexture);
            } finally {
                ctx.Dispose();
            }
        }
    }
}

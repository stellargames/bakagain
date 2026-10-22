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
    using System.Collections;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Two billboards on one spot draw one over the other, whole, from every heading (TASK-942).
    /// Zone 1's WLDs stand tree1 (134) and tree2 (136) on the very same position ten times in the
    /// start tile alone (T011016 items 22 and 35 at 669600,1039200). The sprite shader turns both quads
    /// into the one plane through their shared pivot, so the depth test settled each pixel by rounding
    /// noise: the darker and lighter tree swapped patches while the view turned. The original paints
    /// its sprites far to near with no depth buffer (VISLIST.C vislist_sort, WORLDFRM.C
    /// worldframe_render_chapter), so one tree lies wholly over the other.
    /// </summary>
    [RequiresShippedGameData]
    [Category("Graphics")]
    public class CoLocatedBillboardTests {
        private const int Size = 256;
        // Where the start of chapter 1 puts them, a little north of the pair and looking south:
        // world coordinates this large are what leaves the rounding noise to fight over.
        private static readonly Vector3 Spot = new(6696f, 0f, 10392f);
        private static readonly Vector3 Eye = new(6696f, 2.76f, 10417f);

        [UnityTest]
        public IEnumerator TheLaterOfTwoTreesOnOneSpotCoversTheEarlierAtEveryHeading() => UniTask.ToCoroutine(async () => {
            var provider = ResourceProviderFactory.CreateResourceProvider(BakResourceSettings.GamePath);
            var table = provider.GetResource<ZoneTable>("Z01.TBL");
            ZoneTableExtractor.StampTextureKeys(table, "Z01.TBL", provider);
            var resources = new FakeResourceProviderService();
            foreach (int type in new[] { 134, 136 }) {
                string key = TblMeshConverter.FindSpriteBFace(table.Entries[type].Dat).TextureBitmap;
                string set = key[..key.IndexOf('#')];
                resources.Register(set, provider.GetResource<ImageSet>(set));
            }

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            Shader.SetGlobalFloat("_MapMode", 0f);
            foreach (string id in new[] { "_BakSpriteNearHide", "_BakSpriteNearShow", "_BakSpriteFogStart", "_BakSpriteFogEnd" }) {
                Shader.SetGlobalFloat(id, 0f);
            }
            foreach (string id in new[] { "_BakLightDragonsBreath", "_BakLightTint", "_BakLightDarken", "_BakFlash", "_BakSunTint" }) {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
            var root = new GameObject("CoLocatedBillboards");
            // Any palette whose pens differ will do: the test compares pixels, not colours.
            var palette = new Color[256];
            for (int i = 0; i < 256; i++) palette[i] = new Color32((byte)i, (byte)(255 - i), (byte)(i * 7), 255);
            var ctx = WorldEntityRenderContext.Create(palette, new WorldRenderProfile());
            ctx.ResourceProvider = resources;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            try {
                // In the WLD's order: 134 is item 22, 136 item 35.
                GameObject under = await WorldEntityBuilder.Build(table.Entries[134], 134, Spot, Quaternion.identity,
                    root.transform, root.transform, ctx, null);
                GameObject over = await WorldEntityBuilder.Build(table.Entries[136], 136, Spot, Quaternion.identity,
                    root.transform, root.transform, ctx, null);
                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.fieldOfView = 60f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000f;
                camera.targetTexture = rt;
                camera.aspect = 1f;
                camera.transform.position = Eye;

                Color32[] Render() {
                    camera.Render();
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    RenderTexture.active = null;
                    return read.GetPixels32();
                }

                int worst = 0, covered = 0;
                string where = "";
                // The burst that showed it: a tenth of a degree a frame, the zone's slight upward tilt.
                for (int step = 0; step < 20; step++) {
                    camera.transform.rotation = Quaternion.Euler(-1.8f, 180f - (step * 0.1f), 0f);
                    under.SetActive(false);
                    Color32[] alone = Render();
                    under.SetActive(true);
                    Color32[] both = Render();
                    int wrong = 0;
                    for (int i = 0; i < alone.Length; i++) {
                        if (alone[i].r + alone[i].g + alone[i].b == 0) continue;   // background: not the top tree
                        covered++;
                        if (alone[i].r != both[i].r || alone[i].g != both[i].g || alone[i].b != both[i].b) wrong++;
                    }
                    if (wrong > worst) { worst = wrong; where = $"step {step}"; }
                }

                Assert.Greater(covered, 1000, "the later tree is on screen");
                Assert.AreEqual(0, worst, $"pixels of the later tree that the earlier one showed through ({where})");
            } finally {
                Object.DestroyImmediate(root);
                ctx.Dispose();
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(rt);
                RenderSettings.fog = fog;
            }
        });
    }
}

namespace BakAgain.Tests.PlayMode.World {
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A flat-coloured face must render as its palette colour. The project is in Linear colour space,
    /// and vertex colours are not converted, so the shader received the sRGB palette bytes as if they
    /// were linear and the output encode brightened them. Measured on walk SAVE621 (chapter 5, zone 5)
    /// at noon: the mine entrance's pen 190 (73,44,24) drew as (146,115,86) (TASK-747).
    /// </summary>
    public class ClassicPolygonColourTests {
        [Test]
        public void AFlatFaceRendersItsPaletteColour() {
            Color32 got = Render(new Color32(73, 44, 24, 255), darken: 0f);
            Assert.That(got.r, Is.InRange(71, 75), $"got {got}");
            Assert.That(got.g, Is.InRange(42, 46), $"got {got}");
            Assert.That(got.b, Is.InRange(22, 26), $"got {got}");
        }

        /// <summary>
        /// The original darkens by blending each PALETTE entry toward black (docs/specs/lighting-system.md:58),
        /// i.e. in display space: half darkness is half the palette value. Lerping in linear space left
        /// night scenes far too bright (c492 at 02:34, darkness 0.77: the port's well roof was 1.6x the
        /// original's).
        /// </summary>
        [Test]
        public void DarknessScalesThePaletteValue_NotTheLinearOne() {
            Color32 got = Render(new Color32(73, 44, 24, 255), darken: 0.5f);
            Assert.That(got.r, Is.InRange(34, 39), $"got {got}");
            Assert.That(got.g, Is.InRange(20, 24), $"got {got}");
            Assert.That(got.b, Is.InRange(10, 14), $"got {got}");
        }

        private static Color32 Render(Color32 c, float darken) {
            var go = new GameObject("quad");
            var camGo = new GameObject("cam");
            var rt = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try {
                var mesh = new Mesh {
                    vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) },
                    triangles = new[] { 0, 1, 2, 0, 2, 3 },
                    normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                };
                Shader.SetGlobalVector("_BakLightDarken", new Vector4(0, 0, 0, darken));
                mesh.colors32 = new[] { c, c, c, c };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("BakAgain/ClassicPolygon"));
                go.transform.position = new Vector3(1000, 1000, 5);

                var cam = camGo.AddComponent<Camera>();
                camGo.transform.position = new Vector3(1000, 1000, 0);
                cam.orthographic = true;
                cam.orthographicSize = 0.5f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var read = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                RenderTexture.active = null;
                return read.GetPixel(4, 4);
            } finally {
                Shader.SetGlobalVector("_BakLightDarken", Vector4.zero);
                RenderTexture.active = null;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camGo);
                rt.Release();
            }
        }
    }
}

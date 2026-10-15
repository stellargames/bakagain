namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.World;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// With the Enhanced sky on, the mountain panels' own sky pixels (the panel's top-row pen, which is
    /// also the strip's flat top fill) are see-through so the dome shows above the silhouettes; with it
    /// off the strip is drawn exactly as before (spec 2026-10-15-enhanced-sky-design).
    /// </summary>
    public class HorizonSkyKeyTests {
        private static readonly Color Key = new(40 / 255f, 81 / 255f, 93 / 255f);
        private static readonly Color Hill = new(0.1f, 0.6f, 0.1f);
        private static readonly Color Clear = Color.magenta;

        private GameObject _go;
        private GameObject _camGo;
        private RenderTexture _rt;

        [TearDown]
        public void TearDown() {
            Shader.SetGlobalFloat("_BakSkyOn", 0f);
            RenderTexture.active = null;
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_camGo);
            if (_rt != null) _rt.Release();
        }

        private static Texture2D Panel() {
            const int w = 16, h = 60;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    tex.SetPixel(x, y, y >= 26 ? Key : Hill);   // sky above, hills below
                }
            }
            tex.Apply();
            return tex;
        }

        private IEnumerator Render(bool skyOn, System.Action<Color[]> read) {
            foreach (string id in new[] { "_BakLightDragonsBreath", "_BakLightTint", "_BakLightDarken", "_BakFlash" }) {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
            Shader.SetGlobalFloat("_BakSkyOn", skyOn ? 1f : 0f);
            Shader.SetGlobalVector("_BakHaze", Vector4.zero);   // haze is not under test here

            _camGo = new GameObject("HorizonTestCamera");
            _camGo.transform.position = new Vector3(3000, 3000, 0);
            var cam = _camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Clear;
            _rt = new RenderTexture(32, 32, 24);
            cam.targetTexture = _rt;

            _go = new GameObject("HorizonUnderTest");
            var horizon = _go.AddComponent<HorizonRenderer>();
            horizon.Initialize(new[] { Panel(), Panel(), Panel(), Panel() });
            horizon.SetCamera(cam);
            yield return null;   // LateUpdate places the strip in front of the camera

            cam.Render();
            RenderTexture.active = _rt;
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
            RenderTexture.active = null;
            read(tex.GetPixels());
        }

        private static int Count(Color[] px, Color c) {
            int n = 0;
            foreach (Color p in px) {
                if (Mathf.Abs(p.r - c.r) + Mathf.Abs(p.g - c.g) + Mathf.Abs(p.b - c.b) < 0.12f) n++;
            }
            return n;
        }

        [UnityTest]
        public IEnumerator SkyOffDrawsThePanelsSkyPen() {
            Color[] px = null;
            yield return Render(false, p => px = p);
            Assert.Greater(Count(px, Key), 0, "the strip's sky pen is drawn");
            Assert.Greater(Count(px, Hill), 0, "the hills are drawn");
        }

        [UnityTest]
        public IEnumerator SkyOnLetsTheSkyThrough() {
            Color[] px = null;
            yield return Render(true, p => px = p);
            Assert.AreEqual(0, Count(px, Key), "no sky-pen pixel survives");
            Assert.Greater(Count(px, Hill), 0, "the hills are still drawn");
        }
    }
}

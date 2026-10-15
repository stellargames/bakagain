namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using System.Globalization;
    using BakAgain.Core;
    using BakAgain.World;
    using GameData.Resources.GameState;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// <b>The brightness rule holds under Enhanced Lighting</b> (spec 2026-10-15-enhanced-sky-design): at
    /// every hour the frame's mean luminance with Lighting on is within 3 % of Faithful's. The grading
    /// (white balance, split toning, ACES, contrast) changes colour and contrast; the per-hour exposure in
    /// <see cref="GameData.Resources.World.SkyLayout.ExposureByHour"/> puts the brightness back.
    /// </summary>
    /// <remarks>
    /// A real zone needs the game's data, which a test cannot load, so the scene is a grid of world-like
    /// colours (greens, browns, greys, the zone sky pen) lit by the real <see cref="WorldLightingService"/>
    /// chain. The live check on a real save is the other half (TASK-879 notes).
    /// </remarks>
    [Category("Graphics")]
    public class EnhancedLightingBrightnessTests {
        private const float Tolerance = 0.03f;
        private static readonly Color SkyPen = new(40 / 255f, 81 / 255f, 93 / 255f);

        /// <summary>The terrain of a real noon frame (zone 1, dir.G01 save 1, Faithful), quantised to its
        /// colours and repeated by the area each covers. The sky pen is the camera's clear colour.
        /// A brighter made-up set calibrated the exposure for mid-tones and left real night ground
        /// a third too dark (live, 2026-10-15).</summary>
        private static readonly Color32[] WorldColours = {
            new(28, 61, 29, 255), new(28, 61, 29, 255), new(28, 61, 29, 255), new(28, 61, 29, 255), new(28, 61, 29, 255), new(28, 61, 29, 255),
            new(28, 61, 29, 255), new(28, 61, 29, 255), new(93, 60, 42, 255), new(93, 60, 42, 255), new(93, 60, 42, 255), new(93, 60, 42, 255),
            new(95, 61, 43, 255), new(95, 61, 43, 255), new(95, 61, 43, 255), new(21, 40, 18, 255), new(21, 40, 18, 255), new(21, 40, 18, 255),
            new(35, 75, 39, 255), new(35, 75, 39, 255), new(35, 75, 39, 255), new(16, 31, 13, 255), new(16, 31, 13, 255), new(26, 52, 25, 255),
            new(26, 52, 25, 255), new(38, 46, 28, 255), new(38, 46, 28, 255), new(48, 61, 34, 255), new(48, 77, 47, 255), new(64, 58, 36, 255),
        };

        /// <summary>Texture rows the grid covers: three rows of quads from -2 to +1 units of a camera
        /// 3 units high, 8 px a unit, centred on row 24 (ReadPixels is bottom-up).</summary>
        private static readonly (int Bottom, int Top) TerrainRows = (9, 31);

        private EnhancedPrefsStash _stash;
        private GameObject _root;
        private GameObject _camGo;
        private Camera _camera;
        private RenderTexture _rt;
        private GameSession _session;
        private WorldLightingService _lighting;
        private Texture2D _read;
        private bool _fog;

        [SetUp]
        public void SetUp() {
            _stash = EnhancedPrefsStash.Take();
            // Every global the shaders read is set here: whatever the last test or Play session left
            // (fog on, the map's pen remap, a spell flash) changed the reading 2.2x before this.
            _fog = RenderSettings.fog;
            RenderSettings.fog = false;
            Shader.SetGlobalFloat("_MapMode", 0f);
            Shader.SetGlobalVector("_BakFlash", Vector4.zero);
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.Sky, false);

            _root = new GameObject("BrightnessZone");
            _root.transform.position = new Vector3(5000, 5000, 0);
            var env = _root.AddComponent<ZoneEnvironment>();
            env.SkyColor = SkyPen;
            var shader = Shader.Find("BakAgain/ClassicPolygon");
            for (int i = 0; i < 16; i++) {
                for (int row = 0; row < 3; row++) {
                    Color32 c = WorldColours[((row * 16) + i) % WorldColours.Length];
                    var quad = new GameObject($"q{i}_{row}");
                    quad.transform.SetParent(_root.transform, false);
                    quad.transform.localPosition = new Vector3(-7.5f + i, -1.5f + row, 10f);
                    var mesh = new Mesh {
                        vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) },
                        triangles = new[] { 0, 1, 2, 0, 2, 3 },
                        normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back },
                        colors32 = new[] { c, c, c, c },
                    };
                    quad.AddComponent<MeshFilter>().sharedMesh = mesh;
                    quad.AddComponent<MeshRenderer>().sharedMaterial = new Material(shader);
                }
            }

            _camGo = new GameObject("BrightnessCamera");
            _camGo.transform.position = new Vector3(5000, 5000, 0);
            _camera = _camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 3f;
            _camera.aspect = 16f / 6f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _rt = new RenderTexture(128, 48, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _camera.targetTexture = _rt;
            _read = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBA32, false);

            _session = new GameSession();
            _lighting = new WorldLightingService(_session, null);
            _lighting.BindSky(_camera, env, 1, false);
        }

        [TearDown]
        public void TearDown() {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_read);
            _rt.Release();
            foreach (string id in new[] { "_BakLightDragonsBreath", "_BakLightTint", "_BakLightDarken", "_BakFlash", "_BakSunTint" }) {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
            Shader.SetGlobalFloat("_BakSkyOn", 0f);
            RenderSettings.fog = _fog;
            _stash.Restore();
        }

        /// <summary>Mean luminance of the frame, 0..1, in display (sRGB) space.</summary>
        private float MeanLuminance(int hour, bool lighting) {
            GameOptions.SetFeature(EnhancedFeature.Lighting, lighting);
            _session.GameTimeIn2Seconds = (3L * GameTime.UnitsPerDay) + (hour * (long)GameTime.UnitsPerHour);
            _lighting.Refresh();
            _camera.Render();
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            RenderTexture.active = null;
            // The WORLD's brightness is the rule, so only the terrain rows are measured: the band above
            // is the sky, which the Sky feature owns. (Averaging both let a lifted sky hide ground that
            // ACES had crushed: real night ground came out 14 % dark, live 2026-10-15.)
            Color32[] px = _read.GetPixels32();
            double sum = 0;
            int n = 0;
            for (int y = TerrainRows.Bottom; y < TerrainRows.Top; y++) {
                for (int x = 0; x < _rt.width; x++) {
                    Color32 p = px[(y * _rt.width) + x];
                    sum += (0.2126 * p.r) + (0.7152 * p.g) + (0.0722 * p.b);
                    n++;
                }
            }
            return (float)(sum / n / 255.0);
        }

        private static IEnumerable<int> Hours() {
            for (int h = 0; h < 24; h++) yield return h;
        }

        [Test]
        public void LightingHoldsFaithfulBrightness([ValueSource(nameof(Hours))] int hour) {
            float faithful = MeanLuminance(hour, lighting: false);
            float lit = MeanLuminance(hour, lighting: true);
            Assert.That(Mathf.Abs(lit - faithful) / faithful, Is.LessThanOrEqualTo(Tolerance),
                $"{hour:00}:00 faithful {faithful:F4} lighting {lit:F4}");
        }

        /// <summary>
        /// Finds each hour's post-exposure by bisection and logs the table for
        /// <c>SkyLayout.ExposureByHour</c>. Run by hand after changing the grading keys.
        /// </summary>
        [Test, Explicit("calibration tool: prints SkyLayout.ExposureByHour")]
        public void Calibrate() {
            double[] table = EnhancedSky.Layout.ExposureByHour;
            var line = new System.Text.StringBuilder("ExposureByHour = { ");
            for (int h = 0; h < 24; h++) {
                float target = MeanLuminance(h, lighting: false);
                double lo = -6, hi = 6;
                for (int step = 0; step < 24; step++) {
                    table[h] = (lo + hi) / 2;   // read at :00 exactly, so the next hour's value plays no part
                    float got = MeanLuminance(h, lighting: true);
                    if (got < target) lo = table[h]; else hi = table[h];
                    if (Mathf.Abs(got - target) / target < 0.002f) break;
                }
                line.Append(table[h].ToString("F3", CultureInfo.InvariantCulture)).Append(h < 23 ? ", " : " }");
            }
            Debug.Log(line.ToString());
            Assert.Pass(line.ToString());
        }
    }
}

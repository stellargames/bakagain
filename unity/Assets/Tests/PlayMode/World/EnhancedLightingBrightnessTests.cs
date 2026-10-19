namespace BakAgain.Tests.PlayMode.World {
    using System.Collections.Generic;
    using System.Globalization;
    using BakAgain.Core;
    using BakAgain.World;
    using GameData.Resources.Dialog.Actions;
    using GameData.Resources.GameState;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// <b>The brightness rule holds under Enhanced Lighting</b> (spec 2026-10-15-enhanced-sky-design): at
    /// every hour, between hours on the dawn and dusk ramps, and with a light source lit, the terrain's
    /// mean luminance with Lighting on is within 3 % of Faithful's. The grade keeps each pixel's
    /// luminance (ApplyBakLighting), so this holds without any calibration; a post-processing grade
    /// with ACES and per-hour exposure tables could not hold it once a light source was lit.
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
        private Material _material; // one for every quad: the colour is in the vertices
        private GameSession _session;
        private BakAgain.Core.Services.GameClock _clock;
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
                    quad.AddComponent<MeshRenderer>().sharedMaterial = _material ??= new Material(shader);
                }
            }

            _camGo = new GameObject("BrightnessCamera");
            _camGo.transform.position = new Vector3(5000, 5000, 0);
            _camera = _camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 3f;
            _camera.aspect = 16f / 6f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            // Half float, read back linear and encoded to sRGB here: an 8-bit target stores the night's
            // near-black ground in one or two levels, and a software renderer (CI) that writes it linear
            // leaves the rule's 3 % far below one step.
            _rt = new RenderTexture(128, 48, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _camera.targetTexture = _rt;
            _read = new Texture2D(_rt.width, _rt.height, TextureFormat.RGBAHalf, false, linear: true);

            _session = new GameSession();
            _clock = new BakAgain.Core.Services.GameClock(_session);
            _lighting = new WorldLightingService(_session, _clock);
            _lighting.BindSky(_camera, env, 1, false);
        }

        [TearDown]
        public void TearDown() {
            foreach (MeshFilter f in _root.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(f.sharedMesh);
            Object.DestroyImmediate(_material);
            _material = null;
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_read);
            Object.DestroyImmediate(_rt);
            foreach (string id in new[] { "_BakLightDragonsBreath", "_BakLightTint", "_BakLightDarken", "_BakFlash", "_BakSunTint" }) {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
            Shader.SetGlobalFloat("_BakSkyOn", 0f);
            RenderSettings.fog = _fog;
            _stash.Restore();
        }

        /// <summary>Mean luminance of the terrain, 0..1, in display (sRGB) space.</summary>
        /// <param name="lightMinutes">Minutes left on a light source (<paramref name="source"/>); 0 = none.</param>
        private float MeanLuminance(int hour, bool lighting, int minute = 0, int lightMinutes = 0,
            LightSourceDecay.Source source = LightSourceDecay.Source.Item) {
            GameOptions.SetFeature(EnhancedFeature.Lighting, lighting);
            _session.GameTimeIn2Seconds = (3L * GameTime.UnitsPerDay) + (hour * (long)GameTime.UnitsPerHour)
                + (minute * (long)GameTime.UnitsPerHour / 60);
            foreach (LightSourceDecay.Source s in new[] { LightSourceDecay.Source.Item, LightSourceDecay.Source.Stardusk }) {
                _clock.ScheduleTimer(TimerType.Light, (int)s,
                    s == source ? lightMinutes * (GameTime.UnitsPerHour / 60) : 0, replaceExisting: true);
            }
            _lighting.Refresh();
            _camera.Render();
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            RenderTexture.active = null;
            // The WORLD's brightness is the rule, so only the terrain rows are measured: the band above
            // is the sky, which the Sky feature owns. (Averaging both let a lifted sky hide ground that
            // ACES had crushed: real night ground came out 14 % dark, live 2026-10-15.)
            Color[] px = _read.GetPixels();
            double sum = 0;
            int n = 0;
            for (int y = TerrainRows.Bottom; y < TerrainRows.Top; y++) {
                for (int x = 0; x < _rt.width; x++) {
                    sum += DisplayLuminance(px[(y * _rt.width) + x]);
                    n++;
                }
            }
            return (float)(sum / n);
        }

        /// <summary>A linear pixel's luminance in display (sRGB) space, 0..1: what the screen shows.</summary>
        private static double DisplayLuminance(Color linear) {
            Color c = linear.gamma;
            return (0.2126 * c.r) + (0.7152 * c.g) + (0.0722 * c.b);
        }

        private static IEnumerable<int> Hours() {
            for (int h = 0; h < 24; h++) yield return h;
        }

        private void AssertHolds(int hour, int minute = 0, int lightMinutes = 0,
            LightSourceDecay.Source source = LightSourceDecay.Source.Item) {
            float faithful = MeanLuminance(hour, false, minute, lightMinutes, source);
            float lit = MeanLuminance(hour, true, minute, lightMinutes, source);
            Assert.That(Mathf.Abs(lit - faithful) / faithful, Is.LessThanOrEqualTo(Tolerance),
                $"{hour:00}:{minute:00} {source} {lightMinutes} min (level {_lighting.Current.Light}): faithful {faithful:F4} lighting {lit:F4}");
        }

        [Test]
        public void LightingHoldsFaithfulBrightness([ValueSource(nameof(Hours))] int hour) => AssertHolds(hour);

        /// <summary>Between the calibrated half hours, where the daylight ramps and the grades move fastest.</summary>
        [Test]
        public void LightingHoldsFaithfulBrightnessOnTheRamps(
            [Values(4, 5, 6, 7, 17, 18, 19, 20)] int hour, [Values(15, 45)] int minute) =>
            AssertHolds(hour, minute);

        /// <summary>
        /// A light source raises the world's level above the clock's (daylight + stardusk + item), so a lit
        /// night must not keep the night's exposure boost (review finding, 2026-10-15).
        /// </summary>
        [Test]
        public void LightingHoldsFaithfulBrightnessWithALight([Values(0, 22)] int hour, [Values(2, 4, 6, 8)] int itemMinutes) =>
            AssertHolds(hour, lightMinutes: itemMinutes);

        /// <summary>
        /// The dome replaces the sky pen, so it takes the pen's night darkening too: at night the
        /// Enhanced sky, clouds and moons included, is no brighter than Faithful's darkened pen
        /// (owner, 2026-10-16: "I was expecting black. Even with a full moon").
        /// </summary>
        [Test]
        public void TheNightSkyIsNoBrighterThanFaithfuls([Values(22, 0, 2)] int hour, [Values(1, 4, 9, 15)] int day) {
            _camera.orthographic = false;
            _camera.fieldOfView = 60f;
            _camera.transform.rotation = Quaternion.Euler(-50f, 0f, 0f);   // looking up, above the quads
            float SkyLuminance(bool sky) {
                GameOptions.SetFeature(EnhancedFeature.Sky, sky);
                _session.GameTimeIn2Seconds = (day * (long)GameTime.UnitsPerDay) + (hour * (long)GameTime.UnitsPerHour);
                _lighting.Refresh();
                _camera.Render();
                RenderTexture.active = _rt;
                _read.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
                RenderTexture.active = null;
                double sum = 0;
                Color[] px = _read.GetPixels();
                foreach (Color p in px) sum += DisplayLuminance(p);
                return (float)(sum / px.Length);
            }
            float faithful = SkyLuminance(false);
            float enhanced = SkyLuminance(true);
            Assert.That(enhanced, Is.LessThanOrEqualTo(faithful * (1f + Tolerance)),
                $"day {day} {hour:00}:00: faithful {faithful:F4} sky {enhanced:F4}");
        }

        [Test]
        public void LightingHoldsFaithfulBrightnessUnderStardusk([Values(0, 5)] int hour) =>
            AssertHolds(hour, lightMinutes: 8, source: LightSourceDecay.Source.Stardusk);
    }
}

namespace BakAgain.World {
    using System;
    using GameData.Resources.World;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;

    /// <summary>
    /// The Enhanced sky and lighting for one zone (spec 2026-10-15-enhanced-sky-design): the dome, the
    /// <c>Sun</c> light and the grading volume, all children of the zone root so they die with it.
    /// </summary>
    /// <remarks>
    /// <b>Invented for Enhanced; the original has no sky object.</b> Its sky is one palette pen
    /// (<c>drawHorizonSkyAndGround</c>, ovr138 @0x460c7). Every time and position rule is in
    /// <see cref="SkyClock"/>; this only draws them and hands them to the shaders as globals. The
    /// world's brightness stays <see cref="DaylightLevel"/>'s: the tint keeps each pixel's luminance
    /// and the grading's exposure is calibrated per hour against Faithful.
    /// <para>Driven by <see cref="WorldLightingService"/>'s per-frame refresh; nothing else calls
    /// <see cref="Apply"/>.</para>
    /// </remarks>
    public sealed class EnhancedSky : MonoBehaviour {
        public static readonly SkyLayout Layout = new();
        private static readonly SkyClock Clock = new(Layout);

        private static readonly int SunTintId = Shader.PropertyToID("_BakSunTint");
        private static readonly int SkyOnId = Shader.PropertyToID("_BakSkyOn");
        private static readonly int HazeId = Shader.PropertyToID("_BakHaze");
        private static readonly int SunDirId = Shader.PropertyToID("_BakSkySunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_BakSkySunColor");
        private static readonly int ZoneColorId = Shader.PropertyToID("_BakSkyZoneColor");
        private static readonly int AnchorId = Shader.PropertyToID("_BakSkyAnchor");
        private static readonly int[] MoonDirIds = {
            Shader.PropertyToID("_BakSkyMoonDir0"), Shader.PropertyToID("_BakSkyMoonDir1"), Shader.PropertyToID("_BakSkyMoonDir2"),
        };
        private static readonly int MoonPhaseId = Shader.PropertyToID("_BakSkyMoonPhase");
        private static readonly int MoonBrightId = Shader.PropertyToID("_BakSkyMoonBright");
        private static readonly int CloudId = Shader.PropertyToID("_BakSkyCloud");
        private static readonly int StarRotId = Shader.PropertyToID("_BakSkyStarRot");

        /// <summary>The Large Moon's light when it stands in for the sun at night.</summary>
        private static readonly Color MoonLight = new(0.55f, 0.65f, 1f);

        /// <summary>Tint strength by day and by night (a colour shift; luminance is kept).</summary>
        private const float DayTint = 0.25f;
        private const float NightTint = 0.35f;

        /// <summary>Inside the far clip; the queue, not the radius, puts it behind everything.</summary>
        private const float DomeRadius = 300f;

        private Camera _camera;
        private ZoneEnvironment _env;
        private int _zone;
        private bool _underground;

        private GameObject _dome;
        private Material _domeMaterial;
        private Mesh _domeMesh;
        private Light _sun;
        private Volume _volume;
        private VolumeProfile _profile;
        private WhiteBalance _whiteBalance;
        private SplitToning _splitToning;
        private ColorAdjustments _colorAdjustments;
        private Bloom _bloom;

        public void Bind(Camera camera, ZoneEnvironment env, int zone, bool underground) {
            _camera = camera;
            _env = env;
            _zone = zone;
            _underground = underground;
        }

        /// <summary>One frame of the sky and the light, from the game clock and the two switches.</summary>
        /// <param name="ticks">The game clock, in its two-second unit.</param>
        /// <param name="daylight">The original's daylight level now (15 night .. 64 day).</param>
        public void Apply(long ticks, int daylight, bool skyOn, bool lightingOn) {
            bool map = _env != null && _env.MapModeOn;
            bool live = _camera != null && !_underground && !map;
            bool sky = skyOn && live;
            bool lit = lightingOn && live;
            SkyDirection sun = Clock.Sun(ticks);

            if (sky || lit) {
                PublishSkyGlobals(ticks, sun);
            }
            SetDome(sky);
            SetSun(sky || lit, ticks, sun, daylight);
            SetGrading(lit, ticks);
            Shader.SetGlobalVector(SunTintId, lit ? TintAt(ticks, sun) : Vector4.zero);
            Shader.SetGlobalFloat(SkyOnId, sky ? 1f : 0f);
        }

        /// <summary>No live sky (none bound, or the zone was torn down): leave nothing global behind.</summary>
        public static void Reset(Camera camera) {
            Shader.SetGlobalVector(SunTintId, Vector4.zero);
            Shader.SetGlobalFloat(SkyOnId, 0f);
            if (camera != null) {
                SetCameraPostProcessing(camera, false);
            }
        }

        /// <summary>The only camera state this class touches.</summary>
        public static void SetCameraPostProcessing(Camera camera, bool on) {
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data.renderPostProcessing != on) {
                data.renderPostProcessing = on;
            }
        }

        private void LateUpdate() {
            if (_dome != null && _dome.activeSelf && _camera != null) {
                _dome.transform.SetPositionAndRotation(_camera.transform.position, Quaternion.identity);
            }
        }

        private void OnDestroy() {
            if (_domeMaterial != null) Destroy(_domeMaterial);
            if (_domeMesh != null) Destroy(_domeMesh);
            if (_profile != null) Destroy(_profile);
        }

        /// <summary>The world is +X east, +Y up, +Z north (BakCoordinateConverter: BaK X→X, Y→Z).</summary>
        private static Vector3 ToVector(SkyDirection d) => new((float)d.East, (float)d.Up, (float)d.North);

        private void PublishSkyGlobals(long ticks, SkyDirection sun) {
            Shader.SetGlobalVector(SunDirId, ToVector(sun));
            (double r, double g, double b) = Clock.SunColor(Math.Max(0, sun.ElevationDegrees));
            Shader.SetGlobalVector(SunColorId, new Vector4((float)r, (float)g, (float)b, 1f));
            Shader.SetGlobalVector(ZoneColorId, _env != null ? _env.SkyColor.linear : Color.black);
            Shader.SetGlobalFloat(AnchorId, (float)Layout.ZoneAnchorWeight * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.5f, (float)sun.Up)));

            var phases = Vector4.zero;
            var brightness = Vector4.zero;
            for (int i = 0; i < MoonDirIds.Length && i < Layout.Moons.Length; i++) {
                MoonLayout m = Layout.Moons[i];
                Vector3 dir = ToVector(Clock.Moon(i, ticks));
                float radius = (float)(m.SizeDegrees * 0.5 * Math.PI / 180);
                Shader.SetGlobalVector(MoonDirIds[i], new Vector4(dir.x, dir.y, dir.z, radius));
                phases[i] = (float)Clock.MoonPhase(i, ticks);
                brightness[i] = (float)m.Brightness;
            }
            Shader.SetGlobalVector(MoonPhaseId, phases);
            Shader.SetGlobalVector(MoonBrightId, brightness);

            float hours = (float)((double)ticks / GameData.Resources.GameState.GameTime.UnitsPerHour);
            Shader.SetGlobalVector(CloudId, new Vector4((float)Clock.CloudCover(_zone, ticks), hours,
                Application.isMobilePlatform ? 1f : 2f, 0f));
            Shader.SetGlobalFloat(StarRotId, (hours % 24f) / 24f * 2f * Mathf.PI);
            Shader.SetGlobalVector(HazeId, new Vector4((float)Layout.HazeStrength, (float)Layout.HazeBaseExtra, 0f, 0f));
        }

        private void SetDome(bool on) {
            if (!on) {
                if (_dome != null) _dome.SetActive(false);
                return;
            }
            if (_dome == null) {
                _dome = new GameObject("EnhancedSkyDome");
                _dome.transform.SetParent(transform, false);
                _domeMesh = BuildDome(DomeRadius, 24, 12);
                _domeMaterial = new Material(Shader.Find("BakAgain/EnhancedSky"));
                _domeMaterial.SetTexture("_CloudsA", Resources.Load<Texture2D>("Enhanced/Sky/clouds_cumulus"));
                _domeMaterial.SetTexture("_CloudsB", Resources.Load<Texture2D>("Enhanced/Sky/clouds_cirrus"));
                _domeMaterial.SetTexture("_Moon0", Resources.Load<Texture2D>("Enhanced/Sky/moon_large"));
                _domeMaterial.SetTexture("_Moon1", Resources.Load<Texture2D>("Enhanced/Sky/moon_middle"));
                _domeMaterial.SetTexture("_Moon2", Resources.Load<Texture2D>("Enhanced/Sky/moon_small"));
                _dome.AddComponent<MeshFilter>().sharedMesh = _domeMesh;
                var renderer = _dome.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _domeMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            _dome.SetActive(true);
            if (_camera != null) {
                _dome.transform.SetPositionAndRotation(_camera.transform.position, Quaternion.identity);
            }
        }

        private void SetSun(bool on, long ticks, SkyDirection sun, int daylight) {
            if (!on) {
                if (_sun != null) _sun.gameObject.SetActive(false);
                return;
            }
            if (_sun == null) {
                var go = new GameObject("Sun");
                go.transform.SetParent(transform, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.None;   // TASK-882 turns them on
            }
            _sun.gameObject.SetActive(true);

            SkyDirection large = Clock.Moon(0, ticks);
            bool moonLight = sun.Up <= 0 && large.Up > 0;
            Vector3 toLight = ToVector(moonLight ? large : sun);
            _sun.transform.rotation = Quaternion.LookRotation(-toLight);
            if (moonLight) {
                _sun.color = MoonLight;
                _sun.intensity = 0.15f * MoonFullness(ticks);
            } else {
                (double r, double g, double b) = Clock.SunColor(Math.Max(0, sun.ElevationDegrees));
                _sun.color = new Color((float)r, (float)g, (float)b);
                _sun.intensity = daylight / (float)DaylightLevel.Day;
            }
        }

        private static float MoonFullness(long ticks) => 1f - (Mathf.Abs((float)Clock.MoonPhase(0, ticks) - 0.5f) * 2f);

        /// <summary>rgb = the light's colour scaled to luminance 1 (so the shift keeps brightness), a = strength.</summary>
        private static Vector4 TintAt(long ticks, SkyDirection sun) {
            Color c;
            float strength;
            if (sun.Up > -0.05) {
                (double r, double g, double b) = Clock.SunColor(Math.Max(0, sun.ElevationDegrees));
                c = new Color((float)r, (float)g, (float)b);
                strength = DayTint;
            } else {
                c = MoonLight;
                strength = NightTint * Mathf.Lerp(0.6f, 1f, MoonFullness(ticks));
            }
            float luminance = Mathf.Max((0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b), 1e-4f);
            return new Vector4(c.r / luminance, c.g / luminance, c.b / luminance, strength);
        }

        private void SetGrading(bool on, long ticks) {
            if (_camera != null) {
                SetCameraPostProcessing(_camera, on);
            }
            if (!on) {
                if (_volume != null) _volume.gameObject.SetActive(false);
                return;
            }
            if (_volume == null) {
                var go = new GameObject("EnhancedGrading");
                go.transform.SetParent(transform, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 10;
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _whiteBalance = _profile.Add<WhiteBalance>(true);
                _splitToning = _profile.Add<SplitToning>(true);
                _colorAdjustments = _profile.Add<ColorAdjustments>(true);
                _profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                _bloom = _profile.Add<Bloom>(true);
                _bloom.threshold.Override(1f);
                _bloom.active = !Application.isMobilePlatform;
                _volume.sharedProfile = _profile;
            }
            _volume.gameObject.SetActive(true);

            GradingKey k = Clock.GradingAt(ticks);
            _whiteBalance.temperature.Override((float)k.Temperature);
            _whiteBalance.tint.Override((float)k.Tint);
            _splitToning.shadows.Override(new Color((float)k.Shadows.R, (float)k.Shadows.G, (float)k.Shadows.B));
            _splitToning.highlights.Override(new Color((float)k.Highlights.R, (float)k.Highlights.G, (float)k.Highlights.B));
            _colorAdjustments.contrast.Override((float)k.Contrast);
            _colorAdjustments.saturation.Override((float)k.Saturation);
            _colorAdjustments.postExposure.Override((float)Clock.ExposureAt(ticks));
            _bloom.intensity.Override((float)k.Bloom);
        }

        /// <summary>A UV sphere; seen from inside (the shader culls front faces).</summary>
        private static Mesh BuildDome(float radius, int segments, int rings) {
            var vertices = new Vector3[(segments + 1) * (rings + 1)];
            for (int r = 0; r <= rings; r++) {
                float phi = Mathf.PI * r / rings;          // 0 at the zenith
                for (int s = 0; s <= segments; s++) {
                    float theta = 2f * Mathf.PI * s / segments;
                    vertices[(r * (segments + 1)) + s] = radius * new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                }
            }
            var triangles = new int[segments * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++) {
                for (int s = 0; s < segments; s++) {
                    int a = (r * (segments + 1)) + s;
                    int b = a + segments + 1;
                    triangles[t++] = a; triangles[t++] = a + 1; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = a + 1; triangles[t++] = b + 1;
                }
            }
            var mesh = new Mesh { name = "EnhancedSkyDome", vertices = vertices, triangles = triangles };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2f);
            return mesh;
        }
    }
}

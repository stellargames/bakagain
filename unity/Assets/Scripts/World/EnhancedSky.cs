namespace BakAgain.World {
    using System;
    using BakAgain.Core;
    using BakAgain.World.Rendering;
    using GameData.Resources.World;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;

    /// <summary>
    /// The Enhanced sky and lighting for one zone (spec 2026-10-15-enhanced-sky-design): the dome, the
    /// <c>Sun</c> light and the bloom volume, all children of the zone root so they die with it.
    /// </summary>
    /// <remarks>
    /// <b>Invented for Enhanced; the original has no sky object.</b> Its sky is one palette pen
    /// (<c>drawHorizonSkyAndGround</c>, ovr138 @0x460c7). Every time and position rule is in
    /// <see cref="SkyClock"/>; this only draws them and hands them to the shaders as globals. The
    /// world's brightness stays <see cref="DaylightLevel"/>'s: the grade is done in ApplyBakLighting as
    /// colour shifts that keep each pixel's luminance, so it holds at every hour, light level and tint.
    /// A post-processing grade (ACES, contrast, per-hour exposure) could not: a lit night came out twice
    /// as bright as Faithful. Post-processing is used for bloom only, on desktop.
    /// <para>Driven by <see cref="WorldLightingService"/>'s per-frame refresh; nothing else calls
    /// <see cref="Apply"/>.</para>
    /// </remarks>
    public sealed class EnhancedSky : MonoBehaviour {
        public static readonly SkyLayout Layout = new();
        private static readonly SkyClock Clock = new(Layout);

        private static readonly int SunTintId = Shader.PropertyToID("_BakSunTint");
        private static readonly int ShadeTintId = Shader.PropertyToID("_BakShadeTint");
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
        private static readonly int MoonLightId = Shader.PropertyToID("_BakSkyMoonLight");
        private static readonly int SunShadowId = Shader.PropertyToID("_BakSunShadow");

        /// <summary>How much the sun's shadow darkens at noon (spec Q5: soft, about 35 %).</summary>
        public const float SunShadowStrength = 0.35f;

        /// <summary>The Large Moon's light when it stands in for the sun at night.</summary>
        private static readonly Color MoonLight = new(0.55f, 0.65f, 1f);

        /// <summary>How much of the sun's or the moon's colour the grade takes, by day and by night.</summary>
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
        private Bloom _bloom;

        // The last Apply, so the map switch can be re-applied without the lighting refresh.
        private bool _applied;
        private bool _appliedMap;
        private (long Ticks, int Daylight, bool SkyOn, bool LightingOn, ShadowMode Shadows) _last;

        public void Bind(Camera camera, ZoneEnvironment env, int zone, bool underground) {
            _camera = camera;
            _env = env;
            _zone = zone;
            _underground = underground;
            _applied = false; // a new binding is drawn afresh, whatever the clock says
        }

        /// <summary>One frame of the sky, the light and the shadows, from the game clock and the switches.</summary>
        /// <param name="ticks">The game clock, in its two-second unit.</param>
        /// <param name="daylight">The original's daylight level now (15 night .. 64 day).</param>
        /// <param name="shadows">The shadows shown (<see cref="GameOptions.ShadowsShown"/>).</param>
        public void Apply(long ticks, int daylight, bool skyOn, bool lightingOn, ShadowMode shadows = ShadowMode.Off) {
            bool map = _env != null && _env.MapModeOn;
            // The clock moves in 2-second ticks and stands still between steps: nothing to redo.
            if (_applied && map == _appliedMap && _last == (ticks, daylight, skyOn, lightingOn, shadows)) {
                return;
            }
            _applied = true;
            _appliedMap = map;
            _last = (ticks, daylight, skyOn, lightingOn, shadows);
            bool live = _camera != null && !_underground && !map;
            bool sky = skyOn && live;
            bool lit = lightingOn && live;
            SkyDirection sun = Clock.Sun(ticks);
            (double r, double g, double b) = Clock.SunColor(Math.Max(0, sun.ElevationDegrees));
            var sunColor = new Color((float)r, (float)g, (float)b);

            if (sky || lit) {
                PublishSkyGlobals(ticks, sun, sunColor);
            }
            SetDome(sky);
            bool sunShadows = shadows == ShadowMode.Sun && live;
            SetSun(sky || lit || sunShadows, ticks, sun, sunColor, daylight, sunShadows);
            // Blobs wherever the sun casts nothing: underground and at night too; never on the map.
            ShadowBlobs.Show(_camera, shadows != ShadowMode.Off && !map);
            GradingKey grading = lit ? Clock.GradingAt(ticks) : null;
            SetGrade(grading, ticks, sun, sunColor);
            SetBloom(lit && !Application.isMobilePlatform, grading);
            Shader.SetGlobalFloat(SkyOnId, sky ? 1f : 0f);
        }

        /// <summary>No live sky (none bound, or the zone was torn down): leave nothing global behind.</summary>
        public static void Reset(Camera camera) {
            Shader.SetGlobalVector(SunTintId, Vector4.zero);
            Shader.SetGlobalVector(ShadeTintId, Vector4.zero);
            Shader.SetGlobalFloat(SkyOnId, 0f);
            Shader.SetGlobalFloat(SunShadowId, 0f);
            if (camera != null) {
                SetCameraPostProcessing(camera, false);
                ShadowBlobs.Show(camera, false);
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
            // *** THE MAP IS ITS OWN SCREEN. *** While it is up the travel screen stops refreshing the
            // lighting, so nothing calls Apply: notice the switch here, or the map is graded, tinted
            // and has the dome drawn under it (found live at 19:00).
            if (_applied && _env != null && _env.MapModeOn != _appliedMap) {
                Apply(_last.Ticks, _last.Daylight, _last.SkyOn, _last.LightingOn, _last.Shadows);
            }
            if (_dome != null && _dome.activeSelf && _camera != null) {
                _dome.transform.SetPositionAndRotation(_camera.transform.position, Quaternion.identity);
            }
        }

        private void OnDestroy() {
            // The objects too, not only their assets: when the sky is dropped after a failure the zone
            // lives on, and a dome left behind with its material gone draws magenta.
            if (_dome != null) Destroy(_dome);
            if (_sun != null) Destroy(_sun.gameObject);
            if (_volume != null) Destroy(_volume.gameObject);
            if (_domeMaterial != null) Destroy(_domeMaterial);
            if (_domeMesh != null) Destroy(_domeMesh);
            if (_profile != null) {
                foreach (VolumeComponent c in _profile.components) Destroy(c);
                Destroy(_profile);
            }
        }

        /// <summary>
        /// How far the wind has carried the clouds, in the shader's drift unit (TASK-964). It is the game
        /// clock's, not Unity's: the original's time moves only with the party (gstate_advance_time from
        /// the step, WORLDLP.C:193; map travel, MAP.C:216; resting, MODALSCR.C:669/774, ENCAMP.C:149;
        /// dialogs, DIALOG.C:1511), so the clouds stand still when the party does.
        /// </summary>
        /// <remarks>
        /// Five units a game hour, which at a walking pace (a medium step is 60 ticks, two game minutes)
        /// is about the drift the real-time sky had. A step moves them a fraction of a texel. A long jump
        /// (a rest, a map journey) moves them on at once, with the sun and the cover that jump too: an
        /// ease would be clouds racing past while the clock stands still. Float keeps sub-texel steps for
        /// years of game time.
        /// </remarks>
        public static float CloudDrift(long ticks) => ticks / (GameData.Resources.GameState.GameTime.UnitsPerHour / 5f);

        /// <summary>The world is +X east, +Y up, +Z north (BakCoordinateConverter: BaK X→X, Y→Z).</summary>
        private static Vector3 ToVector(SkyDirection d) => new((float)d.East, (float)d.Up, (float)d.North);

        private void PublishSkyGlobals(long ticks, SkyDirection sun, Color sunColor) {
            Shader.SetGlobalVector(SunDirId, ToVector(sun));
            Shader.SetGlobalVector(SunColorId, new Vector4(sunColor.r, sunColor.g, sunColor.b, 1f));
            Shader.SetGlobalVector(MoonLightId, new Vector4(MoonLight.r, MoonLight.g, MoonLight.b, (float)Clock.MoonFullness(0, ticks)));
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
            Shader.SetGlobalVector(CloudId, new Vector4((float)Clock.CloudCover(_zone, ticks),
                Application.isMobilePlatform ? 1f : 2f, CloudDrift(ticks), 0f));
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
                // Async: until a texture arrives the shader's default (black) shows no cloud or moon.
                SetDomeTexture("_CloudsA", "Enhanced/Sky/clouds_cumulus");
                SetDomeTexture("_CloudsB", "Enhanced/Sky/clouds_cirrus");
                SetDomeTexture("_Moon0", "Enhanced/Sky/moon_large");
                SetDomeTexture("_Moon1", "Enhanced/Sky/moon_middle");
                SetDomeTexture("_Moon2", "Enhanced/Sky/moon_small");
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

        private void SetDomeTexture(string property, string address) {
            Addressables.LoadAssetAsync<Texture2D>(address).Completed += loaded => {
                if (_domeMaterial != null) {
                    _domeMaterial.SetTexture(property, loaded.Result);
                }
            };
        }

        private void SetSun(bool on, long ticks, SkyDirection sun, Color sunColor, int daylight, bool castShadows) {
            if (!on) {
                if (_sun != null) _sun.gameObject.SetActive(false);
                Shader.SetGlobalFloat(SunShadowId, 0f);
                return;
            }
            if (_sun == null) {
                var go = new GameObject("Sun");
                go.transform.SetParent(transform, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.None;
                // URP's main light is the sun when one is set; it is the only light the world has.
                RenderSettings.sun = _sun;
            }
            _sun.gameObject.SetActive(true);

            SkyDirection large = Clock.Moon(0, ticks);
            bool moonLight = sun.Up <= 0 && large.Up > 0;
            Vector3 toLight = ToVector(moonLight ? large : sun);
            _sun.transform.rotation = Quaternion.LookRotation(-toLight);
            if (moonLight) {
                _sun.color = MoonLight;
                _sun.intensity = 0.15f * (float)Clock.MoonFullness(0, ticks);
            } else {
                _sun.color = sunColor;
                _sun.intensity = daylight / (float)DaylightLevel.Day;
            }

            // The sun's shadows (TASK-882): none from the moon; from the sun, rising from nothing at the
            // horizon to full strength by about 12 degrees up, so dawn and dusk hand over to the blobs.
            float weight = castShadows && !moonLight ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.2f, (float)sun.Up)) : 0f;
            _sun.shadows = weight > 0f ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = SunShadowStrength * weight;
            Shader.SetGlobalFloat(SunShadowId, weight);
        }

        /// <summary>
        /// The grade for ApplyBakLighting: white balance, the sun's (or the Large Moon's) colour and split
        /// toning, as a tint for bright pixels and one for dark ones, each scaled to luminance 1 so the
        /// shader only moves hue; plus saturation, which turns about the same luminance.
        /// </summary>
        private static void SetGrade(GradingKey k, long ticks, SkyDirection sun, Color sunColor) {
            if (k == null) {
                Shader.SetGlobalVector(SunTintId, Vector4.zero);
                Shader.SetGlobalVector(ShadeTintId, Vector4.zero);
                return;
            }
            Color light;
            float strength;
            if (sun.Up > -0.05) {
                light = sunColor;
                strength = DayTint;
            } else {
                light = MoonLight;
                strength = NightTint * Mathf.Lerp(0.6f, 1f, (float)Clock.MoonFullness(0, ticks));
            }
            Color common = WhiteBalance(k.Temperature, k.Tint) * Color.Lerp(Color.white, ToLuminanceOne(light), strength);
            Color highlights = ToLuminanceOne(common * Toning(k.Highlights));
            Color shadows = ToLuminanceOne(common * Toning(k.Shadows));
            Shader.SetGlobalVector(SunTintId, new Vector4(highlights.r, highlights.g, highlights.b, 1f));
            Shader.SetGlobalVector(ShadeTintId, new Vector4(shadows.r, shadows.g, shadows.b, 1f + ((float)k.Saturation / 100f)));
        }

        /// <summary>Warm (positive temperature) is more red, less blue; positive tint is less green.</summary>
        private static Color WhiteBalance(double temperature, double tint) => new(
            Mathf.Max(0.1f, 1f + (0.003f * (float)temperature)),
            Mathf.Max(0.1f, 1f - (0.002f * (float)tint)),
            Mathf.Max(0.1f, 1f - (0.004f * (float)temperature)));

        /// <summary>A split-toning colour as a multiplier: 0.5 grey is neutral.</summary>
        private static Color Toning((double R, double G, double B) c) =>
            new((float)c.R * 2f, (float)c.G * 2f, (float)c.B * 2f);

        private static Color ToLuminanceOne(Color c) {
            float l = Mathf.Max((0.2126f * c.r) + (0.7152f * c.g) + (0.0722f * c.b), 1e-4f);
            return new Color(c.r / l, c.g / l, c.b / l, 1f);
        }

        /// <summary>
        /// Bloom on the sun and the moons: the only post effect, on desktop. The threshold sits above
        /// anything the world draws (its palette colours stay at or under 1), so the world's brightness
        /// is untouched.
        /// </summary>
        private void SetBloom(bool on, GradingKey grading) {
            if (_camera != null) {
                SetCameraPostProcessing(_camera, on);
            }
            if (!on) {
                if (_volume != null) _volume.gameObject.SetActive(false);
                return;
            }
            if (_volume == null) {
                var go = new GameObject("EnhancedBloom");
                go.transform.SetParent(transform, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 10;
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _bloom = _profile.Add<Bloom>(true);
                _bloom.threshold.Override(1.5f);
                // *** POST-PROCESSING ALSO APPLIES THE PROJECT'S DEFAULT PROFILE. *** It carries a
                // vignette, camera motion blur, a lens flare and Neutral tonemapping
                // (DefaultVolumeProfile.asset), each of which would change the world: all are off here.
                _profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
                _profile.Add<Vignette>(true).intensity.Override(0f);
                _profile.Add<MotionBlur>(true).intensity.Override(0f);
                _profile.Add<ScreenSpaceLensFlare>(true).intensity.Override(0f);
                _volume.sharedProfile = _profile;
            }
            _volume.gameObject.SetActive(true);
            _bloom.intensity.Override((float)grading.Bloom);
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

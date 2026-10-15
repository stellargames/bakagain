namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.Core;
    using BakAgain.World;
    using GameData.Resources.GameState;
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.Rendering;
    using UnityEngine.Rendering.Universal;

    /// <summary>
    /// The Enhanced sky's switches (spec 2026-10-15-enhanced-sky-design): what exists with Sky and
    /// Lighting on or off, over the map, underground, and after the zone is torn down.
    /// </summary>
    public class EnhancedSkyTests {
        private static readonly int SunTintId = Shader.PropertyToID("_BakSunTint");
        private static long At(int hour) => hour * (long)GameTime.UnitsPerHour;

        private GameObject _root;
        private Camera _camera;
        private ZoneEnvironment _env;
        private EnhancedSky _sky;
        private EnhancedPrefsStash _stash;

        [SetUp]
        public void SetUp() {
            _stash = EnhancedPrefsStash.Take();
            _camera = new GameObject("TestWorldCamera").AddComponent<Camera>();
            _root = new GameObject("Zone_Test");
            _env = _root.AddComponent<ZoneEnvironment>();
            _env.SkyColor = new Color(40 / 255f, 81 / 255f, 93 / 255f);
            _sky = _root.AddComponent<EnhancedSky>();
        }

        [TearDown]
        public void TearDown() {
            if (_root != null) Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_camera.gameObject);
            EnhancedSky.Reset(null);
            _stash.Restore();
        }

        private Transform Child(string name) => _root.transform.Find(name);
        private bool Active(string name) => Child(name) != null && Child(name).gameObject.activeSelf;
        private bool PostOn => _camera.GetUniversalAdditionalCameraData().renderPostProcessing;

        [Test]
        public void SkyOnBuildsTheDomeAndLeavesTheClearColour() {
            _sky.Bind(_camera, _env, zone: 1, underground: false);
            Color clear = _camera.backgroundColor;
            _sky.Apply(At(12), 64, skyOn: true, lightingOn: false);
            Assert.IsTrue(Active("EnhancedSkyDome"));
            Assert.AreEqual(clear, _camera.backgroundColor);
            Assert.IsFalse(PostOn, "Sky alone does not grade");
        }

        [Test]
        public void SkyOffHidesTheDome() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(12), 64, true, false);
            _sky.Apply(At(12), 64, false, false);
            Assert.IsFalse(Active("EnhancedSkyDome"));
        }

        [Test]
        public void UndergroundHasNoDomeAndNoSun() {
            _sky.Bind(_camera, _env, 10, underground: true);
            _sky.Apply(At(12), 64, true, true);
            Assert.IsFalse(Active("EnhancedSkyDome"));
            Assert.IsFalse(Active("Sun"));
            Assert.IsFalse(PostOn);
        }

        [Test]
        public void TheMapHidesEverything() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(7), 40, true, true);
            _env.SetOverheadMapMode(_camera, true, 1000f, 600f, 9);
            _sky.Apply(At(7), 40, true, true);
            Assert.IsFalse(Active("EnhancedSkyDome"));
            Assert.IsFalse(PostOn);
            Assert.AreEqual(0f, Shader.GetGlobalVector(SunTintId).w);
        }

        /// <summary>
        /// The overhead map is its own screen: the travel screen stops refreshing the lighting while it
        /// is up, so nothing calls Apply. The sky must notice the switch by itself (found live: the map
        /// at 19:00 was graded and tinted).
        /// </summary>
        [UnityTest]
        public IEnumerator TheMapIsUngradedEvenWithoutARefresh() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(19), 30, true, true);
            Assert.IsTrue(PostOn);
            _env.SetOverheadMapMode(_camera, true, 1000f, 600f, 9);
            yield return null;
            Assert.IsFalse(PostOn, "graded map");
            Assert.IsFalse(Active("EnhancedSkyDome"), "dome over the map");
            Assert.AreEqual(0f, Shader.GetGlobalVector(SunTintId).w, "tinted map");
            _env.SetOverheadMapMode(_camera, false, 1000f, 600f, 9);
            yield return null;
            Assert.IsTrue(PostOn, "back in travel, graded again");
            Assert.IsTrue(Active("EnhancedSkyDome"));
        }

        [Test]
        public void LightingOnTurnsOnPostTheSunAndTheGrading() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(7), 40, false, true);
            Assert.IsTrue(PostOn);
            Light sun = Child("Sun").GetComponent<Light>();
            Assert.AreEqual(LightType.Directional, sun.type);
            Assert.IsTrue(sun.isActiveAndEnabled);
            Volume volume = Child("EnhancedBloom").GetComponent<Volume>();
            Assert.IsTrue(volume.isGlobal);
            Assert.IsTrue(volume.profile.TryGet(out Bloom bloom));
            Assert.Greater(bloom.intensity.value, 0f, "desktop blooms the sun and moons");
            Assert.Greater(Shader.GetGlobalVector(SunTintId).w, 0f, "07:00 is tinted");
        }

        /// <summary>
        /// Switching post-processing on also applies the project's default volume profile, which carries a
        /// vignette, motion blur and a lens flare: the grading must neutralise them (review finding).
        /// </summary>
        [Test]
        public void TheGradingAddsNoLensEffects() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(12), 64, false, true);
            VolumeManager.instance.Update(_camera.transform, ~0);
            VolumeStack stack = VolumeManager.instance.stack;
            Assert.AreEqual(0f, stack.GetComponent<Vignette>().intensity.value, "vignette");
            Assert.AreEqual(0f, stack.GetComponent<MotionBlur>().intensity.value, "motion blur");
            Assert.AreEqual(0f, stack.GetComponent<ScreenSpaceLensFlare>().intensity.value, "lens flare");
            Assert.AreEqual(TonemappingMode.None, stack.GetComponent<Tonemapping>().mode.value,
                "tonemapping would change the world's brightness; the grade is luminance-keeping instead");
        }

        [Test]
        public void TheSunLightPointsAwayFromTheSun() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(9), 64, false, true);
            Vector3 toSun = Shader.GetGlobalVector("_BakSkySunDir");
            Assert.Greater(Vector3.Dot(Child("Sun").forward, -toSun), 0.999f);
            Assert.Greater(toSun.x, 0f, "morning sun is in the east (+X)");
        }

        [Test]
        public void LightingOffRestoresTheCamera() {
            _sky.Bind(_camera, _env, 1, false);
            _sky.Apply(At(7), 40, false, true);
            _sky.Apply(At(7), 40, false, false);
            Assert.IsFalse(PostOn);
            Assert.AreEqual(0f, Shader.GetGlobalVector(SunTintId).w);
            Assert.IsFalse(Active("EnhancedBloom"));
        }

        [Test]
        public void ATornDownZoneLeavesNoTintAndNoPost() {
            GameOptions.Enhanced = true;
            GameOptions.SetFeature(EnhancedFeature.Lighting, true);
            var session = new GameSession { GameTimeIn2Seconds = At(7) };
            var lighting = new WorldLightingService(session, null);
            Object.DestroyImmediate(_sky);
            lighting.BindSky(_camera, _env, 1, false);
            lighting.Refresh();
            Assert.IsTrue(PostOn, "bound and lit");

            Object.DestroyImmediate(_root);
            lighting.Refresh();

            Assert.IsFalse(PostOn);
            Assert.AreEqual(0f, Shader.GetGlobalVector(SunTintId).w);
        }
    }
}

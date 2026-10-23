namespace BakAgain.Tests.PlayMode.UI {
    using BakAgain.Core.DI;
    using NUnit.Framework;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;

    /// <summary>
    /// The pillarbox is wherever the UI does not draw, so it is only black, and only free of the last
    /// screen's frames, if some camera clears the screen every frame (TASK-320, TASK-952).
    /// </summary>
    /// <remarks>
    /// <b>Not through <c>Camera.main</c>.</b> Main.unity's camera is untagged, so the lookup found
    /// nothing and the bars showed the scene's brown on every boot. A scene with no screen camera at
    /// all (the PlayMode test scene, where TASK-947's drive booted) cleared nothing: the combat pack's
    /// fade-out stayed drawn in the bars after it closed.
    /// </remarks>
    public class ClearBehindUiToBlackTests {
        private readonly List<Camera> _parked = new();
        private readonly List<GameObject> _made = new();

        [SetUp]
        public void ParkTheScreenCameras() {
            // Whatever drew to the screen before this test (the scene's, or one an earlier boot made)
            // is set aside, so each test states its own scene.
            foreach (Camera cam in Camera.allCameras.Where(c => c.targetTexture == null)) {
                cam.enabled = false;
                _parked.Add(cam);
            }
        }

        [TearDown]
        public void Restore() {
            foreach (Camera cam in Camera.allCameras) {
                if (!_made.Contains(cam.gameObject) && !_parked.Contains(cam)) {
                    _made.Add(cam.gameObject);   // the one the call under test made
                }
            }
            foreach (GameObject go in _made) {
                Object.DestroyImmediate(go);
            }
            foreach (Camera cam in _parked.Where(c => c != null)) {
                cam.enabled = true;
            }
            _made.Clear();
            _parked.Clear();
        }

        private Camera MakeCamera(string name, Color background, RenderTexture target = null) {
            var go = new GameObject(name);   // untagged, as Main.unity's is
            _made.Add(go);
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.targetTexture = target;
            return cam;
        }

        [Test]
        public void TheScenesUntaggedScreenCameraClearsToBlack_AndTheWorldCameraKeepsItsSky() {
            var sceneBrown = new Color(0.147f, 0.073f, 0.020f, 1f);
            var sky = new Color(0.157f, 0.318f, 0.365f, 1f);
            Camera screen = MakeCamera("Main Camera", sceneBrown);
            var rt = new RenderTexture(16, 16, 0);
            Camera world = MakeCamera("WorldCamera", sky, rt);
            Assume.That(Camera.main, Is.Null, "the scene camera is untagged, as Main.unity's is");

            GameInitializationService.ClearBehindUiToBlack();

            // Control first: a camera drawing into a texture is the world's, and keeps its sky.
            Assert.That(world.backgroundColor, Is.EqualTo(sky), "the world camera is not the screen's");
            Assert.That(screen.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(screen.backgroundColor, Is.EqualTo(Color.black), "the screen camera clears the bars to black");
            world.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        [Test]
        public void WithNoScreenCamera_OneIsMadeThatClearsTheScreenBlackEveryFrame() {
            Assume.That(Camera.allCameras.Any(c => c.targetTexture == null), Is.False, "no camera draws to the screen");

            GameInitializationService.ClearBehindUiToBlack();

            Camera[] screens = Camera.allCameras.Where(c => c.targetTexture == null).ToArray();
            Assert.That(screens, Has.Length.EqualTo(1), "something clears the screen, or old frames stay in the bars");
            Assert.That(screens[0].clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(screens[0].backgroundColor, Is.EqualTo(Color.black));
            Assert.That(screens[0].cullingMask, Is.EqualTo(0), "it only clears: the world is drawn by its own camera");
        }
    }
}

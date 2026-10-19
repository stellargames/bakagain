namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Rendering;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// The Enhanced shadows as drawn (TASK-882, spec 2026-10-15-enhanced-shadows-design): a billboard
    /// casts its full silhouette whichever way the sun stands, a sprite darkens only in something else's
    /// shadow, and a blob gives way to the sun's shadows near by and takes over past their reach.
    /// </summary>
    /// <remarks>
    /// A white world with every lighting movement at pass-through, so a pixel's darkness is the shadow
    /// alone. The real zone at three hours is the live check (TASK-882 notes).
    /// </remarks>
    [Category("Graphics")]
    public class EnhancedShadowRenderTests {
        private const int Size = 128;
        private static readonly Vector3 Origin = new(7000f, 0f, 7000f);

        private GameObject _root;
        private Light _sun;
        private Camera _camera;
        private RenderTexture _rt;
        private Texture2D _read;
        private Texture2D _white;
        private bool _fog;

        [SetUp]
        public void SetUp() {
            _fog = RenderSettings.fog;
            RenderSettings.fog = false;
            Shader.SetGlobalFloat("_MapMode", 0f);
            foreach (string id in new[] { "_BakLightDragonsBreath", "_BakLightTint", "_BakLightDarken", "_BakFlash", "_BakSunTint" }) {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
            _root = new GameObject("ShadowTestZone");
            _sun = new GameObject("Sun").AddComponent<Light>();
            _sun.transform.SetParent(_root.transform, false);
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.35f;
            // From the east (+X), 35 degrees up.
            Vector3 toLight = Quaternion.AngleAxis(35f, Vector3.forward) * Vector3.right;
            _sun.transform.rotation = Quaternion.LookRotation(-toLight);
            RenderSettings.sun = _sun;

            _camera = new GameObject("ShadowTestCamera").AddComponent<Camera>();
            _camera.transform.SetParent(_root.transform, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.white;
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _camera.targetTexture = _rt;
            _read = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, linear: true);
            _white = new Texture2D(4, 4);
            var white = new Color[16];
            System.Array.Fill(white, Color.white);
            _white.SetPixels(white);
            _white.Apply();
        }

        [TearDown]
        public void TearDown() {
            foreach (MeshFilter f in _root.GetComponentsInChildren<MeshFilter>()) {
                if (f.gameObject.layer != ShadowBlobs.Layer) Object.DestroyImmediate(f.sharedMesh);
            }
            foreach (Renderer r in _root.GetComponentsInChildren<Renderer>()) {
                if (r.gameObject.layer != ShadowBlobs.Layer) Object.DestroyImmediate(r.sharedMaterial);
            }
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_read);
            Object.DestroyImmediate(_rt);
            Object.DestroyImmediate(_white);
            Shader.SetGlobalFloat("_BakSunShadow", 0f);
            RenderSettings.fog = _fog;
        }

        private GameObject Ground(float half) {
            var go = new GameObject("Ground");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = Origin;
            Color32 w = Color.white;
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh {
                vertices = new[] { new Vector3(-half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half), new Vector3(half, 0, -half) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                colors32 = new[] { w, w, w, w },
            };
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("BakAgain/EnhancedPolygon"));
            r.shadowCastingMode = ShadowCastingMode.Off;   // as the game's ground (WorldEntityBuilder)
            return go;
        }

        /// <summary>A billboard 2 wide and 4 tall, standing on its pivot (TblSpriteConverter's quad shape).</summary>
        private GameObject Sprite(Vector3 at) {
            var go = new GameObject("Sprite");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = at;
            go.transform.localScale = new Vector3(2f, 4f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh {
                vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(-0.5f, 1, 0), new Vector3(0.5f, 1, 0), new Vector3(0.5f, 0, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("BakAgain/EnhancedSprite")) { mainTexture = _white };
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        private Color[] Render() {
            _camera.Render();
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = null;
            return _read.GetPixels();
        }

        private static int Darker(Color[] px, float below) {
            int n = 0;
            foreach (Color c in px) if (c.grayscale < below) n++;
            return n;
        }

        private void LookDown(float yaw, float height) {
            _camera.orthographic = true;
            _camera.orthographicSize = 12f;
            _camera.farClipPlane = height + 50f;
            _camera.transform.SetPositionAndRotation(Origin + (Vector3.up * height), Quaternion.Euler(90f, yaw, 0f));
        }

        [Test]
        public void ABillboardCastsItsFullSilhouetteFromAnySunHeading() {
            Ground(30f);
            Sprite(Origin);
            LookDown(0f, 30f);

            int east = Darker(Render(), 0.85f);
            var fromNorth = new Vector3(0f, Mathf.Sin(35f * Mathf.Deg2Rad), Mathf.Cos(35f * Mathf.Deg2Rad));
            _sun.transform.rotation = Quaternion.LookRotation(-fromNorth);
            int north = Darker(Render(), 0.85f);

            // About 2 x 5.7 units at 5.3 px a unit: ~320 px of shade from either side.
            Assert.Greater(east, 150, "the sprite casts its silhouette");
            Assert.That(Mathf.Abs(east - north) / (float)Mathf.Max(east, north), Is.LessThan(0.1f),
                $"{east} px with the sun in the east, {north} in the north");
        }

        [Test]
        public void ASpriteDarkensInAnothersShadowButNotInItsOwn() {
            Ground(30f);
            Sprite(Origin);
            // From a little above, as the party sees it. (A level orthographic camera read no shadow
            // here at all, sprite or ground; why was not looked into.)
            _camera.fieldOfView = 40f;
            _camera.transform.position = Origin + new Vector3(0f, 6f, -14f);
            _camera.transform.LookAt(Origin + new Vector3(0f, 1.5f, 0f));
            // The sprite's middle, which is all sprite.
            float Middle() {
                Color[] px = Render();
                return px[(Size / 2 * Size) + (Size / 2)].grayscale;
            }

            float inSun = Middle();
            Assert.Greater(inSun, 0.95f, "its own shadow starts at its feet and must not shade it");

            // A wall to the east, between it and the sun.
            GameObject wall = Sprite(Origin + new Vector3(3f, 0f, 0f));
            wall.transform.localScale = new Vector3(12f, 12f, 1f);
            wall.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;   // out of the picture

            float inShade = Middle();
            Assert.That(inShade, Is.EqualTo(0.65f).Within(0.05f), "35 % darker in the wall's shadow");
        }

        [Test]
        public void ABlobGivesWayToTheSunNearByAndTakesOverBeyondItsReach() {
            Ground(30f);
            GameObject sprite = Sprite(Origin);
            sprite.GetComponent<Renderer>().enabled = false;
            ShadowBlobs.AttachToSprite(sprite);
            _sun.shadows = LightShadows.None;   // the blob alone

            float Centre(float height) {
                LookDown(0f, height);
                return Render()[(Size / 2 * Size) + (Size / 2)].grayscale;
            }

            Shader.SetGlobalFloat("_BakSunShadow", 0f);
            Assert.Less(Centre(30f), 0.7f, "no sun shadows: the blob, full");
            Shader.SetGlobalFloat("_BakSunShadow", 1f);
            Assert.Greater(Centre(30f), 0.98f, "the sun's shadows near by: no blob");
            Assert.Less(Centre(450f), 0.7f, "past 400 units the sun casts nothing: the blob again");
        }

        [Test]
        public void PropsGetABlobInTheirFootprint_ArchitectureDoesNot() {
            var chest = new GameObject("chest");
            chest.transform.SetParent(_root.transform, false);
            GameObject blob = ShadowBlobs.AttachToModel(chest, new Bounds(new Vector3(0.5f, 1f, 0f), new Vector3(2.2f, 2f, 1.7f)));
            Assert.IsNotNull(blob);
            Assert.AreEqual(ShadowBlobs.Layer, blob.layer, "on the layer the camera switches");
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), blob.transform.localPosition, "centred under it, on its base");
            Assert.AreEqual(new Vector3(2.2f, 1f, 1.7f), blob.transform.localScale);

            var room = new GameObject("m_rm1");
            room.transform.SetParent(_root.transform, false);
            Assert.IsNull(ShadowBlobs.AttachToModel(room, new Bounds(Vector3.zero, new Vector3(16f, 6f, 16f))),
                "a mine room's blob lay on its open floor");
        }
    }
}

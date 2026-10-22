namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.World.Converters;
    using GameData.Resources.World;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// A billboard is culled by what it draws, not by its quad as built (TASK-943). The sprite shader
    /// turns the quad to the camera about its pivot (SpritePass.hlsl, BillboardWorldPos), so a quad built
    /// side-on to the camera has bounds that are a sliver round the pivot: turning in place, a tree
    /// whose pivot had just left the screen vanished while half of it was still drawn.
    /// </summary>
    [Category("Graphics")]
    public class BillboardCullingTests {
        private const int Size = 128;
        private const float Depth = 20f;
        private const float HalfFov = 15f;
        private static readonly Vector3 Origin = new(7000f, 0f, 7000f);

        // Anchored at the middle of its foot: 20x20 source pixels, texture 5x6 per pixel.
        private static readonly SpriteBMeshFace Face = new() { AnchorX = 10, AnchorY = 20, SizeScale = 0 };

        private GameObject _root;
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
            foreach (string id in new[] { "_BakSpriteNearHide", "_BakSpriteNearShow", "_BakSpriteFogStart", "_BakSpriteFogEnd" }) {
                Shader.SetGlobalFloat(id, 0f);
            }
            foreach (string id in new[] { "_BakLightDragonsBreath", "_BakLightTint", "_BakLightDarken", "_BakFlash", "_BakSunTint" }) {
                Shader.SetGlobalVector(id, Vector4.zero);
            }
            _root = new GameObject("BillboardCullingZone");
            _camera = new GameObject("BillboardCullingCamera").AddComponent<Camera>();
            _camera.transform.SetParent(_root.transform, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.fieldOfView = HalfFov * 2f;
            // Looking along +X, so a quad built in its own XY plane stands side-on to the view.
            _camera.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            _camera.targetTexture = _rt;
            _camera.aspect = 1f;
            _read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            _white = new Texture2D(4, 4);
            var white = new Color[16];
            System.Array.Fill(white, Color.white);
            _white.SetPixels(white);
            _white.Apply();
        }

        [TearDown]
        public void TearDown() {
            foreach (MeshFilter f in _root.GetComponentsInChildren<MeshFilter>()) Object.DestroyImmediate(f.sharedMesh);
            foreach (Renderer r in _root.GetComponentsInChildren<Renderer>()) Object.DestroyImmediate(r.sharedMaterial);
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_read);
            Object.DestroyImmediate(_rt);
            Object.DestroyImmediate(_white);
            RenderSettings.fog = _fog;
        }

        /// <summary>A billboard as the game builds one, its pivot <paramref name="pastEdge"/> of its width
        /// beyond the left edge of the view. Returns the renderer.</summary>
        private Renderer Sprite(float pastEdge) {
            (Mesh mesh, Vector3 scale) = TblSpriteConverter.BuildBillboard(Face, 500, 100, 120);
            float width = Mathf.Abs(scale.x);
            float edge = Depth * Mathf.Tan(HalfFov * Mathf.Deg2Rad);
            var go = new GameObject("Tree");
            go.transform.SetParent(_root.transform, false);
            // The camera's right is world -Z, so its left edge is toward +Z.
            go.transform.position = Origin + new Vector3(Depth, 0f, edge + (pastEdge * width));
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("BakAgain/ClassicSprite")) { mainTexture = _white };
            // The sprite's middle at eye height.
            _camera.transform.position = Origin + new Vector3(0f, scale.y / 2f, 0f);
            return r;
        }

        private int LitPixels() {
            _camera.Render();
            RenderTexture.active = _rt;
            _read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = null;
            int n = 0;
            foreach (Color c in _read.GetPixels()) if (c.grayscale > 0.5f) n++;
            return n;
        }

        [Test]
        public void ABillboardWhosePivotIsOffScreenIsDrawnWhileItsNearHalfIsOnScreen() {
            // A quarter of its width is on screen: the pivot is a quarter-width past the edge.
            Renderer r = Sprite(0.25f);
            Assert.Greater(LitPixels(), 100, $"the on-screen quarter is drawn at the left edge (bounds {r.bounds})");
            Assert.That(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(_camera), r.bounds),
                $"bounds {r.bounds} hold the quad turned to the camera");
        }

        [Test]
        public void ABillboardWhollyOffScreenIsStillCulled() {
            // Its near edge a quarter-width past the screen's.
            Renderer r = Sprite(0.75f);
            Assert.IsFalse(GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(_camera), r.bounds),
                $"bounds {r.bounds} stop at what it draws");
            Assert.AreEqual(0, LitPixels());
        }
    }
}

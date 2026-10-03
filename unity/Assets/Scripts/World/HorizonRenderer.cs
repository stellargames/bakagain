namespace BakAgain.World {
    using UnityEngine;

    /// <summary>
    /// Classic mode horizon: stitches 4 BMX panels into a wide texture and scrolls
    /// UV based on camera heading. Panel order [3,0,1,2,3,0] wraps seamlessly.
    /// Placed at far clip distance behind world geometry.
    /// </summary>
    public class HorizonRenderer : MonoBehaviour {
        private Material _material;
        private Camera _camera;

        /// <summary>
        /// The panels' own sky colour (top row of the bitmap). In the original's bitmap-horizon
        /// mode the visible sky comes from the panel, not the DEF <c>skyColor</c> pen — so this is
        /// the colour to clear the camera to behind the mountains.
        /// </summary>
        public Color PanelSkyColor { get; private set; } = Color.black;

        // Camera-relative placement of the skyline backdrop (the quad is built from these).
        // The strip is FOV-aware: LateUpdate scales Y so it occupies a constant *screen* fraction
        // regardless of camera FOV (the original draws the horizon bitmap at a fixed viewport height;
        // the real game uses a narrow ~11° vFOV, WorldTestState a wide 60°).
        private const float Distance = 400f;    // in front of the camera, behind world geometry

        /// <summary>Bind the world camera; the backdrop follows it (yaw only) and scrolls by heading.</summary>
        public void SetCamera(Camera worldCamera) => _camera = worldCamera;

        private void LateUpdate() {
            if (_camera == null || _material == null) return;
            // Follow the camera position and yaw ONLY (stay level — ignore pitch/roll) so the
            // backdrop reads as a distant horizon. The mountains' apparent motion comes from the
            // UV scroll, mirroring the original's per-heading panel offset (drawHorizonSkyAndGround).
            float yaw = _camera.transform.eulerAngles.y;
            transform.SetPositionAndRotation(_camera.transform.position, Quaternion.Euler(0f, yaw, 0f));
            // *** SCREEN SPACE AT NATIVE SIZE (TASK-760). *** The quad exactly fills the viewport's
            // width at Distance and is the panorama's own rows tall (HorizonPanorama), base on the
            // horizon (eye level); the UV window then shows the slice the original blits.
            float tanV = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * _camera.aspect;
            transform.localScale = new Vector3(2f * Distance * tanH,
                2f * Distance * tanV * (float)GameData.Resources.World.HorizonPanorama.HeightFraction(_panelVgaHeight), 1f);
            UpdateHeading(yaw);
        }

        /// <summary>
        /// Initialize with 4 horizon panel textures (N, E, S, W order from BMX).
        /// </summary>
        public void Initialize(Texture2D[] panels) {
            if (panels == null || panels.Length < 4) return;

            // The ring in panel order; the texture repeats, so it wraps by itself.
            int panelWidth = panels[0].width;
            int panelHeight = panels[0].height;
            int totalWidth = panelWidth * GameData.Resources.World.HorizonPanorama.PanelCount;
            _panelVgaWidth = Mathf.Max(1, panelWidth / BakAgain.Graphics.Canonical.VgaScaleX);
            _panelVgaHeight = Mathf.Max(1, panelHeight / BakAgain.Graphics.Canonical.VgaScaleY);

            var stitched = new Texture2D(totalWidth, panelHeight, TextureFormat.RGBA32, false) {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };

            for (int i = 0; i < GameData.Resources.World.HorizonPanorama.PanelCount; i++) {
                var pixels = panels[i].GetPixels();
                stitched.SetPixels(i * panelWidth, 0, panelWidth, panelHeight, pixels);
            }

            // Fill top 34 pixels with solid color from first pixel
            Color topColor = panels[0].GetPixel(0, panelHeight - 1);
            var topFill = new Color[totalWidth * 34];
            for (int j = 0; j < topFill.Length; j++) topFill[j] = topColor;
            stitched.SetPixels(0, panelHeight - 34, totalWidth, 34, topFill);

            stitched.Apply();
            PanelSkyColor = topColor;

            // Skybox-like backdrop shader: Background queue + ZWrite Off so it always renders BEHIND
            // world geometry (the quad sits only ~Distance units ahead of the camera, so otherwise
            // anything farther would draw behind it). _BaseMap is [MainTexture]-tagged, so the
            // mainTexture / mainTextureOffset accessors drive it (and the heading UV scroll).
            _material = new Material(Shader.Find("BakAgain/ClassicHorizon"));
            _material.mainTexture = stitched;

            // Create quad mesh
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = CreateHorizonQuad();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.material = _material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// Free the three assets Initialize creates — the stitched panel texture, the backdrop
        /// material and the quad mesh. Destroying this GameObject releases none of them on its own.
        /// </summary>
        private void OnDestroy() {
            if (_material != null) {
                if (_material.mainTexture != null) Destroy(_material.mainTexture);
                Destroy(_material);
            }
            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
        }

        private int _panelVgaWidth = 256;
        private int _panelVgaHeight = 29;

        /// <summary>The UV window for a camera yaw in degrees: the ring slice the original blits.</summary>
        public void UpdateHeading(float yawDegrees) {
            if (_material == null) return;
            int yaw16 = GameData.Resources.World.HorizonPanorama.YawFromClockwiseDegrees(yawDegrees);
            _material.mainTextureScale = new Vector2(
                (float)GameData.Resources.World.HorizonPanorama.VisibleRingFraction(_panelVgaWidth), 1f);
            _material.mainTextureOffset = new Vector2(
                (float)GameData.Resources.World.HorizonPanorama.LeftEdgeRingFraction(yaw16, _panelVgaWidth), 0f);
        }

        private static Mesh CreateHorizonQuad() {
            var mesh = new Mesh { name = "HorizonQuad" };
            const float hw = 0.5f;        // unit quad; LateUpdate scales it to the viewport
            const float yb = 0f;          // base at eye level → projects to the horizon
            const float yt = 1f;
            mesh.vertices = new[] {
                new Vector3(-hw, yb, Distance),
                new Vector3(hw, yb, Distance),
                new Vector3(hw, yt, Distance),
                new Vector3(-hw, yt, Distance)
            };
            mesh.uv = new[] {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}

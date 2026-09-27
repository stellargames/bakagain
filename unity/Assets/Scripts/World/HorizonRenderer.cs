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
        private const float QuadWidth = 1000f;  // over-spans the horizontal FOV at Distance (no edge gap)
        private const float QuadHeight = 70f;    // strip height above the horizon, calibrated at RefFov
        private const float RefFov = 60f;        // FOV at which QuadHeight gives the intended look
        private static readonly float RefTanHalf = Mathf.Tan(RefFov * 0.5f * Mathf.Deg2Rad);

        /// <summary>Bind the world camera; the backdrop follows it (yaw only) and scrolls by heading.</summary>
        public void SetCamera(Camera worldCamera) => _camera = worldCamera;

        private void LateUpdate() {
            if (_camera == null || _material == null) return;
            // Follow the camera position and yaw ONLY (stay level — ignore pitch/roll) so the
            // backdrop reads as a distant horizon. The mountains' apparent motion comes from the
            // UV scroll, mirroring the original's per-heading panel offset (drawHorizonSkyAndGround).
            float yaw = _camera.transform.eulerAngles.y;
            transform.SetPositionAndRotation(_camera.transform.position, Quaternion.Euler(0f, yaw, 0f));
            // FOV-aware vertical size: keep the strip a constant fraction of the viewport height by
            // scaling Y with tan(vFOV/2). The quad base is at local y=0 (eye level → screen centre),
            // so scaling around the origin anchors the mountain base on the horizon. X is NOT scaled
            // (the quad over-spans horizontally, so a narrow FOV just shows a centred slice — no gap).
            float tanHalf = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            transform.localScale = new Vector3(1f, tanHalf / RefTanHalf, 1f);
            UpdateHeading(yaw);
        }

        /// <summary>
        /// Initialize with 4 horizon panel textures (N, E, S, W order from BMX).
        /// </summary>
        public void Initialize(Texture2D[] panels) {
            if (panels == null || panels.Length < 4) return;

            // Stitch order: [W=3, N=0, E=1, S=2, W=3, N=0] for seamless wrap
            int panelWidth = panels[0].width;
            int panelHeight = panels[0].height;
            int totalWidth = panelWidth * 6;

            var stitched = new Texture2D(totalWidth, panelHeight, TextureFormat.RGBA32, false) {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };

            int[] order = { 3, 0, 1, 2, 3, 0 };
            for (int i = 0; i < 6; i++) {
                var pixels = panels[order[i]].GetPixels();
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

        /// <summary>Update UV offset based on camera yaw (0-360 degrees).</summary>
        public void UpdateHeading(float yawDegrees) {
            if (_material == null) return;
            // Map 0-360 to 0-1 UV offset (panel 1 starts at 1/6)
            float offset = (yawDegrees / 360f) + (1f / 6f);
            _material.mainTextureOffset = new Vector2(offset, 0);
        }

        private static Mesh CreateHorizonQuad() {
            var mesh = new Mesh { name = "HorizonQuad" };
            const float hw = QuadWidth * 0.5f;
            const float yb = 0f;          // base at eye level → projects to the horizon (screen centre); FOV-scaled around here
            const float yt = QuadHeight;  // strip rises above the horizon
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

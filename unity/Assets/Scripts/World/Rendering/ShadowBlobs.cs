namespace BakAgain.World.Rendering {
    using UnityEngine;
    using UnityEngine.Rendering;

    /// <summary>
    /// Enhanced blob shadows (TASK-882, spec 2026-10-15-enhanced-shadows-design): a soft dark ellipse on
    /// the ground under each sprite and model, drawn by one shared material (EnhancedBlob.shader).
    /// </summary>
    /// <remarks>
    /// <b>Every entity gets its blob, whatever the mode; the camera decides whether it is drawn.</b>
    /// Blobs live on their own layer, which <see cref="Show"/> puts in or takes out of the world camera's
    /// culling mask, so switching Shadows or Enhanced mid-game needs no walk over the zone. A blob is a
    /// child of its entity, so it moves, hides and dies with it. How much of a blob shows in Sun mode is
    /// the shader's business (it crossfades with the sun shadows).
    /// </remarks>
    public static class ShadowBlobs {
        public const string LayerName = "ShadowBlob";

        /// <summary>
        /// The widest model that gets a blob, in world units: props (a chest is about 2) do; architecture
        /// (mine corridors and rooms are 8 to 16, houses and hills more) does not, as an ellipse under a
        /// room lies on its open floor (seen live underground, 2026-10-19).
        /// </summary>
        public const float MaxModelFootprint = 4f;

        /// <summary>A blob's width against its sprite's (a figure's feet are narrower than its arms).</summary>
        private const float SpriteFootprint = 0.7f;

        private static Material s_material;
        private static Mesh s_mesh;

        /// <summary>The blob layer, or -1 when the project has none (then no blob is made).</summary>
        public static int Layer => LayerMask.NameToLayer(LayerName);

        /// <summary>Puts a blob under a billboard sprite: a circle as wide as the sprite's feet.</summary>
        public static GameObject AttachToSprite(GameObject sprite) {
            Mesh quad = sprite.GetComponent<MeshFilter>()?.sharedMesh;
            if (quad == null) return null;
            // Local space carries the sprite's scale and mirror; the circle is set in world units and
            // divided back out, so it stays round whichever way the billboard turns.
            // The quad is a unit wide (its bounds cover its turns, TblSpriteConverter.CoverTurns).
            Vector3 s = sprite.transform.localScale;
            float diameter = Mathf.Abs(s.x) * SpriteFootprint;
            return Attach(sprite.transform, Vector3.zero, new Vector3(diameter / s.x, 1f, diameter / Mathf.Max(Mathf.Abs(s.z), 1e-4f)));
        }

        /// <summary>Puts a blob under a model, the ellipse in its footprint: its mesh bounds, in its own
        /// space, seen from above.</summary>
        public static GameObject AttachToModel(GameObject model, Bounds local) =>
            local.size.x <= 0f || local.size.z <= 0f || Mathf.Max(local.size.x, local.size.z) > MaxModelFootprint
                ? null
                : Attach(model.transform, new Vector3(local.center.x, local.min.y, local.center.z),
                    new Vector3(local.size.x, 1f, local.size.z));

        /// <summary>Whether a camera draws the blobs.</summary>
        public static void Show(Camera camera, bool on) {
            int layer = Layer;
            if (camera == null || layer < 0) return;
            int mask = on ? camera.cullingMask | (1 << layer) : camera.cullingMask & ~(1 << layer);
            if (mask != camera.cullingMask) camera.cullingMask = mask;
        }

        private static GameObject Attach(Transform parent, Vector3 localPosition, Vector3 localScale) {
            int layer = Layer;
            if (layer < 0) return null;
            var go = new GameObject("ShadowBlob") { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static Material Material() {
            if (s_material == null) {
                s_material = new Material(Shader.Find("BakAgain/EnhancedBlob")) { name = "ShadowBlob" };
            }
            return s_material;
        }

        /// <summary>A unit quad flat on the ground (XZ), centred on the origin, UVs 0..1.</summary>
        private static Mesh Quad() {
            if (s_mesh == null) {
                s_mesh = new Mesh {
                    name = "ShadowBlob",
                    vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) },
                    uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                    triangles = new[] { 0, 2, 1, 1, 2, 3 },
                };
                s_mesh.RecalculateBounds();
            }
            return s_mesh;
        }
    }
}

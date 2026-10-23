namespace BakAgain.World {
    using UnityEngine;

    /// <summary>
    /// Turns a sprite's transform the way the sprite shader turns its picture, so what reads the
    /// transform — the pick collider, a damage float's origin — agrees with what is drawn.
    /// </summary>
    /// <remarks>
    /// <b>The picture is turned in the shader, not by this.</b> SpritePass.hlsl lays the quad's x
    /// along the rendering camera's flattened right vector and its y up, every frame, whatever the
    /// transform says. This keeps the transform on that same plane: local x = the camera's flat
    /// right, local z = its flat forward.
    ///
    /// <para><b>It faces the WORLD camera, <see cref="Viewer"/>, not <c>Camera.main</c>.</b> The
    /// world camera is untagged; <c>Camera.main</c> is the idle UI camera at (0,1,-10). Facing that,
    /// every pick collider stood edge-on to the party from some headings — a quarter of a sign
    /// or a body could be clicked from the south-east — and, as a look-at that made local x the
    /// camera's LEFT, an off-centre anchor put the collider on the wrong side of its pivot
    /// (TASK-945).</para>
    /// </remarks>
    public class BillboardSprite : MonoBehaviour {
        /// <summary>The camera billboards face: the world camera, set by
        /// <see cref="WorldRuntime"/> when it makes one. <c>Camera.main</c> while there is none
        /// (the debug viewers own a tagged camera of their own).</summary>
        public static Camera Viewer { get; set; }

        // Turned on enable as well as every frame, so a sprite built and read in the same frame (a
        // fight's redraw, then its damage float) is already facing the right way.
        private void OnEnable() => Face(transform);

        private void LateUpdate() => Face(transform);

        private static void Face(Transform sprite) {
            Camera cam = Viewer != null ? Viewer : Camera.main;
            if (cam == null) {
                return;
            }
            // SpritePass.hlsl CameraRightFlat, including its fallback for a camera rolled on its side.
            Vector3 right = cam.transform.right;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-8f ? right.normalized : Vector3.right;
            sprite.rotation = Quaternion.LookRotation(Vector3.Cross(right, Vector3.up));
        }

        /// <summary>The top centre of the drawn picture, in world space — where the original floats a
        /// damage number from (CACTOR.C combat_actor_draw_float_damage: x = the billboard's left +
        /// width / 2, y = its top - 5).</summary>
        /// <remarks>Not the renderer's bounds: those are a column round the pivot that covers every
        /// turn (TblSpriteConverter.CoverTurns), so their centre is the anchor, not the picture.</remarks>
        public static Vector3 DrawnTopCentre(Transform sprite) {
            Mesh mesh = sprite.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) {
                return sprite.position;
            }
            if (sprite.GetComponent<BillboardSprite>() != null) {
                Face(sprite);
            }
            Bounds quad = GeometryUtility.CalculateBounds(mesh.vertices, Matrix4x4.identity);
            return sprite.TransformPoint(new Vector3(quad.center.x, quad.max.y, quad.center.z));
        }
    }
}

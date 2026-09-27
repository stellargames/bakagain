namespace BakAgain.World {
    using UnityEngine;

    /// <summary>
    /// Rotates a quad to always face the camera. Attached to sprite entities.
    /// For classic mode, the shader handles billboarding in the vertex shader.
    /// For enhanced mode (standard URP materials), this CPU-side rotation is needed.
    /// </summary>
    public class BillboardSprite : MonoBehaviour {
        private void LateUpdate() {
            var cam = Camera.main;
            if (cam == null) return;

            // Face camera but stay upright (Y-axis billboard)
            Vector3 lookDir = cam.transform.position - transform.position;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }
}

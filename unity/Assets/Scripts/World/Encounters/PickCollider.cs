namespace BakAgain.World.Encounters {
    using UnityEngine;

    /// <summary>
    /// The pick box of a sprite that may be mirrored, on a child that cancels the mirror.
    /// </summary>
    /// <remarks>
    /// A left-facing encounter sprite is a negative X scale (the shader reads the handedness,
    /// SpritePass.hlsl BillboardWorldPos), and a BoxCollider on that object logs "BoxCollider does not
    /// support negative scale or size" (TASK-1004). The child flips back whenever the parent's sign
    /// does, so the box stays on the drawn quad. Pickers find the sprite with GetComponentInParent.
    /// </remarks>
    public sealed class PickCollider : MonoBehaviour {
        private BoxCollider _box;
        private Vector3 _center;
        private float _sign;

        public static void Add(GameObject sprite, Vector3 center, Vector3 size) {
            var go = new GameObject("Pick");
            go.transform.SetParent(sprite.transform, false);
            go.layer = sprite.layer;
            var pick = go.AddComponent<PickCollider>();
            pick._center = center;
            // Flip BEFORE the box exists: PhysX checks the scale the moment the collider is added.
            go.transform.localScale = new Vector3(sprite.transform.localScale.x < 0f ? -1f : 1f, 1f, 1f);
            pick._box = go.AddComponent<BoxCollider>();
            pick._box.size = size;
            pick.Follow();
            int layer = LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName);
            if (layer >= 0) {
                sprite.layer = layer;
                go.layer = layer;
            }
        }

        private void LateUpdate() => Follow();

        private void Follow() {
            float sign = transform.parent != null && transform.parent.localScale.x < 0f ? -1f : 1f;
            if (sign == _sign || _box == null) {
                return;
            }
            _sign = sign;
            transform.localScale = new Vector3(sign, 1f, 1f);
            _box.center = new Vector3(_center.x * sign, _center.y, _center.z);
        }
    }
}

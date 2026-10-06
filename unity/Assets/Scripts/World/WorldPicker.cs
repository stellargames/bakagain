using UnityEngine;

namespace BakAgain.World {
    /// <summary>
    /// Faithful analogue of the DOS pDetectedItems hit-test (renderAndDetectVisibleItems /
    /// HandleEnvironmentInteraction_impl @0x76573): turns a pointer position into a camera ray
    /// through the world viewport and returns the interactive WorldEntity under it, or null.
    /// </summary>
    public static class WorldPicker {
        public static WorldEntity Pick(Camera camera, IWorldViewport viewport, Vector2 pointerPx, Rect stageScreenRect) {
            if (camera == null || viewport == null) {
                return null;
            }
            Rect r = viewport.ToScreenRect(stageScreenRect);
            if (!r.Contains(pointerPx)) {
                return null; // clicked outside the 3D view (chrome / other REQ area)
            }
            Vector2 vp = (pointerPx - r.min) / r.size; // 0..1 within the viewport
            Ray ray = ViewportRay(camera, vp);
            int mask = 1 << LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName);
            if (Physics.Raycast(ray, out RaycastHit hit, camera.farClipPlane, mask)) {
                return hit.collider.GetComponentInParent<WorldEntity>();
            }
            return null;
        }

        /// <summary>
        /// The pointer ray, built in camera space and turned into the world by the camera's own
        /// transform.
        /// </summary>
        /// <remarks>
        /// <b>Not <c>Camera.ViewportPointToRay</c>.</b> That unprojects through the inverse view-
        /// projection in single precision, and at the world's coordinates (thousands of units from
        /// the origin) the ray it returns misses its own point by about 0.7 units at 100 units —
        /// wider than a distant creature, so a click on one went through it (TASK-795).
        /// </remarks>
        internal static Ray ViewportRay(Camera camera, Vector2 vp) {
            if (camera.orthographic) {
                return camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
            }
            float tanY = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var local = new Vector3((vp.x * 2f - 1f) * tanY * camera.aspect, (vp.y * 2f - 1f) * tanY, 1f);
            return new Ray(camera.transform.position, camera.transform.TransformDirection(local).normalized);
        }

        /// <summary>
        /// The arena body under the pointer, or null.
        /// </summary>
        /// <remarks>
        /// <b>A body is deliberately not a <see cref="WorldEntity"/>.</b> That type is placed
        /// scenery addressed by world position; a corpse left by an encounter is addressed through
        /// the encounter's roster and exists only while the fight does —
        /// <c>EncounterCorpseLoot</c>'s summary draws the same line. They share the picking layer
        /// and nothing else, so this asks the same ray a second question rather than widening what
        /// <see cref="Pick"/> may return.
        /// </remarks>
        public static Encounters.ArenaCorpse PickCorpse(Camera camera, IWorldViewport viewport,
            Vector2 pointerPx, Rect stageScreenRect) =>
            PickOnInteractionLayer<Encounters.ArenaCorpse>(camera, viewport, pointerPx, stageScreenRect);

        /// <summary>The live encounter actor under the pointer in the world, or null (TASK-795).</summary>
        public static Encounters.EncounterGroupMember PickEncounterGroupMember(Camera camera,
            IWorldViewport viewport, Vector2 pointerPx, Rect stageScreenRect) =>
            PickOnInteractionLayer<Encounters.EncounterGroupMember>(camera, viewport, pointerPx, stageScreenRect);

        // *** THERE IS NO COMBATANT PICK HERE, AND THAT IS DELIBERATE. *** Target selection does
        // NOT hit-test a figure: combat_actor_terr_under_cur (CACTOR.C:741) reads the grid's
        // occupant at the cursor's CELL, so the arena picks with PickGroundPoint plus
        // HotspotService.CombatantAtPoint. A ray against the billboard answered a different
        // question and got a different answer -- a tall creature could be grabbed from cells in
        // front of it (TASK-589). Do not reintroduce one.

        /// <summary>
        /// Where the pointer ray meets the arena floor, or null.
        /// </summary>
        /// <remarks>
        /// <b>The floor is a PLANE, not a collider.</b> Every other pick here asks the physics
        /// scene what the ray hit; the arena ground has no geometry of its own, so this intersects
        /// the ray with <c>y = 0</c> analytically. That is where the floor is:
        /// <c>EncounterActorSpriteBuilder</c> places every combatant at
        /// <c>BakCoordinateConverter.ConvertPosition(worldX, worldY, 0)</c>, and that converter maps
        /// BaK <c>(x, y, z)</c> to Unity <c>(x, z, y)</c> — so BaK z is Unity UP and the arena is
        /// flat at zero.
        ///
        /// <para>A ray parallel to the floor, or pointing away from it, returns null rather than a
        /// point behind the camera — which is what <c>Plane.Raycast</c>'s false already means.</para>
        /// </remarks>
        public static Vector3? PickGroundPoint(Camera camera, IWorldViewport viewport,
            Vector2 pointerPx, Rect stageScreenRect) {
            if (camera == null || viewport == null) {
                return null;
            }
            Rect r = viewport.ToScreenRect(stageScreenRect);
            if (!r.Contains(pointerPx)) {
                return null;
            }
            Vector2 vp = (pointerPx - r.min) / r.size;
            Ray ray = ViewportRay(camera, vp);
            var floor = new Plane(Vector3.up, Vector3.zero);
            return floor.Raycast(ray, out float distance) ? ray.GetPoint(distance) : (Vector3?)null;
        }

        /// <summary>
        /// Where a floor point appears on SCREEN: the exact inverse of <see cref="PickGroundPoint"/>.
        /// </summary>
        /// <remarks>
        /// The arena camera renders into a texture shown in the viewport, so
        /// <c>Camera.WorldToScreenPoint</c> answers in the texture's space, not the screen's — found
        /// live when the touch aids' combat cursor landed off the battlefield (2026-10-01).
        /// </remarks>
        public static Vector2? ScreenPointOfGround(Camera camera, IWorldViewport viewport,
            Vector3 floorPoint, Rect stageScreenRect) {
            if (camera == null || viewport == null) {
                return null;
            }
            Vector3 vp = camera.WorldToViewportPoint(floorPoint);
            if (vp.z <= 0f) {
                return null;
            }
            Rect r = viewport.ToScreenRect(stageScreenRect);
            return r.min + (new Vector2(vp.x, vp.y) * r.size);
        }

        /// <summary>
        /// The nearest thing of type <typeparamref name="T"/> the pointer ray hits on the
        /// interaction layer.
        /// </summary>
        /// <remarks>
        /// <b>Nearest wins, and a miss is a miss.</b> The ray reports only its first hit, so a
        /// corpse standing behind a live combatant is not reachable while that combatant is in the
        /// way — which is what a player would expect from clicking, and matches how the single-hit
        /// pick behaved before there was more than one kind of arena marker.
        /// </remarks>
        private static T PickOnInteractionLayer<T>(Camera camera, IWorldViewport viewport,
            Vector2 pointerPx, Rect stageScreenRect) where T : Component {
            if (camera == null || viewport == null) {
                return null;
            }
            Rect r = viewport.ToScreenRect(stageScreenRect);
            if (!r.Contains(pointerPx)) {
                return null;
            }
            Vector2 vp = (pointerPx - r.min) / r.size;
            Ray ray = ViewportRay(camera, vp);
            int mask = 1 << LayerMask.NameToLayer(WorldInteractionLayers.WorldInteractableLayerName);
            return Physics.Raycast(ray, out RaycastHit hit, camera.farClipPlane, mask)
                ? hit.collider.GetComponentInParent<T>()
                : null;
        }
    }
}

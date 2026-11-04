namespace BakAgain.World.Encounters {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Layout;
    using UnityEngine;

    /// <summary>
    /// Enhanced combat's see-through (TASK-972): a unit nearer the camera than the focused cell, whose
    /// drawn sprite covers that cell on screen, dithers down to <see cref="EnhancedCombatLayout.SeeThroughAlpha"/>
    /// so the cell it hides can be read.
    /// </summary>
    /// <remarks>
    /// <b>Nothing is stored on the arena.</b> The arena is rebuilt on every redraw, so this is applied every
    /// frame from the controller's focus cell, and a unit that does not qualify is written back to full.
    /// The fade is the sprite shader's <c>_SeeThrough</c> through the renderer's property block (merged
    /// into the block SpellVfx also uses), so no material is made. <b>Picking is untouched</b>: the arena
    /// picks the CELL under the pointer on a floor plane and then asks who stands on it, so a faded unit
    /// never blocks a pick behind it and the pointer on a unit still picks that unit's cell.
    /// </remarks>
    public static class ArenaSeeThrough {
        private static readonly int SeeThroughId = Shader.PropertyToID("_SeeThrough");
        private static readonly MaterialPropertyBlock Block = new();

        /// <summary>The unit's current see-through opacity (1 when none was set).</summary>
        public static float AlphaOf(Renderer r) {
            r.GetPropertyBlock(Block);
            return Block.HasFloat(SeeThroughId) ? Block.GetFloat(SeeThroughId) : 1f;
        }

        /// <param name="focus">The pointed / forecast cell, or null for none (everything restored).</param>
        /// <param name="cellCentre">A cell's centre in Unity world space.</param>
        /// <param name="cellAt">The arena cell under a world point; a unit standing on the focus cell is the subject, never an obstruction.</param>
        public static void Apply(Camera cam, (int Col, int Row)? focus, IReadOnlyList<ArenaCombatant> units,
            EnhancedCombatLayout layout, Func<(int Col, int Row), Vector3?> cellCentre,
            Func<Vector3, (int Col, int Row)?> cellAt) {
            Vector3? centre = focus.HasValue && cam != null ? cellCentre(focus.Value) : null;
            Vector3 focusScreen = centre.HasValue ? cam.WorldToScreenPoint(centre.Value) : default;
            float focusDistance = centre.HasValue ? Vector3.Distance(cam.transform.position, centre.Value) : 0f;
            foreach (ArenaCombatant u in units) {
                if (u == null) {
                    continue;
                }
                Renderer r = u.GetComponent<Renderer>();
                if (r == null) {
                    continue;
                }
                bool fade = centre.HasValue && focusScreen.z > 0f
                    && cellAt(u.transform.position) != focus
                    && Vector3.Distance(cam.transform.position, u.transform.position) < focusDistance
                    && ScreenRect(cam, r.bounds).Contains(focusScreen);
                Set(r, fade ? layout.SeeThroughAlpha : 1f);
            }
        }

        private static void Set(Renderer r, float alpha) {
            r.GetPropertyBlock(Block);
            float now = Block.HasFloat(SeeThroughId) ? Block.GetFloat(SeeThroughId) : 1f;
            if (Mathf.Approximately(now, alpha)) {
                return;
            }
            Block.SetFloat(SeeThroughId, alpha);
            r.SetPropertyBlock(Block);
        }

        private static Rect ScreenRect(Camera cam, Bounds b) {
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++) {
                Vector3 c = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 s = cam.WorldToScreenPoint(c);
                min = Vector2.Min(min, s);
                max = Vector2.Max(max, s);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }

    /// <summary>Rides on the arena root and applies <see cref="ArenaSeeThrough"/> every frame while Enhanced combat is on.</summary>
    public sealed class ArenaSeeThroughDriver : MonoBehaviour {
        private Func<bool> _on;
        private Func<(int Col, int Row)?> _focus;
        private Func<Camera> _camera;
        private Func<(int Col, int Row), Vector3?> _centre;
        private Func<Vector3, (int Col, int Row)?> _cellAt;
        private readonly List<ArenaCombatant> _units = new();
        private EnhancedCombatLayout _layout;

        public void Bind(Func<bool> on, Func<(int Col, int Row)?> focus, Func<Camera> camera,
            Func<(int Col, int Row), Vector3?> centre, Func<Vector3, (int Col, int Row)?> cellAt,
            EnhancedCombatLayout layout) {
            _on = on; _focus = focus; _camera = camera; _centre = centre; _cellAt = cellAt; _layout = layout;
        }

        private void LateUpdate() {
            if (_on == null) {
                return;
            }
            // Latch gone Classic: apply with no focus, which restores everything.
            GetComponentsInChildren(_units);
            ArenaSeeThrough.Apply(_camera(), _on() ? _focus() : null, _units, _layout, _centre, _cellAt);
        }
    }
}

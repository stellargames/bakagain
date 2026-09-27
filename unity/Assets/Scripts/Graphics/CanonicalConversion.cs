namespace BakAgain.Graphics {
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Screen → canonical-stage conversion. Unity screen positions are bottom-left
    /// origin; the stage's own coordinate space (whatever <see cref="UI.CanonicalStage"/> built
    /// it as — a fixed <c>Contain</c> box or a full-panel <c>Fill</c> span) is top-left origin,
    /// so the Y axis is flipped. Used by the software cursor to place itself in the same space
    /// the UI Toolkit panel content is authored in.</summary>
    public static class CanonicalConversion {
        /// <summary>
        /// Converts a physical screen position into a position local to <paramref name="stage"/>
        /// (0,0 at the stage's own resolved top-left) — i.e. the same coordinate space the stage's
        /// UI content is authored in, whether that stage is a fixed-size <c>Contain</c> box or a
        /// full-panel <c>Fill</c> span. Reads the stage's actual resolved geometry
        /// (<see cref="VisualElement.worldBound"/>) rather than assuming a fixed 1600×1200 frame,
        /// so this is correct under either fit policy without needing to know which one is active.
        /// <paramref name="stage"/> is typically <see cref="UI.CanonicalStage.Find"/>'s result; pass
        /// <c>null</c> before it exists (falls back to a plain top-left-flip with no stage offset —
        /// off-screen-safe for the frame or two before the stage's panel/layout resolves).
        /// </summary>
        public static Vector2 ScreenToCanonical(Vector2 screenPos, Vector2 screenSize, VisualElement stage) {
            // Input System reports bottom-left origin; UI Toolkit panel space is top-left, so flip Y
            // before converting (mirrors the established idiom in ClassicMovementDriver).
            var topLeftScreenPos = new Vector2(screenPos.x, screenSize.y - screenPos.y);

            IPanel panel = stage?.panel;
            if (panel == null) {
                return topLeftScreenPos; // no resolved stage yet — best-effort, no offset available
            }

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, topLeftScreenPos);
            Vector2 origin = stage.worldBound.position;
            if (float.IsNaN(origin.x) || float.IsNaN(origin.y)) {
                origin = Vector2.zero; // layout not resolved yet
            }
            return panelPos - origin;
        }
    }
}

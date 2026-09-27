namespace BakAgain.CutScenes.Extensions {
    using UnityEngine;

    public static class RectExtensions {
        public static Rect Scale(this Rect rect, Vector2 scale) {
            rect.x *= scale.x;
            rect.y *= scale.y;
            rect.width *= scale.x;
            rect.height *= scale.y;

            return rect;
        }
    }
}
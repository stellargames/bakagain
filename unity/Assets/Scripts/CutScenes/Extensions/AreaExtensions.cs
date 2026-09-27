namespace BakAgain.CutScenes.Extensions {
    using GameData.Resources.Animation;
    using UnityEngine;

    public static class AreaExtensions {
        public static int InverseY(this IArea area, int imageHeight) {
            return imageHeight - area.Y - area.Height;
        }

        public static T Scale<T>(this IArea area, Vector2 scale) where T : IArea, new() {
            float x1 = area.X * scale.x;
            float y1 = area.Y * scale.y;
            float x2 = (area.X + area.Width) * scale.x;
            float y2 = (area.Y + area.Height) * scale.y;

            int newX = Mathf.FloorToInt(x1);
            int newY = Mathf.FloorToInt(y1);

            return new T {
                X = newX,
                Y = newY,
                Width = Mathf.CeilToInt(x2) - newX,
                Height = Mathf.CeilToInt(y2) - newY
            };
        }
    }
}
namespace BakAgain.Graphics {
    using UnityEngine;

    /// <summary>
    /// Shared palette-pen helpers — the single source of truth for turning a BaK
    /// pen index into a Unity <see cref="Color"/>. Previously duplicated verbatim
    /// across DialogManager, DialogTextFormatter and UserInterfaceLoader.
    /// </summary>
    public static class PaletteColors {
        /// <summary>
        /// Resolves a pen index against <paramref name="palette"/>, clamping
        /// out-of-range indices to the palette bounds. Returns
        /// <see cref="Color.black"/> when the palette is null or empty.
        /// </summary>
        public static Color ResolvePen(Color[] palette, int pen) {
            if (palette == null || palette.Length == 0) {
                return Color.black;
            }
            return palette[Mathf.Clamp(pen, 0, palette.Length - 1)];
        }

        /// <summary>
        /// Resolves a pen index straight off a loaded <see cref="GameData.Resources.Palette.PaletteResource"/>,
        /// for callers that hold the resource rather than a converted <c>Color[]</c>. Returns
        /// <paramref name="fallback"/> when the palette hasn't loaded yet or the index is out of
        /// range — screens draw before their palette arrives, and a hardcoded colour reads better
        /// there than black.
        /// </summary>
        public static Color ResolvePen(GameData.Resources.Palette.PaletteResource palette, int pen,
            Color fallback) {
            GameData.Resources.Palette.Color[] colors = palette?.Colors;
            if (colors == null || pen < 0 || pen >= colors.Length) {
                return fallback;
            }
            GameData.Resources.Palette.Color c = colors[pen];
            return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
        }
    }
}

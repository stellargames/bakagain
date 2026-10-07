namespace GameData.Resources.Palette;

public record struct Color(byte R, byte G, byte B, byte A = 255) {
    public static Color FromRgb(int r, int g, int b) => new Color((byte)r, (byte)g, (byte)b);

    public override string ToString() => A != 255 ? $"#{R:X2}{G:X2}{B:X2}{A:X2}" : $"#{R:X2}{G:X2}{B:X2}";
}
namespace ResourceExtraction;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// DOS code page 437, our own table so no player build depends on the platform's code pages
/// (TASK-843). The English release draws nothing above 0x7E (GAME.FNT ends there); the bytes
/// above 0x80 are the German release's letters (ä 0x84 .. ß 0xE1, at their CP437 places in its
/// fonts) and DDX's 0xE0-0xFF control bytes, which <c>DialogTextRuns.FromCp437</c> turns into tags.
/// </summary>
public sealed class Cp437Encoding : Encoding {
    public static readonly Cp437Encoding Instance = new();

    private const string High =
        "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜ¢£¥₧ƒáíóúñÑªº¿⌐¬½¼¡«»░▒▓│┤╡╢╖╕╣║╗╝╜╛┐└┴┬├─┼╞╟╚╔╩╦╠═╬╧╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀αßΓπΣσµτΦΘΩδ∞φε∩≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";

    private static readonly Dictionary<char, byte> Bytes = BuildReverse();

    private static Dictionary<char, byte> BuildReverse() {
        var reverse = new Dictionary<char, byte>(High.Length);
        for (int i = 0; i < High.Length; i++) {
            reverse[High[i]] = (byte)(0x80 + i);
        }
        return reverse;
    }

    public override bool IsSingleByte => true;

    public override int GetByteCount(char[] chars, int index, int count) => count;

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) {
        for (int i = 0; i < charCount; i++) {
            char c = chars[charIndex + i];
            bytes[byteIndex + i] = c < 0x80 ? (byte)c : Bytes.TryGetValue(c, out byte b) ? b : (byte)'?';
        }
        return charCount;
    }

    public override int GetCharCount(byte[] bytes, int index, int count) => count;

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) {
        for (int i = 0; i < byteCount; i++) {
            byte b = bytes[byteIndex + i];
            chars[charIndex + i] = b < 0x80 ? (char)b : High[b - 0x80];
        }
        return byteCount;
    }

    public override int GetMaxByteCount(int charCount) => charCount;

    public override int GetMaxCharCount(int byteCount) => byteCount;
}

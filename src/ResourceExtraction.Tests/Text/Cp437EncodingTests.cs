namespace ResourceExtraction.Tests.Text;

using System.Text;
using Xunit;

/// <summary>Our CP437 table (TASK-843) is the platform's code page 437, byte for byte.</summary>
public class Cp437EncodingTests {
    [Fact]
    public void EveryByteDecodesAsThePlatformsCodePage437() {
        Encoding platform = CodePagesEncodingProvider.Instance.GetEncoding(437)!;
        byte[] all = new byte[256];
        for (int i = 0; i < all.Length; i++) {
            all[i] = (byte)i;
        }
        string ours = Cp437Encoding.Instance.GetString(all);
        Assert.Equal(platform.GetString(all), ours);
        Assert.Equal(all, Cp437Encoding.Instance.GetBytes(ours));
    }

    [Fact]
    public void TheGermanReleasesLettersSitWhereItsFontDrawsThem() =>
        Assert.Equal(new byte[] { 0x84, 0x94, 0x81, 0x8E, 0x99, 0x9A, 0xE1 },
            Cp437Encoding.Instance.GetBytes("äöüÄÖÜß"));
}

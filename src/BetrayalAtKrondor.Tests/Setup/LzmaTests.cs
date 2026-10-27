namespace BetrayalAtKrondor.Tests.Setup;

using System;
using System.IO;
using System.Security.Cryptography;
using GameData.Setup.Lzma;
using Xunit;

public class LzmaTests {
    // python3: lzma.compress(data, format=FORMAT_ALONE, 64 KiB dictionary) over
    // random.Random(1014).randbytes(1024) + (b"Betrayal at Krondor - the Inno Setup chunks are LZMA1 compressed. " * 60)[:3072]
    private const string AloneHex =
        "5d00000100ffffffffffffffff00531edb4830b844866c53f024616899b7bd2e7e64691d5225b20c8e6e723558750fd3e3035e4441e6b501444c57ac" +
        "0db66509974eacb4686ae95c41b87c8d3bed8047e1235bdf63d1cf6f1fbcb3dacf4b99d6aa295152d8631da7b02a1383cff5035846c715e2e9adde84" +
        "ab0911e4f287cf026dcf9855ecb32769351e1ec7fdf33af7685dbd76cfd78403095b31c2f8b552d7324b6cbaac3856cab34a01e25153e5f24333967a" +
        "022fb889fe6a2638a6e41d8e9603f40894806b535b149d86b0004457e94e17172530f7e2662f65d614105cd6c786449248a9fca1e112eb20cb8b3f70" +
        "6668a12eaad3d01f627634090866df775d923b3337a066e4b1c2074ec88790d97edb68c05a63feca07e3f5aeb4cca227f8d25cfe3a9efdb74c4f1962" +
        "8292d0d4a862aaff5e197a251006dbf8a4763034d05d23ad293f903c8008049fc250c6b62c5e08724e1aa932b650751f2024ba765d835d271aa49e12" +
        "aa20ba6d7699c2c80dc59ace6410903acb2080c207d39617a163ca2f2b4929a2c59ba7c1b1e610c6f713834b17caea118c66bab841bd1cc44f37f002" +
        "23de4b39358cb43ca40b168b3cbde308ebf64baaa0cbddbe68c2aa9d04bf4e6be99ad38a6154f35c011149e7c7bc561ed8634b9d4c94dc3acd284776" +
        "47d776da21612c486f34b182f426fd6739334d75c06704e0ae3e33547b2c6bdc3a6e37e74835c0c87532a379d76b9d8849e6931dd73d9835ecf8ca58" +
        "7a2474e2e62df285891a5cb64dc0def005cfdf989e5398d2dcdadaeb444ec561214d60b46ec5d99691883e9da1d3d45746ddd6fd9d5efbc65ad126a2" +
        "8bb02e3585ce9c97d2ed9f709525b45fb8de45b1424e854f0d063b4fd73569d23fda872a68f326dc5533241c4203526da0f831d89001dff7bfc31ddf" +
        "e1c0458f13675188d7f35873873c541e8a8aea41cfb3f1bab15ad41c99e45f3f65f98bcc4d0e6c8e7c0b91817511b26fd648dd90f6e4c2bc1d3c751e" +
        "35d9e71f3c8358ba77efd2f84d25571695d2bb766cfee9874d8d77985023170d35f7f22abcf5874d0186a3ad076cfa22e266b38a16738714f8ab2ea4" +
        "03a95e325a3d14583885d9febd09bbdc18c36459ae2c18b3762dfc4e83f954febf2c360c1cd441be4b32693b6b1e743cd5503dede00d6e78050791cb" +
        "bbd35c30319728aeb25d78d3ffe9aab18de045b92e6b0d3b142545f2603339aab6ab2fc62482bb1dd1f771207b48418868d029344b53b66f4037bb75" +
        "2339e2420a018f62d881b70f64c657b984613dd314410149c79987843592d38d37d7f7ac2fb10d1f4d37911a0729447cf346ff78047cf01f00441b02" +
        "7aa5d61f957b2135f79de58fc4c492e810c32852dd08d4dc91906ba835ce377b423982eea46aba65cfe9bfac3dd9b578a9cce53316cbd4e828c2d536" +
        "f0f938fd38be2d6285125a00ef2c3b600bdcd904ef9f7bb092931b472b3ce7181d57cc55ed97b1cd4902fcc4f29436882d5233d53b2fdd6459d62bd6" +
        "93cd2d18be25abaf15b2baa6478eeb3bc82ccb24021ffa2255c477c8f03e64253d4e6bf33f2e16bc4e9a6d77b23b643f804b9c61f5044606303dfbca" +
        "fffe47d800";

    private const string PlainSha256 = "40ae7f747c8005e6ebadfffb9ba077b29692bc943183cd518d8bca9561bc3dcd";

    private static (byte[] Props, byte[] Payload) Fixture() {
        byte[] alone = Convert.FromHexString(AloneHex);
        return (alone[..5], alone[13..]); // 5 props bytes + 8-byte size, then the stream
    }

    private static byte[] Decode(byte[] props, byte[] payload, long size = 4096) {
        var output = new MemoryStream();
        Lzma1.Decode(new MemoryStream(payload), output, props, size);
        return output.ToArray();
    }

    [Fact]
    public void DecodesAKnownStream() {
        var (props, payload) = Fixture();
        byte[] plain = Decode(props, payload);
        Assert.Equal(4096, plain.Length);
        Assert.Equal(PlainSha256, Convert.ToHexString(SHA256.HashData(plain)).ToLowerInvariant());
    }

    [Fact]
    public void ACorruptedStreamThrowsInvalidData() {
        var (props, payload) = Fixture();
        payload[0] = 0x01; // the first range-coder byte is always 0
        Assert.Throws<InvalidDataException>(() => Decode(props, payload));
    }

    [Fact]
    public void ATruncatedStreamThrowsInvalidData() {
        var (props, payload) = Fixture();
        Assert.Throws<InvalidDataException>(() => Decode(props, payload[..200]));
    }

    [Fact]
    public void AnOversizedDictionaryIsRefused() {
        var (props, payload) = Fixture();
        props[4] = 0x05; // 0x05000000 = 80 MiB
        props[3] = props[2] = props[1] = 0;
        Assert.Throws<InvalidDataException>(() => Decode(props, payload));
    }

    [Fact]
    public void AnEndMarkerBeforeTheClaimedSizeThrowsInvalidData() {
        var (props, payload) = Fixture(); // the stream has an end marker after 4096 bytes
        Assert.Throws<InvalidDataException>(() => Decode(props, payload, 8192));
    }

    [Fact]
    public void AClaimedSizeSmallerThanTheDataThrowsAndWritesNothingPastIt() {
        var (props, payload) = Fixture();
        var output = new MemoryStream();
        // 1100 falls inside the first match after the 1 KiB of random bytes, so the match overshoots
        Assert.Throws<InvalidDataException>(() => Lzma1.Decode(new MemoryStream(payload), output, props, 1100));
        Assert.True(output.Length <= 1100);
    }

    [Theory]
    [InlineData(225, 5)] // lc/lp/pb byte with pb = 5
    [InlineData(0x5d, 4)] // too short
    [InlineData(0x5d, 6)] // too long
    public void BadPropertiesThrowInvalidData(byte first, int length) {
        var (_, payload) = Fixture();
        var props = new byte[length];
        props[0] = first;
        if (length > 4) props[3] = 1;
        Assert.Throws<InvalidDataException>(() => Decode(props, payload));
    }

    [Fact]
    public void DecodeToEndStopsAtTheEndMarker() {
        var (props, payload) = Fixture();
        var output = new MemoryStream();
        Lzma1.DecodeToEnd(new MemoryStream(payload), output, props, 1 << 20);
        Assert.Equal(PlainSha256, Convert.ToHexString(SHA256.HashData(output.ToArray())).ToLowerInvariant());
    }

    [Fact]
    public void DecodeToEndRefusesMoreThanItsCap() {
        var (props, payload) = Fixture();
        Assert.Throws<InvalidDataException>(() => Lzma1.DecodeToEnd(new MemoryStream(payload), new MemoryStream(), props, 4000));
    }

    // python3: a copy chunk "Betrayal at Krondor\n" (control 1, dictionary reset), then
    // lzma.compress(Lzma2Text(), FORMAT_RAW, LZMA2, 64 KiB dictionary) with its first control byte changed from
    // "reset everything" to "reset state + new properties": an LZMA chunk, an LZMA chunk continuing its state, the end.
    private const string Lzma2Hex =
        "010013426574726179616c206174204b726f6e646f720adfff5101ad5d0021194a874638e64ad2a01d51f3c01c7edc7f5e9b5f37c8837680ff86565a" +
        "8510a78dbf039042f9e220ab79d9f2ff024efdbf61ac1fe790bb91fc09a5c6ae2dbc31699a74b5a14e591c39c97808e16c23fc97affcafc28e9d2254" +
        "43bf3d3204368a14c1ce0e88aeab25d1180986818f18050201b4d4d14df573586d35fe8a5de1ea62e14cda1ac599405943091b677387917bcfb27ecf" +
        "6a881712fd63e579edd32dfe619706035fc9703b14c0323798a51501a5e8a3092aecaaff5a26ac720c1692a7be398f24e1709ea7235fec28cb85d195" +
        "988a7e2a91f22775f719c006984d98fdd8afd5900fc42553f8f591363105a5b0ee6fc1704d470cd19111aaad601dbaceb127185c5986e9665258bee9" +
        "76ac59e4e55b0508f9c7daadfcfb522b74cd1e5b2042f9dd533df82964093b80cb2a6cdfb53bf0c4bd2e5faa0f3e4b664290130eff1093f8717859f8" +
        "0bcdff9528460fa9fc7cdefb9a302e56c08f85f38381c065c42553f8f591363105a5b0ee6fc1704d470cd19111aaad601dbaceb127185c5986e96652" +
        "58bee976ac59e4e55b0508f9c7daadfcfb522b74cd1e5b2042f9dd533df82964093b80c31f6137801435000b0017b0bd82aca6a4d45cc80000";

    private static byte[] Lzma2Plain() {
        byte[] text = System.Text.Encoding.ASCII.GetBytes("Betrayal at Krondor - the GOG installer's file chunks are LZMA2. ");
        byte[] head = System.Text.Encoding.ASCII.GetBytes("Betrayal at Krondor\n");
        const int Big = 2 * 1024 * 1024; // (text * 40000)[:2 MiB] + (text * 100)[:5000]
        var plain = new byte[head.Length + Big + 5000];
        head.CopyTo(plain, 0);
        for (int k = 0; k < Big + 5000; k++) {
            plain[head.Length + k] = text[(k < Big ? k : k - Big) % text.Length];
        }
        return plain;
    }

    private static byte[] DecodeLzma2(byte[] stream, long size) {
        var output = new MemoryStream();
        Lzma1.DecodeLzma2(new MemoryStream(stream), output, 1 << 16, size);
        return output.ToArray();
    }

    [Fact]
    public void DecodesAnLzma2StreamOfCopyAndLzmaChunks() {
        byte[] plain = Lzma2Plain();
        Assert.Equal(plain, DecodeLzma2(Convert.FromHexString(Lzma2Hex), plain.Length));
    }

    [Fact]
    public void Lzma2StopsAtTheClaimedSize() {
        byte[] plain = Lzma2Plain();
        Assert.Equal(plain[..3000], DecodeLzma2(Convert.FromHexString(Lzma2Hex), 3000));
    }

    [Fact]
    public void Lzma2EndingBeforeTheClaimedSizeThrowsInvalidData() {
        Assert.Throws<InvalidDataException>(() => DecodeLzma2(Convert.FromHexString(Lzma2Hex), Lzma2Plain().Length + 1));
    }

    [Fact]
    public void ATruncatedLzma2StreamThrowsInvalidData() {
        Assert.Throws<InvalidDataException>(() => DecodeLzma2(Convert.FromHexString(Lzma2Hex)[..300], Lzma2Plain().Length));
    }

    [Theory]
    [InlineData(0)] // the copy chunk becomes "copy, no dictionary reset" before any reset
    [InlineData(23)] // the first LZMA chunk becomes "no reset" after the copy chunk's dictionary reset
    public void Lzma2ChunksMustStartWithTheirResets(int at) {
        byte[] stream = Convert.FromHexString(Lzma2Hex);
        stream[at] = at == 0 ? (byte)2 : (byte)(stream[at] & 0x9F);
        Assert.Throws<InvalidDataException>(() => DecodeLzma2(stream, Lzma2Plain().Length));
    }

    [Fact]
    public void ANegativeSizeIsRefused() {
        var (props, payload) = Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => Decode(props, payload, -1));
    }
}

using System;
using System.IO;
using GameData.Setup.Lzma.Compression.LZMA;

namespace GameData.Setup.Lzma;

/// <summary>Decodes a raw LZMA1 stream (Inno Setup chunks) with the vendored LZMA SDK decoder.</summary>
internal static class Lzma1 {
    private const uint MaxDictionary = 64 * 1024 * 1024;

    /// <param name="props5">lc/lp/pb byte, then the dictionary size as a little-endian uint.</param>
    /// <param name="outSize">Exact decoded size; the stream's end marker is not needed.</param>
    public static void Decode(Stream input, Stream output, byte[] props5, long outSize) {
        if (outSize < 0) throw new ArgumentOutOfRangeException(nameof(outSize));
        if (props5.Length != 5) throw new InvalidDataException("LZMA properties must be 5 bytes.");
        if (BitConverter.ToUInt32(props5, 1) > MaxDictionary) throw new InvalidDataException("LZMA dictionary too large.");
        try {
            var decoder = new Decoder();
            decoder.SetDecoderProperties(props5);
            decoder.Code(input, output, -1, outSize, null);
        } catch (Exception e) when (e is DataErrorException or InvalidParamException) {
            throw new InvalidDataException("Corrupt LZMA stream.", e);
        }
    }
}

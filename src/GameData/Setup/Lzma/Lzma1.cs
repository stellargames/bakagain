using System;
using System.Buffers.Binary;
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
        if (BinaryPrimitives.ReadUInt32LittleEndian(props5.AsSpan(1)) > MaxDictionary) throw new InvalidDataException("LZMA dictionary too large.");
        try {
            var decoder = new Decoder();
            decoder.SetDecoderProperties(props5);
            var exact = new ExactSink(output, outSize);
            decoder.Code(input, exact, -1, outSize, null);
            if (exact.Written != outSize) throw new InvalidDataException("LZMA stream ended early.");
        } catch (Exception e) when (e is DataErrorException or InvalidParamException) {
            throw new InvalidDataException("Corrupt LZMA stream.", e);
        }
    }

    // Passes bytes through to the caller's stream, but never beyond the claimed size.
    private sealed class ExactSink : Stream {
        private readonly Stream _inner;
        private readonly long _limit;
        public long Written { get; private set; }

        public ExactSink(Stream inner, long limit) { _inner = inner; _limit = limit; }

        public override void Write(byte[] buffer, int offset, int count) {
            if (Written + count > _limit) throw new InvalidDataException("LZMA stream decodes past its claimed size.");
            _inner.Write(buffer, offset, count);
            Written += count;
        }

        public override void Flush() { }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

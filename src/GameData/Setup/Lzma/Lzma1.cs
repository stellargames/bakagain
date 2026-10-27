using System;
using System.Buffers.Binary;
using System.IO;
using GameData.Setup.Lzma.Compression.LZMA;

namespace GameData.Setup.Lzma;

/// <summary>Decodes raw LZMA1 streams (Inno Setup header blocks) and LZMA2 streams (its file chunks) with the vendored LZMA SDK decoder.</summary>
internal static class Lzma1 {
    private const uint MaxDictionary = 64 * 1024 * 1024;

    /// <param name="props5">lc/lp/pb byte, then the dictionary size as a little-endian uint.</param>
    /// <param name="outSize">Exact decoded size; the stream's end marker is not needed.</param>
    public static void Decode(Stream input, Stream output, byte[] props5, long outSize) {
        if (outSize < 0) throw new ArgumentOutOfRangeException(nameof(outSize));
        CheckProps(props5);
        var exact = new ExactSink(output, outSize, false);
        Run(() => {
            var decoder = new Decoder();
            decoder.SetDecoderProperties(props5);
            decoder.Code(input, exact, -1, outSize, null);
        });
        if (exact.Written != outSize) throw new InvalidDataException("LZMA stream ended early.");
    }

    /// <summary>Decodes up to the stream's end marker (Inno Setup's header blocks), refusing more than <paramref name="maxOutSize"/> bytes.</summary>
    public static void DecodeToEnd(Stream input, Stream output, byte[] props5, long maxOutSize) {
        CheckProps(props5);
        Run(() => {
            var decoder = new Decoder();
            decoder.SetDecoderProperties(props5);
            decoder.Code(input, new ExactSink(output, maxOutSize, false), -1, -1, null);
        });
    }

    /// <summary>Decodes the first <paramref name="outSize"/> bytes of a raw LZMA2 stream; the stream may hold more.</summary>
    public static void DecodeLzma2(Stream input, Stream output, uint dictionarySize, long outSize) {
        if (outSize < 0) throw new ArgumentOutOfRangeException(nameof(outSize));
        if (dictionarySize > MaxDictionary) throw new InvalidDataException("LZMA dictionary too large.");
        var exact = new ExactSink(output, outSize, true);
        Run(() => new Decoder().CodeLzma2(input, exact, dictionarySize, outSize));
        if (exact.Written != outSize) throw new InvalidDataException("LZMA2 stream ended early.");
    }

    private static void CheckProps(byte[] props5) {
        if (props5.Length != 5) throw new InvalidDataException("LZMA properties must be 5 bytes.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(props5.AsSpan(1)) > MaxDictionary) throw new InvalidDataException("LZMA dictionary too large.");
    }

    private static void Run(Action decode) {
        try {
            decode();
        } catch (Exception e) when (e is DataErrorException or InvalidParamException) {
            throw new InvalidDataException("Corrupt LZMA stream.", e);
        }
    }

    // Passes bytes through to the caller's stream, but never beyond the claimed size: past it, throws or (truncate) drops them.
    private sealed class ExactSink : Stream {
        private readonly Stream _inner;
        private readonly long _limit;
        private readonly bool _truncate;
        public long Written { get; private set; }

        public ExactSink(Stream inner, long limit, bool truncate) { _inner = inner; _limit = limit; _truncate = truncate; }

        public override void Write(byte[] buffer, int offset, int count) {
            if (_truncate) count = (int)Math.Min(count, _limit - Written);
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

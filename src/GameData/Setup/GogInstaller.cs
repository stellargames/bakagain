namespace GameData.Setup;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameData.Setup.Lzma;
using ZipLimits = GameData.Mods.ModArchive.ZipLimits;

/// <param name="Destination">The installer's own name for the file, e.g. <c>{app}\KRONDOR.001</c>. Untrusted: never a path to write to.</param>
/// <param name="Sha1">The SHA-1 the installer lists for the file; <see cref="GogInstaller.Extract"/> checks it.</param>
public sealed record InstallerFile(string Destination, long Size, byte[] Sha1) {
    internal int Location { get; init; }
}

/// <summary>
/// Reads the files out of GOG's Inno Setup installer, without installing it. Only setup data version
/// "5.5.0 (u)" (setup_betrayal_at_krondor_2.1.0.19.exe) is supported. The installer is untrusted input:
/// every size and offset is checked, and a malformed one gives null from <see cref="TryOpen"/> or an
/// <see cref="InvalidDataException"/> from <see cref="Extract"/>.
/// Layout learned from innoextract (zlib licence; no code copied): src/loader/offsets.cpp, src/stream/block.cpp,
/// chunk.cpp, src/setup/header.cpp and the entry loaders.
/// </summary>
public sealed class GogInstaller {
    private const string Signature = "Inno Setup Setup Data (5.5.0) (u)";
    // The setup loader's offset table, found by its magic in the stub exe (innoextract finds it as PE resource 11111).
    private static readonly byte[] LoaderMagic = { (byte)'r', (byte)'D', (byte)'l', (byte)'P', (byte)'t', (byte)'S', 0xCD, 0xE6, 0xD7, (byte)'{', 0x0B, (byte)'*' };
    // ponytail: scans the stub's first MiB for the table (it sits at 234 KiB in GOG's); a PE resource walk if a stub ever grows past that.
    private const int LoaderScan = 1 << 20;
    private const int HeaderCap = 16 << 20;
    private static readonly long FileCap = ZipLimits.Default.EntryBytes;
    private const int DataEntrySize = 74;
    private const byte Lzma2 = 4; // the header's compression enum: stored, zlib, bzip2, lzma1, lzma2
    private const ushort CallInstructionOptimized = 1 << 4, ChunkEncrypted = 1 << 6, ChunkCompressed = 1 << 7;
    private static readonly byte[] ChunkMagic = { (byte)'z', (byte)'l', (byte)'b', 0x1A };

    private readonly Stream _exe;
    private readonly long _dataOffset;
    private readonly DataEntry[] _data;

    public IReadOnlyList<InstallerFile> Files { get; }

    private GogInstaller(Stream exe, long dataOffset, DataEntry[] data, IReadOnlyList<InstallerFile> files) {
        _exe = exe;
        _dataOffset = dataOffset;
        _data = data;
        Files = files;
    }

    private readonly struct DataEntry {
        public readonly uint FirstSlice, LastSlice, ChunkOffset;
        public readonly ulong FileOffset, FileSize, ChunkSize;
        public readonly byte[] Sha1;
        public readonly ushort Flags;

        public DataEntry(Cursor c) {
            FirstSlice = c.U32();
            LastSlice = c.U32();
            ChunkOffset = c.U32();
            FileOffset = c.U64();
            FileSize = c.U64();
            ChunkSize = c.U64();
            Sha1 = c.Bytes(20);
            c.Skip(8 + 8); // file time, file version
            Flags = c.U16(); // a 9-flag set
        }
    }

    /// <summary>Null unless <paramref name="exe"/> is a readable Inno Setup 5.5.0 (u) installer. The stream must stay open while files are extracted.</summary>
    public static GogInstaller? TryOpen(Stream exe) {
        try {
            return Open(exe);
        } catch (InvalidDataException) {
            return null;
        }
    }

    private static GogInstaller Open(Stream exe) {
        (long headerOffset, long dataOffset) = FindLoaderTable(exe);
        byte[] signature = ReadAt(exe, headerOffset, 64);
        byte[] expected = new byte[64];
        Encoding.ASCII.GetBytes(Signature, 0, Signature.Length, expected, 0);
        if (!signature.AsSpan().SequenceEqual(expected)) {
            throw new InvalidDataException("Not Inno Setup data 5.5.0 (u).");
        }
        (byte[] header, long next) = ReadBlock(exe, headerOffset + 64);
        (byte[] dataBlock, _) = ReadBlock(exe, next);

        // The setup header (TSetupHeader) as innoextract's header.cpp loads it for 5.5.0 unicode.
        var h = new Cursor(header);
        h.Strings(31);
        var counts = new uint[16]; // languages, messages, permissions, types, components, tasks, directories, files, data entries, ...
        for (int i = 0; i < counts.Length; i++) {
            counts[i] = h.U32();
        }
        h.Skip(77); // version range, colours, password hash + salt, disk space, slices, five enum bytes
        if (h.Byte() != Lzma2) {
            throw new InvalidDataException("Unsupported compression.");
        }
        h.Skip(18); // architectures, two page enums, uninstall size, 46 option flags
        // Entries before the files, skipped as (string count, fixed bytes): Windows version ranges are 20 bytes.
        h.Entries(counts[0], 10, 21); // languages
        h.Entries(counts[1], 2, 4); // messages
        h.Entries(counts[2], 1, 0); // permissions
        h.Entries(counts[3], 4, 30); // types
        h.Entries(counts[4], 5, 42); // components
        h.Entries(counts[5], 6, 26); // tasks
        h.Entries(counts[6], 7, 27); // directories

        if (dataBlock.Length != (long)counts[8] * DataEntrySize) {
            throw new InvalidDataException("Data entry block size mismatch.");
        }
        var d = new Cursor(dataBlock);
        var data = new DataEntry[counts[8]];
        for (int i = 0; i < data.Length; i++) {
            data[i] = new DataEntry(d);
        }

        var files = new List<InstallerFile>();
        for (uint i = 0; i < counts[7]; i++) {
            h.Strings(1); // source
            string destination = Encoding.Unicode.GetString(h.String());
            h.Strings(8); // font name, assembly name, components, tasks, languages, check, after/before install
            h.Skip(20); // Windows version range
            uint location = h.U32();
            h.Skip(4 + 8 + 2 + 4 + 1); // attributes, external size, permission, 32 option flags, file type
            if (location < data.Length) {
                DataEntry e = data[location];
                files.Add(new InstallerFile(destination, (long)Math.Min(e.FileSize, long.MaxValue), e.Sha1) { Location = (int)location });
            }
        }
        return new GogInstaller(exe, dataOffset, data, files);
    }

    private static (long Header, long Data) FindLoaderTable(Stream exe) {
        byte[] stub = ReadAt(exe, 0, (int)Math.Min(exe.Length, LoaderScan));
        for (int at = 0; at + 44 <= stub.Length; at++) {
            if (stub.AsSpan(at, LoaderMagic.Length).SequenceEqual(LoaderMagic)
                && Crc32(stub.AsSpan(at, 40)) == BitConverter.ToUInt32(stub, at + 40)) {
                // magic, revision, unused, exe offset, exe size, exe CRC, header offset, data offset, CRC
                return (BitConverter.ToUInt32(stub, at + 32), BitConverter.ToUInt32(stub, at + 36));
            }
        }
        throw new InvalidDataException("No Inno Setup loader table.");
    }

    // A header block: CRC32, stored size, compressed flag, then 4-KiB sub-blocks each led by its CRC32; LZMA1 inside.
    private static (byte[] Data, long Next) ReadBlock(Stream exe, long offset) {
        byte[] head = ReadAt(exe, offset, 9);
        if (Crc32(head.AsSpan(4)) != BitConverter.ToUInt32(head, 0)) {
            throw new InvalidDataException("Block header CRC mismatch.");
        }
        uint stored = BitConverter.ToUInt32(head, 4);
        if (stored > HeaderCap) {
            throw new InvalidDataException("Block too large.");
        }
        byte[] raw = ReadAt(exe, offset + 9, (int)stored);
        var payload = new MemoryStream(raw.Length);
        for (int at = 0; at < raw.Length;) {
            int n = Math.Min(4096, raw.Length - at - 4);
            if (n <= 0 || Crc32(raw.AsSpan(at + 4, n)) != BitConverter.ToUInt32(raw, at)) {
                throw new InvalidDataException("Block CRC mismatch.");
            }
            payload.Write(raw, at + 4, n);
            at += 4 + n;
        }
        payload.Position = 0;
        if (head[8] == 0) {
            return (payload.ToArray(), offset + 9 + stored);
        }
        var props = new byte[5];
        if (payload.Read(props, 0, 5) != 5) {
            throw new InvalidDataException("Block too short.");
        }
        var decoded = new MemoryStream();
        Lzma1.DecodeToEnd(payload, decoded, props, HeaderCap);
        return (decoded.ToArray(), offset + 9 + stored);
    }

    /// <summary>Writes <paramref name="file"/> to <paramref name="output"/>, decompressing only its own chunk. Throws <see cref="InvalidDataException"/> on corrupt data or a SHA-1 mismatch (after writing).</summary>
    /// <param name="progress">Bytes of the file written so far.</param>
    public void Extract(InstallerFile file, Stream output, IProgress<long>? progress) {
        if (file.Location < 0 || file.Location >= _data.Length) {
            throw new InvalidDataException("Not a file of this installer.");
        }
        DataEntry d = _data[file.Location];
        if ((d.Flags & (CallInstructionOptimized | ChunkEncrypted)) != 0 || (d.Flags & ChunkCompressed) == 0 || d.FirstSlice != 0 || d.LastSlice != 0) {
            throw new InvalidDataException("Unsupported chunk.");
        }
        if (d.FileSize > (ulong)FileCap || d.FileOffset > (ulong)FileCap - d.FileSize) {
            throw new InvalidDataException("File too large.");
        }
        long start = _dataOffset + d.ChunkOffset;
        if (d.ChunkSize < 1 || d.ChunkSize > (ulong)_exe.Length || start + 4 + (long)d.ChunkSize > _exe.Length) {
            throw new InvalidDataException("Chunk outside the installer.");
        }
        if (!ReadAt(_exe, start, 4).AsSpan().SequenceEqual(ChunkMagic)) {
            throw new InvalidDataException("Bad chunk magic.");
        }
        // Inno's LZMA2 framing: one dictionary-size byte, then the LZMA2 stream.
        int prop = _exe.ReadByte();
        if (prop < 0 || prop > 40) {
            throw new InvalidDataException("Bad LZMA2 property.");
        }
        uint dictionary = prop == 40 ? uint.MaxValue : (2u | ((uint)prop & 1)) << (prop / 2 + 11);
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var sink = new FileSink(output, (long)d.FileOffset, sha1, progress);
        Lzma1.DecodeLzma2(_exe, sink, dictionary, (long)(d.FileOffset + d.FileSize));
        if (!sha1.GetHashAndReset().AsSpan().SequenceEqual(d.Sha1)) {
            throw new InvalidDataException("SHA-1 mismatch.");
        }
    }

    private static byte[] ReadAt(Stream s, long offset, int count) {
        if (offset < 0 || count < 0 || offset > s.Length - count) {
            throw new InvalidDataException("Read past the end of the installer.");
        }
        s.Position = offset;
        var buffer = new byte[count];
        for (int n = 0; n < count;) {
            int r = s.Read(buffer, n, count - n);
            if (r <= 0) {
                throw new InvalidDataException("Installer ended early.");
            }
            n += r;
        }
        return buffer;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n => {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) {
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c;
    }).ToArray();

    /// <summary>CRC-32 (IEEE, as zlib), which Inno Setup puts on its loader table and header blocks.</summary>
    internal static uint Crc32(ReadOnlySpan<byte> data) {
        uint c = 0xFFFFFFFF;
        foreach (byte b in data) {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        return ~c;
    }

    // Reads a decoded header block; every read is bounds-checked.
    private sealed class Cursor {
        private readonly byte[] _b;
        private int _pos;

        public Cursor(byte[] b) => _b = b;

        private int Take(long n) {
            if (n < 0 || n > _b.Length - _pos) {
                throw new InvalidDataException("Header ended early.");
            }
            int at = _pos;
            _pos += (int)n;
            return at;
        }

        public void Skip(long n) => Take(n);
        public byte Byte() => _b[Take(1)];
        public ushort U16() => BitConverter.ToUInt16(_b, Take(2));
        public uint U32() => BitConverter.ToUInt32(_b, Take(4));
        public ulong U64() => BitConverter.ToUInt64(_b, Take(8));
        public byte[] Bytes(int n) => _b.AsSpan(Take(n), n).ToArray();
        public byte[] String() => Bytes(checked((int)Math.Min(U32(), int.MaxValue)));

        public void Strings(int n) {
            for (int i = 0; i < n; i++) {
                Skip(U32());
            }
        }

        // Each entry takes at least 4 bytes, so a huge count runs out of header quickly.
        public void Entries(uint count, int strings, int fixedBytes) {
            for (uint i = 0; i < count; i++) {
                Strings(strings);
                Skip(fixedBytes);
            }
        }
    }

    // Drops the chunk's bytes before the file, hashes and passes on the file's own bytes.
    private sealed class FileSink : Stream {
        private readonly Stream _inner;
        private readonly IncrementalHash _sha1;
        private readonly IProgress<long>? _progress;
        private long _skip, _written;

        public FileSink(Stream inner, long skip, IncrementalHash sha1, IProgress<long>? progress) {
            _inner = inner;
            _skip = skip;
            _sha1 = sha1;
            _progress = progress;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            int drop = (int)Math.Min(_skip, count);
            _skip -= drop;
            offset += drop;
            count -= drop;
            if (count == 0) {
                return;
            }
            _sha1.AppendData(buffer, offset, count);
            _inner.Write(buffer, offset, count);
            _written += count;
            _progress?.Report(_written);
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

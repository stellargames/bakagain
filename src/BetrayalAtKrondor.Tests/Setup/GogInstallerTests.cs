namespace BetrayalAtKrondor.Tests.Setup;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using GameData.Setup;
using Xunit;
using Xunit.Abstractions;

// The real installer tests run only with $BAK_GOG_INSTALLER pointing at setup_betrayal_at_krondor_2.1.0.19.exe.
public class GogInstallerTests : IDisposable {
    private readonly ITestOutputHelper _out;
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public GogInstallerTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => Directory.Delete(_root, true);

    private static string? Installer() {
        string? path = Environment.GetEnvironmentVariable("BAK_GOG_INSTALLER");
        return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : path;
    }

    private static readonly (string Destination, string Sha1)[] GameFiles = {
        (@"{app}\KRONDOR.001", "742eea90e45f5711f0b795169229c16109037c03"),
        (@"{app}\FRP.SX", "4b852727629e8f80499e2511ceabc0d8b57bbf42"),
        (@"{app}\STARTUP.GAM", "4d987cb89ab786188265cdb3bafe343d573ccfa5"),
    };

    // Measured: the KRONDOR.001 chunk starts at data offset 0x3B400 + 57304709 (its "zlb\x1a" magic).
    private const long Krondor001Chunk = 0x3B400 + 57304709;

    private static byte[] Extract(GogInstaller installer, string destination) {
        var output = new MemoryStream();
        installer.Extract(installer.Files.Single(f => f.Destination == destination), output, null);
        return output.ToArray();
    }

    [Fact]
    public void TheGogInstallerListsTheGameFiles() {
        string? path = Installer();
        if (path == null) {
            return; // no installer on this machine
        }
        using FileStream exe = File.OpenRead(path);
        GogInstaller installer = GogInstaller.TryOpen(exe)!;
        Assert.NotNull(installer);
        InstallerFile k = installer.Files.Single(f => f.Destination == @"{app}\KRONDOR.001");
        Assert.Equal(11562652, k.Size);
        Assert.Contains(installer.Files, f => f.Destination == @"{app}\__support\save\RESOURCE.CFG");
    }

    [Fact]
    public void ExtractedFilesMatchTheirSha1() {
        string? path = Installer();
        if (path == null) {
            return;
        }
        using FileStream exe = File.OpenRead(path);
        var watch = Stopwatch.StartNew();
        GogInstaller installer = GogInstaller.TryOpen(exe)!;
        foreach ((string destination, string sha1) in GameFiles) {
            Assert.Equal(sha1, Convert.ToHexString(SHA1.HashData(Extract(installer, destination))).ToLowerInvariant());
        }
        _out.WriteLine($"open + extract of the three files: {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ExtractedArchiveMatchesTheWorkspaceCopy() {
        string? path = Installer();
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "OriginalGame", "KRONDOR.001"))) {
            dir = Path.GetDirectoryName(dir);
        }
        if (path == null || string.IsNullOrEmpty(dir)) {
            return;
        }
        using FileStream exe = File.OpenRead(path);
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "OriginalGame", "KRONDOR.001")), Extract(GogInstaller.TryOpen(exe)!, @"{app}\KRONDOR.001"));
    }

    [Fact]
    public void ACorruptChunkFailsTheSha1Check() {
        string? path = Installer();
        if (path == null) {
            return;
        }
        string copy = Path.Combine(_root, "setup.exe");
        File.Copy(path, copy);
        using (FileStream f = File.Open(copy, FileMode.Open)) {
            f.Position = Krondor001Chunk + 3_000_000;
            int b = f.ReadByte();
            f.Position--;
            f.WriteByte((byte)(b ^ 0x10));
        }
        using FileStream exe = File.OpenRead(copy);
        GogInstaller installer = GogInstaller.TryOpen(exe)!;
        Assert.Throws<InvalidDataException>(() => Extract(installer, @"{app}\KRONDOR.001"));
    }

    [Fact]
    public void FromGogInstallerImportsTheGameFiles() {
        string? path = Installer();
        if (path == null) {
            return;
        }
        string dest = Path.Combine(_root, "dest");
        double last = 0;
        ImportResult r = GameFilesImport.FromGogInstaller(path, dest, new Progress<double>(p => last = p));
        Assert.Null(r.Error);
        Assert.True(r.Report.AllOk);
        Assert.Equal(new[] { "FRP.SX", "KRONDOR.001", "RESOURCE.CFG", "STARTUP.GAM" }, Directory.GetFiles(dest).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(1.0, last, 3);
    }

    [Fact]
    public void ANonInnoFileIsNotAnInstaller() {
        var random = new byte[300_000];
        new Random(1014).NextBytes(random);
        Assert.Null(GogInstaller.TryOpen(new MemoryStream(random)));

        var zip = new MemoryStream();
        using (var a = new ZipArchive(zip, ZipArchiveMode.Create, true)) {
            using Stream s = a.CreateEntry("KRONDOR.001").Open();
            s.Write(random, 0, 1000);
        }
        zip.Position = 0;
        Assert.Null(GogInstaller.TryOpen(zip));
        Assert.Null(GogInstaller.TryOpen(new MemoryStream()));
    }

    [Fact]
    public void ANonInstallerImportsNothing() {
        string file = Path.Combine(_root, "setup.exe");
        File.WriteAllBytes(file, new byte[1000]);
        ImportResult r = GameFilesImport.FromGogInstaller(file, Path.Combine(_root, "dest"), null);
        Assert.Equal("not-installer", r.Error);
        Assert.False(r.Report.AllOk);
    }

    [Fact]
    public void ATruncatedInstallerIsRefusedCleanly() {
        string? path = Installer();
        if (path == null) {
            return;
        }
        var head = new byte[300 * 1024];
        using (FileStream f = File.OpenRead(path)) {
            f.ReadExactly(head);
        }
        try {
            Assert.Null(GogInstaller.TryOpen(new MemoryStream(head)));
        } catch (InvalidDataException) {
        }
    }

    [Fact]
    public void Crc32MatchesTheCheckValue() {
        Assert.Equal(0xCBF43926u, GogInstaller.Crc32("123456789"u8));
    }

    private sealed class Progress<T> : IProgress<T> {
        private readonly Action<T> _report;
        public Progress(Action<T> report) => _report = report;
        public void Report(T value) => _report(value);
    }
}

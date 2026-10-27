namespace BetrayalAtKrondor.Tests.Setup;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GameData.Mods;
using GameData.Setup;
using Xunit;

public class GameFilesImportTests : IDisposable {
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;
    private string Dest => Path.Combine(_root, "dest");
    private string Zip => Path.Combine(_root, "in.zip");

    public void Dispose() => Directory.Delete(_root, true);

    // One empty entry: 13-byte name + uint size 0, so the archive check passes.
    private static byte[] Krondor() => new byte[17];

    private void MakeZip(params (string name, byte[] data)[] entries) {
        using ZipArchive z = ZipFile.Open(Zip, ZipArchiveMode.Create);
        foreach ((string name, byte[] data) in entries) {
            using Stream s = z.CreateEntry(name).Open();
            s.Write(data, 0, data.Length);
        }
    }

    private static (string, byte[])[] Three(string prefix, string k = "KRONDOR.001") =>
        new[] { (prefix + k, Krondor()), (prefix + "frp.sx", new byte[] { 1 }), (prefix + "Startup.Gam", new byte[] { 1 }) };

    [Fact]
    public void AZipWithNestedFilesImports() {
        MakeZip(Three("game/").Append(("game/resource.cfg", new byte[] { 7 })).ToArray());
        ImportResult r = GameFilesImport.FromZip(Zip, Dest);
        Assert.Null(r.Error);
        Assert.True(r.Report.AllOk);
        Assert.True(File.Exists(Path.Combine(Dest, "FRP.SX")));
        Assert.True(File.Exists(Path.Combine(Dest, "STARTUP.GAM")));
        Assert.True(File.Exists(Path.Combine(Dest, "RESOURCE.CFG")));
        Assert.Empty(Directory.GetFiles(Dest, "*.part"));
    }

    [Fact]
    public void AZipWithTheFilesTwiceTakesTheShallowest() {
        var deep = Three("a/b/c/");
        deep[1] = ("a/b/c/frp.sx", new byte[] { 9 });
        var shallow = Three("x\\");
        shallow[1] = ("x\\frp.sx", new byte[] { 5 });
        MakeZip(deep.Concat(shallow).ToArray());
        Assert.True(GameFilesImport.FromZip(Zip, Dest).Report.AllOk);
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(Path.Combine(Dest, "FRP.SX")));
    }

    [Fact]
    public void TheResourceConfigComesFromSupportSaveWhenNotBesideTheFiles() {
        MakeZip(Three("g/").Append(("g/__support/save/RESOURCE.CFG", new byte[] { 3 })).ToArray());
        GameFilesImport.FromZip(Zip, Dest);
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(Dest, "RESOURCE.CFG")));
    }

    [Fact]
    public void ATraversalEntryIsNeverWrittenOutsideTheDestination() {
        MakeZip(Three("../x/"));
        ImportResult r = GameFilesImport.FromZip(Zip, Dest);
        Assert.True(r.Report.AllOk);
        Assert.False(Directory.Exists(Path.Combine(_root, "x")));
        Assert.Equal(new[] { "dest", "in.zip" }, Directory.GetFileSystemEntries(_root).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public void AnOversizedEntryIsRefused() {
        MakeZip(Three("", "KRONDOR.001"));
        ImportResult r = GameFilesImport.FromZip(Zip, Dest, new ModArchive.ZipLimits(10, 10, 100));
        Assert.Equal("too-large", r.Error);
        Assert.False(r.Report.AllOk);
        Assert.Empty(Directory.GetFiles(Dest, "*.part"));
    }

    [Fact]
    public void AZipWithoutTheFilesReportsAllMissing() {
        MakeZip(("readme.txt", new byte[] { 1 }));
        ImportResult r = GameFilesImport.FromZip(Zip, Dest);
        Assert.Null(r.Error);
        Assert.Equal(3, r.Report.Problems.Count());
        Assert.All(r.Report.Files.Values, s => Assert.Equal(GameFileState.Missing, s));
    }

    [Fact]
    public void ACorruptZipGivesAnErrorNotAnException() {
        File.WriteAllText(Zip, "this is not a zip");
        ImportResult r = GameFilesImport.FromZip(Zip, Dest);
        Assert.Equal("corrupt-zip", r.Error);
        Assert.False(r.Report.AllOk);
    }

    [Fact]
    public void FromFolderCopiesTheThreeAndTheConfigOverwritingOldFiles() {
        string src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "__support", "save"));
        File.WriteAllBytes(Path.Combine(src, "krondor.001"), Krondor());
        File.WriteAllBytes(Path.Combine(src, "frp.sx"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(src, "STARTUP.GAM"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(src, "__support", "save", "resource.cfg"), new byte[] { 4 });
        Directory.CreateDirectory(Dest);
        File.WriteAllBytes(Path.Combine(Dest, "FRP.SX"), new byte[] { 8, 8 });
        ImportResult r = GameFilesImport.FromFolder(src, Dest);
        Assert.Null(r.Error);
        Assert.True(r.Report.AllOk);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(Dest, "FRP.SX")));
        Assert.True(File.Exists(Path.Combine(Dest, "RESOURCE.CFG")));
    }

    [Fact]
    public void FromFolderOntoItselfOnlyChecks() {
        Directory.CreateDirectory(Dest);
        File.WriteAllBytes(Path.Combine(Dest, "KRONDOR.001"), Krondor());
        ImportResult r = GameFilesImport.FromFolder(Dest + "/", Dest);
        Assert.Null(r.Error);
        Assert.Equal(GameFileState.Ok, r.Report.Files["KRONDOR.001"]);
        Assert.Equal(GameFileState.Missing, r.Report.Files["FRP.SX"]);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070), "x", true)] // Windows ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070027), "x", true)] // Windows ERROR_HANDLE_DISK_FULL
    [InlineData(28, "x", true)] // .NET on Linux and Android: the raw errno, ENOSPC
    [InlineData(0, "No space left on device : '/x/KRONDOR.001.part'", true)]
    [InlineData(0, "There is not enough space on the disk.", true)]
    [InlineData(0, "Disk full. Path /x/KRONDOR.001.part", true)] // Mono's wording
    [InlineData(unchecked((int)0x80070005), "Access to the path is denied.", false)]
    [InlineData(13, "x", false)] // EACCES
    [InlineData(0, "The process cannot access the file because it is being used by another process.", false)]
    public void ADiskFullIOExceptionIsTold(int hresult, string message, bool full) =>
        Assert.Equal(full, GameFilesImport.IsDiskFull(new IOException(message, hresult)));

    [Fact]
    public void AFullDiskIsSpaceNotIo() {
        if (!File.Exists("/dev/full")) {
            return; // Linux only: every write to /dev/full fails with ENOSPC
        }
        MakeZip(Three(""));
        Directory.CreateDirectory(Dest);
        foreach (string name in GameFolder.Required) {
            File.CreateSymbolicLink(Path.Combine(Dest, name + ".part"), "/dev/full");
        }
        Assert.Equal("space", GameFilesImport.FromZip(Zip, Dest).Error);
    }
}

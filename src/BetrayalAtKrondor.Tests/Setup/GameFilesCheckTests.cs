namespace BetrayalAtKrondor.Tests.Setup;

using System;
using System.IO;
using System.Linq;
using GameData.Setup;
using Xunit;

public class GameFilesCheckTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private void WriteArchive(params (string name, int size)[] entries) {
        using var w = new BinaryWriter(File.Create(Path.Combine(_dir, "KRONDOR.001")));
        foreach ((string name, int size) in entries) {
            byte[] n = new byte[13];
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(n, 0);
            w.Write(n);
            w.Write((uint)size);
            w.Write(new byte[size]);
        }
    }

    private void WriteOthers() {
        File.WriteAllText(Path.Combine(_dir, "FRP.SX"), "x");
        File.WriteAllText(Path.Combine(_dir, "STARTUP.GAM"), "x");
    }

    [Fact]
    public void AnIntactArchiveOfAnyLayoutPasses() {
        WriteOthers();
        WriteArchive(("A.BMX", 10), ("B.DAT", 0), ("C.TTM", 300));
        Assert.True(GameFilesCheck.Check(_dir).AllOk);
        WriteArchive(("ONLY.ONE", 5));
        Assert.True(GameFilesCheck.Check(_dir).AllOk);
    }

    [Fact]
    public void ATruncatedArchiveIsDamaged() {
        WriteOthers();
        WriteArchive(("A.BMX", 10), ("C.TTM", 300));
        string path = Path.Combine(_dir, "KRONDOR.001");
        byte[] all = File.ReadAllBytes(path);
        File.WriteAllBytes(path, all[..^1]);
        GameFilesReport r = GameFilesCheck.Check(_dir);
        Assert.Equal(GameFileState.Damaged, r.Files["KRONDOR.001"]);
        Assert.Equal(new[] { "KRONDOR.001" }, r.Problems);
    }

    [Fact]
    public void ATrailingPartialHeaderIsDamaged() {
        WriteOthers();
        WriteArchive(("A.BMX", 10));
        using (var f = new FileStream(Path.Combine(_dir, "KRONDOR.001"), FileMode.Append)) {
            f.Write(new byte[5]);
        }
        Assert.Equal(GameFileState.Damaged, GameFilesCheck.Check(_dir).Files["KRONDOR.001"]);
    }

    [Fact]
    public void AnEmptyStartupGamIsEmpty() {
        WriteArchive(("A.BMX", 10));
        File.WriteAllText(Path.Combine(_dir, "FRP.SX"), "x");
        File.WriteAllBytes(Path.Combine(_dir, "STARTUP.GAM"), Array.Empty<byte>());
        Assert.Equal(GameFileState.Empty, GameFilesCheck.Check(_dir).Files["STARTUP.GAM"]);
    }

    [Fact]
    public void AnEmptyArchiveIsEmptyNotDamaged() {
        WriteOthers();
        File.WriteAllBytes(Path.Combine(_dir, "KRONDOR.001"), Array.Empty<byte>());
        Assert.Equal(GameFileState.Empty, GameFilesCheck.Check(_dir).Files["KRONDOR.001"]);
    }

    [Fact]
    public void AMissingFrpSxIsMissing() {
        File.WriteAllText(Path.Combine(_dir, "STARTUP.GAM"), "x");
        WriteArchive(("A.BMX", 10));
        GameFilesReport r = GameFilesCheck.Check(_dir);
        Assert.Equal(new[] { "FRP.SX" }, r.Problems.ToArray());
        Assert.Equal(GameFileState.Missing, r.Files["FRP.SX"]);
    }

    [Fact]
    public void ResourceCfgIsNotRequired() {
        WriteOthers();
        WriteArchive(("A.BMX", 10));
        Assert.True(GameFilesCheck.Check(_dir).AllOk);
    }

    [Fact]
    public void AFileThatCannotBeReadIsDamagedNotAnException() {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return; // chmod 000 blocks nothing there
        WriteOthers();
        WriteArchive(("A.BMX", 10));
        string frp = Path.Combine(_dir, "FRP.SX");
        File.SetUnixFileMode(frp, UnixFileMode.None);
        try {
            Assert.Equal(GameFileState.Damaged, GameFilesCheck.Check(_dir).Files["FRP.SX"]);
        } finally {
            File.SetUnixFileMode(frp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void LowerCaseNamesPass() {
        WriteArchive(("A.BMX", 10));
        File.Move(Path.Combine(_dir, "KRONDOR.001"), Path.Combine(_dir, "krondor.001"));
        File.WriteAllText(Path.Combine(_dir, "frp.sx"), "x");
        File.WriteAllText(Path.Combine(_dir, "startup.gam"), "x");
        Assert.True(GameFilesCheck.Check(_dir).AllOk);
    }

    [Fact]
    public void TheRealArchivePasses() {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "OriginalGame", "KRONDOR.001"))) {
            dir = Path.GetDirectoryName(dir);
        }
        if (string.IsNullOrEmpty(dir)) {
            return; // no game data in this checkout
        }
        using FileStream f = File.OpenRead(Path.Combine(dir, "OriginalGame", "KRONDOR.001"));
        Assert.True(GameFilesCheck.ArchiveIsIntact(f));
    }
}

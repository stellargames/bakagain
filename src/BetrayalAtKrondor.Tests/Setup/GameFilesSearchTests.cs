namespace BetrayalAtKrondor.Tests.Setup;

using System;
using System.IO;
using System.Linq;
using GameData.Setup;
using Xunit;

public class GameFilesSearchTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine(parts.Prepend(_dir).ToArray())).FullName;

    private static void Put(string dir, params string[] names) {
        foreach (string n in names) {
            File.WriteAllText(Path.Combine(dir, n), "x");
        }
    }

    [Fact]
    public void TheFilesOneLevelDownAreFound() {
        string sub = Dir("game");
        Put(sub, "krondor.001", "frp.sx", "Startup.Gam");
        Assert.Equal(sub, GameFilesSearch.FindGameFolder(_dir));
        Assert.Equal(3, GameFilesSearch.CountFound(sub));
    }

    [Fact]
    public void TheShallowestCompleteFolderWins() {
        string deep = Dir("a", "b");
        string shallow = Dir("z");
        Put(deep, GameFolder.Required);
        Put(shallow, GameFolder.Required);
        Assert.Equal(shallow, GameFilesSearch.FindGameFolder(_dir));
    }

    [Fact]
    public void FilesFromTwoFoldersAreNeverMixed() {
        Put(_dir, "KRONDOR.001", "FRP.SX");
        Put(Dir("sub"), "STARTUP.GAM");
        Assert.Null(GameFilesSearch.FindGameFolder(_dir));
        Assert.Equal(2, GameFilesSearch.CountFound(_dir));
    }

    [Fact]
    public void DepthThreeIsNotSearched() {
        Put(Dir("a", "b", "c"), GameFolder.Required);
        Assert.Null(GameFilesSearch.FindGameFolder(_dir));
        Assert.Null(GameFilesSearch.FindGameFolder(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void ClassifyRecognisesTheGogInstaller() =>
        Assert.Equal(PickKind.GogInstaller, GameFilesSearch.Classify(new[] { "Setup_Betrayal_at_Krondor_1.0.exe", "readme.txt" }));

    [Fact]
    public void ClassifyRecognisesTheCdImage() {
        Assert.Equal(PickKind.CdImage, GameFilesSearch.Classify(new[] { "BAK.GOG" }));
        Assert.Equal(PickKind.CdImage, GameFilesSearch.Classify(new[] { "bak.inst" }));
    }

    [Fact]
    public void ClassifyRecognisesAZip() =>
        Assert.Equal(PickKind.Zip, GameFilesSearch.Classify(new[] { "Game.ZIP" }));

    [Fact]
    public void ClassifyRecognisesTheThreeFiles() {
        Assert.Equal(PickKind.GameFiles, GameFilesSearch.Classify(new[] { "krondor.001", "FRP.SX", "startup.gam", "extra.dat", "bak.gog" }));
        Assert.Equal(PickKind.Nothing, GameFilesSearch.Classify(new[] { "krondor.001", "notes.txt" }));
    }
}

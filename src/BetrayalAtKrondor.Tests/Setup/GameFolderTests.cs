namespace BetrayalAtKrondor.Tests.Setup;

using System;
using System.IO;
using GameData.Setup;
using Xunit;

public class GameFolderTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void FindMatchesALowerCaseName() {
        File.WriteAllText(Path.Combine(_dir, "startup.gam"), "x");
        Assert.EndsWith("startup.gam", GameFolder.Find(_dir, "STARTUP.GAM"));
    }

    [Fact]
    public void FindReturnsNullWhenAbsent() {
        Assert.Null(GameFolder.Find(_dir, "STARTUP.GAM"));
        Assert.Null(GameFolder.Find(Path.Combine(_dir, "missing"), "STARTUP.GAM"));
    }

    [Fact]
    public void FindResourceConfigFallsBackToGogSaveFolder() {
        string save = Directory.CreateDirectory(Path.Combine(_dir, "__support", "save")).FullName;
        string cfg = Path.Combine(save, "RESOURCE.CFG");
        File.WriteAllText(cfg, "x");
        Assert.Equal(cfg, GameFolder.FindResourceConfig(_dir));
    }
}

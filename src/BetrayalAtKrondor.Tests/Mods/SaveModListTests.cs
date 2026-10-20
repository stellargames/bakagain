namespace BetrayalAtKrondor.Tests.Mods;

using System.IO;
using System.Linq;
using GameData.Mods;
using Xunit;

public class SaveModListTests {
    private static InstalledMod Mod(string id, string version = "1.0.0") {
        string json = $$"""{ "schema": 1, "id": "{{id}}", "name": "{{id}}", "version": "{{version}}" }""";
        ModManifest? m = ModManifest.Parse(json, out var errors);
        Assert.Empty(errors);
        return new InstalledMod(m!, "mods/" + id);
    }

    private static SavedMod Saved(string id, string version = "1.0.0", params ModMode[] modes)
        => new(id, version, modes.Length == 0 ? new[] { ModMode.Enhanced } : modes);

    [Fact]
    public void SidecarPath_NextToSave() {
        Assert.Equal(Path.Combine("a", "b", "SAVE03.MODS.json"), SaveModList.SidecarPath(Path.Combine("a", "b", "SAVE03.GAM")));
    }

    [Fact]
    public void WriteRead_RoundTrips_WithModes() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            string save = Path.Combine(dir, "SAVE01.GAM");
            SaveModList.Write(save, new[] { Mod("aa", "1.4.0") });
            Assert.Contains("\"enhanced\"", File.ReadAllText(SaveModList.SidecarPath(save)));
            SavedMod s = Assert.Single(SaveModList.Read(save));
            Assert.Equal("aa", s.Id);
            Assert.Equal("1.4.0", s.Version);
            Assert.Equal(new[] { ModMode.Enhanced }, s.Modes);
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_EmptyList_StillWritesSidecar() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            string save = Path.Combine(dir, "SAVE01.GAM");
            SaveModList.Write(save, new InstalledMod[0]);
            Assert.True(File.Exists(SaveModList.SidecarPath(save)));
            Assert.Empty(SaveModList.Read(save));
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Compare_NoSidecar_NoIssues() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            var saved = SaveModList.Read(Path.Combine(dir, "SAVE01.GAM"));
            Assert.Empty(SaveModList.Compare(saved, new[] { Mod("aa") }, ModMode.Enhanced));
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Compare_MissingInMode_Issue() {
        var issues = SaveModList.Compare(new[] { Saved("aa", "1.2.0") }, new InstalledMod[0], ModMode.Enhanced);
        SaveModIssue i = Assert.Single(issues);
        Assert.Equal(new SaveModIssue("aa", "1.2.0", null, SaveModIssueKind.Missing), i);
    }

    [Fact]
    public void Compare_IgnoresModsOfOtherModes() {
        Assert.Empty(SaveModList.Compare(new[] { Saved("aa", "1.0.0", ModMode.Enhanced) }, new InstalledMod[0], ModMode.Classic));
    }

    [Fact]
    public void Compare_MajorMismatch_Issue() {
        var issues = SaveModList.Compare(new[] { Saved("aa", "1.4.0") }, new[] { Mod("aa", "2.0.0") }, ModMode.Enhanced);
        Assert.Equal(new SaveModIssue("aa", "1.4.0", "2.0.0", SaveModIssueKind.MajorVersion), Assert.Single(issues));
    }

    [Fact]
    public void Compare_MinorDifference_NoIssue() {
        Assert.Empty(SaveModList.Compare(new[] { Saved("aa", "1.4.0") }, new[] { Mod("aa", "1.9.2") }, ModMode.Enhanced));
    }

    [Fact]
    public void Compare_ExtraActiveMods_NoIssue() {
        Assert.Empty(SaveModList.Compare(new SavedMod[0], new[] { Mod("aa"), Mod("bb") }, ModMode.Enhanced));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "mods": null }""")]
    [InlineData("""{ "mods": [ null ] }""")]
    public void Read_CorruptJson_Empty(string json) {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            string save = Path.Combine(dir, "SAVE01.GAM");
            File.WriteAllText(SaveModList.SidecarPath(save), json);
            Assert.Empty(SaveModList.Read(save));
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_NullIdOrVersion_Skipped_NoThrow() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            string save = Path.Combine(dir, "SAVE01.GAM");
            File.WriteAllText(SaveModList.SidecarPath(save),
                """{ "mods": [ { "id": null, "version": "1.0.0" }, { "id": "aa", "version": null }, { "id": "bb", "version": "1.0.0", "modes": ["classic"] } ] }""");
            var read = SaveModList.Read(save);
            Assert.Equal("bb", Assert.Single(read).Id);
            Assert.Empty(SaveModList.Compare(read, new[] { Mod("bb") }, ModMode.Classic));
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_UnknownModeInOneEntry_SkipsOnlyThatEntry() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            string save = Path.Combine(dir, "SAVE01.GAM");
            File.WriteAllText(SaveModList.SidecarPath(save),
                """{ "mods": [ { "id": "aa", "version": "1.0.0", "modes": ["turbo"] }, { "id": "bb", "version": "1.0.0", "modes": ["enhanced", "Classic"] }, { "id": "cc", "version": "1.0.0", "modes": ["1"] } ] }""");
            SavedMod s = Assert.Single(SaveModList.Read(save));
            Assert.Equal("bb", s.Id);
            Assert.Equal(new[] { ModMode.Enhanced, ModMode.Classic }, s.Modes);
        } finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Write_ReplacesExistingSidecar_LeavesNoTempFile() {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try {
            string save = Path.Combine(dir, "SAVE01.GAM");
            SaveModList.Write(save, new[] { Mod("aa") });
            SaveModList.Write(save, new[] { Mod("bb", "2.0.0") });
            Assert.Equal("bb", Assert.Single(SaveModList.Read(save)).Id);
            Assert.Equal(new[] { "SAVE01.MODS.json" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
        } finally { Directory.Delete(dir, true); }
    }
}

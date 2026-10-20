namespace BetrayalAtKrondor.Tests.Mods;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GameData.Mods;
using Xunit;

public class ModArchiveTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Manifest(string id = "pack", string version = "1.0.0", string extra = "")
        => $$"""{ "schema": 1, "id": "{{id}}", "name": "{{id}}", "version": "{{version}}"{{extra}} }""";

    private string Zip(IDictionary<string, string> files, string name = "m.zip") {
        string path = Path.Combine(_dir, name);
        using (ZipArchive z = ZipFile.Open(path, ZipArchiveMode.Create)) {
            foreach (var f in files) {
                using var w = new StreamWriter(z.CreateEntry(f.Key).Open());
                w.Write(f.Value);
            }
        }
        return path;
    }

    private string Folder(IDictionary<string, string> files, string name = "src") {
        string root = Path.Combine(_dir, name);
        foreach (var f in files) {
            string p = Path.Combine(root, f.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, f.Value);
        }
        return root;
    }

    private static Dictionary<string, string> Good() => new() {
        ["mod.json"] = Manifest(),
        ["BMX/Z01SLOT0/3.png"] = "png",
        ["SCX/TITLE.png"] = "png",
    };

    [Fact]
    public void Validate_GoodMod_Ok() {
        ModValidation v = ModArchive.ValidateZip(Zip(Good()));
        Assert.True(v.Ok, string.Join("; ", v.Errors));
        Assert.Equal("pack", v.Manifest!.Id);
        Assert.Empty(v.Warnings);
    }

    [Fact]
    public void Validate_GoodMod_Folder_Ok() {
        Assert.True(ModArchive.ValidateFolder(Folder(Good())).Ok);
    }

    [Fact]
    public void Validate_ColourSetFolder_And_DataExtensions_Ok() {
        var f = Good();
        f["BMX/MOR1_CS2/0.png"] = "png";
        f["DDX/DIALOG.json"] = "{}";
        f["ObjectInfo/1.json"] = "{}";
        f["Resources/Enhanced/Sky/moon_large.png"] = "png";
        Assert.True(ModArchive.ValidateZip(Zip(f)).Ok);
    }

    [Fact]
    public void Validate_NoManifest_Error() {
        ModValidation v = ModArchive.ValidateZip(Zip(new Dictionary<string, string> { ["SCX/TITLE.png"] = "png" }));
        Assert.False(v.Ok);
        Assert.Contains(v.Errors, e => e.Contains("mod.json"));
    }

    [Fact]
    public void Validate_DllInside_Error() {
        var f = Good();
        f["evil.dll"] = "x";
        f["BMX/Z01SLOT0/payload.dll"] = "x";
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.Contains(v.Errors, e => e.Contains("evil.dll"));
        Assert.Contains(v.Errors, e => e.Contains("payload.dll"));
    }

    [Theory]
    [InlineData("../evil.png")]
    [InlineData("/abs.png")]
    [InlineData("C:/x.png")]
    [InlineData("BMX\\Z01SLOT0\\4.png")]
    [InlineData("BMX/../../x.png")]
    public void Validate_UnsafePath_Error(string entry) {
        var f = Good();
        f[entry] = "x";
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.False(v.Ok);
        Assert.Contains("unsafe path: " + entry, v.Errors);
    }

    [Fact]
    public void Validate_UnknownTopLevel_Error() {
        var f = Good();
        f["readme.png"] = "x";
        f["stuff/a.png"] = "x";
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.Contains(v.Errors, e => e.Contains("readme.png"));
        Assert.Contains(v.Errors, e => e.Contains("stuff/a.png"));
    }

    [Fact]
    public void Validate_SunVariantWithoutBase_Error() {
        var f = Good();
        f["BMX/Z01SLOT0/7.front.png"] = "png";
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.Contains(v.Errors, e => e.Contains("7.front.png"));
        f["BMX/Z01SLOT0/7.png"] = "png";
        Assert.True(ModArchive.ValidateZip(Zip(f, "ok.zip")).Ok);
    }

    [Fact]
    public void Validate_ClassicWithSunVariants_Warning() {
        var f = Good();
        f["mod.json"] = Manifest(extra: ", \"modes\": [\"enhanced\", \"classic\"]");
        f["BMX/Z01SLOT0/3.front.png"] = "png";
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.True(v.Ok);
        Assert.Single(v.Warnings);
    }

    [Fact]
    public void Validate_AliasTargetMissing_Error() {
        var f = Good();
        f["aliases.json"] = """{ "BMX/Z02SLOT1/4": "BMX/Z01SLOT0/9" }""";
        Assert.Contains(ModArchive.ValidateZip(Zip(f)).Errors, e => e.Contains("BMX/Z01SLOT0/9"));
        f["aliases.json"] = """{ "BMX/Z02SLOT1/4": "BMX/Z01SLOT0/3" }""";
        Assert.True(ModArchive.ValidateZip(Zip(f, "ok.zip")).Ok);
    }

    [Fact]
    public void Validate_LanguageWithoutLang_Error() {
        var f = Good();
        f["mod.json"] = Manifest(extra: ", \"kinds\": [\"language\"]");
        Assert.Contains(ModArchive.ValidateZip(Zip(f)).Errors, e => e.Contains("Lang/"));
        f["Lang/nl/nl.po"] = "x";
        Assert.True(ModArchive.ValidateZip(Zip(f, "ok.zip")).Ok);
    }

    [Fact]
    public void Validate_LanguageModFonts_Ok() {
        string folder = Folder(new Dictionary<string, string> {
            ["mod.json"] = Manifest(extra: ", \"kinds\": [\"language\"]"),
            ["Lang/xx/xx.po"] = "x",
            ["Lang/xx/fonts/GAME.bdf"] = "STARTFONT 2.1",
        });
        ModValidation v = ModArchive.ValidateFolder(folder);
        Assert.True(v.Ok, string.Join("; ", v.Errors));
    }

    [Fact]
    public void Validate_UnknownKind_IsAWarning() {
        var f = Good();
        f["mod.json"] = Manifest(extra: ", \"kinds\": [\"smells\"]");
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.True(v.Ok, string.Join("; ", v.Errors));
        Assert.Contains(v.Warnings, w => w.Contains("smells"));
    }

    [Fact]
    public void Install_Extracts_ToIdFolder() {
        string root = Path.Combine(_dir, "mods");
        InstalledMod m = ModArchive.Install(Zip(Good()), root);
        Assert.Equal(Path.Combine(root, "pack"), m.Folder);
        Assert.Equal("pack", m.Manifest.Id);
        Assert.True(File.Exists(Path.Combine(root, "pack", "BMX", "Z01SLOT0", "3.png")));
        Assert.False(Directory.Exists(Path.Combine(root, "pack.tmp")));
    }

    [Fact]
    public void Install_ReplacesPreviousVersion() {
        string root = Path.Combine(_dir, "mods");
        ModArchive.Install(Zip(Good(), "a.zip"), root);
        var v2 = Good();
        v2["mod.json"] = Manifest(version: "2.0.0");
        v2.Remove("SCX/TITLE.png");
        InstalledMod m = ModArchive.Install(Zip(v2, "b.zip"), root);
        Assert.Equal("2.0.0", m.Manifest.Version.ToString());
        Assert.False(File.Exists(Path.Combine(root, "pack", "SCX", "TITLE.png")));
    }

    [Fact]
    public void Install_OldFolderUndeletable_StillInstalled() {
        if (OperatingSystem.IsWindows()) return; // the lock below is a Unix permission
        string root = Path.Combine(_dir, "mods");
        ModArchive.Install(Zip(Good(), "a.zip"), root);
        // A folder whose files cannot be removed: deleting <id>.old fails after the swap.
        string scx = Path.Combine(root, "pack", "SCX");
        File.SetUnixFileMode(scx, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try {
            var v2 = Good();
            v2["mod.json"] = Manifest(version: "2.0.0");
            InstalledMod m = ModArchive.Install(Zip(v2, "b.zip"), root);
            Assert.Equal("2.0.0", m.Manifest.Version.ToString());
            Assert.True(Directory.Exists(Path.Combine(root, "pack.old")), "left behind; the scan skips it");
        } finally {
            string oldScx = Path.Combine(root, "pack.old", "SCX");
            if (Directory.Exists(oldScx)) File.SetUnixFileMode(oldScx, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Install_ZipSlipEntry_Refused_PreviousVersionKept() {
        string root = Path.Combine(_dir, "sandbox", "mods");
        ModArchive.Install(Zip(Good(), "a.zip"), root);
        var evil = Good();
        evil["../evil.png"] = "x";
        evil["../../evil2.png"] = "x";
        Assert.Throws<InvalidDataException>(() => ModArchive.Install(Zip(evil, "b.zip"), root));
        Assert.True(File.Exists(Path.Combine(root, "pack", "SCX", "TITLE.png")));
        Assert.False(Directory.Exists(Path.Combine(root, "pack.tmp")));
        Assert.Equal(new[] { "pack" }, Directory.GetFileSystemEntries(root).Select(Path.GetFileName).ToArray());
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "sandbox")));
        Assert.False(File.Exists(Path.Combine(_dir, "evil2.png")));
    }

    [Fact]
    public void Install_DuplicateManifest_Refused_PreviousVersionKept() {
        string root = Path.Combine(_dir, "mods");
        ModArchive.Install(Zip(Good(), "a.zip"), root);
        string path = Path.Combine(_dir, "dup.zip");
        using (ZipArchive z = ZipFile.Open(path, ZipArchiveMode.Create)) {
            foreach (var (name, text) in new[] { ("mod.json", Manifest(version: "2.0.0")), ("mod.json", "{"), ("SCX/X.png", "x") }) {
                using var w = new StreamWriter(z.CreateEntry(name).Open());
                w.Write(text);
            }
        }
        Assert.Contains(ModArchive.ValidateZip(path).Errors, e => e.Contains("duplicate") && e.Contains("mod.json"));
        Assert.Throws<InvalidDataException>(() => ModArchive.Install(path, root));
        Assert.True(File.Exists(Path.Combine(root, "pack", "SCX", "TITLE.png")));
        Assert.Equal("1.0.0", ModArchive.ReadInstalled(Path.Combine(root, "pack"))!.Manifest.Version.ToString());
        Assert.False(Directory.Exists(Path.Combine(root, "pack.tmp")));
    }

    [Fact]
    public void Pack_Folder_WritesIdVersionZip_ThatValidates() {
        string src = Folder(Good());
        string outDir = Path.Combine(_dir, "out");
        string zip = ModArchive.Pack(src, outDir);
        Assert.Equal(Path.Combine(outDir, "pack-1.0.0.zip"), zip);
        Assert.True(ModArchive.ValidateZip(zip).Ok);
        using ZipArchive z = ZipFile.OpenRead(zip);
        Assert.Contains(z.Entries, e => e.FullName == "BMX/Z01SLOT0/3.png");
    }

    [Fact]
    public void Validate_MissingFolder_IsAnError() {
        ModValidation v = ModArchive.ValidateFolder(Path.Combine(_dir, "nope"));
        Assert.False(v.Ok);
        Assert.Contains(v.Errors, e => e.Contains("cannot read folder"));
    }

    [Fact]
    public void Pack_InvalidFolder_Throws() {
        string src = Folder(new Dictionary<string, string> { ["mod.json"] = Manifest(), ["x.dll"] = "x" });
        Assert.Throws<InvalidDataException>(() => ModArchive.Pack(src, Path.Combine(_dir, "out")));
    }

    [Fact]
    public void ReadInstalled_NullWithoutValidManifest() {
        Assert.Null(ModArchive.ReadInstalled(Folder(new Dictionary<string, string> { ["SCX/A.png"] = "x" }, "none")));
        Assert.Null(ModArchive.ReadInstalled(Folder(new Dictionary<string, string> { ["mod.json"] = "{" }, "bad")));
        Assert.Equal("pack", ModArchive.ReadInstalled(Folder(Good(), "ok"))!.Manifest.Id);
    }

    [Fact]
    public void Aliases_Read() {
        string dir = Folder(new Dictionary<string, string> { ["aliases.json"] = """{ "BMX/Z02SLOT1/4": "BMX/Z01SLOT0/3" }""" });
        Assert.Equal("BMX/Z01SLOT0/3", ModAliases.Read(dir)["BMX/Z02SLOT1/4"]);
        Assert.Empty(ModAliases.Read(Path.Combine(_dir, "nowhere")));
    }

    [Fact]
    public void Validate_TooManyEntries_Refused() {
        string zip = Zip(Good());
        ModValidation v = ModArchive.ValidateZip(zip, new ModArchive.ZipLimits(2, long.MaxValue, long.MaxValue));
        Assert.False(v.Ok);
        Assert.Equal("archive has 3 entries; the limit is 2", Assert.Single(v.Errors));
        Assert.Null(v.Manifest);
    }

    [Fact]
    public void Validate_OneEntryTooBig_Refused() {
        var f = Good();
        f["SCX/BIG.png"] = new string('x', 5000);
        ModValidation v = ModArchive.ValidateZip(Zip(f), new ModArchive.ZipLimits(100, 4000, long.MaxValue));
        Assert.Equal("entry 'SCX/BIG.png' is 5000 bytes uncompressed; the limit is 4000 per entry", Assert.Single(v.Errors));
    }

    [Fact]
    public void Validate_TotalTooBig_Refused() {
        var f = Good();
        f["SCX/A.png"] = new string('x', 3000);
        f["SCX/B.png"] = new string('x', 3000);
        ModValidation v = ModArchive.ValidateZip(Zip(f), new ModArchive.ZipLimits(100, 4000, 5000));
        string error = Assert.Single(v.Errors);
        Assert.StartsWith("archive is ", error);
        Assert.EndsWith("bytes uncompressed; the limit is 5000", error);
    }

    [Fact]
    public void Validate_DefaultLimits_AreTheDocumentedOnes() {
        Assert.Equal(new ModArchive.ZipLimits(20_000, 256L * 1024 * 1024, 1024L * 1024 * 1024), ModArchive.ZipLimits.Default);
        Assert.True(ModArchive.ValidateZip(Zip(Good())).Ok);
    }

    // System.IO.Compression bounds a read by the size the header declares, so a zip with an understated header
    // cannot be made to overrun here. The counting path is therefore driven through Extract (no validation) with
    // lowered limits, which is the same loop a lying header would reach.
    [Fact]
    public void Extract_EntryOverLimit_Aborted_PreviousVersionKept() {
        string root = Path.Combine(_dir, "mods");
        ModArchive.Install(Zip(Good(), "a.zip"), root);
        var v2 = Good();
        v2["mod.json"] = Manifest(version: "2.0.0");
        v2["SCX/BIG.png"] = new string('x', 50_000);
        var e = Assert.Throws<InvalidDataException>(() => ModArchive.Extract(Zip(v2, "b.zip"), "pack", root, new ModArchive.ZipLimits(100, 4000, long.MaxValue)));
        Assert.Contains("larger than its header says", e.Message);
        Assert.Equal("1.0.0", ModArchive.ReadInstalled(Path.Combine(root, "pack"))!.Manifest.Version.ToString());
        Assert.Equal(new[] { "pack" }, Directory.GetFileSystemEntries(root).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void Extract_TotalOverLimit_Aborted_NoTempFolder() {
        string root = Path.Combine(_dir, "mods");
        var f = Good();
        f["SCX/A.png"] = new string('x', 3000);
        f["SCX/B.png"] = new string('x', 3000);
        var e = Assert.Throws<InvalidDataException>(() => ModArchive.Extract(Zip(f), "pack", root, new ModArchive.ZipLimits(100, 4000, 5000)));
        Assert.Contains("larger than its headers say", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact]
    public void Install_OverLimitByHeaders_RefusedBeforeExtracting() {
        string root = Path.Combine(_dir, "mods");
        Assert.Throws<InvalidDataException>(() => ModArchive.Install(Zip(Good()), root, new ModArchive.ZipLimits(2, long.MaxValue, long.MaxValue)));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Extract_ZipSlipEntry_RefusedByInstallCheck_WithoutValidation() {
        string root = Path.Combine(_dir, "sandbox", "mods");
        var evil = Good();
        evil["../evil.png"] = "x";
        string zip = Zip(evil);
        Assert.False(ModArchive.ValidateZip(zip).Ok);
        var e = Assert.Throws<InvalidDataException>(() => ModArchive.Extract(zip, "pack", root, ModArchive.ZipLimits.Default));
        Assert.StartsWith("entry escapes the mod folder: ", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(root));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "sandbox")));
    }

    [Fact]
    public void RecoverStrandedInstalls_MovesOldBack_OnlyWhenIdIsMissing() {
        string root = Path.Combine(_dir, "mods");
        ModArchive.Install(Zip(Good(), "a.zip"), root);
        // A crash between the two moves: only <id>.old and a finished <id>.tmp remain.
        Directory.Move(Path.Combine(root, "pack"), Path.Combine(root, "pack.old"));
        ModArchive.RecoverStrandedInstalls(root);
        Assert.Equal("pack", ModArchive.ReadInstalled(Path.Combine(root, "pack"))!.Manifest.Id);
        Assert.False(Directory.Exists(Path.Combine(root, "pack.old")));

        // When the new version is in place, .old is left alone.
        Directory.CreateDirectory(Path.Combine(root, "pack.old"));
        ModArchive.RecoverStrandedInstalls(root);
        Assert.True(Directory.Exists(Path.Combine(root, "pack.old")));
        Assert.NotNull(ModArchive.ReadInstalled(Path.Combine(root, "pack")));
        ModArchive.RecoverStrandedInstalls(Path.Combine(_dir, "no-such-root"));
    }

    [Fact]
    public void Pack_OutDirInsideFolder_Refused() {
        string src = Folder(Good());
        foreach (string outDir in new[] { Path.Combine(src, "out"), src, src + Path.DirectorySeparatorChar }) {
            var e = Assert.Throws<InvalidDataException>(() => ModArchive.Pack(src, outDir));
            Assert.Contains("is inside the mod folder", e.Message);
        }
        Assert.False(Directory.Exists(Path.Combine(src, "out")));
        Assert.True(File.Exists(ModArchive.Pack(src, src + "-sibling-out")), "a sibling whose name starts the same is not inside");
    }

    [Fact]
    public void Pack_ReusesGivenValidation() {
        string src = Folder(Good());
        ModValidation v = ModArchive.ValidateFolder(src);
        File.Delete(Path.Combine(src, "mod.json")); // a second validation would now fail
        Assert.True(File.Exists(ModArchive.Pack(src, Path.Combine(_dir, "out"), v)));
    }

    [Theory]
    [InlineData("SCX//X.png")]
    [InlineData("SCX/./X.png")]
    [InlineData("//X.png")]
    public void Validate_EmptyOrDotSegments_UnsafePath(string entry) {
        var f = Good();
        f[entry] = "x";
        ModValidation v = ModArchive.ValidateZip(Zip(f));
        Assert.Equal("unsafe path: " + entry, v.Errors.First());
    }

    [Fact]
    public void Validate_FolderEntries_AreNotUnsafe() {
        string path = Path.Combine(_dir, "dirs.zip");
        using (ZipArchive z = ZipFile.Open(path, ZipArchiveMode.Create)) {
            z.CreateEntry("SCX/");
            foreach (var f in Good()) {
                using var w = new StreamWriter(z.CreateEntry(f.Key).Open());
                w.Write(f.Value);
            }
        }
        ModValidation v = ModArchive.ValidateZip(path);
        Assert.True(v.Ok, string.Join("; ", v.Errors));
    }

    [Theory]
    [InlineData("mod")]
    [InlineData("BMX/Z01SLOT0/3.front")]
    [InlineData("aliases")]
    public void Validate_AliasTarget_MustBeAnImageUnderScxOrBmx(string target) {
        var f = Good();
        f["aliases.json"] = "{ \"BMX/Z02SLOT1/4\": \"" + target + "\" }";
        f["BMX/Z01SLOT0/3.front.png"] = "png"; // so the sun-variant target exists as a file
        Assert.Contains(ModArchive.ValidateZip(Zip(f)).Errors, e => e.Contains("target is not an SCX/ or BMX/ image of this mod") && e.Contains(target));
    }
}

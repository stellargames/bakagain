namespace BetrayalAtKrondor.Tests.Mods;

using System;
using System.IO;
using System.Linq;
using GameData.Mods;
using Xunit;

public class ModResolverTests {
    private static InstalledMod Mod(string id, string version = "1.0.0", string extra = "") {
        string json = $$"""{ "schema": 1, "id": "{{id}}", "name": "{{id}}", "version": "{{version}}"{{extra}} }""";
        ModManifest? m = ModManifest.Parse(json, out var errors);
        Assert.Empty(errors);
        return new InstalledMod(m!, "mods/" + id);
    }

    private static ModStateEntry On(string id) => new(id, "1.0.0", true);
    private static ModStateEntry Off(string id) => new(id, "1.0.0", false);
    private static string[] Ids(System.Collections.Generic.IEnumerable<InstalledMod> mods) => mods.Select(m => m.Manifest.Id).ToArray();

    private static ModResolution Resolve(InstalledMod[] installed, ModStateEntry[] state, ModMode mode = ModMode.Enhanced, SemVersion? game = null)
        => ModResolver.Resolve(installed, state, mode, game);

    [Fact]
    public void Resolve_OrderFollowsState_HighestFirst() {
        ModResolution r = Resolve(new[] { Mod("aa"), Mod("bb"), Mod("cc") }, new[] { On("cc"), On("aa"), On("bb") });
        Assert.Equal(new[] { "cc", "aa", "bb" }, Ids(r.Active));
        Assert.Empty(r.Problems);
    }

    [Fact]
    public void Resolve_UnlistedInstalledMod_EnabledAtTop() {
        ModResolution r = Resolve(new[] { Mod("aa"), Mod("bb") }, new[] { On("aa") });
        Assert.Equal(new[] { "bb", "aa" }, Ids(r.Active));
    }

    [Fact]
    public void Resolve_DisabledExcluded_NoProblem() {
        ModResolution r = Resolve(new[] { Mod("aa"), Mod("bb") }, new[] { Off("aa"), On("bb") });
        Assert.Equal(new[] { "bb" }, Ids(r.Active));
        Assert.Empty(r.Problems);
    }

    [Fact]
    public void Resolve_ModeFilter_EnhancedOnlyAbsentInClassic() {
        InstalledMod[] installed = { Mod("aa"), Mod("bb", extra: ", \"modes\": [\"enhanced\", \"classic\"]") };
        ModStateEntry[] state = { On("aa"), On("bb") };
        Assert.Equal(new[] { "aa", "bb" }, Ids(Resolve(installed, state).Active));
        Assert.Equal(new[] { "bb" }, Ids(Resolve(installed, state, ModMode.Classic).Active));
    }

    [Fact]
    public void Resolve_LanguageModsGoToLanguages_NotActive() {
        InstalledMod[] installed = { Mod("aa"), Mod("de-lang", extra: ", \"kinds\": [\"language\"]"), Mod("fr-lang", extra: ", \"kinds\": [\"language\"]") };
        ModResolution r = Resolve(installed, new[] { On("fr-lang"), Off("de-lang"), On("aa") });
        Assert.Equal(new[] { "aa" }, Ids(r.Active));
        Assert.Equal(new[] { "fr-lang", "de-lang" }, Ids(r.Languages));
    }

    [Fact]
    public void Resolve_MissingDependency_Excluded() {
        ModResolution r = Resolve(new[] { Mod("bb", extra: ", \"dependencies\": { \"aa\": \"^1.0.0\" }") }, new[] { On("bb") });
        Assert.Empty(r.Active);
        Assert.Equal(new ModProblem("bb", ModProblemKind.MissingDependency, "needs 'aa', which is not active", "aa"), Assert.Single(r.Problems));
    }

    [Fact]
    public void Resolve_DependencyTooOld_Excluded() {
        InstalledMod[] installed = { Mod("aa", "1.1.0"), Mod("bb", extra: ", \"dependencies\": { \"aa\": \"^1.2.0\" }") };
        ModResolution r = Resolve(installed, new[] { On("aa"), On("bb") });
        Assert.Equal(new[] { "aa" }, Ids(r.Active));
        ModProblem p = Assert.Single(r.Problems);
        Assert.Equal("bb", p.ModId);
        Assert.Equal(ModProblemKind.DependencyVersion, p.Kind);
    }

    [Fact]
    public void Resolve_ExclusionCascadesThroughDependents() {
        InstalledMod[] installed = {
            Mod("aa"),
            Mod("bb", extra: ", \"dependencies\": { \"aa\": \">=1.0.0\" }"),
            Mod("cc", extra: ", \"dependencies\": { \"bb\": \">=1.0.0\" }"),
        };
        ModResolution r = Resolve(installed, new[] { On("cc"), On("bb"), Off("aa") });
        Assert.Empty(r.Active);
        Assert.Equal(2, r.Problems.Count);
        Assert.Equal(new[] { "bb", "cc" }, r.Problems.Select(p => p.ModId).OrderBy(x => x).ToArray());
        Assert.All(r.Problems, p => Assert.Equal(ModProblemKind.MissingDependency, p.Kind));
    }

    [Fact]
    public void Resolve_DependencyEnhancedOnly_DependentExcludedInClassic() {
        InstalledMod[] installed = {
            Mod("aa"),
            Mod("bb", extra: ", \"dependencies\": { \"aa\": \">=1.0.0\" }, \"modes\": [\"enhanced\", \"classic\"]"),
        };
        ModStateEntry[] state = { On("aa"), On("bb") };
        Assert.Equal(new[] { "aa", "bb" }, Ids(Resolve(installed, state).Active));
        ModResolution r = Resolve(installed, state, ModMode.Classic);
        Assert.Empty(r.Active);
        Assert.Equal(ModProblemKind.MissingDependency, Assert.Single(r.Problems).Kind);
    }

    [Fact]
    public void Resolve_Conflict_LowerPriorityExcluded() {
        // Declared by the higher-priority side, then by the lower-priority side.
        InstalledMod[] installed = { Mod("aa", extra: ", \"conflicts\": [\"bb\"]"), Mod("bb") };
        ModResolution r = Resolve(installed, new[] { On("aa"), On("bb") });
        Assert.Equal(new[] { "aa" }, Ids(r.Active));
        ModProblem p = Assert.Single(r.Problems);
        Assert.Equal(("bb", ModProblemKind.Conflict), (p.ModId, p.Kind));

        r = Resolve(installed, new[] { On("bb"), On("aa") });
        Assert.Equal(new[] { "bb" }, Ids(r.Active));
        Assert.Equal("aa", Assert.Single(r.Problems).ModId);
    }

    [Fact]
    public void Resolve_GameVersionOutOfRange_Excluded() {
        InstalledMod[] installed = { Mod("aa", extra: ", \"game\": \">=0.3.0\"") };
        ModResolution r = Resolve(installed, new[] { On("aa") }, game: new SemVersion(0, 2, 0, null));
        Assert.Empty(r.Active);
        Assert.Equal(ModProblemKind.GameVersion, Assert.Single(r.Problems).Kind);
        Assert.Single(Resolve(installed, new[] { On("aa") }, game: new SemVersion(0, 3, 0, null)).Active);
    }

    [Fact]
    public void Resolve_NullGameVersion_SkipsGameCheck() {
        ModResolution r = Resolve(new[] { Mod("aa", extra: ", \"game\": \">=9.0.0\"") }, new[] { On("aa") });
        Assert.Single(r.Active);
        Assert.Empty(r.Problems);
    }

    [Fact]
    public void StateFile_RoundTrips() {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "mods.json");
        try {
            ModStateEntry[] entries = { new("hd-trees", "1.0.0", true), new("other-mod", "2.1.0", false) };
            ModStateFile.Write(path, entries);
            Assert.Contains("\"enabled\": true", File.ReadAllText(path));
            Assert.Equal(entries, ModStateFile.Read(path));
        } finally {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void StateFile_Missing_Empty() {
        Assert.Empty(ModStateFile.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "mods.json")));
    }
}

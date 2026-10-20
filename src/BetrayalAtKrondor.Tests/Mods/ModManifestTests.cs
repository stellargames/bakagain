namespace BetrayalAtKrondor.Tests.Mods;

using System.Collections.Generic;
using System.Linq;
using GameData.Mods;
using Xunit;

public class ModManifestTests {
    private const string SpecExample = """
        {
          "schema": 1,
          "id": "hd-trees",
          "name": "HD Trees",
          "version": "1.0.0",
          "author": "JvE",
          "description": "Photographic trees that follow the sun.",
          "license": "CC-BY-4.0",
          "game": ">=0.3.0",
          "dependencies": { "some-mod": "^1.0.0" },
          "conflicts": [ "other-tree-mod" ],
          "kinds": [ "sprites" ],
          "modes": [ "enhanced" ]
        }
        """;

    private static ModManifest? Parse(string json, out IReadOnlyList<string> errors) => ModManifest.Parse(json, out errors);

    [Fact]
    public void Parse_SpecExample_AllFields() {
        ModManifest? m = Parse(SpecExample, out IReadOnlyList<string> errors);
        Assert.Empty(errors);
        Assert.NotNull(m);
        Assert.Equal(1, m.Schema);
        Assert.Equal("hd-trees", m.Id);
        Assert.Equal("HD Trees", m.Name);
        Assert.Equal(new SemVersion(1, 0, 0, null), m.Version);
        Assert.Equal("JvE", m.Author);
        Assert.Equal("Photographic trees that follow the sun.", m.Description);
        Assert.Equal("CC-BY-4.0", m.License);
        Assert.NotNull(m.Game);
        Assert.True(m.Game.Satisfies(new SemVersion(0, 3, 0, null)));
        Assert.False(m.Game.Satisfies(new SemVersion(0, 2, 9, null)));
        VersionRange dep = Assert.Single(m.Dependencies).Value;
        Assert.Equal("some-mod", Assert.Single(m.Dependencies).Key);
        Assert.Equal("^1.0.0", dep.ToString());
        Assert.Equal(new[] { "other-tree-mod" }, m.Conflicts);
        Assert.Equal(new[] { "sprites" }, m.Kinds);
        Assert.Equal(new[] { ModMode.Enhanced }, m.Modes);
        Assert.False(m.IsLanguage);
    }

    [Fact]
    public void Parse_ModesAbsent_DefaultsToEnhanced() {
        ModManifest? m = Parse("""{"schema":1,"id":"ab","name":"N","version":"1.0.0"}""", out IReadOnlyList<string> errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { ModMode.Enhanced }, m!.Modes);
    }

    [Fact]
    public void Parse_ModesBoth_AndLanguageKind() {
        ModManifest? m = Parse("""{"schema":1,"id":"ab","name":"N","version":"1.0.0","kinds":["language"],"modes":["enhanced","classic"]}""", out _);
        Assert.Equal(new[] { ModMode.Enhanced, ModMode.Classic }, m!.Modes);
        Assert.True(m.IsLanguage);
    }

    [Fact]
    public void Parse_UnknownFields_Ignored() {
        string json = SpecExample.Replace("\"schema\": 1,", "\"schema\": 1, \"future\": 1,");
        ModManifest? m = Parse(json, out IReadOnlyList<string> errors);
        Assert.Empty(errors);
        Assert.NotNull(m);
    }

    [Theory]
    [InlineData("HD_Trees")]
    [InlineData("hd-trees\\n")]
    public void Parse_BadId_Error(string id) {
        ModManifest? m = Parse(SpecExample.Replace("hd-trees", id), out IReadOnlyList<string> errors);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("id"));
    }

    [Fact]
    public void Parse_SchemaTooNew_Error() {
        ModManifest? m = Parse(SpecExample.Replace("\"schema\": 1", "\"schema\": 2"), out IReadOnlyList<string> errors);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("newer"));
    }

    [Fact]
    public void Parse_UnknownKind_Warning() {
        ModManifest? m = Parse(SpecExample.Replace("\"sprites\"", "\"smells\""), out IReadOnlyList<string> errors);
        Assert.Empty(errors);
        Assert.NotNull(m);
        Assert.Contains(m.Warnings, w => w.Contains("smells"));
    }

    [Fact]
    public void Parse_UnknownMode_Error() {
        ModManifest? m = Parse(SpecExample.Replace("\"enhanced\"", "\"turbo\""), out IReadOnlyList<string> errors);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("turbo"));
    }

    [Theory]
    [InlineData("\"~1.0\"")]
    [InlineData("\"^1\"")]
    [InlineData("null")]
    public void Parse_BadDependencyRange_Error(string badValue) {
        ModManifest? m = Parse(SpecExample.Replace("\"^1.0.0\"", badValue), out IReadOnlyList<string> errors);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains("some-mod"));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("version")]
    public void Parse_MissingRequired_Error(string field) {
        string json = SpecExample.Replace($"\"{field}\":", "\"renamed\":");
        ModManifest? m = Parse(json, out IReadOnlyList<string> errors);
        Assert.Null(m);
        Assert.Contains(errors, e => e.Contains(field));
    }

    [Fact]
    public void Parse_CollectsEveryError() {
        ModManifest? m = Parse("""{"schema":1,"id":"BAD","name":"N","version":"x"}""", out IReadOnlyList<string> errors);
        Assert.Null(m);
        Assert.True(errors.Count >= 2);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Parse_NotAnObject_Error(string json) {
        Assert.Null(Parse(json, out IReadOnlyList<string> errors));
        Assert.NotEmpty(errors);
    }
}

namespace ResourceExtraction.Tests.Text;

using ResourceExtraction.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Every string in the shipped data has exactly one key (TASK-772). Skips without the game data.
/// </summary>
public class TextInventoryShippedTests {
    private readonly ITestOutputHelper _out;
    public TextInventoryShippedTests(ITestOutputHelper output) => _out = output;

    private static string? GameDir() {
        string? dir = System.AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir)) {
            if (File.Exists(Path.Combine(dir, "OriginalGame", "KRONDOR.001"))) {
                return Path.Combine(dir, "OriginalGame");
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [Fact]
    public void EveryShippedStringHasOneUniqueKey() {
        string? game = GameDir();
        if (game == null) {
            return;
        }
        List<TextEntry> all = TextInventory.Enumerate(ResourceProviderFactory.CreateResourceProvider(game)).ToList();
        foreach (IGrouping<string, TextEntry> source in all.GroupBy(e => e.Source.Split('_')[0]).OrderBy(g => g.Key)) {
            _out.WriteLine($"{source.Key,-12} {source.Count(),5}");
        }
        _out.WriteLine($"total        {all.Count,5}");

        List<string> duplicates = all.GroupBy(e => e.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "duplicate keys: " + string.Join(", ", duplicates.Take(10)));
        Assert.All(all, e => Assert.True(GameData.Resources.Content.ContentKey.IsValid(e.Key), e.Key));
    }

    [Theory]
    [InlineData("DIAL_Z")]
    [InlineData(".BOK")]
    [InlineData("REQ_")]
    [InlineData("IN_")]
    [InlineData("LBL_")]
    [InlineData("KEYWORD.DAT")]
    [InlineData("FMAP_TWN.DAT")]
    [InlineData("CRED.DAT")]
    [InlineData("OBJINFO.DAT")]
    [InlineData("SPELLS.DAT")]
    [InlineData("SPELLDOC.DAT")]
    [InlineData("MNAMES.DAT")]
    public void EachTextDomainIsReached(string source) {
        string? game = GameDir();
        if (game == null) {
            return;
        }
        Assert.Contains(TextInventory.Enumerate(ResourceProviderFactory.CreateResourceProvider(game)),
            e => e.Source.Contains(source, System.StringComparison.OrdinalIgnoreCase));
    }
}

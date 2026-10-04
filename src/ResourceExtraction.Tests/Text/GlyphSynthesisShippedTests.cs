namespace ResourceExtraction.Tests.Text;

using GameData.Resources.Font;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// The player's own GAME and BOOK fonts get the letters of the common European languages composed
/// (TASK-778). Prints them, so a change in the marks can be looked at. Skips without the game data.
/// </summary>
public class GlyphSynthesisShippedTests {
    // Dutch, German, French, Spanish, Portuguese, Italian, Scandinavian, Polish, Czech — less ß.
    private const string European = "äëïöüÄËÏÖÜéèêáàâíìîóòôúùûçñãõÉÈÊÁÀÂÍÓÚÇÑåÅæÆøØœŒąćęłńśźżĄĆĘŁŃŚŹŻčďěňřšťůžČĎĚŇŘŠŤŮŽ’“”–…";

    private readonly ITestOutputHelper _out;
    public GlyphSynthesisShippedTests(ITestOutputHelper output) => _out = output;

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

    [Theory]
    [InlineData("GAME.FNT")]
    [InlineData("BOOK.FNT")]
    public void TheEuropeanLettersAreAllMade(string fontId) {
        string? game = GameDir();
        if (game == null) {
            return;
        }
        FontResource font = ResourceProviderFactory.CreateResourceProvider(game).GetResource<FontResource>(fontId);

        IReadOnlyList<int> missing = GlyphSynthesis.AddComposed(font, European.Select(c => (int)c));

        foreach (string line in Dump(font, "aäAÄiïíeéêoøæOØÆcçnñlłsšzżuůEĘ…")) {
            _out.WriteLine(line);
        }
        Assert.Empty(missing);
    }

    private static IEnumerable<string> Dump(FontResource font, string text) {
        for (int y = 0; y < font.Height; y++) {
            var line = new StringBuilder();
            foreach (char c in text) {
                FontGlyph g = font.GlyphFor(c)!;
                for (int x = 0; x < g.Width; x++) {
                    line.Append(g.IsSet(x, y) ? '#' : '.');
                }
                line.Append(' ');
            }
            yield return line.ToString();
        }
    }
}

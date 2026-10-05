namespace BetrayalAtKrondor.Tests.Palette;

using System.Collections.Generic;
using GameData.Resources.Palette;
using Xunit;

/// <summary>
/// A spell's remap tint is taken from its RMP table (COMBAT.C:174-189), not a hard-coded colour:
/// the pens the table maps the sprite onto, averaged through the active palette (TASK-805).
/// </summary>
public class SpellRemapTests {
    [Theory]
    [InlineData(1, "RED.RMP")]
    [InlineData(2, "GREEN.RMP")]
    [InlineData(3, "WHITE.RMP")]
    [InlineData(4, "BLUE.RMP")]
    public void EachRemapNamesItsTable(int remap, string file) {
        Assert.Equal(file, SpellRemap.FileFor(remap));
    }

    [Fact]
    public void TheShadeIsThePensTheTableMapsOnto() {
        var table = new RemapResource("RED.RMP") {
            Mappings = new Dictionary<int, Dictionary<byte, byte>> {
                [0] = new() { [112] = 160, [113] = 161, [114] = 160 },
            },
        };

        Assert.Equal(new[] { 160, 161, 160 }, SpellRemap.TargetPens(table));
    }
}

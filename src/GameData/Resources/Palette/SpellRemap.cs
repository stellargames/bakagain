namespace GameData.Resources.Palette;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The four remap tables a spell's glow and struck-target flash go through —
/// <c>combat_arena_load_remap_pals</c> (COMBAT.C:174-189): 1 red, 2 green, 3 white, 4 blue.
/// </summary>
/// <remarks>
/// Each table maps the sprite's pens 112-255 onto a ramp of shades, so the original recolours the
/// sprite while keeping its shading. The port draws a translucent tint instead; its colour is these
/// target pens averaged through the zone palette, which keeps the original's hue — the hard-coded
/// green and blue were far more saturated than the tables (TASK-805).
/// </remarks>
public static class SpellRemap {
    public static string FileFor(int remap) => remap switch {
        1 => "RED.RMP",
        2 => "GREEN.RMP",
        4 => "BLUE.RMP",
        _ => "WHITE.RMP",
    };

    /// <summary>The pens the table maps onto, one per source pen.</summary>
    public static IReadOnlyList<int> TargetPens(RemapResource table) =>
        table?.Mappings != null && table.Mappings.TryGetValue(0, out Dictionary<byte, byte> map)
            ? map.Values.Select(v => (int)v).ToList()
            : new List<int>();
}

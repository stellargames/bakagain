namespace GameData.Resources.Inventory;

using System.Collections.Generic;

/// <summary>
/// The small enchantment icons an item cell carries at its top-left — <c>invui_status_icons_render</c>
/// (INVENTOR.C:315-327).
/// </summary>
/// <remarks>
/// Bits 7..15 of the slot's flags (poisoned, flaming, steel-fired, frosted, the enhancements and the
/// blessings), in bit order, each showing <c>INVSHP2.BMX</c> sprite <c>bit + 5</c>, laid left to
/// right. The grid draws them on every cell (INVENTOR.C:443-444) and the inspect panel on its icon
/// (INVINSP.C:190).
/// </remarks>
public static class ItemStatusIcons {
    private const int FirstBit = 7;
    private const int LastBit = 15;
    private const int SpriteOffset = 5;

    /// <summary>The INVSHP2 sprite numbers to draw, left to right.</summary>
    public static IReadOnlyList<int> SpriteIndices(ushort slotFlags) {
        var indices = new List<int>();
        for (int bit = FirstBit; bit <= LastBit; bit++) {
            if ((slotFlags & (1 << bit)) != 0) {
                indices.Add(bit + SpriteOffset);
            }
        }
        return indices;
    }
}

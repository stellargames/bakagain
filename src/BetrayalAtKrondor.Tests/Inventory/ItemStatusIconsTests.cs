namespace BetrayalAtKrondor.Tests.Inventory;

using GameData;
using GameData.Resources.Inventory;
using Xunit;

public class ItemStatusIconsTests {
    [Fact]
    public void EachEnchantmentBitFromSevenUpShowsSpriteBitPlusFive() {
        // invui_status_icons_render (INVENTOR.C:315-327): bits 7..15 of the slot's flags, in bit
        // order, each blitting g_pInvSpriteHiAssetTable[bit + 5] (INVSHP2). James's blessed sword
        // in a chapter-3 save carries 0x8000 and shows #20, a gold cross, in the original.
        Assert.Equal(new[] { 20 }, ItemStatusIcons.SpriteIndices((ushort)ItemFlags.Blessed3));
        Assert.Equal(new[] { 12, 13 },
            ItemStatusIcons.SpriteIndices((ushort)(ItemFlags.Poisoned | ItemFlags.Flaming)));
    }

    [Fact]
    public void TheLowSevenBitsShowNothing() {
        // `status_bits &= 0xff80` first: equipped, broken, repairable and the rest are not icons.
        Assert.Empty(ItemStatusIcons.SpriteIndices(
            (ushort)(ItemFlags.Equipped | ItemFlags.Broken | ItemFlags.Repairable | ItemFlags.Lit)));
    }
}

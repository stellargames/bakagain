namespace BetrayalAtKrondor.Tests.Inventory;

using GameData;
using GameData.Resources.Character;
using GameData.Resources.Data;
using GameData.Resources.Inventory;
using GameData.Resources.Object;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Looking through the Brass Spyglass: <c>ITEMUSE.C</c>'s <c>case 7</c> (ITEMUSE.C:398-405).
/// </summary>
/// <remarks>
/// Outside a location scene and outside combat: record 0x1B7742 (the item-7 text, "...able to see
/// objects that were hidden from his view..."), then the look-down view
/// (<c>itemuse_view_look_south_modal</c>), and the use returns -1, so the tail adds nothing.
/// Otherwise record 0x1B7770, "neither the time nor the place".
/// </remarks>
public class BrassSpyglassTests {
    private const byte SpyglassId = 7;
    private const int UsedRecord = 1800002;      // 0x1B7742
    private const int NotNowRecord = 1800048;    // 0x1B7770

    // The shipped flags: "Protected, NotUsableInCombat".
    private static ObjectInfoSet Objects() => new ObjectInfoSet("O", new List<ObjectInfo> {
        new ObjectInfo("O") {
            Number = SpyglassId, Name = "Brass Spyglass", ObjectType = ObjectType.Usable,
            InventorySlots = 1, MaxAmount = 1,
            Flags = ObjectFlags.Protected | ObjectFlags.NotUsableInCombat,
        },
    });

    private static ItemUseResult Look(bool inCombat = false, bool inScene = false) {
        var pack = new RuntimeContainer();
        pack.Items.Add(new RuntimeItem(SpyglassId, 0, 0));
        var ctx = new ItemUseContext(new ActorStat[16], 1, _ => 0, (_, __) => { }, _ => 0) {
            InCombat = inCombat, InLocationScene = inScene,
        };
        ItemUseResult r = InventoryUse.Use(pack, 0, InventoryUse.NoTarget, Objects(), ctx);
        Assert.Single(pack.Items);   // never spent
        return r;
    }

    [Fact]
    public void InTheWorldItSaysWhatHeSeesAndOpensTheView() {
        ItemUseResult r = Look();
        Assert.Equal(UsedRecord, r.DialogId);
        Assert.Equal(SpyglassId, r.DialogVar0);
        Assert.True(r.OpensSpyglassView);
        Assert.Equal(ItemUseOutcome.Handled, r.Outcome);   // returns -1: no tail record
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InAFightOrALocationItIsPutAway(bool inCombat, bool inScene) {
        ItemUseResult r = Look(inCombat, inScene);
        Assert.Equal(NotNowRecord, r.DialogId);
        Assert.False(r.OpensSpyglassView);
    }
}

namespace BetrayalAtKrondor.Tests.Inventory;

using GameData;
using GameData.Resources.Character;
using GameData.Resources.Data;
using GameData.Resources.Inventory;
using GameData.Resources.Object;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Opening the Wooden Chest: <c>ITEMUSE.C</c>'s <c>case 0x66</c> (ITEMUSE.C:389-397).
/// </summary>
/// <remarks>
/// Outside a scene, outside combat and above ground (<c>g_game_mode != 2</c>): outcome 1, so the tail
/// plays 0x1B7742 ("...shot into the air like a crossbow bolt into the blue...") and spends a charge,
/// and the result 0x66 makes the inventory screen raise the camera when it closes
/// (CMBINV.C:463-466). Otherwise 0x1B7770 and nothing else.
/// </remarks>
public class WoodenChestTests {
    private const byte ChestId = 102;
    private const int UsedRecord = 1800002;      // 0x1B7742
    private const int NotNowRecord = 1800048;    // 0x1B7770
    private const int Underground = 2;           // Z##DEF's first word, g_game_mode

    // The shipped flags: "NotUsableInCombat, LimitedUses".
    private static ObjectInfoSet Objects() => new ObjectInfoSet("O", new List<ObjectInfo> {
        new ObjectInfo("O") {
            Number = ChestId, Name = "Wooden Chest", ObjectType = ObjectType.Usable,
            InventorySlots = 1, MaxAmount = 1,
            Flags = ObjectFlags.NotUsableInCombat | ObjectFlags.LimitedUses,
        },
    });

    private static (ItemUseResult, RuntimeContainer) Open(byte charges = 3, bool inCombat = false,
        bool inScene = false, int zoneKind = 0) {
        var pack = new RuntimeContainer();
        pack.Items.Add(new RuntimeItem(ChestId, charges, 0));
        var ctx = new ItemUseContext(new ActorStat[16], 1, _ => 0, (_, __) => { }, _ => 0) {
            InCombat = inCombat, InLocationScene = inScene, ZoneKind = zoneKind,
        };
        return (InventoryUse.Use(pack, 0, InventoryUse.NoTarget, Objects(), ctx), pack);
    }

    [Fact]
    public void AboveGroundItLaunchesTheUserAndSpendsACharge() {
        (ItemUseResult r, RuntimeContainer pack) = Open(charges: 3);
        Assert.Equal(UsedRecord, r.DialogId);
        Assert.Equal(ChestId, r.DialogVar0);
        Assert.True(r.RaisesCameraOnClose);
        Assert.Equal(2, pack.Items[0].Variable);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(false, false, Underground)]
    public void InAFightAScenOrUndergroundItStaysShut(bool inCombat, bool inScene, int zoneKind) {
        (ItemUseResult r, RuntimeContainer pack) = Open(inCombat: inCombat, inScene: inScene, zoneKind: zoneKind);
        Assert.Equal(NotNowRecord, r.DialogId);
        Assert.False(r.RaisesCameraOnClose);
        Assert.Equal(3, pack.Items[0].Variable);
    }
}

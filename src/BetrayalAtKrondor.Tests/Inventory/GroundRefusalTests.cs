namespace BetrayalAtKrondor.Tests.Inventory;

using GameData;
using GameData.Resources.Data;
using GameData.Resources.Inventory;
using Xunit;

/// <summary>
/// What a discard refuses BEFORE it looks for a ground bag: dropItemToGround@0x55eb8 makes these
/// refusals first (CMBINV.C:1052-1063). The port claimed (or recycled and emptied) a bag first, so
/// a refused drop could wipe another pile (TASK-1129 row 1).
/// </summary>
public class GroundRefusalTests {
    private const ushort Equipped = (ushort)ItemFlags.Equipped;

    [Fact]
    public void AWornSwordIsRefused() =>
        Assert.Equal(InventoryTransfer.Result.MustKeepEquipped, InventoryTransfer.GroundRefusal(
            new RuntimeItem(18, 100, Equipped), ObjectType.Sword, SaveGameContainerType.Inventory));

    [Fact]
    public void ALitTorchIsRefused() =>
        Assert.Equal(InventoryTransfer.Result.LitTorchRefused, InventoryTransfer.GroundRefusal(
            new RuntimeItem(0x54, 3, 0x0001), ObjectType.Misc, SaveGameContainerType.Inventory));

    [Fact]
    public void AnActiveRingOfPrandurIsRefused() =>
        Assert.Equal(InventoryTransfer.Result.MustKeepEquipped, InventoryTransfer.GroundRefusal(
            new RuntimeItem(6, 1, 0x0001), ObjectType.Misc, SaveGameContainerType.Inventory));

    [Fact]
    public void AnOrdinaryItemIsNot() =>
        Assert.Null(InventoryTransfer.GroundRefusal(
            new RuntimeItem(18, 100, 0), ObjectType.Sword, SaveGameContainerType.Inventory));
}

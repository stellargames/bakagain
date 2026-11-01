namespace BetrayalAtKrondor.Tests.Inventory;
using GameData.Resources.Data;
using GameData.Resources.Inventory;
using GameData.Resources.Object;
using GameData;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// The Enhanced loot screen's "Get all" (TASK-1019).
public class TakeAllTests {
    private const byte Rations = 72, Trinket = 100, Key = 61;

    private static ObjectInfoSet Objs() {
        var list = new List<ObjectInfo> {
            new ObjectInfo("r") { Number = Rations, Name = "Rations", InventorySlots = 1, MaxAmount = 30, Flags = (ObjectFlags)0x800 },
            new ObjectInfo("g") { Number = 53, Name = "Gold Sovereigns", InventorySlots = 1, MaxAmount = 1000 },
            new ObjectInfo("k") { Number = Key, Name = "Peasant's Key", InventorySlots = 1, ObjectType = ObjectType.Key },
        };
        for (int i = 0; i < 25; i++) {
            list.Add(new ObjectInfo("t" + i) { Number = Trinket + i, Name = "Trinket" + i, InventorySlots = 1, MaxAmount = 1 });
        }
        return new ObjectInfoSet("O", list);
    }

    private static RuntimeContainer C(SaveGameContainerType type, params RuntimeItem[] items) {
        var c = new RuntimeContainer { Capacity = 24, ContainerType = type };
        c.Items.AddRange(items); return c;
    }

    private static RuntimeContainer Pack(int trinkets) =>
        C(SaveGameContainerType.Inventory, Enumerable.Range(0, trinkets)
            .Select(i => new RuntimeItem((byte)(Trinket + i), 1, 0)).ToArray());

    [Fact] public void AStackIsSharedAndEachOtherItemGoesToTheEmptiestPack() {
        var chest = C(SaveGameContainerType.Chest,
            new RuntimeItem(Rations, 9, 0), new RuntimeItem(Trinket + 20, 1, 0), new RuntimeItem(Trinket + 21, 1, 0));
        RuntimeContainer a = Pack(5), b = Pack(2), c = Pack(1);
        int gold = 0;
        int left = InventoryTransfer.TakeAll(chest, new[] { a, b, c }, Objs(), ref gold);
        Assert.Equal(0, left);
        Assert.Empty(chest.Items);
        // Share's progressive portions, remaining x assigned / count: 9/3 = 3, 6x2/3 = 4, then the 2 left.
        Assert.Equal(new[] { 3, 4, 2 }, new[] { a, b, c }.Select(m => (int)m.Items.Single(i => i.ObjectId == Rations).Variable));
        // Walked from the last item down: 121 to c (most room); then b and c both have 18 free and
        // the tie goes in roster order, so 120 to b.
        Assert.Contains(c.Items, i => i.ObjectId == Trinket + 21);
        Assert.Contains(b.Items, i => i.ObjectId == Trinket + 20);
        Assert.DoesNotContain(a.Items, i => i.ObjectId >= Trinket + 20);
    }

    [Fact] public void GoldBecomesPurseMoneyAndAKeyGoesOnTheRing() {
        var chest = C(SaveGameContainerType.Chest, new RuntimeItem(53, 7, 0), new RuntimeItem(Key, 0, 0));
        var keys = C(SaveGameContainerType.SharedKeys);
        RuntimeContainer a = Pack(0);
        int gold = 5;
        int left = InventoryTransfer.TakeAll(chest, new[] { a }, Objs(), ref gold, sharedKeys: keys);
        Assert.Equal(0, left);
        Assert.Equal(75, gold);
        Assert.Single(keys.Items);
        Assert.Empty(a.Items);
    }

    [Fact] public void WhatNobodyCanCarryStaysInTheContainer() {
        var chest = C(SaveGameContainerType.Chest, new RuntimeItem(Trinket + 22, 1, 0), new RuntimeItem(53, 1, 0));
        RuntimeContainer a = Pack(20), b = Pack(20);
        int gold = 0;
        int left = InventoryTransfer.TakeAll(chest, new[] { a, b }, Objs(), ref gold);
        Assert.Equal(1, left);
        Assert.Equal(Trinket + 22, chest.Items.Single().ObjectId);
        Assert.Equal(10, gold); // the gold still came out
    }
}

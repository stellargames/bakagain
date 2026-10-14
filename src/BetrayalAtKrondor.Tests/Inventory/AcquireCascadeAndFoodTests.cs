namespace BetrayalAtKrondor.Tests.Inventory;

using GameData;
using GameData.Resources.Character;
using GameData.Resources.Data;
using GameData.Resources.Inventory;
using GameData.Resources.Object;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// The two arms of <c>cmbinv_actor_acquire_item</c> (CMBINV.C:1002) either side of the space test:
/// a full pack cascades to the rest of the party and then to the ground, and food handed to a
/// starving receiver is eaten on the spot.
/// </summary>
public class AcquireCascadeAndFoodTests {
    /// <summary>Object 72 — named rather than typed, because it is what the engine feeds you.</summary>
    private const int Rations = UpkeepEngine.RationsObjectId;

    private const int Broadsword = 18;

    private static ObjectInfoSet Objects() => new ObjectInfoSet("O", new List<ObjectInfo> {
        new ObjectInfo("O") {
            Number = Rations, Name = "Rations",
            ObjectType = ObjectType.Food, InventorySlots = 1, MaxAmount = 99,
        },
        new ObjectInfo("O") {
            Number = Broadsword, Name = "Broadsword",
            ObjectType = ObjectType.Sword, InventorySlots = 3, MaxAmount = 1,
        },
    });

    private static RuntimeContainer Pack() =>
        new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.Inventory };

    private static RuntimeContainer Ground() =>
        new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.Bag };

    /// <summary>A pack with its last slot spoken for, so nothing more will go in.</summary>
    private static RuntimeContainer FullPack() {
        var p = new RuntimeContainer { Capacity = 1, ContainerType = SaveGameContainerType.Inventory };
        p.Items.Add(new RuntimeItem(Broadsword, 1, 0));
        return p;
    }

    private static (RuntimeContainer, ActorConditions?)[] Cascade(
        params (RuntimeContainer, ActorConditions?)[] entries) => entries;

    private static ActorConditions Starving() {
        var c = new ActorConditions();
        c[ActorCondition.Starving] = 40;
        return c;
    }

    [Fact]
    public void AFullPackHandsTheGiftToTheNextMember() {
        RuntimeContainer named = FullPack(), other = Pack(), ground = Ground();

        Assert.True(InventoryAcquire.TryGive(named, new RuntimeItem(Broadsword, 1, 0), Objects(),
            sharedKeys: null, othersThenGround: Cascade((other, null), (ground, null))));

        Assert.Single(named.Items);   // still just the one it started with
        Assert.Single(other.Items);
        Assert.Empty(ground.Items);
    }

    [Fact]
    public void WhenEveryMemberIsFullItFallsToTheGround() {
        // The ground pile is LAST in the list for exactly this reason: it is the original's final
        // resort, after the loop over the active party.
        RuntimeContainer named = FullPack(), other = FullPack(), ground = Ground();

        Assert.True(InventoryAcquire.TryGive(named, new RuntimeItem(Broadsword, 1, 0), Objects(),
            sharedKeys: null, othersThenGround: Cascade((other, null), (ground, null))));

        Assert.Equal(Broadsword, Assert.Single(ground.Items).ObjectId);
    }

    [Fact]
    public void OnlyWhenNOTHINGHasRoomIsItRefused() {
        // False is what stops the caller charging for it, so it has to mean "nobody took it",
        // not "the first one I asked was full".
        RuntimeContainer named = FullPack(), other = FullPack();
        RuntimeContainer ground = FullPack();

        Assert.False(InventoryAcquire.TryGive(named, new RuntimeItem(Broadsword, 1, 0), Objects(),
            sharedKeys: null, othersThenGround: Cascade((other, null), (ground, null))));
    }

    [Fact]
    public void WithNoPartyToCascadeToTheOldBehaviourStands() {
        RuntimeContainer named = FullPack();

        Assert.False(InventoryAcquire.TryGive(named, new RuntimeItem(Broadsword, 1, 0), Objects()));
        Assert.Single(named.Items);
    }

    [Fact]
    public void FoodHandedToAStarvingMemberIsEatenOnTheSpot() {
        RuntimeContainer pack = Pack();
        ActorConditions conditions = Starving();

        Assert.True(InventoryAcquire.TryGive(pack, new RuntimeItem(Rations, 3, 0), Objects(),
            sharedKeys: null, receiverConditions: conditions));

        // One of the three is gone, and the hunger with it: acquiring food is the only recovery
        // from Starving the field offers.
        Assert.Equal(2, Assert.Single(pack.Items).Variable);
        Assert.False(conditions.Has(ActorCondition.Starving));
    }

    [Fact]
    public void FoodHandedToSomebodyWhoIsNotStarvingIsJustStored() {
        RuntimeContainer pack = Pack();
        var conditions = new ActorConditions();

        Assert.True(InventoryAcquire.TryGive(pack, new RuntimeItem(Rations, 3, 0), Objects(),
            sharedKeys: null, receiverConditions: conditions));

        Assert.Equal(3, Assert.Single(pack.Items).Variable);
    }

    [Fact]
    public void ANonFoodGiftNeverFeedsAnybody() {
        // The original's first guard is the CATEGORY: a sword handed to a starving man is still
        // just a sword.
        RuntimeContainer pack = Pack();
        ActorConditions conditions = Starving();

        Assert.True(InventoryAcquire.TryGive(pack, new RuntimeItem(Broadsword, 1, 0), Objects(),
            sharedKeys: null, receiverConditions: conditions));

        Assert.True(conditions.Has(ActorCondition.Starving));
    }

    [Fact]
    public void FoodThatCascadedAwayDoesNotFeedTheReceiverItNeverReached() {
        // The original reads the status rank of the actor it was called FOR, and only feeds when
        // that actor is the one holding it.
        RuntimeContainer named = FullPack(), other = Pack();
        ActorConditions conditions = Starving();

        Assert.True(InventoryAcquire.TryGive(named, new RuntimeItem(Rations, 3, 0), Objects(),
            sharedKeys: null, receiverConditions: conditions,
            othersThenGround: Cascade((other, null))));

        Assert.Equal(3, Assert.Single(other.Items).Variable);
        Assert.True(conditions.Has(ActorCondition.Starving));
    }

    [Fact]
    public void THEMEMBERWHOENDSUPHOLDINGTHEFOODISTHEONEWHOEATSIT() {
        // CMBINV.C:1027 re-reads the slot inside the loop, so hunger follows the item, not the
        // addressee. Feeding the named receiver here would feed somebody holding nothing.
        RuntimeContainer named = FullPack(), other = Pack();
        ActorConditions namedIsStarving = Starving(), otherIsStarving = Starving();

        Assert.True(InventoryAcquire.TryGive(named, new RuntimeItem(Rations, 3, 0), Objects(),
            sharedKeys: null, receiverConditions: namedIsStarving,
            othersThenGround: Cascade((other, otherIsStarving))));

        Assert.Equal(2, Assert.Single(other.Items).Variable);
        Assert.False(otherIsStarving.Has(ActorCondition.Starving));
        Assert.True(namedIsStarving.Has(ActorCondition.Starving), "he never touched it");
    }

    [Fact]
    public void FoodThatFALLSTOTHEGROUNDFeedsNobody() {
        RuntimeContainer named = FullPack(), other = FullPack(), ground = Ground();
        ActorConditions conditions = Starving();

        Assert.True(InventoryAcquire.TryGive(named, new RuntimeItem(Rations, 3, 0), Objects(),
            sharedKeys: null, receiverConditions: conditions,
            othersThenGround: Cascade((other, null), (ground, null))));

        Assert.Equal(3, Assert.Single(ground.Items).Variable);
        Assert.True(conditions.Has(ActorCondition.Starving));
    }

    // --- Share with party (CMBINV.C:858-861): a starving recipient eats from what he got ---

    [Fact]
    public void ShareFeedsAStarvingRecipientOneMeal() {
        // Measured in the original from the same split: 13 over three -> 4, 6, 3, and the starving
        // third member ends on his own 5 + 3 - 1 = 7 with Starving cleared.
        RuntimeContainer src = Pack(), a = Pack(), b = Pack(), c = Pack();
        src.Items.Add(new RuntimeItem(Rations, 13, 0));
        c.Items.Add(new RuntimeItem(Rations, 5, 0));
        ActorConditions hungry = Starving();

        InventoryTransfer.Distribute(src, 0, new[] { a, b, c }, Objects(),
            r => ReferenceEquals(r, c) ? hungry : new ActorConditions());

        Assert.Equal(4, Assert.Single(a.Items).Variable);
        Assert.Equal(6, Assert.Single(b.Items).Variable);
        Assert.Equal(7, System.Linq.Enumerable.Sum(c.Items, i => i.Variable)); // this fixture's rations do not merge
        Assert.Equal(0, hungry[ActorCondition.Starving]);
    }

    [Fact]
    public void AStarvingMemberWhoseShareRoundsToNothingDoesNotEat() {
        // One ration over three: portions 0, 0, 1. The first member gets nothing, so nothing
        // calls the meal for him, and his own rations stay where they are.
        RuntimeContainer src = Pack(), a = Pack(), b = Pack(), c = Pack();
        src.Items.Add(new RuntimeItem(Rations, 1, 0));
        a.Items.Add(new RuntimeItem(Rations, 5, 0));
        ActorConditions hungry = Starving();

        InventoryTransfer.Distribute(src, 0, new[] { a, b, c }, Objects(),
            r => ReferenceEquals(r, a) ? hungry : null);

        Assert.Equal(5, Assert.Single(a.Items).Variable);
        Assert.Equal(40, hungry[ActorCondition.Starving]);
        Assert.Equal(1, Assert.Single(c.Items).Variable);
    }

    [Fact]
    public void UsingRationsChangesNothingAndSaysTheTailsNoEffectRecord() {
        // ITEMUSE.C has no category-23 branch: Use leaves the stack alone and plays 0x1B7743,
        // whose item switch has no case for rations. Measured in the original 2026-10-14: the
        // Use button on 10 rations left 10 and showed nothing.
        RuntimeContainer pack = Pack();
        pack.Items.Add(new RuntimeItem(Rations, 10, 0));

        ItemUseResult r = InventoryUse.Use(pack, 0, InventoryUse.NoTarget, Objects());

        Assert.Equal(ItemUseOutcome.NoEffect, r.Outcome);
        Assert.Equal(0x1B7743, r.DialogId);
        Assert.Equal(10, Assert.Single(pack.Items).Variable);
    }
}

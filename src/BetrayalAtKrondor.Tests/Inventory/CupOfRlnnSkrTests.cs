namespace BetrayalAtKrondor.Tests.Inventory;

using GameData;
using GameData.Resources.Character;
using GameData.Resources.Data;
using GameData.Resources.Inventory;
using GameData.Resources.Object;
using GameData.Resources.Spells;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Drinking from the Cup of Rlnn Skr: <c>ITEMUSE.C</c>'s <c>case 8</c>, the item-8 arm of the
/// category-25 switch (ITEMUSE.C:460-479).
/// </summary>
/// <remarks>
/// With Pug in the party, Pug learns one random spell, with a second roll if the first was already
/// known. Then Owyn's and Pug's books become their union. Whether the roll taught anything goes into
/// <c>lEvtArgGoldCost</c> (global 30014), and the scene record 0x1B7742 branches on it for item 8.
/// Without Pug nothing happens (outcome 0, record 0x1B7743).
/// </remarks>
public class CupOfRlnnSkrTests {
    private const byte CupId = 8;
    private const int UsedRecord = 1800002;      // 0x1B7742
    private const int NoEffectRecord = 1800003;  // 0x1B7743
    private const int EvtArgGoldCost = 30014;
    private const int Owyn = 2, Pug = 3;

    // The shipped flags: "SpellcastersOnly, NotUsableInCombat" — no charges, so it is never spent.
    private static ObjectInfoSet Objects() => new ObjectInfoSet("O", new List<ObjectInfo> {
        new ObjectInfo("O") {
            Number = CupId, Name = "Cup of Rlnn Skr", ObjectType = ObjectType.Usable,
            InventorySlots = 1, MaxAmount = 1,
            Flags = ObjectFlags.SpellcastersOnly | ObjectFlags.NotUsableInCombat,
        },
    });

    private static RuntimeContainer Pack() {
        var c = new RuntimeContainer();
        c.Items.Add(new RuntimeItem(CupId, 0, 0));
        return c;
    }

    private sealed class World {
        public readonly ushort[] OwynBook = SpellBook.Empty();
        public readonly ushort[] PugBook = SpellBook.Empty();
        public readonly Dictionary<int, int> Globals = new Dictionary<int, int>();
        public bool PugInParty = true;
        public Queue<int> Rolls = new Queue<int>();

        public ItemUseContext Context() =>
            new ItemUseContext(new ActorStat[16], 1, k => Globals.TryGetValue(k, out int v) ? v : 0,
                (k, v) => Globals[k] = v, n => Rolls.Count > 0 ? Rolls.Dequeue() : 0) {
                IsPartyMember = character => character == Pug ? PugInParty : true,
                SpellsOfCharacter = character => character == Owyn ? OwynBook
                    : character == Pug ? PugBook : null,
            };
    }

    private static ItemUseResult Drink(World w) =>
        InventoryUse.Use(Pack(), 0, InventoryUse.NoTarget, Objects(), w.Context());

    [Fact]
    public void PugLearnsTheRolledSpellAndTheSceneHearsIt() {
        var w = new World();
        w.Rolls.Enqueue(17);
        ItemUseResult r = Drink(w);
        Assert.True(SpellBook.IsKnown(w.PugBook, 17));
        Assert.Equal(1, w.Globals[EvtArgGoldCost]);
        Assert.Equal(UsedRecord, r.DialogId);
        Assert.Equal(CupId, r.DialogVar0);   // 0x1B7742 branches on the item id
        Assert.False(r.SourceRemoved, "no charges: the cup is kept");
    }

    [Fact]
    public void AKnownSpellGetsOneMoreRoll() {
        var w = new World();
        SpellBook.Learn(w.PugBook, 17);
        w.Rolls.Enqueue(17);
        w.Rolls.Enqueue(30);
        Drink(w);
        Assert.True(SpellBook.IsKnown(w.PugBook, 30));
        Assert.Equal(1, w.Globals[EvtArgGoldCost]);
    }

    [Fact]
    public void TwoKnownRollsTeachNothingAndTheSceneSaysSo() {
        var w = new World();
        SpellBook.Learn(w.PugBook, 17);
        w.Rolls.Enqueue(17);
        w.Rolls.Enqueue(17);
        ItemUseResult r = Drink(w);
        Assert.Equal(0, w.Globals[EvtArgGoldCost]);
        Assert.Equal(UsedRecord, r.DialogId);   // still the cup's scene, its other branch
    }

    [Fact]
    public void OwynAndPugEndWithTheUnionOfTheirBooks() {
        var w = new World();
        SpellBook.Learn(w.OwynBook, 3);
        SpellBook.Learn(w.PugBook, 40);
        w.Rolls.Enqueue(17);
        Drink(w);
        foreach (int spell in new[] { 3, 17, 40 }) {
            Assert.True(SpellBook.IsKnown(w.OwynBook, spell), $"Owyn knows {spell}");
            Assert.True(SpellBook.IsKnown(w.PugBook, spell), $"Pug knows {spell}");
        }
    }

    [Fact]
    public void WithoutPugNothingHappens() {
        var w = new World { PugInParty = false };
        w.Rolls.Enqueue(17);
        ItemUseResult r = Drink(w);
        Assert.Equal(NoEffectRecord, r.DialogId);
        Assert.False(SpellBook.IsKnown(w.PugBook, 17));
        Assert.False(w.Globals.ContainsKey(EvtArgGoldCost));
    }

    [Fact]
    public void TheRollCoversTheOriginalsRange() {
        var w = new World();
        var asked = new List<int>();
        var ctx = w.Context();
        var probe = new ItemUseContext(new ActorStat[16], 1, ctx.ReadFlag, ctx.WriteFlag,
            n => { asked.Add(n); return 0; }) {
            IsPartyMember = ctx.IsPartyMember, SpellsOfCharacter = ctx.SpellsOfCharacter,
        };
        InventoryUse.Use(Pack(), 0, InventoryUse.NoTarget, Objects(), probe);
        Assert.Equal(0x2d, asked[0]);   // RND(0x2d)
    }
}

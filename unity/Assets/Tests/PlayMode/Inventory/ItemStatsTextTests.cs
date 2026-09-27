namespace BakAgain.Tests.PlayMode.Inventory {
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Pins the "More Info" stat panel against <c>UI_showItemStats</c> @0x5A1DA. The panel is a
    /// cursor walk — it starts at <see cref="InventoryLayout.StatsOrigin"/> and each section
    /// advances it by one of that type's named distances — so the tables below are written in
    /// design-frame px, with the original's own coordinate beside each lookup so they stay
    /// checkable against the disassembly line by line.
    ///
    /// <para>The absolute pin is <see cref="Lines_CarryDesignFramePositions_NotTheOriginalsOwnUnits"/>,
    /// whose numbers are hand-derived rather than read from the layout, so a wrong default cannot
    /// hide behind the fixture. That the walk really reads the data — rather than having the same
    /// numbers baked in — is pinned separately by
    /// <see cref="Walk_ReadsItsGeometryFromTheLayout_NotFromConstants"/>.</para>
    /// </summary>
    public class ItemStatsTextTests {
        private static ObjectInfo Obj(ObjectType type, int swingDmg = 0, int thrustDmg = 0,
            int swingAcc = 0, int thrustAcc = 0, Race race = 0, int equipMask = 0) =>
            new ObjectInfo("test") {
                ObjectType = type, SwingBaseDamage = swingDmg, ThrustBaseDamage = thrustDmg,
                SwingAccuracy_ArmorMod_BowAccuracy = swingAcc, ThrustAccuracy = thrustAcc,
                Race = race, EquipAttributeMask = (ActorAttributeFlag)equipMask,
            };

        private static RuntimeItem Item(byte objectId = 1, ushort itemFlags = 0) =>
            new RuntimeItem(objectId, 100, itemFlags);

        // The faithful geometry. Passed explicitly so a test that wants to prove the walk is
        // data-driven can hand in a different one.
        private static readonly InventoryLayout Faithful = new InventoryLayout();

        private static IReadOnlyList<ItemStatsText.Line> Build(RuntimeItem item, ObjectInfo obj,
            bool affecting) => ItemStatsText.Build(item, obj, affecting, Faithful);

        // Lookup by design-frame position — the space Lines are actually in. Each call carries the
        // original's own coordinate in a trailing comment so the tables below stay diffable against
        // the disassembly line by line (design = VGA x5 horizontally, x6 vertically).
        private static string TextAt(IReadOnlyList<ItemStatsText.Line> lines, float x, float y) =>
            lines.FirstOrDefault(l => Mathf.Approximately(l.X, x) && Mathf.Approximately(l.Y, y))
                .Text;

        private static IEnumerable<string> Texts(IReadOnlyList<ItemStatsText.Line> lines) =>
            lines.Select(l => l.Text);

        /// <summary>The absolute pin: a Line's position is in the screen's own design-frame space,
        /// not the original's. These four numbers are hand-derived (the original's 115,45 and
        /// 200,45 scaled x5 horizontally / x6 vertically) and do not go through
        /// <see cref="TextAtOriginal"/>, so a wrong scale factor fails here even though the tables
        /// below would still pass.</summary>
        [Test]
        public void Lines_CarryDesignFramePositions_NotTheOriginalsOwnUnits() {
            var lines = Build(Item(),
                Obj(ObjectType.Sword, swingDmg: 6, thrustDmg: 4), affecting: true);

            ItemStatsText.Line label = lines.First(l => l.Text == "Base Dmg:");
            Assert.AreEqual(575f, label.X, 0.001f, "label x = 115 * 5");
            Assert.AreEqual(270f, label.Y, 0.001f, "label y = 45 * 6");

            ItemStatsText.Line thrust = lines.First(l => l.Text == "4+Strength");
            Assert.AreEqual(1000f, thrust.X, 0.001f, "thrust column x = (115 + 85) * 5");
            Assert.AreEqual(270f, thrust.Y, 0.001f, "same row");
        }

        // ---- sword: two-column melee table -------------------------------------------

        [Test]
        public void Sword_TwoColumnTable_AtTheOriginalCoordinates() {
            var lines = Build(Item(),
                Obj(ObjectType.Sword, swingDmg: 6, thrustDmg: 4, swingAcc: 10, thrustAcc: 8), affecting: true);

            // Headers centred over each column at x = 115+85 and 115+150, y = 30.
            Assert.AreEqual("Thrust", TextAt(lines, 1000f, 180f)   /* VGA 200,30 */);
            Assert.AreEqual("Swing", TextAt(lines, 1325f, 180f)   /* VGA 265,30 */);
            Assert.AreEqual("________", TextAt(lines, 1000f, 198f)   /* VGA 200,33 */, "underline sits 3 rows below the header");

            // Base damage row at y = 45, accuracy at y = 55.
            Assert.AreEqual("Base Dmg:", TextAt(lines, 575f, 270f)   /* VGA 115,45 */);
            Assert.AreEqual("4+Strength", TextAt(lines, 1000f, 270f)   /* VGA 200,45 */, "thrust column takes ThrustBaseDamage");
            Assert.AreEqual("6+Strength", TextAt(lines, 1325f, 270f)   /* VGA 265,45 */, "swing column takes SwingBaseDamage");
            Assert.AreEqual("Accuracy:", TextAt(lines, 575f, 330f)   /* VGA 115,55 */);
            Assert.AreEqual("8+Skill", TextAt(lines, 1000f, 330f)   /* VGA 200,55 */, "thrust column takes ThrustAccuracy");
            Assert.AreEqual("10+Skill", TextAt(lines, 1325f, 330f)   /* VGA 265,55 */, "swing column takes SwingAccuracy");
        }

        [Test]
        public void Staff_SameTable_ShiftedDownTenRows() {
            var lines = Build(Item(), Obj(ObjectType.Staff), affecting: true);
            Assert.AreEqual("Thrust", TextAt(lines, 1000f, 240f)   /* VGA 200,40 */, "staff starts at y=40, not 30");
            Assert.IsNull(TextAt(lines, 1000f, 180f)   /* VGA 200,30 */);
        }

        [Test]
        public void Staff_HasNoEnchantmentBlock_UnlikeSword() {
            var staff = Build(Item(), Obj(ObjectType.Staff), affecting: true);
            var sword = Build(Item(), Obj(ObjectType.Sword), affecting: true);
            Assert.IsFalse(Texts(staff).Contains("Active Mods:"), "the block is sword/armor only");
            Assert.IsTrue(Texts(sword).Contains("Active Mods:"));
        }

        // ---- ranged: names the partner item ------------------------------------------

        [Test]
        public void Crossbow_NamesTheQuarrelItCombinesWith() {
            var lines = Build(Item(), Obj(ObjectType.Crossbow, swingDmg: 5, swingAcc: 9), affecting: true);
            Assert.AreEqual("Base Damage:", TextAt(lines, 700f, 330f)   /* VGA 140,55 */);
            Assert.AreEqual("5+Quarrel", TextAt(lines, 1050f, 330f)   /* VGA 210,55 */);
            Assert.AreEqual("9+Quarrel+Skill", TextAt(lines, 1050f, 390f)   /* VGA 210,65 */);
        }

        [Test]
        public void QuarrelIds36To43_NameTheCrossBow_EvenThoughTheirCategoryIsnt2() {
            var lines = Build(Item(objectId: 36), Obj(ObjectType.Misc, swingDmg: 3), affecting: true);
            Assert.AreEqual("3+CrossBow", TextAt(lines, 1050f, 330f)   /* VGA 210,55 */);

            var outside = Build(Item(objectId: 44), Obj(ObjectType.Misc), affecting: true);
            Assert.IsFalse(Texts(outside).Contains("Base Damage:"), "id 44 is past the quarrel range");
        }

        // ---- armor --------------------------------------------------------------------

        [Test]
        public void Armor_ShowsPercentageAndShiftsRight() {
            var lines = Build(Item(), Obj(ObjectType.Armor, swingAcc: 23), affecting: true);
            Assert.AreEqual("Armor Mod:", TextAt(lines, 775f, 270f)   /* VGA 155,45 */, "armor indents x by 15");
            Assert.AreEqual("23%", TextAt(lines, 1125f, 270f)   /* VGA 225,45 */);
            Assert.AreEqual("Resistances:", TextAt(lines, 775f, 366f)   /* VGA 155,61 */, "armor's block is Resistances, not Active Mods");
        }

        // ---- enchantment flags ---------------------------------------------------------

        [Test]
        public void ActiveMods_None_WhenNoEnchantmentBits() {
            var lines = Build(Item(), Obj(ObjectType.Sword), affecting: true);
            Assert.AreEqual("None", TextAt(lines, 925f, 426f)   /* VGA 185,71 */);
            Assert.AreEqual("None", TextAt(lines, 925f, 486f)   /* VGA 185,81 */, "bless type likewise");
        }

        [Test]
        public void ActiveMods_ConcatenatesInTheOriginalOrder() {
            ushort flags = (ushort)(ItemFlags.Poisoned | ItemFlags.Frosted | ItemFlags.Flaming | ItemFlags.SteelFired);
            var lines = Build(Item(itemFlags: flags), Obj(ObjectType.Sword), affecting: true);
            Assert.AreEqual("Poisoned Frosted Flaming Steelfired ", TextAt(lines, 925f, 426f)   /* VGA 185,71 */);
        }

        /// <summary>Faithful oddity: 0x800 and 0x1000 both render the same word.</summary>
        [Test]
        public void BothEnhancedBits_RenderTheWordTwice() {
            ushort flags = (ushort)(ItemFlags.Enhanced1 | ItemFlags.Enhanced2);
            var lines = Build(Item(itemFlags: flags), Obj(ObjectType.Sword), affecting: true);
            Assert.AreEqual("EnhancedEnhanced", TextAt(lines, 925f, 426f)   /* VGA 185,71 */);
        }

        [Test]
        public void BlessType_ReportsTheBlessingTier() {
            foreach ((ushort bit, string expected) in new (ushort, string)[] {
                         (0x2000, "#1 (+5%)"), (0x4000, "#2 (+10%)"), (0x8000, "#3 (+15%)") }) {
                var lines = Build(Item(itemFlags: bit), Obj(ObjectType.Sword), affecting: true);
                Assert.AreEqual(expected, TextAt(lines, 925f, 486f)   /* VGA 185,81 */);
            }
        }

        // ---- racial + player-stat lines -------------------------------------------------

        [Test]
        public void RacialMod_OnlyWhenTheMaskIsNonZero() {
            Assert.IsFalse(Texts(Build(Item(), Obj(ObjectType.Potion), affecting: true))
                .Contains("Racial Mod:"), "no mask -> no line");

            var lines = Build(Item(), Obj(ObjectType.Potion, race: (Race)2), affecting: true);
            Assert.AreEqual("Racial Mod:", TextAt(lines, 700f, 216f)   /* VGA 140,36 */);
            Assert.AreEqual("Elf", TextAt(lines, 1050f, 216f)   /* VGA 210,36 */);
        }

        /// <summary>
        /// The Race enum's numeric values must stay equal to the original's +0x38 racial mask, since
        /// the panel switches on the raw value. Human=3 is deliberately NOT one of the 1/2/4 cases —
        /// it is the original's fallback for any other non-zero mask, and only reads correctly
        /// because 3 falls through. Reordering this enum would silently mislabel items.
        /// </summary>
        [Test]
        public void RaceEnumValues_MatchTheRawMask() {
            Assert.AreEqual(0, (int)Race.None);
            Assert.AreEqual(1, (int)Race.Tsurani);
            Assert.AreEqual(2, (int)Race.Elf);
            Assert.AreEqual(3, (int)Race.Human, "the fallback case — must not collide with 1/2/4");
            Assert.AreEqual(4, (int)Race.Dwarf);
        }

        [Test]
        public void RacialMod_HumanMask_StillDrawsALine() {
            // Verified against real data: the Broadsword (id 18) carries mask 3, and the original
            // labels it "Human" rather than suppressing the row.
            var lines = Build(Item(), Obj(ObjectType.Sword, race: Race.Human), affecting: true);
            Assert.IsTrue(Texts(lines).Contains("Racial Mod:"));
            Assert.IsTrue(Texts(lines).Contains("Human"));
        }

        [Test]
        public void RacialMod_UnknownMaskFallsBackToHuman() {
            var lines = Build(Item(), Obj(ObjectType.Potion, race: (Race)8), affecting: true);
            Assert.AreEqual("Human", TextAt(lines, 1050f, 216f)   /* VGA 210,36 */);
        }

        [Test]
        public void PlayerStatLine_WordingFollowsTheAffectingFlag() {
            var on = Build(Item(), Obj(ObjectType.Potion, equipMask: 4), affecting: true);
            var off = Build(Item(), Obj(ObjectType.Potion, equipMask: 4), affecting: false);
            Assert.IsTrue(Texts(on).Contains("Affecting player statistics"));
            Assert.IsTrue(Texts(off).Contains("Can affect player statistics"));
        }

        // ---- the "nothing to show" case --------------------------------------------------

        [Test]
        public void PlainItem_ProducesNoLines_SoNoPanelIsDrawn() {
            // A potion with no racial mask and no attribute effect: the original's cursor never
            // moves off its start row, which is how it decides not to draw the panel at all.
            Assert.IsEmpty(Build(Item(objectId: 72), Obj(ObjectType.Food), affecting: true));
        }

        // ---- the walk is layout data, not constants --------------------------------------

        /// <summary>
        /// Move the origin and the value column and the lines follow. Without this, every table
        /// above would still pass if the walk had the faithful numbers hardcoded and ignored the
        /// layout entirely — they only ever exercise the default.
        /// </summary>
        [Test]
        public void Walk_ReadsItsGeometryFromTheLayout_NotFromConstants() {
            var moved = new InventoryLayout {
                StatsOrigin = new LayoutHint {
                    Left = LayoutLength.Px(1000f),
                    Top = LayoutLength.Px(600f),
                },
                StatsSectionGap = 0f,
                StatsValueColumn = 100f,
            };
            // A potion with a racial mask: no combat block, so the racial row is the first thing
            // the walk emits and it lands on the origin itself.
            var lines = ItemStatsText.Build(Item(), Obj(ObjectType.Potion, race: (Race)2),
                affecting: true, moved);

            Assert.AreEqual("Racial Mod:", TextAt(lines, 1000f, 600f), "label sits on the origin");
            Assert.AreEqual("Elf", TextAt(lines, 1100f, 600f), "value sits one StatsValueColumn right");
        }

        /// <summary>
        /// A percentage origin cannot be summed with the walk's px columns without measuring a
        /// parent this class deliberately never measures. It must refuse out loud and walk from the
        /// faithful origin — not silently treat "50" as 50px, which would put the panel somewhere
        /// neither the author nor the original asked for.
        /// </summary>
        [Test]
        public void PercentOrigin_IsRefusedLoudly_AndFallsBackToTheFaithfulPosition() {
            var percent = new InventoryLayout {
                StatsOrigin = new LayoutHint {
                    Left = LayoutLength.Percent(50f),
                    Top = LayoutLength.Percent(25f),
                },
            };
            LogAssert.Expect(LogType.Error, new Regex("InventoryLayout.StatsOrigin"));

            var lines = ItemStatsText.Build(Item(), Obj(ObjectType.Potion, race: (Race)2),
                affecting: true, percent);

            // Faithful origin (700,180) + the default section gap (36).
            Assert.AreEqual("Racial Mod:", TextAt(lines, 700f, 216f),
                "falls back to the original's position, not to the bare percentage numbers");
            Assert.IsNull(TextAt(lines, 50f, 25f + 36f), "the percentage's numbers are not read as px");
        }
    }
}

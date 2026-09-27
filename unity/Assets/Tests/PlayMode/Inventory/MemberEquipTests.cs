namespace BakAgain.Tests.PlayMode.Inventory {
    using System.Collections;
    using System.Text.RegularExpressions;
    using BakAgain.UI.Inventory;
    using BakAgain.UI.Layout;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Equip-by-drop rules, from the original's two gates (task-44):
    ///   * the drop hit test in <c>invui_handle_item_drag</c> (INVENTOR.C:609) — the paperdoll box,
    ///     which is <see cref="InventoryLayout.PaperdollBox"/> and is exactly the panel fill the
    ///     screen paints there;
    ///   * the category gate <c>cmbinv_member_can_equip_cat</c> (CMBINV.C:1084) — a caster may
    ///     equip Armor + Staff; a non-caster Armor + Sword + Crossbow.
    /// Both are pure functions so they can be pinned here without a live REQ build.
    /// </summary>
    public class MemberEquipTests {
        // The shipped geometry. The hit-test tests below assert against the real box because the
        // original's edge behaviour is what they exist to pin; the reflow test uses a synthetic
        // box no implementation could hardcode by accident.
        private static readonly LayoutHint Box = new InventoryLayout().PaperdollBox;

        // ---- cmbinv_member_can_equip_cat -----------------------------------------------

        [Test]
        public void Caster_MayEquip_ArmorAndStaff_Only() {
            Assert.IsTrue(MemberEquip.CanEquipCategory(GameData.ObjectType.Armor, caster: true), "caster: armor");
            Assert.IsTrue(MemberEquip.CanEquipCategory(GameData.ObjectType.Staff, caster: true), "caster: staff");
            Assert.IsFalse(MemberEquip.CanEquipCategory(GameData.ObjectType.Sword, caster: true), "caster: no sword");
            Assert.IsFalse(MemberEquip.CanEquipCategory(GameData.ObjectType.Crossbow, caster: true), "caster: no crossbow");
        }

        [Test]
        public void NonCaster_MayEquip_ArmorSwordCrossbow_ButNotStaff() {
            Assert.IsTrue(MemberEquip.CanEquipCategory(GameData.ObjectType.Armor, caster: false), "non-caster: armor");
            Assert.IsTrue(MemberEquip.CanEquipCategory(GameData.ObjectType.Sword, caster: false), "non-caster: sword");
            Assert.IsTrue(MemberEquip.CanEquipCategory(GameData.ObjectType.Crossbow, caster: false), "non-caster: crossbow");
            Assert.IsFalse(MemberEquip.CanEquipCategory(GameData.ObjectType.Staff, caster: false), "non-caster: no staff");
        }

        [Test]
        public void NonEquippableCategories_AreAlwaysRejected() {
            foreach (GameData.ObjectType t in new[] {
                         GameData.ObjectType.Misc, GameData.ObjectType.Key, GameData.ObjectType.Potion,
                         GameData.ObjectType.Book, GameData.ObjectType.MagicalScroll,
                     }) {
                Assert.IsFalse(MemberEquip.CanEquipCategory(t, caster: true), $"caster must not equip {t}");
                Assert.IsFalse(MemberEquip.CanEquipCategory(t, caster: false), $"non-caster must not equip {t}");
            }
        }

        // ---- invui_handle_item_drag drop hit test ---------------------------------------

        [Test]
        public void PaperdollDropZone_IsTheShippedPaperdollBox() {
            // The shipped box is canonical (65,66)-(475,792) — the same rect DrawPanelBackground
            // paints as the member-mode paperdoll fill, so the visible box IS the drop target.
            Rect z = MemberEquip.PaperdollDropZone(Box);
            Assert.AreEqual(65f, z.xMin, "xMin");
            Assert.AreEqual(66f, z.yMin, "yMin");
            Assert.AreEqual(475f, z.xMax, "xMax");
            Assert.AreEqual(792f, z.yMax, "yMax");
        }

        /// <summary>The zone is the DATA's box, not a remembered rect: a hostile synthetic box
        /// (no round numbers, no coincidence with the shipped one, both axes different) has to come
        /// out exactly, and points that would hit the shipped box must miss it.</summary>
        [Test]
        public void PaperdollDropZone_FollowsTheBoxItIsGiven() {
            var moved = new LayoutHint {
                Left = LayoutLength.Px(311f),
                Top = LayoutLength.Px(97f),
                Width = LayoutLength.Px(233f),
                Height = LayoutLength.Px(419f),
            };

            Rect z = MemberEquip.PaperdollDropZone(moved);
            Assert.AreEqual(311f, z.xMin, "xMin");
            Assert.AreEqual(97f, z.yMin, "yMin");
            Assert.AreEqual(544f, z.xMax, "xMax = 311 + 233");
            Assert.AreEqual(516f, z.yMax, "yMax = 97 + 419");

            // Inside the moved box, and outside the shipped one on the x axis.
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(500f, 300f), moved),
                "inside the moved box");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(500f, 300f), Box),
                "the same point is outside the shipped box — so the box is really being read");
            // Inside the shipped box, outside the moved one.
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(100f, 700f), moved),
                "below the moved box");
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(100f, 700f), Box),
                "but inside the shipped box");
        }

        /// <summary>An override may null a box out; that must mean "no drop target", not a crash
        /// or a zero-rect that swallows the origin.</summary>
        [Test]
        public void NullBox_IsNeverADrop() {
            Assert.AreEqual(Rect.zero, MemberEquip.PaperdollDropZone(null));
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), null));
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(Vector2.zero, null));
        }

        [Test]
        public void IsPaperdollDrop_TrueInsideTheBox_FalseOutside() {
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), Box), "centre of the paperdoll box");

            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(64f, 400f), Box), "just left of the box");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(600f, 400f), Box), "the general grid (x=525..1535)");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 60f), Box), "above the box");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 800f), Box), "below the box");
        }

        /// <summary>
        /// The original compares are strict on all four edges (invui_handle_item_drag 0x57307-0x5731e:
        /// `jle`/`jge` both skip the hit), so the boundary itself is OUTSIDE the drop zone.
        /// Rect.Contains would wrongly accept the min edge, which is what this pins.
        /// </summary>
        [Test]
        public void DropZoneEdges_AreExclusive_MatchingTheOriginalCompares() {
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(65f, 400f), Box), "the box's own left edge is outside");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(475f, 400f), Box), "the box's own right edge is outside");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 66f), Box), "the box's own top edge is outside");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 792f), Box), "the box's own bottom edge is outside");
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(65f, 66f), Box), "the top-left corner is outside");

            // One canonical unit inside each edge must still hit.
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(66f, 400f), Box), "just inside xMin");
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(474f, 400f), Box), "just inside xMax");
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(270f, 67f), Box), "just inside yMin");
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(270f, 791f), Box), "just inside yMax");
        }

        // ---- the drop zone is the PAINTED box, in any unit -------------------------------

        private GameObject _panelGo;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_panelGo != null) { Object.DestroyImmediate(_panelGo); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        /// <summary>A 1600x1200 stage on a real panel, plus the paperdoll fill painted into it
        /// through <see cref="LayoutApplier.Apply"/> — exactly how <c>InventoryMenu.AddPanelBox</c>
        /// paints it. Returns the painted element once UI Toolkit has laid it out.</summary>
        private VisualElement PaintFill(LayoutHint hint) {
            _panelGo = new GameObject("PaperdollFillUnderTest");
            var document = _panelGo.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var stage = new VisualElement {
                name = "BakStage",
                style = { position = Position.Absolute, width = 1600f, height = 1200f },
            };
            document.rootVisualElement.Add(stage);
            var fill = new VisualElement { name = "panel_bg" };
            LayoutApplier.Apply(fill, hint);
            stage.Add(fill);
            return fill;
        }

        /// <summary>
        /// The failing case the shared-hint design could not cover: a paperdoll authored as
        /// percentages of the stage. <see cref="LayoutApplier"/> honours them, so the black fill is
        /// painted at 4% x 1600 = 64 etc.; reading <c>hint.Left.Value</c> would have put the drop
        /// zone at x 4, and a drop on the visible paperdoll would never equip. Taking the zone from
        /// the painted element's own resolved geometry makes the two the same thing.
        /// </summary>
        [UnityTest]
        public IEnumerator DropZone_IsThePaintedGeometry_SoAPercentBoxStillEquips() {
            // 4% / 5.5% / 25.625% / 60.5% of 1600x1200 is the shipped (65,66,410,726) rect, near
            // enough: the same paperdoll expressed relatively.
            var percent = new LayoutHint {
                Left = LayoutLength.Percent(4f), Top = LayoutLength.Percent(5.5f),
                Width = LayoutLength.Percent(25.625f), Height = LayoutLength.Percent(60.5f),
            };
            VisualElement fill = PaintFill(percent);
            yield return null;
            yield return null;

            Rect painted = fill.layout;
            Assert.Greater(painted.width, 0f, "the fill must have been laid out for this test to mean anything");
            Assert.AreEqual(64f, painted.xMin, 0.5f, "4% of the 1600-wide stage");
            Assert.AreEqual(410f, painted.width, 1f, "25.625% of the 1600-wide stage");

            Assert.AreEqual(painted, MemberEquip.PaperdollDropZone(fill, percent),
                "the zone IS the painted rect");

            // A point over the visible paperdoll equips...
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), fill, percent),
                "a drop on the painted paperdoll must equip");
            // ...and the same point read from the hint's bare numbers would not have — which is the
            // bug. That path also refuses out loud rather than answering from a percentage.
            LogAssert.Expect(LogType.Error, new Regex("PaperdollBox is authored in percent"));
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), percent),
                "reading the hint's percent numbers as px is what this fix removes");
        }

        /// <summary>With no painted fill (loot mode, the inspect view, before the first render) a
        /// px hint still answers exactly as before — the shipped path is untouched.</summary>
        [Test]
        public void DropZone_WithNoPaintedFill_FallsBackToThePxHint() {
            Assert.AreEqual(MemberEquip.PaperdollDropZone(Box),
                MemberEquip.PaperdollDropZone(null, Box));
            Assert.IsTrue(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), null, Box));
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(600f, 400f), null, Box));
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), null, Box, lootMode: true));
        }

        /// <summary>A percent hint with nothing painted to read back from cannot become a stage-px
        /// rect at all, so it refuses loudly and swallows no drop silently.</summary>
        [Test]
        public void DropZone_PercentHintWithNoPaintedFill_RefusesLoudly() {
            var percent = new LayoutHint {
                Left = LayoutLength.Percent(4f), Top = LayoutLength.Percent(5.5f),
                Width = LayoutLength.Percent(25.625f), Height = LayoutLength.Percent(60.5f),
            };

            LogAssert.Expect(LogType.Error, new Regex("PaperdollBox is authored in percent"));
            Assert.AreEqual(Rect.zero, MemberEquip.PaperdollDropZone(null, percent));
        }

        [Test]
        public void LootMode_HasNoPaperdoll_SoNothingIsAnEquipDrop() {
            // Loot mode paints one continuous box and has no equipped area at all — the equip path
            // must never trigger there even though the coordinates overlap the member paperdoll rect.
            Assert.IsFalse(MemberEquip.IsPaperdollDrop(new Vector2(270f, 400f), Box, lootMode: true),
                "loot mode has no paperdoll, so the centre of that rect is not an equip drop");
        }
    }
}

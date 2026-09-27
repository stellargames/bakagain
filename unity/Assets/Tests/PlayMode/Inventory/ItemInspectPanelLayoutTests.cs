namespace BakAgain.Tests.PlayMode.Inventory {
    using System.Collections.Generic;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using GameData.Resources.Object;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// Geometry tests for <see cref="ItemInspectPanel"/>, in the same two groups as
    /// <see cref="ItemGridRendererTests"/>.
    ///
    /// <para><b>Mechanism</b> tests drive the panel with a SYNTHETIC <see cref="InventoryLayout"/>
    /// whose every number is asymmetric and non-round (lines at x 311/337, y 97/163/229/641, icon
    /// 412/349, shadow 13x7, flight lattice 25x17). None of those is a value any implementation
    /// would plausibly hardcode, and none coincides with the shipped geometry, so a panel that
    /// quietly kept its own constants cannot reproduce these results by accident.</para>
    ///
    /// <para><b>Faithfulness</b> tests use the shipped defaults and assert the exact positions the
    /// pre-change implementation produced (its VGA constants times 5 across / 6 down), so the
    /// screen is pinned numerically.</para>
    ///
    /// <para>Every assertion reads an element's own inset — which IS its position here, because the
    /// panel adds its labels and icon as absolutely-positioned direct children of the root it is
    /// given. <see cref="Inset"/> asserts both of those preconditions rather than assuming
    /// them.</para>
    /// </summary>
    public class ItemInspectPanelLayoutTests {
        private readonly List<Object> _scratch = new List<Object>();
        private GameObject _go;
        private PanelSettings _panelSettings;

        // --- fixtures -------------------------------------------------------------------------

        private const float SynthTextX = 311f;
        private const float SynthName1Y = 97f;
        private const float SynthName2Y = 163f;
        private const float SynthTypeY = 229f;
        private const float SynthStatusX = 337f;
        private const float SynthStatusY = 641f;
        private const float SynthIconX = 412f;
        private const float SynthIconY = 349f;
        private const float SynthShadowX = 13f;
        private const float SynthShadowY = 7f;
        private const float SynthStepX = 25f;
        private const float SynthStepY = 17f;

        private static LayoutHint Point(float left, float top) => new LayoutHint {
            Left = LayoutLength.Px(left),
            Top = LayoutLength.Px(top),
        };

        private static InventoryLayout SyntheticLayout(float stepX = SynthStepX, float stepY = SynthStepY) =>
            new InventoryLayout {
                InspectNameFirstLine = Point(SynthTextX, SynthName1Y),
                InspectNameSecondLine = Point(SynthTextX, SynthName2Y),
                InspectTypeLine = Point(SynthTextX, SynthTypeY),
                InspectStatusLine = Point(SynthStatusX, SynthStatusY),
                InspectIcon = Point(SynthIconX, SynthIconY),
                TextShadowOffsetX = SynthShadowX,
                TextShadowOffsetY = SynthShadowY,
                IconFlightStepX = stepX,
                IconFlightStepY = stepY,
            };

        // Flags 0x8000 is the "Amount: n" gate, so this object always produces a type line.
        // WordWrap > 0 splits the name across two lines; 0 leaves it on one.
        private static ObjectInfo Obj(int wordWrap, string name = "Elvandar Bow") =>
            new ObjectInfo("obj80") {
                Name = name, Number = 80, Icon = 0, MaxAmount = 5, InventorySlots = 1,
                Flags = (ObjectFlags)0x8000, WordWrap = wordWrap,
            };

        // ItemFlags.Broken (0x10) guarantees a non-empty status line without needing `affecting`.
        private static RuntimeItem Item() => new RuntimeItem(80, 3, (ushort)GameData.ItemFlags.Broken);

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
            foreach (Object asset in _scratch) {
                if (asset != null) { Object.DestroyImmediate(asset); }
            }
            _scratch.Clear();
        }

        // A real UIDocument panel, needed only by the icon tests: AddIconAsync bails when
        // root.panel is null, exactly as it does when the screen has been torn down mid-load.
        private VisualElement BuildPanelRoot() {
            _go = new GameObject("InspectPanelUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            return document.rootVisualElement;
        }

        private sealed class SpriteResources : IResourceProviderService {
            private readonly Sprite _sprite;
            public SpriteResources(Sprite sprite) { _sprite = sprite; }
            public UniTask<T> LoadAssetAsync<T>(object key, object owner) where T : class =>
                UniTask.FromResult(_sprite as T); // the palette request resolves to null, which is fine
            public void ReleaseAssets(object owner) { }
        }

        private SpriteResources BuildResources() {
            var tex = new Texture2D(4, 4);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), Vector2.zero);
            _scratch.Add(tex);
            _scratch.Add(sprite);
            return new SpriteResources(sprite);
        }

        // --- assertion helpers ----------------------------------------------------------------

        /// <summary>An element's inset, which is its position only while it is absolutely
        /// positioned and its lengths are explicit px — both asserted here rather than assumed. An
        /// unset inline style reads back as 0px, so the keyword check is what stops a missing
        /// position from passing as "at the origin".</summary>
        private static Vector2 Inset(VisualElement el, string what) {
            Assert.IsNotNull(el, what + " not found");
            Assert.AreEqual(Position.Absolute, el.style.position.value,
                what + " must be absolutely positioned for its inset to be a position");
            AssertExplicitPx(el.style.left, what + " left");
            AssertExplicitPx(el.style.top, what + " top");
            return new Vector2(el.style.left.value.value, el.style.top.value.value);
        }

        private static void AssertExplicitPx(StyleLength length, string what) {
            Assert.AreEqual(StyleKeyword.Undefined, length.keyword,
                what + " must be an explicit length, not a style keyword");
            Assert.AreEqual(LengthUnit.Pixel, length.value.unit, what + " must be in px");
        }

        private static void AssertAt(Vector2 expected, VisualElement el, string what) {
            Vector2 actual = Inset(el, what);
            Assert.AreEqual(expected.x, actual.x, 0.001f, what + " left");
            Assert.AreEqual(expected.y, actual.y, 0.001f, what + " top");
        }

        // The panel emits a shadow label then the body label for each line, in draw order.
        private static List<Label> Labels(VisualElement root) {
            var labels = new List<Label>();
            foreach (VisualElement child in root.Children()) {
                if (child is Label label) { labels.Add(label); }
            }
            return labels;
        }

        // ======================================================================================
        // Mechanism — synthetic geometry only.
        // ======================================================================================

        [Test]
        public void TextLines_TakeTheirPositionsFromTheLayout() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 8), affecting: true,
                resources: null, layout: SyntheticLayout(), from: Vector2.zero);

            List<Label> labels = Labels(root);
            // name line 1 + line 2 + type line + status line, each a shadow/body pair.
            Assert.AreEqual(8, labels.Count, "four lines, each drawn twice");

            AssertAt(new Vector2(SynthTextX, SynthName1Y), labels[1], "name line 1");
            AssertAt(new Vector2(SynthTextX, SynthName2Y), labels[3], "name line 2");
            AssertAt(new Vector2(SynthTextX, SynthTypeY), labels[5], "type line");
            AssertAt(new Vector2(SynthStatusX, SynthStatusY), labels[7], "status line");

            Assert.AreEqual("Elvandar", labels[1].text, "the wrapped name's first half");
            Assert.AreEqual("Bow", labels[3].text, "and its second");
            Assert.AreEqual("Amount: 3", labels[5].text);
            Assert.AreEqual(", Broken", labels[7].text);
        }

        [Test]
        public void TextShadow_IsOffsetByTheLayoutsOwnAmount() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 8), affecting: true,
                resources: null, layout: SyntheticLayout(), from: Vector2.zero);

            List<Label> labels = Labels(root);
            // Non-uniform on purpose: 13 across, 7 down. An implementation that used one number for
            // both axes, or the shipped 5/6, fails on at least one of these.
            AssertAt(new Vector2(SynthTextX + SynthShadowX, SynthName1Y + SynthShadowY),
                labels[0], "name line 1 shadow");
            AssertAt(new Vector2(SynthStatusX + SynthShadowX, SynthStatusY + SynthShadowY),
                labels[6], "status line shadow");
            Assert.AreEqual("inspect_text_shadow", labels[0].name);
            Assert.AreEqual("inspect_text", labels[1].name);
        }

        /// <summary>The wrap is a CONTENT decision, not a geometry one: both line positions are
        /// data, and what the view chooses is which of them this name needs. A one-line name is
        /// faithfully drawn on the SECOND line — lower than a wrapped name's first line, which is
        /// the behaviour this pins.</summary>
        [Test]
        public void UnwrappedName_DrawsOnTheSecondLine_NotTheFirst() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: null, layout: SyntheticLayout(), from: Vector2.zero);

            List<Label> labels = Labels(root);
            Assert.AreEqual(6, labels.Count, "three lines now — the name takes only one");
            Assert.AreEqual("Elvandar Bow", labels[1].text, "the whole name, unsplit");
            AssertAt(new Vector2(SynthTextX, SynthName2Y), labels[1], "unwrapped name");

            foreach (Label label in labels) {
                Assert.Greater(Mathf.Abs(label.style.top.value.value - SynthName1Y), 0.001f,
                    "nothing is drawn on the first name line when the name does not wrap");
            }
        }

        /// <summary>The icon flight moves on a lattice whose spacing is data. The synthetic icon
        /// sits at (412,349) — deliberately NOT a multiple of the 25x17 step — so the snapped start
        /// AND the snapped destination are both values only a data-driven lattice produces.</summary>
        [Test]
        public void IconFlight_SnapsToTheLatticeTheLayoutDeclares() {
            VisualElement root = BuildPanelRoot();
            var panel = new ItemInspectPanel();

            // from (137,219): round(137/25) = 5 -> 125, round(219/17) = 13 -> 221.
            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: BuildResources(), layout: SyntheticLayout(), from: new Vector2(137f, 219f));

            AssertAt(new Vector2(125f, 221f), root.Q("inspect_icon"), "flight start on a 25x17 lattice");
        }

        /// <summary>Same start point, a 1px lattice: the icon starts where it was actually told to,
        /// not on a coarse grid. Together with the test above this is the proof that the step is
        /// read rather than remembered — the two runs differ ONLY in the step lengths.</summary>
        [Test]
        public void IconFlight_WithAUnitStep_DoesNotSnapAtAll() {
            VisualElement root = BuildPanelRoot();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: BuildResources(), layout: SyntheticLayout(stepX: 1f, stepY: 1f),
                from: new Vector2(137f, 219f));

            AssertAt(new Vector2(137f, 219f), root.Q("inspect_icon"), "flight start on a 1px lattice");
        }

        /// <summary>A degenerate step (an override's 0) must not produce a lattice with no
        /// spacing — the flight would never terminate and the screen would hang on the await.</summary>
        [Test]
        public void IconFlight_ZeroStep_DegradesToAUnitLattice() {
            VisualElement root = BuildPanelRoot();
            var panel = new ItemInspectPanel();
            InventoryLayout layout = SyntheticLayout();
            layout.IconFlightStepX = 0f;
            layout.IconFlightStepY = 0f;

            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: BuildResources(), layout: layout, from: new Vector2(137f, 219f));

            AssertAt(new Vector2(137f, 219f), root.Q("inspect_icon"), "degenerate step -> 1px lattice");
        }

        /// <summary>
        /// The flight runs in design-frame px — its start comes from the grid cell's centre, its
        /// lattice is a px spacing, and the panel measures nothing — so a percentage-authored icon
        /// position cannot be resolved into it. The panel must say so and NOT fly: mixing the units
        /// would run the flight to <c>18.125</c> px, park the icon at a number stamped with the
        /// percent unit, and leave that disagreeing with the resting style. Instead the icon stays
        /// exactly where the resting hint put it, and an error names the property.
        /// </summary>
        [Test]
        public void IconFlight_PercentAuthoredPosition_IsRefusedLoudly_AndTheIconRestsWhereAuthored() {
            VisualElement root = BuildPanelRoot();
            var panel = new ItemInspectPanel();
            InventoryLayout layout = SyntheticLayout();
            layout.InspectIcon = new LayoutHint {
                Left = LayoutLength.Percent(18.125f), Top = LayoutLength.Percent(35.5f),
            };

            // The wording is LayoutApplier.RefuseUnresolvable's, shared with every other refusal
            // across the UI; what this test pins is that the refusal names THIS field.
            LogAssert.Expect(LogType.Error,
                new Regex("InventoryLayout.InspectIcon cannot be resolved as authored"));
            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: BuildResources(), layout: layout, from: new Vector2(137f, 219f));

            VisualElement icon = root.Q("inspect_icon");
            Assert.IsNotNull(icon, "the icon is still drawn — only the animation is skipped");
            // The resting style, untouched: still the authored percentages, not a snapped number
            // wearing the percent unit.
            Assert.AreEqual(LengthUnit.Percent, icon.style.left.value.unit, "left stays a percentage");
            Assert.AreEqual(18.125f, icon.style.left.value.value, 0.001f, "left is the authored value");
            Assert.AreEqual(LengthUnit.Percent, icon.style.top.value.unit, "top stays a percentage");
            Assert.AreEqual(35.5f, icon.style.top.value.value, 0.001f, "top is the authored value");
        }

        /// <summary>The same rule one line over: the drop-shadow offset is a px scalar, so it
        /// cannot be added to a percentage-authored line position either. The shadow is then drawn
        /// on top of the line rather than 13% away from it, and the reason is logged once.</summary>
        [Test]
        public void TextShadow_OnAPercentAuthoredLine_IsRefusedLoudly_AndDrawnUnoffset() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();
            InventoryLayout layout = SyntheticLayout();
            layout.InspectNameSecondLine = new LayoutHint {
                Left = LayoutLength.Percent(18.125f), Top = LayoutLength.Percent(35.5f),
            };

            LogAssert.Expect(LogType.Error, new Regex("TextShadowOffset"));
            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: null, layout: layout, from: Vector2.zero);

            List<Label> labels = Labels(root);
            Assert.AreEqual("inspect_text_shadow", labels[0].name);
            Assert.AreEqual(LengthUnit.Percent, labels[0].style.left.value.unit);
            Assert.AreEqual(18.125f, labels[0].style.left.value.value, 0.001f,
                "the shadow keeps the line's own percentage — 18.125 + 13 would be a silent lie");
            Assert.AreEqual(35.5f, labels[0].style.top.value.value, 0.001f);
        }

        [Test]
        public void NullLayout_FallsBackToTheFaithfulDefaults_AndDoesNotThrow() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();

            Assert.DoesNotThrow(() => panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: null, layout: null, from: Vector2.zero));

            AssertAt(new Vector2(290f, 150f), Labels(root)[1], "name on the shipped second line");
        }

        // ======================================================================================
        // Faithfulness — the shipped geometry, at the coordinates the pre-change code produced.
        // ======================================================================================

        /// <summary>The four line positions the removed VGA constants produced: text x 58 -> 290,
        /// status x 59 -> 295, name y 15/25 -> 90/150, type y 35 -> 210, status y 101 -> 606.
        /// Hand-derived, absolute, no fixture arithmetic.</summary>
        [Test]
        public void Faithful_TextLinesLandWhereTheOriginalDrawsThem() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 8), affecting: true,
                resources: null, layout: new InventoryLayout(), from: Vector2.zero);

            List<Label> labels = Labels(root);
            AssertAt(new Vector2(290f, 90f), labels[1], "wrapped name line 1");
            AssertAt(new Vector2(290f, 150f), labels[3], "wrapped name line 2");
            AssertAt(new Vector2(290f, 210f), labels[5], "type line");
            AssertAt(new Vector2(295f, 606f), labels[7], "status line");
        }

        /// <summary>The drop shadow is one of the original's pixels down-right, which is 5 across
        /// and 6 down in the design frame — the two differ, and both matter.</summary>
        [Test]
        public void Faithful_ShadowSitsFivePxRightAndSixPxDown() {
            var root = new VisualElement();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: null, layout: new InventoryLayout(), from: Vector2.zero);

            // Unwrapped name -> three lines: name, type, status; each a shadow/body pair.
            List<Label> labels = Labels(root);
            Assert.AreEqual(6, labels.Count);
            AssertAt(new Vector2(295f, 156f), labels[0], "name shadow = (290,150) + (5,6)");
            AssertAt(new Vector2(295f, 216f), labels[2], "type shadow = (290,210) + (5,6)");
            AssertAt(new Vector2(300f, 612f), labels[4], "status shadow = (295,606) + (5,6)");
        }

        /// <summary>
        /// All four lines are drawn with the original's <c>align == 1</c> (<c>x -= width/2</c>), so
        /// each label's inset is the line's CENTRE, not its left edge. UI Toolkit has no
        /// centre-on-a-point primitive, so the panel does it with a -50% horizontal translate.
        ///
        /// <para>This asserts the RESOLVED geometry, not the inline style: each label ends up with
        /// its own centre on the hint's x, which is a claim a "-50% is set" style check cannot
        /// make. The widths are supplied by the test because the throwaway
        /// <see cref="PanelSettings"/> has no theme font to measure text with — the point being
        /// pinned is that a label of ANY width is centred on its x rather than starting at it, and
        /// with the translate removed every one of these lands half its width to the right.</para>
        /// </summary>
        [Test]
        public void Faithful_LinesAreCentredOnTheirX_NotLeftAlignedAtIt() {
            VisualElement root = BuildPanelRoot();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: null, layout: new InventoryLayout(), from: Vector2.zero);

            const float width = 100f;
            List<Label> labels = Labels(root);
            Assert.AreEqual(6, labels.Count, "name, type and status, each a shadow/body pair");
            foreach (Label label in labels) {
                label.style.width = width;
                label.style.height = 20f;
            }
            ForceLayout(root);

            foreach (Label label in labels) {
                float anchorX = label.style.left.value.value;
                Vector2 expected = root.LocalToWorld(new Vector2(anchorX, 0f));
                Assert.Greater(label.worldBound.width, 0f, label.text + " needs a width to be centred");
                Assert.AreEqual(expected.x, label.worldBound.center.x, 0.5f,
                    label.text + " must have its CENTRE on the hint's x, not its left edge");
            }
        }

        // Runs the panel's layout so worldBound is real inside a single-frame test — the same
        // reflection call InventoryPointerTests uses, for the same reason.
        private static void ForceLayout(VisualElement element) {
            MethodInfo validateLayout = element.panel.GetType()
                .GetMethod("ValidateLayout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            validateLayout?.Invoke(element.panel, null);
        }

        /// <summary>The icon's flight lattice is one original pixel per step: 5 across, 6 down.
        /// A cell centre of (803,433) — chosen because it is a multiple of neither — snaps to
        /// (805,432), which is exactly what the pre-change code produced when it divided into the
        /// original's units, rounded, and multiplied back.</summary>
        [Test]
        public void Faithful_IconFlightUsesTheOriginalsOwnPixelLattice() {
            VisualElement root = BuildPanelRoot();
            var panel = new ItemInspectPanel();

            panel.Render(root, Item(), Obj(wordWrap: 0), affecting: true,
                resources: BuildResources(), layout: new InventoryLayout(),
                from: new Vector2(803f, 433f));

            AssertAt(new Vector2(805f, 432f), root.Q("inspect_icon"),
                "round(803/5)*5 = 805, round(433/6)*6 = 432");
        }
    }
}

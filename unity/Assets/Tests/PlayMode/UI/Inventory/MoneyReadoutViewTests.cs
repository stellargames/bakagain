namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Inventory;
    using GameData.Resources.Inventory;
    using GameData.Resources.Layout;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// The party-money readout (<c>UI_DrawInventory</c> @0x56dd0). Spec:
    /// <c>docs/specs/party-money-display.md</c> §3.1.
    ///
    /// <para>Two groups, as elsewhere in this folder. <b>Mechanism</b> tests drive the view with a
    /// synthetic layout whose numbers are non-round and unlike the shipped ones, so a view that
    /// quietly kept its own literals could not reproduce them. <b>Faithfulness</b> tests use the
    /// shipped defaults and assert the exact values the original draws.</para>
    /// </summary>
    public class MoneyReadoutViewTests {
        // Measures text with the game font, which is built from the player's own files (TASK-662).
        [NUnit.Framework.OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        private const string TextName = "inventory_money";
        private const string ShadowName = "inventory_money_shadow";

        private static float Px(StyleLength length) => length.value.value;

        private static VisualElement Render(int royals, InventoryLayout layout,
            out MoneyReadoutView view) {
            var root = new VisualElement();
            view = new MoneyReadoutView();
            view.Render(root, royals, layout);
            return root;
        }

        // ---- faithfulness -----------------------------------------------------------------

        [Test]
        public void ShippedLayoutAnchorsTheNumbersRightEdgeAt1295() {
            VisualElement root = Render(1234, new InventoryLayout(), out _);
            Label text = root.Q<Label>(TextName);
            Assert.That(text, Is.Not.Null, "the readout should be drawn");
            // VGA 259 right-aligned -> canonical 1295, i.e. 1600 - 1295 = 305 from the right edge.
            Assert.That(Px(text.style.right), Is.EqualTo(305f));
            Assert.That(Px(text.style.top), Is.EqualTo(1098f)); // VGA 183 -> 1098
        }

        [Test]
        public void ReadoutIsNotPinnedOnBothHorizontalEdges() {
            // Regression (found live, not by the inset assertions above): an element pinned left AND
            // right is STRETCHED between them, so with the default TopLeft anchor's implicit
            // left:0 the readout's box spanned the whole 1600-wide frame. It still LOOKED right —
            // unityTextAlign was quietly doing the alignment — but the box was a lie. The left edge
            // must stay free so the number sizes to itself.
            VisualElement root = Render(1234, new InventoryLayout(), out _);
            foreach (string name in new[] { TextName, ShadowName }) {
                Label label = root.Q<Label>(name);
                Assert.That(label.style.left.keyword, Is.EqualTo(StyleKeyword.Null),
                    name + " must not be pinned on the left as well as the right");
            }
        }

        [Test]
        public void ShowsTheAbbreviatedWording() {
            VisualElement root = Render(1234, new InventoryLayout(), out _);
            Assert.That(root.Q<Label>(TextName).text, Is.EqualTo("123s 4r"));
        }

        [Test]
        public void EmptyPurseStillShowsBothParts() {
            VisualElement root = Render(0, new InventoryLayout(), out _);
            Assert.That(root.Q<Label>(TextName).text, Is.EqualTo("0s 0r"));
        }

        [Test]
        public void PastTheLimitTheUnitLettersGo() {
            VisualElement root = Render(100000, new InventoryLayout(), out _);
            Assert.That(root.Q<Label>(TextName).text, Is.EqualTo("10000"));
        }

        [Test]
        public void ShadowIsTheSameStringOnePixelDownRight() {
            var layout = new InventoryLayout();
            VisualElement root = Render(1234, layout, out _);
            Label text = root.Q<Label>(TextName);
            Label shadow = root.Q<Label>(ShadowName);
            Assert.That(shadow, Is.Not.Null, "the readout is drawn twice, shadow first");
            Assert.That(shadow.text, Is.EqualTo(text.text));
            // Pinned from the RIGHT edge, so "one pixel further right" is one pixel LESS inset —
            // the sign that a left-anchored line would not need.
            Assert.That(Px(shadow.style.right),
                Is.EqualTo(Px(text.style.right) - layout.TextShadowOffsetX));
            Assert.That(Px(shadow.style.top),
                Is.EqualTo(Px(text.style.top) + layout.TextShadowOffsetY));
        }

        [Test]
        public void ShadowIsDrawnBeforeTheText() {
            VisualElement root = Render(1234, new InventoryLayout(), out _);
            Assert.That(root.IndexOf(root.Q<Label>(ShadowName)),
                Is.LessThan(root.IndexOf(root.Q<Label>(TextName))),
                "the text must sit on top of its own shadow");
        }

        [Test]
        public void ReadoutDoesNotTakePointerPicks() {
            // REQ_INV's action-34 ClickArea underneath is the button; a picking label would
            // shadow it exactly where the player aims.
            VisualElement root = Render(1234, new InventoryLayout(), out _);
            Assert.That(root.Q<Label>(TextName).pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(root.Q<Label>(ShadowName).pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        // ---- mechanism --------------------------------------------------------------------

        [Test]
        public void PositionComesFromTheLayoutNotFromLiterals() {
            var layout = new InventoryLayout {
                MoneyReadout = new LayoutHint {
                    Right = LayoutLength.Px(211f), Top = LayoutLength.Px(937f),
                },
                TextShadowOffsetX = 3f,
                TextShadowOffsetY = 7f,
            };
            VisualElement root = Render(1234, layout, out _);
            Assert.That(Px(root.Q<Label>(TextName).style.right), Is.EqualTo(211f));
            Assert.That(Px(root.Q<Label>(TextName).style.top), Is.EqualTo(937f));
            Assert.That(Px(root.Q<Label>(ShadowName).style.right), Is.EqualTo(208f));
            Assert.That(Px(root.Q<Label>(ShadowName).style.top), Is.EqualTo(944f));
        }

        [Test]
        public void AnOverrideCanDropTheReadout() {
            VisualElement root = Render(1234, new InventoryLayout { MoneyReadout = null }, out _);
            Assert.That(root.childCount, Is.Zero);
        }

        [Test]
        public void RenderReplacesRatherThanAccumulates() {
            var root = new VisualElement();
            var view = new MoneyReadoutView();
            view.Render(root, 1234, new InventoryLayout());
            view.Render(root, 50, new InventoryLayout());
            Assert.That(root.childCount, Is.EqualTo(2), "one text plus one shadow, not four");
            Assert.That(root.Q<Label>(TextName).text, Is.EqualTo("5s 0r"));
        }

        [Test]
        public void ClearRemovesTheReadout() {
            VisualElement root = Render(1234, new InventoryLayout(), out MoneyReadoutView view);
            view.Clear();
            Assert.That(root.childCount, Is.Zero);
        }
    }
}

namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced inventory's layout and interactions (spec 2026-10-13): the REQ chrome moves out
    /// and back, a drop finds a moved portrait from the sheet, and the touch rule and the verbs behave
    /// as designed. The rules themselves stay in the faithful code these call.
    /// </summary>
    public class EnhancedInventoryLayoutTests {
        private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly string[] Chrome =
            { "hotspot_2", "hotspot_3", "hotspot_4", "hotspot_32", "hotspot_34", "imagebutton_22", "imagebutton_1" };
        private static readonly Rect ItemBox = new(65f, 66f, 1470f, 726f);

        private GameObject _go;
        private PanelSettings _panelSettings;
        private VisualElement _root, _reqStage;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        private InventoryMenu Build(GameSession session = null) {
            _go = new GameObject("InventoryMenuUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            session ??= new GameSession();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());
            _root = document.rootVisualElement;
            _reqStage = new VisualElement { name = "BakStage", style = { position = Position.Absolute, width = 1600, height = 1200 } };
            _root.Add(_reqStage);
            float x = 70f;
            foreach (string n in Chrome) {
                _reqStage.Add(new VisualElement {
                    name = n,
                    style = { position = Position.Absolute, left = x, top = 888, width = 200, height = 200 },
                });
                x += 210f;
            }
            return menu;
        }

        private static void Layout(VisualElement root) =>
            root.panel.GetType().GetMethod("ValidateLayout", BindingFlags.Public | Any)?.Invoke(root.panel, null);

        private static T Get<T>(InventoryMenu m, string field) => (T)typeof(InventoryMenu).GetField(field, Any).GetValue(m);
        private static void Set(InventoryMenu m, string field, object v) => typeof(InventoryMenu).GetField(field, Any).SetValue(m, v);
        private static object Call(InventoryMenu m, string method, params object[] args) =>
            typeof(InventoryMenu).GetMethod(method, Any).Invoke(m, args);

        [Test]
        public void ApplyMovesTheChromeOutAndHidesTheReqStage_RevertPutsItBack() {
            Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());

            Assert.IsTrue(layout.Apply(_reqStage, ItemBox));

            VisualElement enhanced = _root.Q("enhanced-inv");
            foreach (string n in Chrome) {
                VisualElement e = _root.Q(n);
                Assert.IsTrue(enhanced.Contains(e), $"{n} is in the Enhanced layout");
                Assert.AreEqual(StyleKeyword.Null, e.style.left.keyword, $"{n} lost its inline REQ placement");
            }
            Assert.AreEqual(DisplayStyle.None, _reqStage.style.display.value, "the REQ stage is hidden");

            layout.Revert();

            Assert.IsNull(_root.Q("enhanced-inv"));
            foreach (string n in Chrome) {
                VisualElement e = _root.Q(n);
                Assert.AreSame(_reqStage, e.parent, $"{n} is back on the REQ stage");
                Assert.AreEqual(888f, e.style.top.value.value, $"{n} has its REQ placement back");
            }
            Assert.AreNotEqual(DisplayStyle.None, _reqStage.style.display.value);
        }

        [Test]
        public void AMissingReqElementKeepsTheFaithfulLayout() {
            Build();
            _root.Q("imagebutton_1").RemoveFromHierarchy();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("imagebutton_1"));
            Assert.IsFalse(layout.Apply(_reqStage, ItemBox));

            Assert.IsNull(_root.Q("enhanced-inv"), "nothing half-moved");
            Assert.AreSame(_reqStage, _root.Q("hotspot_2").parent);
        }

        /// <summary>An items box authored in percent (or missing) has no px rect to fit the sheet to:
        /// the faithful screen stays, rather than a sheet fitted to a box at the origin.</summary>
        [Test]
        public void AnItemBoxWithNoPxRectKeepsTheFaithfulLayout() {
            Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("px item box"));
            Assert.IsFalse(layout.Apply(_reqStage, Rect.zero));

            Assert.IsNull(_root.Q("enhanced-inv"), "nothing half-moved");
        }

        [Test]
        public void RevertAfterTheRootWasClearedThrowsNothing() {
            Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());
            layout.Apply(_reqStage, ItemBox);

            _root.Clear(); // what the REQ loader does when the screen goes away

            Assert.DoesNotThrow(layout.Revert);
            Assert.IsFalse(layout.Applied);
        }

        [Test]
        public void AfterTheReqRebuildsTheRootTheLayoutAppliesToTheNewElements() {
            Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());
            layout.Apply(_reqStage, ItemBox);
            VisualElement oldSheet = layout.Sheet;

            _root.Clear(); // the REQ loader tears down...
            _reqStage = new VisualElement { name = "BakStage" }; // ...and builds again
            _root.Add(_reqStage);
            foreach (string n in Chrome) _reqStage.Add(new VisualElement { name = n });

            Assert.IsTrue(layout.Apply(_reqStage, ItemBox));
            Assert.AreNotSame(oldSheet, layout.Sheet, "a fresh sheet, in the panel");
            Assert.IsNotNull(layout.Sheet.panel);
            Assert.IsTrue(_root.Q("enhanced-inv").Contains(_root.Q("hotspot_2")), "the new portrait was moved");
        }

        [Test]
        public void ADropFromTheSheetFindsAMovedPortrait() {
            InventoryMenu menu = Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());
            layout.Apply(_reqStage, ItemBox);
            Set(menu, "_enhanced", layout);
            Set(menu, "_stage", layout.Sheet);
            // No theme in this harness: give the moved portrait the size the theme would.
            VisualElement moved = _root.Q("hotspot_3");
            moved.style.width = 205;
            moved.style.height = 205;
            layout.Portrait(1).style.display = DisplayStyle.Flex; // no party here, so no name to show it
            Layout(_root);

            Rect owyn = _root.Q("hotspot_3").worldBound;
            Vector2 onSheet = layout.Sheet.WorldToLocal(owyn.center);

            Assert.AreEqual(1, (int)Call(menu, "PortraitUnder", onSheet),
                "the drop test follows the portrait into the column, through the sheet's scale");
        }

        [Test]
        public void AFingersDoubleTapNeverUsesWithTheEnhancedLayout_AMouseStillDoes() {
            InventoryMenu menu = Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());
            layout.Apply(_reqStage, ItemBox);
            Set(menu, "_enhanced", layout);
            var gesture = new BakAgain.UI.InputCore.DragGestureManipulator(layout.Sheet, 20f);
            Set(menu, "_gesture", gesture);
            PropertyInfo touch = typeof(BakAgain.UI.InputCore.DragGestureManipulator).GetProperty("LastPressWasTouch");

            touch.SetValue(gesture, true);
            Assert.IsFalse((bool)typeof(InventoryMenu).GetProperty("DoubleTapUses", Any).GetValue(menu));

            touch.SetValue(gesture, false);
            Assert.IsTrue((bool)typeof(InventoryMenu).GetProperty("DoubleTapUses", Any).GetValue(menu));
        }

        [TestCase(false, true, "Give")]
        [TestCase(false, false, "Take")]
        [TestCase(true, false, "Buy")]
        public void TheGiveVerbFollowsTheView(bool shopShelf, bool memberPack, string verb) {
            object v = typeof(InventoryMenu).GetMethod("VerbFor", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { shopShelf, memberPack });
            Assert.AreEqual(verb, v.ToString());
        }

        [Test]
        public void TheUseAndExitButtonsKeepTheirOriginalFacesInTheEnhancedBar() {
            Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());

            Assert.IsTrue(layout.Apply(_reqStage, ItemBox));

            foreach (string n in new[] { "imagebutton_22", "imagebutton_1" }) {
                VisualElement b = _root.Q(n);
                Assert.IsNull(b.Q(BakAgain.UI.StoneButton.DiscName), $"{n} carries no stone over its face");
                Assert.AreEqual(StyleKeyword.Null, b.style.unityBackgroundImageTintColor.keyword,
                    $"{n}'s original face is not tinted out");
            }
        }

        // The faithful third arm draws no face and lets the SCX's stone show through; the Enhanced
        // layout hides the SCX, so the original's own blank stone (the face a disabled button wears)
        // stands in for it there, and goes when a face returns. Not the HD disc: beside the
        // original Exit face it reads as a different material.
        [Test]
        public void TheUseButtonWithNoFaceWearsTheOriginalBlankStone_AFaceTakesItOff() {
            Build();
            var layout = new EnhancedInventoryLayout(_root, new GameSession());
            layout.Apply(_reqStage, ItemBox);
            var tex = new Texture2D(4, 4);
            Sprite blank = Sprite.Create(tex, new Rect(0, 0, 4, 4), Vector2.zero);
            layout.BlankStone = blank;
            VisualElement use = _root.Q("imagebutton_22");

            try {
                layout.ShowUseFace(false);
                VisualElement stone = use.Q(EnhancedInventoryLayout.BlankStoneName);
                Assert.IsNotNull(stone, "no face: the blank stone");
                Assert.AreEqual(blank, stone.style.backgroundImage.value.sprite);
                Assert.IsNull(use.Q(BakAgain.UI.StoneButton.DiscName), "not the HD disc");

                layout.ShowUseFace(true);
                Assert.IsNull(use.Q(EnhancedInventoryLayout.BlankStoneName), "a face: the original's own");
            } finally {
                Object.DestroyImmediate(blank);
                Object.DestroyImmediate(tex);
            }
        }
    }
}

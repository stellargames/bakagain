namespace BakAgain.Tests.UI.Combat {
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.UI.Combat.Enhanced;
    using GameData.Resources.Combat;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>Enhanced combat's quarrel picker (TASK-1043): one row of carried kinds and Exit, modal until a pick.</summary>
    public class EnhancedShootRowTests {
        private static int[] Carrying(params (int Kind, int Count)[] held) {
            var counts = new int[CombatMenuSlots.ActionIdByQuarrelKind.Length];
            foreach ((int kind, int count) in held) counts[kind] = count;
            return counts;
        }

        private static List<VisualElement> Buttons(VisualElement root) =>
            root.Query(className: EnhancedShootRow.ButtonClass).ToList();

        [Test]
        public void ShowsTheCarriedKindsInKindOrderWithTheirCounts_thenExit_andNoEmptyOrMoreStones() {
            var root = new VisualElement();
            var row = new EnhancedShootRow(_ => null);
            row.Show(root, Carrying((6, 4), (1, 12)));

            List<VisualElement> buttons = Buttons(root);
            CollectionAssert.AreEqual(
                new[] { CombatMenuSlots.ActionIdByQuarrelKind[1], CombatMenuSlots.ActionIdByQuarrelKind[6],
                    CombatMenuSlots.DistinctEnableGateActionId },
                buttons.Select(b => (int)b.userData));
            CollectionAssert.AreEqual(new[] { "12", "4" },
                buttons.Take(2).Select(b => b.Q<Label>(className: EnhancedShootRow.CountClass).text));
            Assert.IsNull(buttons[2].Q<Label>(className: EnhancedShootRow.CountClass), "Exit has no count");
        }

        [Test]
        public void ItIsModalUntilAPick_thenTheFieldTakesTheTargetClick() {
            var root = new VisualElement();
            var row = new EnhancedShootRow(_ => null);
            var pressed = new List<int>();
            row.Pressed += pressed.Add;
            row.Show(root, Carrying((0, 3), (2, 5)));

            VisualElement overlay = root.Q(EnhancedShootRow.OverlayName);
            Assert.AreEqual(PickingMode.Position, overlay.pickingMode, "nothing under it can be clicked before a pick");

            int kind2 = CombatMenuSlots.ActionIdByQuarrelKind[2];
            row.Press(kind2);
            CollectionAssert.AreEqual(new[] { kind2 }, pressed);
            Assert.AreEqual(PickingMode.Ignore, overlay.pickingMode, "after a pick the target is clicked on the field");
            Assert.IsTrue(Buttons(root).Single(b => (int)b.userData == kind2).ClassListContains(EnhancedShootRow.ChosenClass));

            row.Show(root, Carrying((0, 3)));
            Assert.AreEqual(1, root.Query(EnhancedShootRow.OverlayName).ToList().Count, "a reopen does not stack");
            Assert.AreEqual(PickingMode.Position, root.Q(EnhancedShootRow.OverlayName).pickingMode, "and is modal again");

            row.Hide();
            Assert.IsNull(root.Q(EnhancedShootRow.OverlayName));
        }
    }
}

namespace BakAgain.Tests.UI.Combat {
    using BakAgain.UI.Combat;
    using GameData.Resources.Combat;
    using NUnit.Framework;
    using UnityEngine;

    /// <summary>
    /// The quarrel picker's own decisions — which kinds claim cells, which page is showing, and what
    /// a press means.
    /// </summary>
    /// <remarks>
    /// <b>No <c>UserInterfaceLoader</c> and no SHOOT.DAT.</b> With no loader the menu skips its
    /// layout pass, which leaves exactly the part this fixture owns: the packing and the press
    /// routing. Where the buttons END UP is the layout's job and is asserted against the real
    /// resource by the screen tests.
    /// </remarks>
    public class ShootMenuTests {
        private ShootMenu _menu;
        private GameObject _host;

        /// <summary>Kind indices, so a test can say which ammunition it means.</summary>
        private static int[] Carrying(params (int Kind, int Count)[] held) {
            var counts = new int[CombatMenuSlots.ActionIdByQuarrelKind.Length];
            foreach ((int Kind, int Count) h in held) {
                counts[h.Kind] = h.Count;
            }
            return counts;
        }

        [SetUp]
        public void SetUp() {
            BakAgain.Core.LogManager.Initialize();
            _host = new GameObject("ShootMenuTestHost");
            _menu = _host.AddComponent<ShootMenu>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        [Test]
        public void CarriedKindsClaimTheFirstCells_soLateKindsLandOnPageOne() {
            // Kinds 6 and 7 are the LAST two, and a table split down the middle would put them on
            // page two. The original repacks, so an archer carrying only these has them first.
            _menu.Open(Carrying((6, 4), (7, 2)));

            Assert.AreEqual(CombatMenuSlots.ActionIdByQuarrelKind[6], _menu.Cells[0]);
            Assert.AreEqual(CombatMenuSlots.ActionIdByQuarrelKind[7], _menu.Cells[1]);
            Assert.AreEqual(-1, _menu.Cells[2], "nothing else is carried, so the cell stays empty");
            Assert.AreEqual(CombatMenuSlots.FirstPage,
                CombatMenuSlots.PageOfSlot(0), "and cell 0 is on page one");
        }

        [Test]
        public void AnEmptyQuiverClaimsNothing() {
            _menu.Open(Carrying());

            foreach (int cell in _menu.Cells) {
                Assert.AreEqual(-1, cell);
            }
        }

        [Test]
        public void MoreFlipsThePage_andIsNotAChoice() {
            var chosen = 0;
            _menu.QuarrelChosen += (kind, id) => chosen++;
            _menu.Open(Carrying((0, 1)));

            _menu.PrimaryAction(CombatMenuSlots.PageFlipActionId);
            Assert.AreEqual(CombatMenuSlots.SecondPage, _menu.Page);

            _menu.PrimaryAction(CombatMenuSlots.PageFlipActionId);
            Assert.AreEqual(CombatMenuSlots.FirstPage, _menu.Page, "flipping is a toggle, not a walk");
            Assert.AreEqual(0, chosen, "MORE is a menu control, not a quarrel");
        }

        [Test]
        public void ReopeningAlwaysStartsOnPageOne() {
            // The cells are repacked for whoever is acting, so a page the previous character had
            // flipped to would not mean the same thing carried over.
            _menu.Open(Carrying((0, 1)));
            _menu.PrimaryAction(CombatMenuSlots.PageFlipActionId);
            Assert.AreEqual(CombatMenuSlots.SecondPage, _menu.Page);

            _menu.Open(Carrying((0, 1)));

            Assert.AreEqual(CombatMenuSlots.FirstPage, _menu.Page);
        }

        [Test]
        public void BackCancels_andIsNotAQuarrel() {
            // Id 33 is Retreat on the melee menu and a cancel here — the split BacksOutOfShootMenu
            // exists for.
            var cancelled = 0;
            var chosen = 0;
            _menu.Cancelled += () => cancelled++;
            _menu.QuarrelChosen += (kind, id) => chosen++;
            _menu.Open(Carrying((0, 1)));

            _menu.PrimaryAction(CombatMenuSlots.DistinctEnableGateActionId);

            Assert.AreEqual(1, cancelled);
            Assert.AreEqual(0, chosen);
        }

        [Test]
        public void AQuarrelButtonReportsItsKind_notItsId() {
            // Kind 5's id is 8 and kind 7's is 7: the ids are not in kind order, so reporting the id
            // as if it were the kind would fire the wrong ammunition.
            int reportedKind = -1;
            int reportedId = -1;
            _menu.QuarrelChosen += (kind, id) => { reportedKind = kind; reportedId = id; };
            _menu.Open(Carrying((5, 3)));

            _menu.PrimaryAction(CombatMenuSlots.ActionIdByQuarrelKind[5]);

            Assert.AreEqual(5, reportedKind);
            Assert.AreEqual(8, reportedId);
        }

        [Test]
        public void RightClickDescribes_andQuarrelHelpIsAddressedByMenuPosition() {
            // Help records run consecutively from the menu's first position, and the quarrel ids
            // occupy the first eight of those positions in kind order.
            int record = -1;
            _menu.HelpRequested += r => record = r;

            _menu.SecondaryAction(CombatMenuSlots.ActionIdByQuarrelKind[6]);

            Assert.AreEqual(CombatActionDispatch.HelpRecordBase + 6, record);
        }
    }
}

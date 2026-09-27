namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using GameData.Resources.Shop;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The mender charges on the QUOTE's own answer, not on a gold test of our own.
    /// </summary>
    /// <remarks>
    /// <c>MODALSCR.C</c> is <c>dialog_play_record(0x1b7763, 0); if (gstate_event_read(0x104) != 0)
    /// { gold -= cost; condition = 100; }</c> — no affordability test in C. Dialog 1800035's accept
    /// branch is flag 260 (= 0x104) and leads to an entry gated on Var 3, whose default arm is
    /// "I seem to be short" and <b>clears flag 260</b>.
    ///
    /// <para>Measured side by side at Highcastle on 2026-09-13 with a 23% Standard Kingdom Armor and
    /// 143 royals against a 173-royal quote: the original plays the mender's refusal, the port
    /// returned in silence because it asked <c>ShowConfirmById</c> — which stops at the first entry —
    /// and then re-derived the rule with its own <c>PartyGold &lt; price</c> guard.</para>
    /// </remarks>
    public class MenderQuoteTests {
        private const int ArmourId = 48;
        private const int Price = 450;

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        public IEnumerator AQuoteTheDialogRefusesLeavesTheGoldAndTheDamageAlone() {
            // 143 royals against a 450 * 50 * (100-23) / 10000 = 173 quote, the Highcastle numbers.
            (InventoryMenu menu, GameSession session, RecordingDialogs dialogs) = Build(gold: 143);
            // What the refusal leaf does on its way past, and the whole reason no C# guard is needed.
            dialogs.OnShow = id => session.SetGlobalFlag(ShopRepair.AgreedEventKey, false);

            yield return Repair(menu);

            Assert.AreEqual(ShopRepair.QuoteDialogId, dialogs.LastShownById,
                "the quote has to be RUN, not merely asked as a confirm — its gate lives past the "
                + "first entry");
            Assert.AreEqual(143, session.PartyGold, "a refused quote costs nothing");
            Assert.AreEqual(23, ItemUnderTest(menu).Variable, "and mends nothing");
        }

        [UnityTest]
        public IEnumerator AQuoteTheDialogAgreesToChargesAndMends() {
            (InventoryMenu menu, GameSession session, RecordingDialogs dialogs) = Build(gold: 500);
            // Flag 260 as the player's Accept leaves it when the Var 3 gate passes.
            dialogs.OnShow = id => session.SetGlobalFlag(ShopRepair.AgreedEventKey, true);

            yield return Repair(menu);

            Assert.AreEqual(500 - 173, session.PartyGold, "charged the quoted price");
            Assert.AreEqual(InventoryQuery.PristineCondition, ItemUnderTest(menu).Variable);
        }

        private static RuntimeItem ItemUnderTest(InventoryMenu menu) =>
            ((RuntimeContainer)Field("_displayed").GetValue(menu)).Items[0];

        private static FieldInfo Field(string name) =>
            typeof(InventoryMenu).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private static IEnumerator Repair(InventoryMenu menu) {
            typeof(InventoryMenu)
                .GetMethod("RepairAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(menu, new object[] { 0 });
            // Every collaborator completes synchronously, but RepairAsync is a UniTaskVoid: give it
            // frames rather than assuming which await resumes where.
            for (int frame = 0; frame < 5; frame++) { yield return null; }
        }

        private (InventoryMenu, GameSession, RecordingDialogs) Build(int gold) {
            _go = new GameObject("MenderUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>();
            var session = new GameSession { PartyGold = gold };
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("armour") {
                    Number = ArmourId, Name = "Standard Kingdom Armor", InventorySlots = 1,
                    ObjectType = ObjectType.Armor, Price = Price,
                },
            }));
            var dialogs = new RecordingDialogs();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                dialogs);

            var displayed = new RuntimeContainer {
                Capacity = 24, ContainerType = SaveGameContainerType.Inventory,
            };
            displayed.Items.Add(new RuntimeItem(ArmourId, 23, 0));
            Field("_displayed").SetValue(menu, displayed);
            // The mender IS the mode; SetRepair would also need a hydrated party to pick a portrait.
            Field("_mender").SetValue(menu, Mender());
            // RenderCurrent on the success path wants the real REQ_INV build; drop the document so it
            // returns at its first guard and the test measures gold and condition, not chrome.
            Field("_document").SetValue(menu, null);
            return (menu, session, dialogs);
        }

        /// <summary>Highcastle's Battleworks: all three categories, markup 50.</summary>
        private static SaveGameContainerShopData Mender() =>
            new SaveGameContainerShopData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                repairCategories: 7, repairCostMarkup: 50, shopCategories: default);

        private sealed class RecordingDialogs : NoOpDialogs {
            public int LastShownById = -1;
            public System.Action<int> OnShow;

            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                LastShownById = id;
                OnShow?.Invoke(id);
                return UniTask.FromResult(BakAgain.UI.DialogManager.NoDialogResult);
            }
        }
    }
}

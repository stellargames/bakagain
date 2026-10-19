namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// A ration handed to a starving member through the inventory screen is eaten on the spot
    /// (CMBINV.C:799-805, the receiver): the screen's own transfer path, with the pack mapped back to
    /// its member, not only <c>InventoryAcquire.FeedIfStarving</c> in isolation.
    /// </summary>
    public class InventoryFeedsTheStarvingTests {
        [OneTimeSetUp]
        public void RequireShippedGameData() => ShippedGameData.RequireOrIgnore();

        private GameObject _go;
        private PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        private static RuntimeContainer Pack() =>
            new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.Inventory };

        [Test]
        public void GivingARationToAStarvingMember_FeedsThem() {
            var session = new GameSession();
            session.SetObjectInfo(new ObjectInfoSet("objects", new List<ObjectInfo> {
                new ObjectInfo("rations") {
                    Number = UpkeepEngine.RationsObjectId, ObjectType = ObjectType.Food, InventorySlots = 1, MaxAmount = 99,
                },
            }));
            session.SetActiveParty(2, new byte[] { 0, 1 });
            RuntimeContainer giver = Pack(), hungry = Pack();
            giver.Items.Add(new RuntimeItem(UpkeepEngine.RationsObjectId, 1, 0));
            session.SetActorInventoryForTest(0, giver);
            session.SetActorInventoryForTest(1, hungry);
            var starving = new ActorConditions();
            starving[ActorCondition.Starving] = 40;
            session.SetActorConditionsForTest(0, new ActorConditions());
            session.SetActorConditionsForTest(1, starving);

            _go = new GameObject("InventoryFeedUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(InventoryMenu).GetField("_displayed", any).SetValue(menu, giver);
            typeof(InventoryMenu).GetField("_stage", any).SetValue(menu, document.rootVisualElement);

            typeof(InventoryMenu).GetMethod("RunTransfer", any).Invoke(menu, new object[] { 0, hungry, false });

            Assert.IsFalse(session.ConditionsOf(1).Has(ActorCondition.Starving), "the ration was eaten on arrival");
            Assert.AreEqual(0, hungry.Items.Count, "the one ration given is the one eaten");
        }
    }
}

namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using GameData;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// An item that its own use spends still makes its use sound (forum p=50: the scroll, the Herbal
    /// Pack, the last Aventurine). The original plays <c>wUse_sfx</c> from the record it already holds
    /// and only then removes the item (ITEMUSE.C:491-494); the port looked the sound up in the slot
    /// after the use, where the next item had moved up.
    /// </summary>
    public class ItemUseCueTests {
        private const int LightBowstringId = 77, LightCrossbowId = 30, BowstringCue = 22;

        private readonly List<int> _cues = new();
        private GameObject _go;
        private PanelSettings _panelSettings;

        [SetUp]
        public void SetUp() {
            _cues.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _cues.Add;
        }

        [TearDown]
        public void TearDown() {
            BakAgain.Audio.MenuSoundService.CueRequested -= _cues.Add;
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [Test]
        public void ABowstringSpentOnACrossbowStillMakesItsSound() {
            var session = new GameSession();
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("bowstring") {
                    Number = LightBowstringId, Name = "Light Bowstring", InventorySlots = 1,
                    ObjectType = ObjectType.BowString, Flags = ObjectFlags.ConsumedOnUse,
                    SoundId = BowstringCue,
                },
                new ObjectInfo("crossbow") {
                    Number = LightCrossbowId, Name = "Light Crossbow", InventorySlots = 2,
                    ObjectType = ObjectType.Crossbow,
                },
            }));
            var pack = new RuntimeContainer { Capacity = 20, ContainerType = SaveGameContainerType.Inventory };
            pack.Items.Add(new RuntimeItem(LightBowstringId, 1, 0));
            pack.Items.Add(new RuntimeItem(LightCrossbowId, 50, 0));

            _go = new GameObject("ItemUseCueUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = BakAgain.Tests.TestSupport.TestPanelSettings.Create();
            document.panelSettings = _panelSettings;
            var menu = _go.AddComponent<InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());
            const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(InventoryMenu).GetField("_displayed", any).SetValue(menu, pack);
            VisualElement stage = document.rootVisualElement;
            typeof(InventoryMenu).GetField("_stage", any).SetValue(menu, stage);
            // The crossbow's cell is where the bowstring is dropped.
            stage.Add(new VisualElement {
                name = "item_slot_1",
                style = { position = Position.Absolute, left = 0, top = 0, width = 2000, height = 2000 },
            });
            stage.panel.GetType().GetMethod("ValidateLayout",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(stage.panel, null);

            bool used = (bool)typeof(InventoryMenu).GetMethod("UseOnItemDrop", any)
                .Invoke(menu, new object[] { 0, Vector2.zero });

            Assert.IsTrue(used, "control: the drop is a use on the crossbow");
            Assert.AreEqual(1, pack.Items.Count, "control: the bowstring was spent");
            Assert.That(_cues, Is.EqualTo(new[] { BowstringCue }),
                "the spent bowstring's own sound, not the sound of whatever moved into its slot");
        }
    }
}

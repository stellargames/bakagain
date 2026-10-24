namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using BakAgain.Core;
    using BakAgain.UI.Inventory;
    using Cysharp.Threading.Tasks;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Inventory;
    using GameData.Resources.Object;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// An accepted, paid blessing sounds 62_BLESS: <c>audio_sfx_play_n_times(0x3e, 0, 1)</c> right after
    /// the gold and the bless bits (MODALSCR.C:474-478). A declined offer is silent.
    /// </summary>
    public class BlessingCueTests {
        private const int SwordId = 7;
        private const int BlessCue = 62;

        private readonly List<int> _cues = new();
        private GameObject _go;
        private PanelSettings _panelSettings;

        [SetUp]
        public void SetUp() {
            _cues.Clear();
            _shownWithItem.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _cues.Add;
        }

        [TearDown]
        public void TearDown() {
            BakAgain.Audio.MenuSoundService.CueRequested -= _cues.Add;
            if (_go != null) { Object.DestroyImmediate(_go); }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        public IEnumerator AnAcceptedBlessingPlaysTheBlessSound() {
            (InventoryMenu menu, GameSession session) = Build(accept: true);

            yield return Bless(menu, session);

            Assert.That(session.PartyGold, Is.LessThan(5000), "the blessing was paid for");
            Assert.That(_cues, Is.EqualTo(new[] { BlessCue }));
        }

        [UnityTest]
        public IEnumerator ADeclinedBlessingIsSilent() {
            (InventoryMenu menu, GameSession session) = Build(accept: false);

            yield return Bless(menu, session);

            Assert.That(session.PartyGold, Is.EqualTo(5000), "nothing was charged");
            Assert.That(_cues, Is.Empty);
        }

        /// <summary>
        /// The already-blessed page names the item: the original sets <c>nEvtArgItemId</c> before
        /// <c>dialog_play_record(0x13d66f)</c> (MODALSCR.C:458-462), so "@ presented the @1 to a
        /// priest" reads "the Moredhel Lamprey". The port named it only after that page (TASK-990).
        /// </summary>
        [UnityTest]
        public IEnumerator TheAlreadyBlessedPageNamesTheItem() {
            (InventoryMenu menu, GameSession session) = Build(accept: false, alreadyBlessed: true);

            yield return Bless(menu, session);

            Assert.That(_shownWithItem, Is.EqualTo(new[] {
                ((int)TempleBlessing.AlreadyBlessedDialogId, SwordId),
                ((int)TempleBlessing.PriceOfferDialogId, SwordId),
            }));
        }

        private static IEnumerator Bless(InventoryMenu menu, GameSession session) {
            ObjectInfo sword = session.ObjectInfo.GetById(SwordId);
            typeof(InventoryMenu).GetMethod("BlessAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(menu, new object[] { 0, sword });
            for (int frame = 0; frame < 5; frame++) { yield return null; }
        }

        private readonly List<(int, int)> _shownWithItem = new();

        private (InventoryMenu, GameSession) Build(bool accept, bool alreadyBlessed = false) {
            _go = new GameObject("BlessingUnderTest");
            var document = _go.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;

            var menu = _go.AddComponent<InventoryMenu>();
            var session = new GameSession { PartyGold = 5000 };
            session.SetObjectInfo(new ObjectInfoSet("O", new List<ObjectInfo> {
                new ObjectInfo("sword") {
                    Number = SwordId, Name = "Sword", InventorySlots = 1,
                    ObjectType = ObjectType.Sword, Price = 400,
                },
            }));
            var dialogs = new AnsweringDialogs(id => {
                _shownWithItem.Add((id, session.DialogKeyObjectId));
                if (id == (int)TempleBlessing.PriceOfferDialogId) {
                    session.SetGlobalFlag(TempleBlessing.AcceptedFlag, accept);
                }
            });
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                dialogs);

            var displayed = new RuntimeContainer { Capacity = 24, ContainerType = SaveGameContainerType.Inventory };
            displayed.Items.Add(new RuntimeItem(SwordId, 100,
                alreadyBlessed ? (ushort)TempleBlessing.FirstTierFlag : (ushort)0));
            Field("_displayed").SetValue(menu, displayed);
            Field("_blessing").SetValue(menu, new SaveGameContainerShopData(1, 25, 60, 3, 65, 0, 0, 0, 0, 0, 0, 0,
                0, 0, (ShopItemCategories)0));
            Field("_document").SetValue(menu, null);   // RenderCurrent returns at its first guard
            return (menu, session);
        }

        private static FieldInfo Field(string name) =>
            typeof(InventoryMenu).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private sealed class AnsweringDialogs : NoOpDialogs {
            private readonly System.Action<int> _onShow;
            public AnsweringDialogs(System.Action<int> onShow) => _onShow = onShow;

            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                _onShow(id);
                return UniTask.FromResult(BakAgain.UI.DialogManager.NoDialogResult);
            }

            public override UniTask<bool> ShowConfirmById(int id, System.Threading.CancellationToken ct = default) {
                _onShow(id);
                return UniTask.FromResult(true);   // "Do you wish for me to do so?" -- Yes
            }
        }
    }
}

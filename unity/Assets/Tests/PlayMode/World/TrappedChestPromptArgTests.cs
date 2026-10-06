namespace BakAgain.Tests.PlayMode.World {
    using System;
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.Tests.PlayMode.UI.Inventory;
    using BakAgain.World.Interaction;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    /// <summary>
    /// The trapped chest's prompt is asked with the dialog arg (Var 0, global 30000) at 0.
    /// </summary>
    /// <remarks>
    /// Dialog 79's root picks its wording by Var 0 = 0..3 (chest, door, building, ladder), and
    /// WCURSOR.C:574 sets <c>g_gameState.nEvtArgCount = 0</c> before playing it. The port left
    /// whatever was there — 78 on t117/SAVE02 — so no branch matched, nothing was shown, the empty
    /// walk answered 0 = Yes, and the chest went off without asking. TASK-117.
    /// </remarks>
    public class TrappedChestPromptArgTests {
        private sealed class RecordingDialogs : NoOpDialogs {
            private readonly GameSession _session;
            public int? ArgAtPrompt;
            public RecordingDialogs(GameSession session) => _session = session;

            public override UniTask<bool> ShowConfirmById(int id, System.Threading.CancellationToken ct = default) {
                if (id == ChestTrap.OpenStillTrappedDialog) {
                    ArgAtPrompt = _session.GetGlobalValue(30000);
                }
                return UniTask.FromResult(false);
            }
        }

        private sealed class YesDialogs : NoOpDialogs {
            public override UniTask<bool> ShowConfirmById(int id, System.Threading.CancellationToken ct = default) =>
                UniTask.FromResult(true);
        }

        private sealed class CountingNavigator : BakAgain.UI.Navigation.IScreenNavigator {
            public int Pushes;
            public BakAgain.UI.Navigation.IScreen Current => null;
            public UniTask ResetTo(BakAgain.UI.Navigation.IScreen root) => UniTask.CompletedTask;
            public UniTask Push(BakAgain.UI.Navigation.IScreen screen) { Pushes++; return UniTask.CompletedTask; }
            public UniTask PushAndWaitAsync(BakAgain.UI.Navigation.IScreen screen) { Pushes++; return UniTask.CompletedTask; }
            public UniTask Pop() => UniTask.CompletedTask;
            public UniTask Replace(BakAgain.UI.Navigation.IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        private UnityEngine.GameObject _menuGo;
        private UnityEngine.UIElements.PanelSettings _panelSettings;

        [TearDown]
        public void TearDown() {
            if (_menuGo != null) { UnityEngine.Object.DestroyImmediate(_menuGo); }
            if (_panelSettings != null) { UnityEngine.Object.DestroyImmediate(_panelSettings); }
        }

        private CountingNavigator LootScreensAfterYes(byte trapDamage) {
            var session = new GameSession();
            var chest = new SaveGameContainerData(
                new SaveGameContainerLocationData(2, 0, 10, 162, 1096693, 1039832, 0),
                SaveGameContainerType.Chest, 0, 4, 0, Array.Empty<SaveGameInventoryItemData>(),
                new SaveGameContainerLockData(0, 0, 0, trapDamage), null, null, null, null, null);
            session.SetZoneContainersForTest(new SaveGameZoneContainerStateData(new[] {
                new SaveGameZoneContainerEntryData(2, 0, new[] { chest }),
            }), chapter: 1);
            session.CurrentZone = 2;

            _menuGo = new UnityEngine.GameObject("InventoryMenuUnderTest");
            _panelSettings = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            _menuGo.AddComponent<UnityEngine.UIElements.UIDocument>().panelSettings = _panelSettings;
            var menu = _menuGo.AddComponent<BakAgain.UI.Inventory.InventoryMenu>();
            menu.Construct(session, new NoOpResources(), new NoOpNavigator(),
                new BakAgain.Core.Services.DialogExecutor(
                    new Microsoft.Extensions.Logging.Abstractions.NullLogger<BakAgain.Core.Services.DialogExecutor>(),
                    session, new BakAgain.Core.Services.GameClock(session)),
                new NoOpDialogs());

            var navigator = new CountingNavigator();
            var handler = new ContainerInteractionHandler(session, new YesDialogs(), menu, navigator,
                null, null, (_, _) => UniTask.CompletedTask);
            handler.TryTrappedAsync(chest, null, 2, 1096693, 1039832,
                GameData.Resources.World.WorldEntityType.Container).Forget();
            return navigator;
        }

        /// <summary>
        /// A sprung trap does NOT open the chest. WCURSOR.C: Yes on 0x4f sets bDenied, the
        /// explosion arm plays 0xc0 and spends the trap, and bHandled stays 0, so
        /// cmbinv_inventory_screen_run (:624) is skipped. Measured in the original on t117/SAVE03:
        /// after "Something clicked..." it is back on the travel view.
        /// </summary>
        [UnityTest]
        public IEnumerator ASprungTrapDoesNotOpenTheLootScreen() {
            var nav = LootScreensAfterYes(trapDamage: 40);
            yield return null;
            yield return null;
            Assert.AreEqual(0, nav.Pushes, "the explosion ends the click; the chest stays shut");
        }

        /// <summary>Control: an ex-trapped chest's Yes (0x13d -> apply_bonus) does open it.</summary>
        [UnityTest]
        public IEnumerator AnExTrappedChestStillOpensOnYes() {
            var nav = LootScreensAfterYes(trapDamage: 0);
            yield return null;
            yield return null;
            Assert.AreEqual(1, nav.Pushes);
        }

        [UnityTest]
        public IEnumerator TheOpenPromptIsAskedWithVarZeroCleared() {
            var session = new GameSession();
            session.SetGlobalValue(30000, 78);
            var dialogs = new RecordingDialogs(session);
            var handler = new ContainerInteractionHandler(session, dialogs, null, null, null, null,
                (_, _) => UniTask.CompletedTask);
            var chest = new SaveGameContainerData(
                new SaveGameContainerLocationData(1, 1, 9, 195, 670423, 1059778, 0),
                SaveGameContainerType.Chest, 0, 4, 0, Array.Empty<SaveGameInventoryItemData>(),
                new SaveGameContainerLockData(0, 0, 0, 40), null, null, null, null, null);

            yield return handler.TryTrappedAsync(chest, null, 1, 0, 0,
                GameData.Resources.World.WorldEntityType.Container).ToCoroutine();

            Assert.AreEqual(0, dialogs.ArgAtPrompt, "dialog 79 is played with nEvtArgCount = 0");
        }
    }
}

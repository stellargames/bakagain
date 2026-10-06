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

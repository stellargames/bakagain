namespace BakAgain.Tests.PlayMode.UI.Inventory {
    using System.Collections;
    using System.Collections.Generic;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Data;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    /// <summary>
    /// The bookmark button in MAINMENU.C:1450-1499 order (TASK-752). Measured on c740.G01/SAVE01: the
    /// original asked "Left click to save Bookmark file. Right click to cancel."; the port wrote the
    /// file and then said "The Bookmark could not be saved!".
    /// </summary>
    public class BookmarkFlowTests {
        private sealed class Recording : NoOpDialogs {
            public readonly List<string> Calls = new();
            public bool Accept;
            public override UniTask<int> ShowById(int id, System.Threading.CancellationToken ct = default) {
                Calls.Add("show " + id);
                return UniTask.FromResult(0);
            }
            public override UniTask<bool> ShowAcceptOrCancelById(int id, System.Threading.CancellationToken ct = default) {
                Calls.Add("ask " + id);
                return UniTask.FromResult(Accept);
            }
            public override UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
                System.Threading.CancellationToken ct = default) {
                Calls.Add("display " + id);
                return UniTask.FromResult<GameData.Resources.Dialog.DialogPlay>(null);
            }
        }

        [UnityTest]
        public IEnumerator AGoodWriteIsSilent_AfterTheVerifyPrompt() => UniTask.ToCoroutine(async () => {
            var d = new Recording { Accept = true };
            bool wrote = false;
            await BakAgain.UI.InGame.InGameScreen.SaveBookmarkFlow(d, true, () => { wrote = true; return UniTask.FromResult(true); });
            Assert.IsTrue(wrote);
            CollectionAssert.AreEqual(new[] { "ask " + 0x14c, "display " + 0x14e }, d.Calls);
        });

        [UnityTest]
        public IEnumerator OnlyAFailedWriteSaysSo() => UniTask.ToCoroutine(async () => {
            var d = new Recording { Accept = true };
            await BakAgain.UI.InGame.InGameScreen.SaveBookmarkFlow(d, true, () => UniTask.FromResult(false));
            CollectionAssert.AreEqual(new[] { "ask " + 0x14c, "display " + 0x14e, "show " + 0x90 }, d.Calls);
        });

        [UnityTest]
        public IEnumerator DecliningTheVerifyPromptWritesNothing() => UniTask.ToCoroutine(async () => {
            var d = new Recording { Accept = false };
            bool wrote = false;
            await BakAgain.UI.InGame.InGameScreen.SaveBookmarkFlow(d, true, () => { wrote = true; return UniTask.FromResult(true); });
            Assert.IsFalse(wrote);
            CollectionAssert.AreEqual(new[] { "ask " + 0x14c, "show " + 0x14d }, d.Calls);
        });

        [UnityTest]
        public IEnumerator NoDirectoryRefusesBeforeAsking() => UniTask.ToCoroutine(async () => {
            var d = new Recording { Accept = true };
            await BakAgain.UI.InGame.InGameScreen.SaveBookmarkFlow(d, false, () => UniTask.FromResult(true));
            CollectionAssert.AreEqual(new[] { "show " + BookmarkSave.NoSlotDialog }, d.Calls);
        });
    }
}

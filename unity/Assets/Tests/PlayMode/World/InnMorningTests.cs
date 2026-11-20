namespace BakAgain.Tests.PlayMode.World {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.World.Scenes;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    /// <summary>The inn's morning: what is on screen when the panel fades out.</summary>
    public class InnMorningTests {
        /// <summary>Accepts the first offer and declines any after it; counts clears.</summary>
        private sealed class AcceptOnceDialogs : EndChapterHotspotTests.ClearCountingDialogs, BakAgain.UI.IDialogManager {
            private int _offers;
            public new UniTask<int> ShowById(int id, System.Threading.CancellationToken cancellationToken = default) =>
                UniTask.FromResult(_offers++ == 0 ? 0 : 1);
        }

        [UnityTest]
        public IEnumerator AWholePartysMorningFadesOutWithTheSleepPageStillUp() =>
            UniTask.ToCoroutine(async () => {
                // TASK-1111 row 7: the night redraws only the top rect (MODALSCR.C:777-789), so "In
                // moments, they were all fast asleep" is still drawn when a whole party leaves and the
                // panel fades (:815-816). The port cleared it first.
                var session = new GameSession();
                var clock = new BakAgain.Core.Services.GameClock(session);
                var upkeep = new BakAgain.Core.Services.PartyUpkeepService(session, clock,
                    NullLogger<BakAgain.Core.Services.PartyUpkeepService>.Instance);
                var dialogs = new AcceptOnceDialogs();
                var inn = new InnService(session, clock, upkeep, dialogs, null);
                int clearsAtFade = -1;

                await inn.RunAsync(
                    new GameData.Resources.Data.SaveGameContainerShopData(
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 1, 0, 0, (GameData.ShopItemCategories)0),
                    closing: () => {
                        clearsAtFade = dialogs.Cleared;
                        return UniTask.CompletedTask;
                    });

                Assert.AreEqual(0, clearsAtFade, "the sleep page fades out with the panel");
                Assert.GreaterOrEqual(dialogs.Cleared, 1, "and is gone once the panel is down");
            });
    }
}

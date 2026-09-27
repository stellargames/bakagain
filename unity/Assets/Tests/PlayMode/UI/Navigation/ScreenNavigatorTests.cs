namespace BakAgain.Tests.PlayMode.UI.Navigation {
    using System.Collections;
    using System.Collections.Generic;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    public class ScreenNavigatorTests {
        private sealed class RecordingScreen : IScreen {
            private readonly List<string> _log;
            private readonly string _name;
            public bool Shown;
            public RecordingScreen(string name, List<string> log) { _name = name; _log = log; }
            public UniTask ShowAsync() { Shown = true; _log.Add("show:" + _name); return UniTask.CompletedTask; }
            public UniTask HideAsync() { Shown = false; _log.Add("hide:" + _name); return UniTask.CompletedTask; }
        }

        [UnityTest]
        public IEnumerator Push_HidesCurrentBeforeShowingNew_OnlyTopEnabled() => UniTask.ToCoroutine(async () => {
            var log = new List<string>();
            var nav = new ScreenNavigator();
            var travel = new RecordingScreen("travel", log);
            var menu = new RecordingScreen("menu", log);
            var prefs = new RecordingScreen("prefs", log);

            await nav.ResetTo(travel);
            await nav.Push(menu);
            log.Clear();
            await nav.Push(prefs);

            // Only prefs (the top) is shown; menu was hidden first.
            Assert.IsTrue(prefs.Shown && !menu.Shown && !travel.Shown, "only the top is enabled. log=" + string.Join(",", log));
            int hideMenu = log.IndexOf("hide:menu");
            int showPrefs = log.IndexOf("show:prefs");
            Assert.GreaterOrEqual(hideMenu, 0, "menu hidden. log=" + string.Join(",", log));
            Assert.Less(hideMenu, showPrefs, "current must hide before new shows (dip). log=" + string.Join(",", log));
        });

        [UnityTest]
        public IEnumerator PoppingTheBaseScreenWarns() => UniTask.ToCoroutine(async () => {
            // TASK-569's defect: a pop that removes the screen ResetTo installed.
            var nav = new ScreenNavigator();
            await nav.ResetTo(new RecordingScreen("travel", new List<string>()));
            LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("Pop emptied the stack"));
            await nav.Pop();
        });

        [UnityTest]
        public IEnumerator PoppingYourOwnScreenOffAnEmptyStackIsSilent() => UniTask.ToCoroutine(async () => {
            // TASK-635: the chapter change clears the HUD and the cutscene player then pushes and
            // pops its own screen. Emptying the stack that way is the caller finishing.
            var warnings = 0;
            void Count(string message, string trace, UnityEngine.LogType type) {
                if (message.Contains("Pop emptied the stack")) {
                    warnings++;
                }
            }
            UnityEngine.Application.logMessageReceived += Count;
            try {
                var nav = new ScreenNavigator();
                await nav.ResetTo(new RecordingScreen("travel", new List<string>()));
                await nav.Clear();
                await nav.Push(new RecordingScreen("cutscene", new List<string>()));
                await nav.Pop();
                Assert.AreEqual(0, warnings, "popping your own pushed screen is not TASK-569");

                // Control: the same counter does see the real defect.
                await nav.ResetTo(new RecordingScreen("travel", new List<string>()));
                LogAssert.Expect(UnityEngine.LogType.Warning,
                    new System.Text.RegularExpressions.Regex("Pop emptied the stack"));
                await nav.Pop();
                Assert.AreEqual(1, warnings, "and the base pop is still reported");
            } finally {
                UnityEngine.Application.logMessageReceived -= Count;
            }
        });

        [UnityTest]
        public IEnumerator Pop_ReShowsPreviousTop() => UniTask.ToCoroutine(async () => {
            var log = new List<string>();
            var nav = new ScreenNavigator();
            var travel = new RecordingScreen("travel", log);
            var menu = new RecordingScreen("menu", log);
            var prefs = new RecordingScreen("prefs", log);
            await nav.ResetTo(travel);
            await nav.Push(menu);
            await nav.Push(prefs);
            log.Clear();
            await nav.Pop();
            Assert.IsTrue(menu.Shown && !prefs.Shown && !travel.Shown, "menu re-shown, prefs hidden. log=" + string.Join(",", log));
            int hidePrefs = log.IndexOf("hide:prefs");
            int showMenu = log.IndexOf("show:menu");
            Assert.Less(hidePrefs, showMenu, "outgoing hides before incoming shows. log=" + string.Join(",", log));
        });

        [UnityTest]
        public IEnumerator ResetTo_HidesEverythingAndShowsRoot() => UniTask.ToCoroutine(async () => {
            var log = new List<string>();
            var nav = new ScreenNavigator();
            var travel = new RecordingScreen("travel", log);
            var menu = new RecordingScreen("menu", log);
            var main = new RecordingScreen("main", log);
            await nav.ResetTo(travel);
            await nav.Push(menu);
            await nav.ResetTo(main);
            Assert.IsTrue(main.Shown && !menu.Shown && !travel.Shown, "only main shown. log=" + string.Join(",", log));
        });
        /// <summary>
        /// TWO PUSHES OF ONE SINGLETON, TWO POPS, AND THE STACK IS EMPTY. (TASK-569, candidate B)
        /// </summary>
        /// <remarks>
        /// <b>This pins today's behaviour; it does not endorse it.</b> <c>AddTop</c> treats a
        /// re-push of a screen already on the stack as move-to-top — it removes the existing entry
        /// and adds one, so the DEPTH does not change — while each close still pops one. Two
        /// independent openers of the same singleton therefore cost the screen underneath them.
        ///
        /// <para>That matters because <c>InventoryMenu</c> is a singleton with <b>eight</b> push
        /// sites, two of which can fire around one fight: <c>WorldRuntime.OpenCorpseLoot</c> and the
        /// combat inventory. TASK-569 records a fight ending with the navigator completely empty and
        /// the travel screen disabled, which is exactly the end state here.</para>
        ///
        /// <para><b>LATENT, not currently reachable — established 2026-09-20 and kept anyway.</b>
        /// Reaching it needs two pushes with NO pop between them and then two pops, and no caller
        /// does that: every push is paired with one closer, and the one "extra" pop
        /// (<c>InventoryMenu</c> popping itself when a lock opens) is paired with the push that
        /// opened the lock screen. The lock-pick-then-loot sequence looked like a way in until
        /// <c>Serialize</c> was read — it is FIFO and each op runs start-to-finish, so the
        /// fire-and-forget push queues BEHIND the in-flight self-pop and the order is always
        /// pop-then-push.</para>
        ///
        /// <para>So this pins a property of the navigator that its callers do not exercise today.
        /// It is worth keeping as the guard for the day one does — a screen with a second closer,
        /// or a push that skips the queue — and so the asymmetry is not re-derived from scratch a
        /// third time.</para>
        /// </remarks>
        [UnityTest]
        public IEnumerator RePushingOneScreenThenPoppingTwiceEmptiesTheStack() =>
            UniTask.ToCoroutine(async () => {
                var log = new List<string>();
                var nav = new ScreenNavigator();
                var travel = new RecordingScreen("travel", log);
                var inventory = new RecordingScreen("inventory", log);

                await nav.ResetTo(travel);
                await nav.Push(inventory);          // opener one: a corpse loot
                LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "pushed while already on the stack"));
                await nav.Push(inventory);          // opener two: the same singleton again

                // Each opener closes what it opened.
                await nav.Pop();
                // The second pop empties the stack, which the navigator now reports with a stack
                // trace -- that warning IS the TASK-569 instrument, and expecting it here is what
                // proves it fires on this shape.
                LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "Pop emptied the stack"));
                await nav.Pop();

                Assert.IsNull(nav.Current,
                    "two pushes of one singleton collapse to a single entry, so the second pop takes "
                    + "the screen UNDERNEATH -- the travel screen -- and nothing is left shown");
                Assert.IsFalse(travel.Shown,
                    "and the travel screen is disabled with nothing replacing it, which is the state "
                    + "TASK-569 reports after a fight");
            });

        /// <summary>The control: two DIFFERENT screens balance exactly.</summary>
        [UnityTest]
        public IEnumerator TwoDifferentScreensPushedAndPoppedLeaveTheBase() =>
            UniTask.ToCoroutine(async () => {
                var log = new List<string>();
                var nav = new ScreenNavigator();
                var travel = new RecordingScreen("travel", log);
                var inventory = new RecordingScreen("inventory", log);
                var sheet = new RecordingScreen("sheet", log);

                await nav.ResetTo(travel);
                await nav.Push(inventory);
                await nav.Push(sheet);
                await nav.Pop();
                await nav.Pop();

                Assert.AreSame(travel, nav.Current, "the base screen survives a balanced pair");
                Assert.IsTrue(travel.Shown);
            });
    }
}

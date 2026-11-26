namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>The book's bookmarks turn its pages and keep the draft (spec 2026-11-26-enhanced-settings-bookmarks).</summary>
    public class EnhancedSettingsPagesTests : EnhancedSettingsBookFixture {
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ABookmarkTurnsThePageAndKeepsTheDraft() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                Assert.AreEqual("Display", Title);
                bool skyBefore = GameOptions.GetFeature(EnhancedFeature.Sky);
                await ClickAsync("sky");

                await ClickAsync("bookmark-combat");
                Assert.AreEqual("Combat", Title);
                Assert.IsNull(Root.Q("sky"), "one page at a time");

                await ClickAsync("bookmark-display");
                Assert.AreEqual(!skyBefore, Screen.Shows("sky"), "the change is still in the draft");
                Assert.AreEqual(skyBefore, GameOptions.GetFeature(EnhancedFeature.Sky), "and not saved");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EnhancedOffGreysOnlyTheFeatures() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-general");
                await ClickAsync("enhanced");
                await ClickAsync("bookmark-display");
                Assert.IsFalse(Named("sky").enabledSelf, "a feature waits for the master");
                await ClickAsync("bookmark-combat");
                Assert.IsTrue(Named("combat_speed").enabledInHierarchy, "combat speed is Classic's too");
                Assert.IsFalse(Named("enhanced_combat").enabledSelf);
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator RestartMarkersSurvivePageTurns() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                // Pending means "differs from the font this run loaded", so it may take one click or two.
                for (int i = 0; i < 2 && !TextOf(Named("book_font")).Contains("(restart)"); i++) {
                    await ClickAsync("book_font");
                }
                StringAssert.Contains("(restart)", TextOf(Named("book_font")));
                await ClickAsync("bookmark-combat");
                await ClickAsync("bookmark-display");
                StringAssert.Contains("(restart)", TextOf(Named("book_font")), "still marked after a page turn");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ArrowsReachTheBookmarks() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-combat");
                Assert.AreEqual("enhanced-settings", UiDriver.Stack?.ResolveInputTarget()?.Id, "the book takes the keys");
                VisualElement Focused() => Root.focusController.focusedElement as VisualElement;
                for (int i = 0; i < 4 && (Focused()?.name ?? "bookmark-").StartsWith("bookmark-"); i++) {
                    await UiDriver.MoveFocusAsync(NavDirection.Left);
                }
                Assert.IsFalse((Focused()?.name ?? "bookmark-").StartsWith("bookmark-"), "Left goes into the page");
                for (int i = 0; i < 6 && !(Focused()?.name ?? "").StartsWith("bookmark-"); i++) {
                    await UiDriver.MoveFocusAsync(NavDirection.Right);
                }
                StringAssert.StartsWith("bookmark-", Focused()?.name, "Right from the page reaches the bookmarks");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ItOpensOnTheLastPageTurnedTo() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-audio");
                await CloseAsync();
                await OpenAsync();
                Assert.AreEqual("Audio", Title);
            } finally {
                await CloseAsync();
            }
        });
    }
}

namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
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

        /// <summary>Owner, 2026-11-26: the bookmarks keep one text size on every page, one that fits them all
        /// (the Controls page's long key names used to shrink them).</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheBookmarksKeepOneTextSizeOnEveryPage() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                var sizes = new System.Collections.Generic.HashSet<float>();
                foreach (SettingsPage page in EnhancedSettingsPages.Order) {
                    await ClickAsync("bookmark-" + EnhancedSettingsScreen.NameOf(page));
                    await Frames(3);
                    foreach (SettingsPage p in EnhancedSettingsPages.Order) {
                        Label caption = Named("bookmark-" + EnhancedSettingsScreen.NameOf(p)).Q<Label>("caption");
                        sizes.Add(Mathf.Round(caption.resolvedStyle.fontSize * 10) / 10);
                        // The cloth's swallow-tail end takes the last tenth of its width (the texture's notch,
                        // 80 of its 800 px): the words must end before it, not run into the cut.
                        VisualElement mark = caption.parent;
                        float words = caption.MeasureTextSize(caption.text, 0, VisualElement.MeasureMode.Undefined, 0,
                            VisualElement.MeasureMode.Undefined).x;
                        float end = mark.worldBound.xMin + mark.resolvedStyle.paddingLeft + words;
                        Assert.LessOrEqual(end, mark.worldBound.xMin + 0.9f * mark.worldBound.width + 0.5f, $"{p} ends before the notch");
                    }
                }
                Assert.AreEqual(1, sizes.Count, "one size: " + string.Join(", ", sizes));
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Owner, 2026-11-26: the check boxes and radio buttons were cut off at the bottom; they are
        /// drawn at their native size, so their box must be as tall as the sprite.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheCheckBoxesAndRadioButtonsAreWhole() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                foreach (string id in new[] { "sky", "shadows-0" }) {
                    VisualElement icon = Named(id).Q(className: "enhanced-book__icon");
                    for (int i = 0; i < 120 && icon.resolvedStyle.backgroundImage.sprite == null; i++) await Frames(1);
                    Sprite sprite = icon.resolvedStyle.backgroundImage.sprite;
                    Assert.NotNull(sprite, id);
                    Assert.GreaterOrEqual(icon.layout.height + 0.5f, sprite.rect.height, $"{id}: the box is as tall as the art");
                    Assert.GreaterOrEqual(icon.layout.width + 0.5f, sprite.rect.width, $"{id}: and as wide");
                }
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Owner, 2026-11-26: the folder paths get the page's width, not half of it.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheFolderPathsUseThePagesWidth() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-game-files");
                float page = Root.Q(className: "enhanced-book__content").contentRect.width;
                foreach (string id in new[] { "game_folder", "dev_folder" }) {
                    Assert.Greater(Named(id).layout.width, page * 0.9f, id);
                }
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Owner, 2026-11-26: turning to Game Files changed the text size. The words on the lines
        /// and the bookmarks keep one size on every page.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheLinesKeepOneTextSizeOnEveryPage() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                var lines = new System.Collections.Generic.Dictionary<string, float>();
                var marks = new System.Collections.Generic.HashSet<float>();
                var buttons = new System.Collections.Generic.HashSet<float>();
                float Size(VisualElement e) => Mathf.Round(e.Q<Label>("caption").resolvedStyle.fontSize * 10) / 10;
                foreach (SettingsPage page in EnhancedSettingsPages.Order) {
                    await ClickAsync("bookmark-" + EnhancedSettingsScreen.NameOf(page));
                    await Frames(3);
                    foreach (Button line in Root.Query<Button>(className: "enhanced-book__line").ToList()) {
                        if (line.ClassListContains("enhanced-book__keyrow")) continue; // the key list has its own columns
                        lines[$"{page}/{line.name}"] = Size(line);
                    }
                    marks.Add(Size(Named("bookmark-general")));
                    buttons.Add(Size(Named("ok")));
                }
                Assert.AreEqual(1, new System.Collections.Generic.HashSet<float>(lines.Values).Count, "one size for the lines: "
                    + string.Join(", ", System.Linq.Enumerable.Select(lines, kv => $"{kv.Key}={kv.Value}")));
                Assert.AreEqual(1, marks.Count, "one size for the bookmarks: " + string.Join(", ", marks));
                Assert.AreEqual(1, buttons.Count, "OK keeps its size: " + string.Join(", ", buttons));
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Owner, 2026-11-26: the key list in two columns, the actions' names and their keys each
        /// lined up.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheKeyListHasTwoAlignedColumns() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-controls");
                var rows = Root.Query<Button>(className: "enhanced-book__keyrow").ToList();
                Assert.Greater(rows.Count, 10, "the key rows");
                float actionX = rows[0].Q<Label>("caption").worldBound.xMin, keyX = rows[0].Q<Label>("key").worldBound.xMin;
                Assert.Greater(keyX, actionX + 100, "the key stands in a column of its own");
                foreach (Button row in rows) {
                    Assert.AreEqual(actionX, row.Q<Label>("caption").worldBound.xMin, 0.5f, row.name + " action");
                    Assert.AreEqual(keyX, row.Q<Label>("key").worldBound.xMin, 0.5f, row.name + " key");
                }
                StringAssert.DoesNotContain(":", rows[0].Q<Label>("caption").text, "the action alone, its key beside it");
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Owner, 2026-11-26: Language moves from Preferences to the book's General page, chosen
        /// like the original's choices and saved with OK for the next start.</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator LanguageIsChosenOnTheGeneralPage() => UniTask.ToCoroutine(async () => {
            string saved = BakAgain.ResourceManagement.BakResourceSettings.Language;
            try {
                await OpenAsync();
                await ClickAsync("bookmark-general");
                VisualElement group = Named("language");
                var choices = group.Query<Button>(className: "enhanced-book__line").ToList();
                if (choices.Count < 2) Assert.Ignore("one language installed: no choice to show");
                Button other = choices.First(b => !Screen.Shows(b.name));
                await ClickAsync(other.name);
                StringAssert.Contains("(restart)", TextOf(other), "it takes effect at the next start");
                Assert.AreEqual(saved, BakAgain.ResourceManagement.BakResourceSettings.Language, "not before OK");
                await ClickAsync("ok");
                Assert.AreNotEqual(saved, BakAgain.ResourceManagement.BakResourceSettings.Language, "OK saves it");
            } finally {
                BakAgain.ResourceManagement.BakResourceSettings.Language = saved;
                await CloseAsync();
            }
        });
    
        /// <summary>Owner, 2026-11-26: the book's buttons press like the ones outside it. A bevelled button
        /// wears the original's pressed pens while held (TASK-1000), and a check or radio line shows the
        /// toggle's +1 frame, as Preferences does (menu_type_3_4's highlight).</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator HeldButtonsAndLinesShowTheirPressedLook() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-general");
                VisualElement ok = Named("ok");
                Send<PointerDownEvent>(ok);
                Assert.IsTrue(ok.ClassListContains("text-button--pressed"), "OK held: pressed");
                Send<PointerLeaveEvent>(ok);
                Assert.IsFalse(ok.ClassListContains("text-button--pressed"), "let go elsewhere: plain");

                VisualElement line = Named("check_updates");
                VisualElement icon = line.Q(className: "enhanced-book__icon");
                await Frames(5);
                Background plain = icon.resolvedStyle.backgroundImage;
                Send<PointerDownEvent>(line);
                await Frames(5);
                Assert.AreNotEqual(plain, icon.resolvedStyle.backgroundImage, "line held: the pressed frame");
                Send<PointerLeaveEvent>(line);
                await Frames(5);
                Assert.AreEqual(plain, icon.resolvedStyle.backgroundImage, "let go elsewhere: the plain frame");
            } finally {
                await CloseAsync();
            }
        });

        private static void Send<T>(VisualElement target) where T : EventBase<T>, new() {
            using (T e = EventBase<T>.GetPooled()) {
                e.target = target;
                target.SendEvent(e);
            }
        }
}
}

namespace BakAgain.Tests.PlayMode.UI {
    using System.Collections;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using T = GameData.Resources.Text.UiTemplates;

    /// <summary>The book's hint line, OK, Cancel, and Escape's "Keep your changes?" (spec
    /// 2026-11-26-enhanced-settings-bookmarks, owner: ask only if something changed).</summary>
    public class EnhancedSettingsClosingTests : EnhancedSettingsBookFixture {
        private bool IsUp => ReferenceEquals(Navigator.Current, Screen);

        private string Hint => Root.Q<Label>("hint")?.text;

        private RebindSlot Slot(string id) {
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            var input = (SystemInputSource)scope.Container.Resolve(typeof(SystemInputSource));
            return input.RebindSlots.Single(s => s.Id == id);
        }

        private System.Collections.Generic.IReadOnlyList<RebindSlot> Slots {
            get {
                var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
                return ((SystemInputSource)scope.Container.Resolve(typeof(SystemInputSource))).RebindSlots;
            }
        }

        [TearDown]
        public void Keys() => InputBindings.Reset(Slots);

        private static void Send<TEvent>(VisualElement target) where TEvent : PointerEventBase<TEvent>, new() {
            using var e = PointerEventBase<TEvent>.GetPooled(new Event { type = EventType.MouseMove, mousePosition = target.worldBound.center });
            e.target = target;
            target.SendEvent(e);
        }

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheHintLineExplainsThePointedSetting() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-combat");
                Assert.AreEqual(T.Format(T.SettingsHintIdle), Hint);
                Send<PointerEnterEvent>(Named("combat_speed"));
                await Frames(1);
                Assert.AreEqual(T.Format(T.HintCombatSpeed), Hint);
                Send<PointerLeaveEvent>(Named("combat_speed"));
                await Frames(1);
                Assert.AreEqual(T.Format(T.SettingsHintIdle), Hint, "a mouse that leaves leaves nothing explained");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator ATapLeavesItsHint() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                await ClickAsync("sky");
                Assert.AreEqual(T.Format(T.HintSky), Hint);
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EscapeWithNoChangesCloses() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await UiDriver.CancelAsync(2);
                Assert.IsFalse(IsUp, "nothing to keep: Escape closes");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EscapeWithAChangeAsks_YesSaves() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                bool sky = GameOptions.GetFeature(EnhancedFeature.Sky);
                await ClickAsync("sky");
                await UiDriver.CancelAsync(2);
                Assert.IsTrue(IsUp, "a change: Escape asks first");
                Named("keep-changes");
                await ClickAsync("keep-yes");
                Assert.IsFalse(IsUp);
                Assert.AreEqual(!sky, GameOptions.GetFeature(EnhancedFeature.Sky), "Yes saves");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EscapeWithAChangeAsks_NoDiscardsAndPutsKeysBack() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                bool sky = GameOptions.GetFeature(EnhancedFeature.Sky);
                await ClickAsync("sky");
                InputBindings.Set(Slots, Slot("forward"), "<Keyboard>/i");
                await UiDriver.CancelAsync(2);
                await ClickAsync("keep-no");
                Assert.IsFalse(IsUp);
                Assert.AreEqual(sky, GameOptions.GetFeature(EnhancedFeature.Sky), "No discards");
                Assert.AreEqual("<Keyboard>/w", Slot("forward").Path, "and the key comes back");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EscapeOnTheQuestionGoesBack() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                await ClickAsync("sky");
                bool shown = Screen.Shows("sky");
                await UiDriver.CancelAsync(2);
                await UiDriver.CancelAsync(2);
                Assert.IsTrue(IsUp, "still in the book");
                Assert.IsNull(Root.Q("keep-changes"), "the question is gone");
                Assert.AreEqual(shown, Screen.Shows("sky"), "the change is still there");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator EscapeBelongsToARebindFirst() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-controls");
                await ClickAsync("control-forward");
                await UiDriver.CancelAsync(2);
                Assert.IsTrue(IsUp, "the rebind's Escape does not close the book");
                Assert.IsNull(Root.Q("keep-changes"), "nor ask");
            } finally {
                await CloseAsync();
            }
        });

        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator OkSavesCancelDiscards() => UniTask.ToCoroutine(async () => {
            try {
                await OpenAsync();
                await ClickAsync("bookmark-combat");
                await ClickAsync("combat_speed-2");
                InputBindings.Set(Slots, Slot("forward"), "<Keyboard>/i");
                await ClickAsync("ok");
                Assert.AreEqual(GameData.Resources.Combat.CombatSpeed.Fast, GameOptions.CombatSpeed);
                Assert.AreEqual("<Keyboard>/i", Slot("forward").Path, "OK keeps the key");

                await OpenAsync();
                await ClickAsync("combat_speed-0");
                InputBindings.Set(Slots, Slot("forward"), "<Keyboard>/u");
                await ClickAsync("cancel");
                Assert.AreEqual(GameData.Resources.Combat.CombatSpeed.Fast, GameOptions.CombatSpeed, "Cancel discards");
                Assert.AreEqual("<Keyboard>/i", Slot("forward").Path, "and puts the key back");
            } finally {
                await CloseAsync();
            }
        });

        /// <summary>Review C1: a bookmark clicked while the question is up must not turn the page under it,
        /// and no layer of the book may outlive it (a stranded Exclusive layer kills the keys).</summary>
        [UnityTest]
        [RequiresShippedGameData]
        public IEnumerator TheQuestionHoldsTheBookmarksAndLeavesNoLayerBehind() => UniTask.ToCoroutine(async () => {
            GameViewResolutionScope view = GameViewResolutionScope.Force(1920, 1080); // bookmarks out on the table
            await Frames(2);
            try {
                await OpenAsync();
                await ClickAsync("bookmark-display");
                await ClickAsync("sky");
                await UiDriver.CancelAsync(2);
                // What a real pointer at the bookmark hits: whatever the panel picks there.
                VisualElement mark = Named("bookmark-combat");
                VisualElement hit = Root.panel.Pick(mark.worldBound.center);
                Assert.IsFalse(hit == mark || (hit != null && mark.Contains(hit)),
                    $"the question's layer leaves the bookmark unpickable (picked {hit?.name})");
                if (hit != null) Click(hit);
                await Frames(2);
                Assert.AreEqual("Display", Title, "the page does not turn under the question");
                Assert.NotNull(Root.Q("keep-changes"), "the question stays");
                await ClickAsync("keep-no");
                await Frames(2);
                Assert.IsFalse(IsUp);
                Assert.IsFalse(UiDriver.Stack.Layers.Any(l => l.Id.StartsWith("enhanced-settings")),
                    "no layer of the book is left: " + string.Join(", ", UiDriver.Stack.Layers.Select(l => l.Id)));
            } finally {
                await CloseAsync();
                view.Restore();
            }
        });
    }
}

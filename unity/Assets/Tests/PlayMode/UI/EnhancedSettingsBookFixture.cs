namespace BakAgain.Tests.PlayMode.UI {
    using System.Linq;
    using BakAgain.Tests.TestSupport;
    using BakAgain.UI;
    using BakAgain.UI.Navigation;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Raises the Enhanced settings book the way Preferences does (the navigator), and reads and
    /// clicks its elements by name (spec 2026-11-26-enhanced-settings-bookmarks).</summary>
    public abstract class EnhancedSettingsBookFixture {
        protected EnhancedSettingsScreen Screen;
        protected IScreenNavigator Navigator;
        private EnhancedPrefsStash _stash;

        protected VisualElement Root => Screen.GetComponent<UIDocument>().rootVisualElement;

        /// <summary>Opens the book; prefs start from their defaults, with Enhanced on unless asked otherwise.</summary>
        protected async UniTask OpenAsync(bool enhanced = true) {
            _stash ??= EnhancedPrefsStash.Take();
            BakAgain.Core.GameOptions.Enhanced = enhanced;
            var scope = Object.FindAnyObjectByType<VContainer.Unity.LifetimeScope>();
            if (scope == null) Assert.Ignore("no LifetimeScope: DI has not built the screens");
            Screen = (EnhancedSettingsScreen)scope.Container.Resolve(typeof(EnhancedSettingsScreen));
            Navigator = (IScreenNavigator)scope.Container.Resolve(typeof(IScreenNavigator));
            await Navigator.PushUnfaded(Screen);
            await Frames(5);
        }

        protected async UniTask CloseAsync() {
            if (Screen != null && ReferenceEquals(Navigator.Current, Screen)) {
                await Navigator.PopUnfaded();
            }
            await Frames(2);
        }

        [NUnit.Framework.TearDown]
        public void RestorePrefs() {
            _stash?.Restore();
            _stash = null;
        }

        protected static async UniTask Frames(int n) {
            for (int i = 0; i < n; i++) await UniTask.Yield();
        }

        protected VisualElement Named(string name) {
            VisualElement e = Root.Q(name);
            Assert.NotNull(e, $"'{name}' is on the page");
            return e;
        }

        protected string Title => Root.Q<Label>(className: "enhanced-book__title")?.text;

        protected static string TextOf(VisualElement e) =>
            string.Join(" ", e.Query<Label>().ToList().Select(l => l.text));

        protected async UniTask ClickAsync(string name) {
            Click(Named(name));
            await Frames(2);
        }

        protected static void Click(VisualElement target) {
            Vector2 center = target.worldBound.center;
            using (PointerDownEvent down = PointerDownEvent.GetPooled(
                       new Event { type = EventType.MouseDown, mousePosition = center, button = 0 })) {
                down.target = target;
                target.SendEvent(down);
            }
            using (PointerUpEvent up = PointerUpEvent.GetPooled(
                       new Event { type = EventType.MouseUp, mousePosition = center, button = 0 })) {
                up.target = target;
                target.SendEvent(up);
            }
        }
    }
}

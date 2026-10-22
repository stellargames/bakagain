namespace BakAgain.UI {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.Cursor;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Mods;
    using UnityEngine.UIElements;
    using VContainer;
    using Templates = GameData.Resources.Text.UiTemplates;

    /// <summary>
    /// Asked before a restore when the save was played with mods that are not active now, or are
    /// installed at another major version (mod system spec 4.7): names each one and offers Load anyway
    /// or Cancel. In the Enhanced settings' look. The caller sets it up with <see cref="Show"/>, pushes
    /// it, and awaits <see cref="Result"/>: the push returns as soon as the prompt is up.
    /// </summary>
    public sealed class MissingModsScreen : Navigation.ScreenBase {
        private Navigation.IScreenNavigator _navigator;
        private InputLayerStack _stack;
        private ICursorManager _cursor;
        private NavigableLayer _layer;
        private VisualElement _panel;
        private IReadOnlyList<SaveModIssue> _issues = Array.Empty<SaveModIssue>();
        private UniTaskCompletionSource<bool> _result = new();
        private bool? _choice;

        [Inject]
        public void Construct(Navigation.IScreenNavigator navigator, InputLayerStack stack, ICursorManager cursor) {
            _navigator = navigator;
            _stack = stack;
            _cursor = cursor;
        }

        /// <summary>Sets the mods to name, and starts a new <see cref="Result"/>.</summary>
        public void Show(IReadOnlyList<SaveModIssue> issues) {
            _issues = issues;
            _choice = null;
            _result = new UniTaskCompletionSource<bool>();
        }

        /// <summary>True for Load anyway, false for Cancel.</summary>
        public UniTask<bool> Result => _result.Task;

        /// <summary>
        /// "Not active" while any mod is missing; when every one is installed at another major version,
        /// worded as a version mismatch. Each line says which case it is.
        /// </summary>
        internal string Title => Templates.Format(_issues.Any(i => i.Kind == SaveModIssueKind.Missing)
            ? Templates.ModsMissingTitle
            : Templates.ModsMismatchTitle);

        /// <summary>One line per mod. A missing mod has no manifest, so it is named by its saved id.</summary>
        internal IReadOnlyList<string> Lines => _issues.Select(Line).ToList();

        private static string Line(SaveModIssue issue) {
            if (issue.Kind == SaveModIssueKind.Missing) {
                return Templates.Format(Templates.ModsMissingLine, ("name", issue.Id), ("version", issue.SavedVersion));
            }
            string name = ModLayers.Current.Active.FirstOrDefault(m => m.Manifest.Id == issue.Id)?.Manifest.Name ?? issue.Id;
            return Templates.Format(Templates.ModsMajorLine, ("name", name), ("version", issue.SavedVersion),
                ("installed", issue.InstalledVersion));
        }

        internal void LoadAnyway() => Close(true);

        internal void Cancel() => Close(false);

        // Once only: a second press (a double click, a key and a click) would pop the restore screen too.
        // Pop first: the navigator runs its operations in order, so the restore that follows Load anyway
        // starts from the restore screen, as without a prompt. The choice is kept first because the pop
        // can hide the screen before it returns.
        private void Close(bool loadAnyway) {
            if (_choice != null) {
                return;
            }
            _choice = loadAnyway;
            _navigator.Pop().Forget();
            _result.TrySetResult(loadAnyway);
        }

        protected override UniTask BuildWhileHiddenAsync() {
            UIDocument document = GetComponent<UIDocument>();
            if (document?.rootVisualElement == null) {
                return UniTask.CompletedTask;
            }
            _clicks.Clear(); // the previous show's buttons are gone
            _panel = MovedElements.Box("enhanced-settings");
            Label title = EnhancedSettingsScreen.Text(Title, "enhanced-settings__title");
            _panel.Add(title);
            VisualElement list = MovedElements.Box("enhanced-settings__column");
            foreach (string line in Lines) {
                list.Add(EnhancedSettingsScreen.Text(line, "enhanced-settings__label"));
            }
            _panel.Add(list);
            VisualElement buttons = MovedElements.Box("enhanced-settings__buttons");
            buttons.Add(Choice(Templates.ModsLoadAnyway, LoadAnyway));
            buttons.Add(Choice(Templates.ModsCancel, Cancel));
            _panel.Add(buttons);
            CanonicalStage.GetOrCreate(document.rootVisualElement, null).Add(_panel);
            return UniTask.CompletedTask;
        }

        protected override void OnAfterShow() {
            UIDocument document = GetComponent<UIDocument>();
            List<NavWidget> widgets = _panel.Query<Button>().ToList()
                .Select(b => new NavWidget(b, b.Q<Label>("caption")?.text ?? string.Empty,
                    () => EnhancedSettingsScreen.CanonicalRect(document, b), () => Click(b), null))
                .ToList();
            _layer = new NavigableLayer("missing-mods", CaptureMode.Exclusive, widgets, Cancel, _cursor);
            _stack?.Push(_layer);
        }

        // Closed without a choice: Cancel, not a restore left waiting. On leaving the stack, not on a
        // hide, which a screen pushed over this one would also cause (TASK-925).
        protected override void OnClosing() => _result.TrySetResult(_choice ?? false);

        protected override void OnBeforeHide() {
            if (_layer != null) {
                _stack?.Remove(_layer);
                _layer = null;
            }
            _panel?.RemoveFromHierarchy();
            _panel = null;
        }

        private readonly Dictionary<Button, Action> _clicks = new();

        private void Click(Button b) {
            if (_clicks.TryGetValue(b, out Action a)) a();
        }

        private Button Choice(string key, Action action) {
            var button = new Button(action);
            button.AddToClassList("text-button");
            button.AddToClassList("enhanced-settings__row");
            GameFontText.Caption(button, Templates.Format(key));
            _clicks[button] = action;
            return button;
        }
    }
}

namespace BakAgain.UI {
    using System;
    using System.Collections.Generic;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.Cursor;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Mods;
    using SimpleFileBrowser;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using Templates = GameData.Resources.Text.UiTemplates;

    /// <summary>
    /// The Enhanced settings, on a screen of their own (opened from Preferences): the master switch and
    /// every <see cref="EnhancedFeature"/>, the start-up prompt's fields — the game-files folder and
    /// the dev overrides switch and folder — so they can be changed without clearing the game path, and
    /// the way to the installed mods (<see cref="ModsScreen"/>). In the
    /// Preferences look: OPTIONS2.SCX behind, the game font, "[x]" rows. Nothing is saved until Save;
    /// a folder or a font change takes effect at the next start and says so.
    /// </summary>
    public sealed class EnhancedSettingsScreen : Navigation.ScreenBase {
        private Navigation.IScreenNavigator _navigator;
        private ModsScreen _modsScreen;
        private InputLayerStack _stack;
        private ICursorManager _cursor;
        private NavigableLayer _layer;
        private VisualElement _panel;
        private EnhancedOptionsDraft _draft;
        private string _gamePath, _modsPath;
        private bool _modsOn;
        private Label _folderError;
        private readonly List<(Button Row, Func<string> Caption, Func<bool> Enabled)> _rows = new();

        [Inject]
        public void Construct(Navigation.IScreenNavigator navigator, InputLayerStack stack, ICursorManager cursor, ModsScreen modsScreen) {
            _navigator = navigator;
            _modsScreen = modsScreen;
            _stack = stack;
            _cursor = cursor;
        }

        /// <summary>The feature rows, top to bottom: every <see cref="EnhancedFeature"/> once.</summary>
        internal static readonly (string Key, EnhancedFeature Feature)[] FeatureRows = {
            (Templates.EnhancedFeatureFullScreen, EnhancedFeature.FullScreenTravel),
            (Templates.EnhancedFeatureMouseLook, EnhancedFeature.MouseLook),
            (Templates.EnhancedFeatureRings, EnhancedFeature.PortraitRings),
            (Templates.EnhancedFeatureBookFont, EnhancedFeature.BookFont),
            (Templates.EnhancedFeaturePageTurn, EnhancedFeature.PageTurn),
            (Templates.EnhancedFeatureInventory, EnhancedFeature.Inventory),
            (Templates.EnhancedFeatureSky, EnhancedFeature.Sky),
            (Templates.EnhancedFeatureLighting, EnhancedFeature.Lighting),
            (Templates.EnhancedFeatureCasting, EnhancedFeature.Casting),
            (Templates.EnhancedFeatureContinuousMovement, EnhancedFeature.ContinuousMovement),
        };

        /// <summary>Whether a feature row can be toggled: every feature needs the master, and the health
        /// rings only draw on the full-screen HUD.</summary>
        internal static bool EnhancedRowEnabled(EnhancedOptionsDraft draft, EnhancedFeature feature) =>
            draft.Master && (feature != EnhancedFeature.PortraitRings || draft[EnhancedFeature.FullScreenTravel]);

        /// <summary>Whether Save would change the font from the one loaded. The fonts are bound when the
        /// game starts (<see cref="GameFonts.Build()"/>), so the change waits for a restart.</summary>
        internal static bool FontChangePending(EnhancedOptionsDraft draft) =>
            (draft.Master && draft[EnhancedFeature.BookFont]) != GameFonts.IsBookFont;

        /// <summary>Whether Save would change the mode from the one the mods were resolved for at start
        /// (<see cref="ModLayers.Mode"/>): the mod set follows at the next start (mod system spec 4.4).</summary>
        internal static bool ModeChangePending(EnhancedOptionsDraft draft) =>
            draft.Master != (ModLayers.Mode == ModMode.Enhanced);

        /// <summary>A row's caption: "[x] Sky", or for the master "Enhanced: On"; "(restart)" appended
        /// while the choice waits for the next start.</summary>
        internal static string BoxText(string key, bool on, bool state = false, bool pending = false) {
            string text = state
                ? Templates.Format(key, ("state", Templates.Format(on ? Templates.EnhancedStateOn : Templates.EnhancedStateOff)))
                : (on ? "[x] " : "[ ] ") + Templates.Format(key);
            return pending ? Templates.Format(Templates.EnhancedPendingKey, ("text", text)) : text;
        }

        /// <summary>The shadows row: "Shadows: Sun". A click steps Off, Blobs, Sun and round.</summary>
        internal static string ShadowsText(ShadowMode mode) =>
            Templates.Format(Templates.EnhancedShadowsKey, ("state", Templates.Format(mode switch {
                ShadowMode.Blobs => Templates.EnhancedShadowsBlobs,
                ShadowMode.Sun => Templates.EnhancedShadowsSun,
                _ => Templates.EnhancedStateOff,
            })));

        internal static ShadowMode NextShadows(ShadowMode mode) => (ShadowMode)(((int)mode + 1) % 3);

        // Here, not in OnBeforeShowAsync: the document has no root until the screen is active.
        protected override UniTask BuildWhileHiddenAsync() {
            VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) {
                return UniTask.CompletedTask;
            }
            // Back from the Mods screen, which this screen is still under: the same visit, so its edits stay.
            if (!_visitingMods || _draft == null) {
                _draft = EnhancedOptionsDraft.Load();
                (_gamePath, _modsOn, _modsPath) = BakResourceSettings.NextStart;
                if (string.IsNullOrWhiteSpace(_modsPath)) _modsPath = MetaMenu.DefaultOverridePath;
            }
            Build(CanonicalStage.GetOrCreate(root, null));
            return UniTask.CompletedTask;
        }

        protected override void OnAfterShow() {
            var widgets = new List<NavWidget>();
            foreach ((Button row, Func<string> _, Func<bool> _) in _rows) {
                Button b = row;
                widgets.Add(new NavWidget(b, b.Q<Label>("caption")?.text ?? string.Empty, () => CanonicalRect(GetComponent<UIDocument>(), b),
                    () => { if (b.enabledSelf && !FileBrowser.IsOpen) ClickOf(b)?.Invoke(); }, null));
            }
            _layer = new NavigableLayer("enhanced-settings", CaptureMode.Exclusive, widgets, CancelOrClosePicker, _cursor);
            _stack?.Push(_layer);
        }

        protected override void OnBeforeHide() {
            if (_layer != null) {
                _stack?.Remove(_layer);
                _layer = null;
            }
            _panel?.RemoveFromHierarchy();
            _panel = null;
            _rows.Clear();
        }

        private readonly Dictionary<Button, Action> _clicks = new();

        private Action ClickOf(Button b) => _clicks.TryGetValue(b, out Action a) ? a : null;

        /// <summary>A row's rect on the canonical stage, for focus and the cursor.</summary>
        internal static Rect CanonicalRect(UIDocument document, VisualElement e) {
            VisualElement stage = CanonicalStage.Find(document?.rootVisualElement);
            Rect wb = e.worldBound;
            if (stage == null || float.IsNaN(wb.x) || wb.width <= 0f) return Rect.zero;
            Vector2 min = stage.WorldToLocal(wb.min), max = stage.WorldToLocal(wb.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Build(VisualElement stage) {
            _panel?.RemoveFromHierarchy();
            _rows.Clear();
            _clicks.Clear();
            _paths.Clear();
            _panel = MovedElements.Box("enhanced-settings");
            _panel.Add(Text(Templates.Format(Templates.EnhancedSettingsTitle), "enhanced-settings__title"));

            VisualElement columns = MovedElements.Box("enhanced-settings__columns");
            VisualElement features = MovedElements.Box("enhanced-settings__column");
            VisualElement folders = MovedElements.Box("enhanced-settings__column");
            columns.Add(features);
            columns.Add(folders);
            _panel.Add(columns);

            features.Add(Text(Templates.Format(Templates.EnhancedFeaturesHeading), "enhanced-settings__heading"));
            Row(features, () => BoxText(Templates.EnhancedButtonKey, _draft.Master, state: true,
                    pending: FontChangePending(_draft) || ModeChangePending(_draft)),
                () => true, () => _draft.Master = !_draft.Master);
            foreach ((string key, EnhancedFeature feature) in FeatureRows) {
                Row(features, () => BoxText(key, _draft[feature], pending: feature == EnhancedFeature.BookFont && FontChangePending(_draft)),
                    () => EnhancedRowEnabled(_draft, feature), () => _draft[feature] = !_draft[feature]);
            }
            Row(features, () => ShadowsText(_draft.Shadows), () => _draft.Master, () => _draft.Shadows = NextShadows(_draft.Shadows));

            folders.Add(Text(Templates.Format(Templates.EnhancedFoldersHeading), "enhanced-settings__heading"));
            folders.Add(Text(Templates.Format(Templates.EnhancedGameFolder), "enhanced-settings__label"));
            folders.Add(PathLine(() => Shown(_gamePath), () => _gamePath != BakResourceSettings.GamePath));
            if (FoldersPickable) Row(folders, () => Templates.Format(Templates.EnhancedBrowse), () => true, BrowseGameFolder);
            _folderError = Text(string.Empty, "enhanced-settings__error");
            folders.Add(_folderError);
            Row(folders, () => Templates.Format(Templates.ModsInstalled), () => true, () => VisitModsAsync().Forget());
            Row(folders, () => BoxText(Templates.EnhancedMods, _modsOn, pending: _modsOn != BakResourceSettings.OverrideEnabled),
                () => true, () => _modsOn = !_modsOn);
            folders.Add(Text(Templates.Format(Templates.EnhancedModsFolder), "enhanced-settings__label"));
            folders.Add(PathLine(() => Shown(_modsPath), () => _modsOn && _modsPath != BakResourceSettings.OverridePath));
            if (FoldersPickable) Row(folders, () => Templates.Format(Templates.EnhancedBrowse), () => _modsOn, BrowseModsFolder);

            VisualElement buttons = MovedElements.Box("enhanced-settings__buttons");
            Row(buttons, () => Templates.Format(Templates.EnhancedSave), () => true, Save);
            Row(buttons, () => Templates.Format(Templates.EnhancedCancel), () => true, Cancel);
            _panel.Add(buttons);

            stage.Add(_panel);
            Refresh();
        }

        private static string Shown(string path) =>
            string.IsNullOrWhiteSpace(path) ? Templates.Format(Templates.EnhancedNotSet) : path;

        private readonly List<(Label Line, Func<string> Text, Func<bool> Pending)> _paths = new();

        private Label PathLine(Func<string> text, Func<bool> pending) {
            Label line = Text(string.Empty, "enhanced-settings__path");
            // One line, cut from the left: the folder's own name is the part worth reading. Inline,
            // because the game-font helper sets wrapping inline and that beats the theme.
            line.style.whiteSpace = WhiteSpace.NoWrap;
            line.style.overflow = Overflow.Hidden;
            line.style.textOverflow = TextOverflow.Ellipsis;
            line.style.unityTextOverflowPosition = TextOverflowPosition.Start;
            _paths.Add((line, text, pending));
            return line;
        }

        private void Row(VisualElement parent, Func<string> caption, Func<bool> enabled, Action toggle) {
            var row = new Button();
            row.AddToClassList("text-button");
            row.AddToClassList("enhanced-settings__row");
            GameFontText.Caption(row, string.Empty);
            Action click = () => { toggle(); Refresh(); };
            row.clicked += click;
            _clicks[row] = click;
            parent.Add(row);
            _rows.Add((row, caption, enabled));
        }

        private void Refresh() {
            foreach ((Button row, Func<string> caption, Func<bool> enabled) in _rows) {
                Label label = row.Q<Label>("caption");
                if (label != null) label.text = caption();
                row.SetEnabled(enabled());
            }
            foreach ((Label line, Func<string> text, Func<bool> pending) in _paths) {
                string t = text();
                line.text = pending() ? Templates.Format(Templates.EnhancedPendingKey, ("text", t)) : t;
            }
        }

        /// <summary>
        /// Whether the folders can be picked here. Not on Android: its picker answers a document URI,
        /// which only the start-up prompt can use (it copies the game files in once, and a mods or
        /// language folder has to be a real directory), so the rows would save a path that never works.
        /// </summary>
        internal static bool FoldersPickable => Application.platform != RuntimePlatform.Android;

        private void BrowseGameFolder() {
            string initial = string.IsNullOrEmpty(_gamePath) ? Application.persistentDataPath : _gamePath;
            MetaMenu.PickGameArchiveFolder(initial, Templates.Format(Templates.EnhancedBrowse), folder => {
                bool ok = MetaMenu.AreRequiredGameFilesPresentIn(folder);
                _folderError.text = ok ? string.Empty : Templates.Format(Templates.EnhancedBadGameFolder);
                if (ok) _gamePath = folder;
                Refresh();
            });
        }

        private void BrowseModsFolder() =>
            MetaMenu.PickFolder(_modsPath, Templates.Format(Templates.EnhancedBrowse), folder => {
                _modsPath = folder;
                Refresh();
            });

        private void Save() {
            _draft.Commit();
            BakResourceSettings.SetForNextStart(_gamePath, _modsOn, _modsPath);
            _navigator.Pop().Forget();
        }

        private void Cancel() => _navigator.Pop().Forget();

        /// <summary>True while the Mods screen is open over this screen.</summary>
        private bool _visitingMods;

        private async UniTask VisitModsAsync() {
            _visitingMods = true;
            try {
                await _navigator.PushAndWaitAsync(_modsScreen); // until the Mods screen leaves the stack
            } finally {
                _visitingMods = false;
            }
        }

        /// <summary>Escape over an open folder picker closes the picker, not the screen under it.</summary>
        private void CancelOrClosePicker() {
            if (FileBrowser.IsOpen) {
                FileBrowser.HideDialog(invokeCancelCallback: true);
                return;
            }
            Cancel();
        }

        internal static Label Text(string text, string cls) {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            GameFontText.Apply(label);
            return label;
        }
    }
}

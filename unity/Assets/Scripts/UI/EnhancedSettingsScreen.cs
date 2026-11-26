namespace BakAgain.UI {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.UI.Cursor;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Mods;
    using Microsoft.Extensions.Logging;
    using SimpleFileBrowser;
    using UnityEngine;
    using UnityEngine.InputSystem;
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

        private IReadOnlyList<RebindSlot> _slots;
        private IResourceProviderService _resources;
        private readonly List<Slider> _sliders = new();
        private (string Game, bool ModsOn, string Mods) _openedFolders;
        private IReadOnlyDictionary<string, string> _bindings;
        private Label _hint;
        private NavigableLayer _questionLayer;

        [Inject]
        public void Construct(Navigation.IScreenNavigator navigator, InputLayerStack stack, ICursorManager cursor, ModsScreen modsScreen,
            SystemInputSource input, IResourceProviderService resources) {
            _resources = resources;
            _slots = input?.RebindSlots;
            _navigator = navigator;
            _modsScreen = modsScreen;
            _stack = stack;
            _cursor = cursor;
        }

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
                _page = _lastPage;
                _openedFolders = (_gamePath, _modsOn, _modsPath);
                // The language actually running, not the raw setting: a setting naming a pack that is not
                // installed runs English, and starting from it would show "(restart)" for good.
                _language = LanguagePacks.Current.Locale;
                _bindings = _slots != null ? InputBindings.Snapshot(_slots) : null;
            }
            Build(CanonicalStage.GetOrCreate(root, null));
            return UniTask.CompletedTask;
        }

        protected override void OnAfterShow() => PushLayer();

        private void PushLayer() {
            if (_layer != null) {
                _stack?.Remove(_layer);
            }
            var widgets = new List<NavWidget>();
            foreach ((Button row, Func<string> _, Func<bool> _) in _rows) {
                Button b = row;
                widgets.Add(new NavWidget(b, b.Q<Label>("caption")?.text ?? string.Empty, () => RowRect(b),
                    () => { if (b.enabledSelf && !FileBrowser.IsOpen && !Rebinding) ClickOf(b)?.Invoke(); }, null));
            }
            foreach (Slider slider in _sliders) {
                Slider sl = slider;
                string id = sl.name[..^"-slider".Length];
                widgets.Add(new NavWidget(sl, id, () => RowRect(sl), () => StepSlider(sl, id), null));
            }
            _layer = new NavigableLayer("enhanced-settings", CaptureMode.Exclusive, widgets, CancelOrClosePicker, _cursor);
            _stack?.Push(_layer);
        }

        protected override void OnBeforeHide() {
            _rebind?.Cancel();
            CloseQuestion();
            Audio.MidiPlaybackManager.Instance?.StopPreviewSong();
            Audio.AudioLevels.ResetToSaved(); // after Save these are the new levels; otherwise the old ones come back
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

        // A row on the Controls page is cut to the list's viewport, as on the Mods screen.
        private Rect RowRect(VisualElement row) {
            UIDocument document = GetComponent<UIDocument>();
            Rect rect = CanonicalRect(document, row);
            return _scroll != null && _scroll.Contains(row)
                ? ModsScreen.ClipTo(rect, CanonicalRect(document, _scroll.contentViewport))
                : rect;
        }

        /// <summary>A row's rect on the canonical stage, for focus and the cursor.</summary>
        internal static Rect CanonicalRect(UIDocument document, VisualElement e) {
            VisualElement stage = CanonicalStage.Find(document?.rootVisualElement);
            Rect wb = e.worldBound;
            if (stage == null || float.IsNaN(wb.x) || wb.width <= 0f) return Rect.zero;
            Vector2 min = stage.WorldToLocal(wb.min), max = stage.WorldToLocal(wb.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // ---- The book (spec 2026-11-26-enhanced-settings-bookmarks) ----

        private SettingsPage _page;

        /// <summary>The page the book opens on: the last one turned to this session, General the first time.</summary>
        private static SettingsPage _lastPage = SettingsPage.General;

        internal SettingsPage CurrentPage => _page;

        /// <summary>Lays the book out for fingers (taller lines and bookmarks) whatever the platform; tests.</summary>
        internal static bool? TouchOverride;

        private static bool Touch => TouchOverride ?? Application.isMobilePlatform;

        /// <summary>A page's name in element names: <c>bookmark-combat</c>, <c>bookmark-game-files</c>.</summary>
        internal static string NameOf(SettingsPage page) =>
            page == SettingsPage.GameFiles ? "game-files" : page.ToString().ToLowerInvariant();

        /// <summary>Turns to <paramref name="page"/>; the draft is kept.</summary>
        internal void TurnTo(SettingsPage page) {
            _page = _lastPage = page;
            VisualElement stage = CanonicalStage.Find(GetComponent<UIDocument>()?.rootVisualElement);
            if (stage == null) return;
            Build(stage);
            PushLayer();
            _panel.Q("bookmark-" + NameOf(page))?.Focus();
        }

        // BICONS (the player's own art, as the Preferences screen draws it): a check box at 123 (on) / 125
        // (off), a radio button at 127 / 129 (REQ_PREF's toggles; MenuIconLoader's index scheme).
        private const int CheckOnIcon = 123, CheckOffIcon = 125, RadioOnIcon = 127, RadioOffIcon = 129;

        private readonly List<(VisualElement Icon, Func<int> Index)> _icons = new();
        private readonly ResourceManagement.Loaders.MenuIconLoader _iconLoader =
            new(null, LogManager.LoggerFactory.CreateLogger(nameof(EnhancedSettingsScreen)));

        private void Build(VisualElement stage) {
            _panel?.RemoveFromHierarchy();
            _rows.Clear();
            _clicks.Clear();
            _paths.Clear();
            _icons.Clear();
            _sliders.Clear();
            _keys.Clear();
            _scroll = null;
            // The Audio page plays a song to listen to when none is sounding; leaving the page ends it.
            if (_page == SettingsPage.Audio) Audio.MidiPlaybackManager.Instance?.StartPreviewSongAsync(_resources).Forget();
            else Audio.MidiPlaybackManager.Instance?.StopPreviewSong();
            _panel = MovedElements.Box("enhanced-book");
            _panel.EnableInClassList("enhanced-book--touch", Touch);
            _panel.Add(Text(Templates.Format(EnhancedSettingsPages.TitleKey(_page)), "enhanced-book__title"));

            VisualElement content = MovedElements.Box("enhanced-book__content");
            foreach (SettingItem item in EnhancedSettingsPages.ItemsOn(_page)) {
                AddItem(content, item);
            }
            _probe = Line(content, () => string.Empty, () => CheckOffIcon, () => true, () => { });
            _probe.AddToClassList("enhanced-book__probe");
            _rows.RemoveAll(r => r.Row == _probe);
            content.RegisterCallback<GeometryChangedEvent>(_ => FitBookText(content));
            _panel.Add(content);

            _hint = Text(Templates.Format(Touch ? Templates.SettingsHintIdleTouch : Templates.SettingsHintIdle), "enhanced-book__hint");
            _hint.name = "hint";
            _panel.Add(_hint);

            VisualElement buttons = MovedElements.Box("enhanced-book__buttons");
            Row(buttons, () => Templates.Format(Templates.SettingsOk), () => true, Save, "enhanced-book__button").name = "ok";
            Row(buttons, () => Templates.Format(Templates.EnhancedCancel), () => true, Cancel, "enhanced-book__button").name = "cancel";
            buttons.RegisterCallback<GeometryChangedEvent>(_ => FitButtons());
            _panel.Add(buttons);

            VisualElement marks = MovedElements.Box("enhanced-book__bookmarks");
            Texture2D cloth = EnhancedArt.Texture("bookmark");
            foreach (SettingsPage page in EnhancedSettingsPages.Order) {
                SettingsPage p = page;
                Button mark = Row(marks, () => Templates.Format(EnhancedSettingsPages.TitleKey(p)), () => true, () => TurnTo(p),
                    "enhanced-book__bookmark");
                mark.name = "bookmark-" + NameOf(p);
                mark.AddToClassList("enhanced-book__bookmark--" + NameOf(p));
                mark.EnableInClassList("enhanced-book__bookmark--open", p == _page);
                if (cloth != null) mark.style.backgroundImage = Background.FromTexture2D(cloth);
                // One text size for every bookmark on every page (owner), fitted among themselves.
            }
            marks.RegisterCallback<GeometryChangedEvent>(_ => FitBookmarks(marks));
            _panel.Add(marks);

            stage.Add(_panel);
            VisualElement root = stage.panel?.visualTree ?? stage.parent ?? stage;
            root.UnregisterCallback<GeometryChangedEvent>(OnWindowLaidOut);
            root.RegisterCallback<GeometryChangedEvent>(OnWindowLaidOut);
            _panel.RegisterCallback<GeometryChangedEvent>(OnWindowLaidOut);
            Refresh();
            FitButtons();
        }

        // Where the window has no table to the right of the page (4:3), the bookmarks lie over the page's
        // right margin instead of off the window. Measured, not a constant: the room either side of the
        // page against the bookmarks' own width from the theme.
        private void OnWindowLaidOut(GeometryChangedEvent _) {
            if (_panel?.panel == null) return;
            VisualElement stage = _panel.parent;
            VisualElement marks = _panel.Q(className: "enhanced-book__bookmarks");
            float window = _panel.panel.visualTree.layout.width;
            float room = (window - stage.layout.width) / 2;
            float need = 0;
            foreach (VisualElement m in marks.Children()) need = Mathf.Max(need, m.resolvedStyle.width);
            need += marks.resolvedStyle.marginLeft; // they start over the page's torn edge (a negative margin)
            if (float.IsNaN(room) || need <= 0) return;
            _panel.EnableInClassList("enhanced-book--narrow", room < need);
            // Narrower than the page (a square window), the stage is cropped both sides: bring the bookmarks
            // in by the crop so they stay in the window.
            float crop = Mathf.Max(0f, -room);
            if (Mathf.Abs(marks.resolvedStyle.right - crop) > 0.5f || crop == 0f) {
                marks.style.right = crop > 0f ? new StyleLength(crop) : new StyleLength(StyleKeyword.Null);
            }
        }

        /// <summary>One size for every bookmark's name (owner): the largest at which all seven fit a closed
        /// bookmark, measured at the full size so the answer does not depend on what is drawn now.</summary>
        private static void FitBookmarks(VisualElement marks) {
            VisualElement closed = marks.Children().FirstOrDefault(m => !m.ClassListContains("enhanced-book__bookmark--open"));
            Label probe = closed?.Q<Label>("caption");
            if (probe == null || probe.resolvedStyle.fontSize <= 0f || closed.contentRect.width <= 0f) return;
            float full = GameFontText.FontSizePx, now = probe.resolvedStyle.fontSize, scale = 1f;
            foreach (SettingsPage page in EnhancedSettingsPages.Order) {
                float width = probe.MeasureTextSize(Templates.Format(EnhancedSettingsPages.TitleKey(page)), 0,
                    VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x * full / now;
                if (width > 0f) scale = Mathf.Min(scale, closed.contentRect.width / width);
            }
            float size = full * Mathf.Max(scale, GameFontText.MinFitScale);
            foreach (Label caption in marks.Query<Label>("caption").ToList()) {
                if (Mathf.Abs(caption.resolvedStyle.fontSize - size) > 0.1f) caption.style.fontSize = size;
            }
        }

        /// <summary>Every word a bevelled button of the book can carry.</summary>
        private static readonly string[] ButtonKeys = {
            Templates.SettingsOk, Templates.EnhancedCancel, Templates.EnhancedBrowse, Templates.ModsInstalled,
            Templates.ControlsReset, Templates.SettingsYes, Templates.SettingsNo,
        };

        /// <summary>One size for the book's bevelled buttons on every page, as for the lines: measured
        /// against OK's room, which every page has.</summary>
        private void FitButtons() {
            Button ok = _panel?.Q<Button>("ok");
            Label probe = ok?.Q<Label>("caption");
            if (probe == null || probe.resolvedStyle.fontSize <= 0f || ok.contentRect.width <= 0f) return;
            float full = GameFontText.FontSizePx, now = probe.resolvedStyle.fontSize, scale = 1f;
            foreach (string key in ButtonKeys) {
                float width = probe.MeasureTextSize(Templates.Format(key), 0, VisualElement.MeasureMode.Undefined, 0,
                    VisualElement.MeasureMode.Undefined).x * full / now;
                if (width > 0f) scale = Mathf.Min(scale, ok.contentRect.width / width);
            }
            float size = full * Mathf.Max(scale, GameFontText.MinFitScale);
            foreach (Button b in _panel.Query<Button>(className: "enhanced-book__button").ToList()) {
                Label caption = b.Q<Label>("caption");
                if (caption != null && Mathf.Abs(caption.resolvedStyle.fontSize - size) > 0.1f) caption.style.fontSize = size;
            }
        }

        /// <summary>A hidden line laid out like every other: the room a line's words have.</summary>
        private Button _probe;

        /// <summary>
        /// One text size for the lines and headings of every page (owner, 2026-11-26): the largest at
        /// which the longest words of ANY page fit a line, so turning a page never changes it.
        /// </summary>
        private void FitBookText(VisualElement content) {
            Label probe = _probe?.Q<Label>("caption");
            if (probe == null || probe.resolvedStyle.fontSize <= 0f) return;
            float lineRoom = _probe.contentRect.width - probe.layout.x;
            float headingRoom = _probe.contentRect.width;
            if (lineRoom <= 0f) return;
            float full = GameFontText.FontSizePx, now = probe.resolvedStyle.fontSize;
            float scale = 1f;
            foreach ((string text, bool heading) in BookTexts()) {
                float width = probe.MeasureTextSize(text, 0, VisualElement.MeasureMode.Undefined, 0,
                    VisualElement.MeasureMode.Undefined).x * full / now;
                if (width > 0f) scale = Mathf.Min(scale, (heading ? headingRoom : lineRoom) / width);
            }
            float size = full * Mathf.Max(scale, GameFontText.MinFitScale);
            foreach (Label label in content.Query<Label>().ToList()) {
                bool line = label.name == "caption" && label.parent is Button b && b.ClassListContains("enhanced-book__line")
                    && !b.ClassListContains("enhanced-book__keyrow");
                if ((line || label.ClassListContains("enhanced-book__heading")) && Mathf.Abs(label.resolvedStyle.fontSize - size) > 0.5f) {
                    label.style.fontSize = size;
                }
            }
        }

        /// <summary>Every page's line and heading words, each in its longest form ("(restart)" added
        /// where it can be).</summary>
        private static IEnumerable<(string Text, bool Heading)> BookTexts() {
            string Restart(string t) => Templates.Format(Templates.EnhancedPendingKey, ("text", t));
            foreach (SettingsPage page in EnhancedSettingsPages.Order) {
                foreach (SettingItem item in EnhancedSettingsPages.ItemsOn(page)) {
                    string label = Templates.Format(item.LabelKey);
                    switch (item.Kind) {
                        case SettingKind.Check:
                            yield return (item.Id is "enhanced" or "book_font" or "dev_overrides" ? Restart(label) : label, false);
                            break;
                        case SettingKind.Choice:
                            yield return (label, true);
                            if (item.Id == "language") {
                                foreach (string locale in Languages) {
                                    yield return (Templates.Format(Templates.LanguageChoicePendingKey, ("name", LanguageName(locale))), false);
                                }
                            } else {
                                foreach (string key in item.ChoiceKeys) yield return (Templates.Format(key), false);
                            }
                            break;
                        case SettingKind.Slider:
                        case SettingKind.Path:
                            yield return (label, true);
                            break;
                    }
                }
            }
        }

        private void AddItem(VisualElement parent, SettingItem item) {
            int before = parent.childCount;
            AddItemCore(parent, item);
            if (parent.childCount > before) Explains(parent[parent.childCount - 1], item.HintKey);
        }

        /// <summary>The hint line explains the setting under the mouse or the keyboard focus; on a touch
        /// screen, which has no hover, the one last tapped stays explained.</summary>
        private void Explains(VisualElement element, string hintKey) {
            void Show() { if (_hint != null) _hint.text = Templates.Format(hintKey); }
            element.RegisterCallback<PointerEnterEvent>(_ => Show());
            element.RegisterCallback<PointerDownEvent>(_ => Show(), TrickleDown.TrickleDown);
            element.RegisterCallback<FocusInEvent>(_ => Show());
            element.RegisterCallback<PointerLeaveEvent>(e => {
                if (e.pointerType == UnityEngine.UIElements.PointerType.mouse && _hint != null) {
                    _hint.text = Templates.Format(Templates.SettingsHintIdle);
                }
            });
        }

        private void AddItemCore(VisualElement parent, SettingItem item) {
            switch (item.Kind) {
                case SettingKind.Check: {
                    Button line = Line(parent, () => Caption(item), () => CheckOn(item.Id) ? CheckOnIcon : CheckOffIcon,
                        () => Enabled(item), () => Toggle(item.Id));
                    line.name = item.Id;
                    break;
                }
                case SettingKind.Choice: {
                    IReadOnlyList<string> options = ChoicesOf(item);
                    if (options.Count < 2) break; // one language installed: nothing to choose
                    VisualElement group = MovedElements.Box("enhanced-book__group");
                    group.name = item.Id;
                    group.Add(Text(Templates.Format(item.LabelKey), "enhanced-book__heading"));
                    for (int i = 0; i < options.Count; i++) {
                        int choice = i;
                        Button line = Line(group, () => ChoiceCaption(item, options, choice),
                            () => ChoiceOf(item.Id) == choice ? RadioOnIcon : RadioOffIcon,
                            () => Enabled(item), () => SetChoice(item.Id, choice));
                        line.name = item.Id + "-" + choice;
                    }
                    parent.Add(group);
                    break;
                }
                case SettingKind.Slider: {
                    VisualElement group = MovedElements.Box("enhanced-book__group");
                    group.name = item.Id;
                    group.Add(Text(Templates.Format(item.LabelKey), "enhanced-book__heading"));
                    VisualElement row = MovedElements.Box("enhanced-book__slider-row");
                    var slider = new Slider(0f, 1f) { value = LevelOf(item.Id), name = item.Id + "-slider" };
                    slider.AddToClassList("enhanced-book__slider");
                    Label percent = Text(PercentOf(slider.value), "enhanced-book__percent");
                    percent.name = item.Id + "-percent";
                    slider.RegisterValueChangedCallback(e => {
                        SetLevel(item.Id, e.newValue);
                        percent.text = PercentOf(e.newValue);
                    });
                    if (item.Id == "sound_volume") {
                        // Heard when let go, not on every step of a drag.
                        slider.RegisterCallback<PointerCaptureOutEvent>(_ => Audio.MenuSoundService.Cue(Audio.MenuSoundService.PoundSoundId));
                    }
                    _sliders.Add(slider);
                    row.Add(slider);
                    row.Add(percent);
                    group.Add(row);
                    parent.Add(group);
                    break;
                }
                case SettingKind.Path: {
                    VisualElement group = MovedElements.Box("enhanced-book__group");
                    group.AddToClassList("enhanced-book__group--path");
                    group.name = item.Id;
                    group.Add(Text(Templates.Format(item.LabelKey), "enhanced-book__heading"));
                    bool game = item.Id == "game_folder";
                    group.Add(PathLine(() => Shown(game ? _gamePath : _modsPath),
                        () => game ? _gamePath != BakResourceSettings.GamePath : _modsOn && _modsPath != BakResourceSettings.OverridePath));
                    if (FoldersPickable) {
                        Row(group, () => Templates.Format(Templates.EnhancedBrowse), () => game || _modsOn,
                            game ? BrowseGameFolder : BrowseModsFolder, "enhanced-book__button").name = item.Id + "-browse";
                    }
                    if (game) {
                        _folderError = Text(string.Empty, "enhanced-settings__error");
                        group.Add(_folderError);
                    }
                    parent.Add(group);
                    break;
                }
                case SettingKind.Action:
                    Row(parent, () => Templates.Format(item.LabelKey), () => true, () => VisitModsAsync().Forget(),
                        "enhanced-book__button").name = item.Id;
                    break;
                case SettingKind.KeyList:
                    if (_slots != null && !Application.isMobilePlatform) AddKeyList(parent, item.Id);
                    break;
            }
        }

        private string Caption(SettingItem item) {
            string text = Templates.Format(item.LabelKey);
            return Pending(item.Id) ? Templates.Format(Templates.EnhancedPendingKey, ("text", text)) : text;
        }

        /// <summary>Whether a check line shows ticked in the draft (tests read it).</summary>
        internal bool Shows(string id) {
            int dash = id.LastIndexOf('-');
            return dash > 0 && int.TryParse(id[(dash + 1)..], out int n) ? ChoiceOf(id[..dash]) == n : CheckOn(id);
        }

        private bool CheckOn(string id) => id switch {
            "enhanced" => _draft.Master,
            "check_updates" => _draft.CheckForUpdates,
            "restore_shop_buyback_bug" => _draft.RestoreShopBuybackBug,
            "dev_overrides" => _modsOn,
            _ => _draft[EnhancedSettingsPages.FeatureOf(id).Value],
        };

        private void Toggle(string id) {
            switch (id) {
                case "enhanced": _draft.Master = !_draft.Master; break;
                case "check_updates": _draft.CheckForUpdates = !_draft.CheckForUpdates; break;
                case "restore_shop_buyback_bug": _draft.RestoreShopBuybackBug = !_draft.RestoreShopBuybackBug; break;
                case "dev_overrides": _modsOn = !_modsOn; break;
                default: {
                    EnhancedFeature f = EnhancedSettingsPages.FeatureOf(id).Value;
                    _draft[f] = !_draft[f];
                    break;
                }
            }
        }

        /// <summary>A feature needs the master (and the rings full-screen travel); Shadows too. The rest are
        /// Classic's as well.</summary>
        private bool Enabled(SettingItem item) =>
            EnhancedSettingsPages.FeatureOf(item.Id) is { } f ? EnhancedRowEnabled(_draft, f)
            : item.UsableInClassic || _draft.Master;

        private bool Pending(string id) => id switch {
            "enhanced" => FontChangePending(_draft) || ModeChangePending(_draft),
            "book_font" => FontChangePending(_draft),
            "dev_overrides" => _modsOn != BakResourceSettings.OverrideEnabled,
            _ => false,
        };

        private static IReadOnlyList<string> Languages =>
            GameData.Resources.Text.LanguageChoice.Available(LanguagePacks.Installed(), Debug.isDebugBuild);

        /// <summary>A choice's options: the table's, or for Language the installed packs (locales).</summary>
        private static IReadOnlyList<string> ChoicesOf(SettingItem item) =>
            item.Id == "language" ? Languages : item.ChoiceKeys;

        private string ChoiceCaption(SettingItem item, IReadOnlyList<string> options, int choice) {
            if (item.Id != "language") return Templates.Format(options[choice]);
            string name = LanguageName(options[choice]);
            // Chosen but not what is running: it takes effect at the next start.
            return ChoiceOf(item.Id) == choice && options[choice] != LanguagePacks.Current.Locale
                ? Templates.Format(Templates.LanguageChoicePendingKey, ("name", name)) : name;
        }

        // "Deutsch (beta)" for a pack its header marks as not yet reviewed.
        private static string LanguageName(string locale) {
            string name = GameData.Resources.Text.LanguageChoice.DisplayName(locale);
            return LanguagePacks.IsBeta(locale) ? Templates.Format(Templates.LanguageBetaKey, ("name", name)) : name;
        }

        /// <summary>The language chosen on this visit (a locale); saved with OK for the next start.</summary>
        private string _language;

        private int ChoiceOf(string id) => id switch {
            "shadows" => (int)_draft.Shadows,
            "language" => IndexOf(Languages, _language),
            _ => (int)_draft.CombatSpeed,
        };

        private static int IndexOf(IReadOnlyList<string> list, string value) {
            for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
            return -1;
        }

        private void SetChoice(string id, int choice) {
            if (id == "shadows") _draft.Shadows = (ShadowMode)choice;
            else if (id == "language") _language = Languages[choice];
            else _draft.CombatSpeed = (GameData.Resources.Combat.CombatSpeed)choice;
        }

        private static string PercentOf(float level) => Templates.Percent(Mathf.RoundToInt(level * 100f));

        private float LevelOf(string id) => id == "music_volume" ? _draft.MusicVolume : _draft.SoundVolume;

        /// <summary>A slider moved: into the draft, and heard at once (Cancel puts the saved level back).</summary>
        private void SetLevel(string id, float level) {
            if (id == "music_volume") _draft.MusicVolume = Audio.AudioLevels.Music = level;
            else _draft.SoundVolume = Audio.AudioLevels.Sound = level;
        }

        /// <summary>A slider has no left and right of its own under the page's arrow keys, which move between
        /// lines: Enter steps it a quarter up, round from full to silent.</summary>
        private static void StepSlider(Slider slider, string id) {
            float next = slider.value >= 0.999f ? 0f : Mathf.Min(1f, Mathf.Round(slider.value * 4f + 1f) / 4f);
            slider.value = next;
            if (id == "sound_volume") Audio.MenuSoundService.Cue(Audio.MenuSoundService.PoundSoundId);
        }

        /// <summary>One line of a page: the original's check box or radio button and its words, the whole
        /// line the target.</summary>
        private Button Line(VisualElement parent, Func<string> caption, Func<int> icon, Func<bool> enabled, Action click) {
            Button line = Row(parent, caption, enabled, click, "enhanced-book__line");
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("enhanced-book__icon");
            line.Insert(0, box);
            // Held, the toggle shows its +1 frame, as Preferences' toggles do (menu_type_3_4's highlight).
            bool held = false;
            Func<int> face = () => icon() + (held ? 1 : 0);
            ResourceManagement.Loaders.UserInterfaceLoader.RegisterPressFace(line, pressed => {
                held = pressed && enabled();
                _iconLoader.LoadAndApplyIcon(box, face());
            });
            _icons.Add((box, face));
            return line;
        }

        private void ApplyIcons() {
            foreach ((VisualElement box, Func<int> index) in _icons) {
                _iconLoader.LoadAndApplyIcon(box, index());
            }
        }

        private void OnDestroy() => _iconLoader.ReleaseHandles();

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

        private Button Row(VisualElement parent, Func<string> caption, Func<bool> enabled, Action toggle, string cls) {
            var row = new Button();
            if (cls != "enhanced-book__line" && cls != "enhanced-book__bookmark") {
                row.AddToClassList("text-button");
                // Held, it wears the original's pressed pens, like every TextButton outside the book.
                ResourceManagement.Loaders.UserInterfaceLoader.RegisterTextPressFace(row, () => !enabled());
            }
            row.AddToClassList(cls);
            Label label = GameFontText.Caption(row, string.Empty);
            // Lines and bookmarks are sized by the book itself (FitBookText, the bookmarks' FitTogether),
            // not by the screen-wide fit, which one long line would let shrink a whole page.
            if (cls is "enhanced-book__line" or "enhanced-book__bookmark" or "enhanced-book__button") {
                label.RemoveFromClassList(GameFontText.CaptionClass);
            }
            Action click = () => {
                if (Rebinding) return;
                toggle();
                if (_panel != null) Refresh();
            };
            row.clicked += click;
            _clicks[row] = click;
            parent.Add(row);
            _rows.Add((row, caption, enabled));
            return row;
        }

        private void Refresh() {
            foreach ((Button row, Func<string> caption, Func<bool> enabled) in _rows) {
                Label label = row.Q<Label>("caption");
                if (label != null) label.text = caption();
                row.SetEnabled(enabled());
            }
            foreach ((Label key, Func<string> text) in _keys) key.text = text();
            foreach ((Label line, Func<string> text, Func<bool> pending) in _paths) {
                string t = text();
                line.text = pending() ? Templates.Format(Templates.EnhancedPendingKey, ("text", t)) : t;
            }
            ApplyIcons();
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
                bool ok = GameData.Setup.GameFilesCheck.Check(folder).AllOk;
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
            if (!string.IsNullOrEmpty(_language) && _language != LanguagePacks.Current.Locale) {
                BakResourceSettings.Language = _language;
            }
            _navigator.Pop().Forget();
        }

        private void Cancel() {
            if (_bindings != null) InputBindings.Restore(_slots, _bindings);
            _navigator.Pop().Forget();
        }

        /// <summary>Whether anything differs from what the book opened with: a setting, a folder, a key.</summary>
        internal bool HasChanges =>
            _draft != null && (!_draft.SameAs(EnhancedOptionsDraft.Load())
                || (_gamePath, _modsOn, _modsPath) != _openedFolders
                || _language != LanguagePacks.Current.Locale
                || (_bindings != null && InputBindings.Differs(_slots, _bindings)));

        /// <summary>"Keep your changes?" over the page: Yes is OK, No is Cancel, Escape goes back to the book.</summary>
        private void AskToKeep() {
            var scrim = new VisualElement { name = "keep-changes", pickingMode = PickingMode.Position };
            scrim.AddToClassList("enhanced-book__scrim");
            VisualElement box = MovedElements.Box("enhanced-book__question");
            box.Add(Text(Templates.Format(Templates.SettingsKeepChanges), "enhanced-book__question-text"));
            VisualElement buttons = MovedElements.Box("enhanced-book__question-buttons");
            box.Add(buttons);
            scrim.Add(box);
            _panel.Add(scrim);
            Button yes = Row(buttons, () => Templates.Format(Templates.SettingsYes), () => true, () => { CloseQuestion(); Save(); }, "enhanced-book__button");
            yes.name = "keep-yes";
            Button no = Row(buttons, () => Templates.Format(Templates.SettingsNo), () => true, () => { CloseQuestion(); Cancel(); }, "enhanced-book__button");
            no.name = "keep-no";
            Refresh();
            FitButtons();
            var widgets = new List<NavWidget>();
            foreach (Button b in new[] { yes, no }) {
                Button button = b;
                widgets.Add(new NavWidget(button, button.Q<Label>("caption")?.text ?? string.Empty, () => RowRect(button),
                    () => ClickOf(button)?.Invoke(), null));
            }
            _questionLayer = new NavigableLayer("enhanced-settings-question", CaptureMode.Exclusive, widgets, CloseQuestion, _cursor);
            _stack?.Push(_questionLayer);
        }

        private void CloseQuestion() {
            if (_questionLayer != null) {
                _stack?.Remove(_questionLayer);
                _questionLayer = null;
            }
            _panel?.Q("keep-changes")?.RemoveFromHierarchy();
            _rows.RemoveAll(r => r.Row.name is "keep-yes" or "keep-no");
        }

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

        /// <summary>Escape over an open folder picker closes the picker, not the screen under it; on the
        /// Controls page it goes back to the settings, and while a key is awaited it is the rebind's.</summary>
        private void CancelOrClosePicker() {
            if (Rebinding) {
                return;
            }
            if (FileBrowser.IsOpen) {
                FileBrowser.HideDialog(invokeCancelCallback: true);
                return;
            }
            if (HasChanges) {
                AskToKeep();
                return;
            }
            Cancel();
        }

        // ---- The Controls page (TASK-1080) ----

        private InputActionRebindingExtensions.RebindingOperation _rebind;
        private RebindSlot _waiting;
        private int _rebindEndedFrame = -1;

        /// <summary>A key is awaited, or was taken this frame: that press must not also work the page
        /// (Enter would start the next rebind, Escape would leave it). A mouse button taken is held off until
        /// it is let go: its release would otherwise click the row under the pointer.</summary>
        private bool Rebinding {
            get {
                if (_rebind != null || _rebindEndedFrame == Time.frameCount) return true;
                if (_heldOffMouse && MouseBusy()) return true;
                _heldOffMouse = false;
                return false;
            }
        }

        private bool _heldOffMouse;

        private static bool MouseBusy() {
            Mouse mouse = Mouse.current;
            if (mouse == null) return false;
            foreach (UnityEngine.InputSystem.Controls.ButtonControl b in new[] {
                         mouse.leftButton, mouse.rightButton, mouse.middleButton, mouse.forwardButton, mouse.backButton }) {
                if (b.isPressed || b.wasReleasedThisFrame) return true;
            }
            return false;
        }

        /// <summary>"Walk forward: W", "Walk forward (2nd key): Up Arrow", "Hotkey M: M", or the prompt
        /// while that row waits for a key.</summary>
        internal static string ControlText(RebindSlot slot, bool waiting) =>
            Templates.Format(Templates.ControlsRow, ("action", ActionText(slot)), ("key", KeyText(slot, waiting)));

        /// <summary>"Walk forward", "Walk forward (2nd key)", "Hotkey M": the key list's left column.</summary>
        internal static string ActionText(RebindSlot slot) {
            string action = slot.Letter != null
                ? Templates.Format(slot.LabelKey, ("letter", slot.Letter))
                : Templates.Format(slot.LabelKey);
            return slot.Second ? Templates.Format(Templates.ControlsSecondKey, ("action", action)) : action;
        }

        /// <summary>The key, or the prompt while the row waits for one: the key list's right column.</summary>
        internal static string KeyText(RebindSlot slot, bool waiting) =>
            waiting ? Templates.Format(Templates.ControlsPressAKey) : KeyName(slot.Path);

        private readonly List<(Label Key, Func<string> Text)> _keys = new();

        /// <summary>The heading a slot is listed under.</summary>
        internal static string HeadingOf(RebindSlot slot) => slot.Section ?? slot.Group switch {
            InputBindings.Walk => Templates.ControlsGroupWalk,
            InputBindings.Combat => Templates.ControlsGroupCombat,
            _ => Templates.ControlsGroupMenus,
        };

        private ScrollView _scroll;

        // The key's own name on this keyboard ("M" for a hotkey's "#(m)"), else the path's.
        internal static string KeyName(string path) =>
            InputSystem.FindControl(path)?.displayName
            ?? InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);

        private void AddKeyList(VisualElement parent, string id) {
            // Some seventy keys: they scroll, as the Mods list does.
            _scroll = new ScrollView(ScrollViewMode.Vertical) {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                name = id,
            };
            _scroll.AddToClassList("enhanced-settings__scroll");
            _scroll.AddToClassList("enhanced-book__keys");
            _scroll.contentViewport.style.overflow = Overflow.Hidden;
            VisualElement column = new VisualElement(); // not __column: its zero basis collapses inside a scroll
            column.AddToClassList("enhanced-settings__list");
            _scroll.Add(column);
            parent.Add(_scroll);
            string heading = null;
            foreach (RebindSlot slot in _slots) {
                RebindSlot s = slot;
                if (HeadingOf(s) != heading) {
                    heading = HeadingOf(s);
                    column.Add(Text(Templates.Format(heading), "enhanced-book__heading"));
                }
                Button row = Row(column, () => ActionText(s), () => true, () => StartRebind(s), "enhanced-book__line");
                row.AddToClassList("enhanced-book__keyrow");
                row.name = "control-" + s.Id;
                var key = new Label { name = "key", pickingMode = PickingMode.Ignore };
                key.AddToClassList("enhanced-book__key");
                GameFontText.Apply(key);
                row.Add(key);
                _keys.Add((key, () => KeyText(s, _waiting == s)));
                ScrollIntoView(row);
            }
            column.Add(Text(Templates.Format(Templates.ControlsSkipNote), "enhanced-settings__label"));
            ScrollIntoView(Row(column, () => Templates.Format(Templates.ControlsReset), () => true, () => InputBindings.Reset(_slots),
                "enhanced-book__button"));
        }

        // A row reached with the keys scrolls into view; the cursor follows it there (RowRect).
        private void ScrollIntoView(Button row) =>
            row.RegisterCallback<FocusInEvent>(_ => _scroll?.ScrollTo(row));

        // The press is read by the Input System's own rebinding: any key, a gamepad button or any mouse
        // button; click and inspect take only a mouse button. Not pointer motion. Escape keeps the old key.
        // A key another row holds swaps with it.
        private void StartRebind(RebindSlot slot) {
            _waiting = slot;
            slot.Action.Disable();
            InputActionRebindingExtensions.RebindingOperation op = slot.Action.PerformInteractiveRebinding(slot.Index)
                .WithControlsExcluding("<Pointer>/position")
                .WithControlsExcluding("<Pointer>/delta")
                .WithControlsExcluding("<Pointer>/press")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape");
            if (slot.MouseButton) op.WithControlsHavingToMatchPath("<Mouse>");
            _rebind = op
                .OnApplyBinding((_, path) => InputBindings.Set(_slots, slot, path))
                .OnComplete(_ => EndRebind(slot))
                .OnCancel(_ => EndRebind(slot))
                .Start();
            Refresh();
        }

        private void EndRebind(RebindSlot slot) {
            _rebind?.Dispose();
            _rebind = null;
            _waiting = null;
            _rebindEndedFrame = Time.frameCount;
            _heldOffMouse = MouseBusy();
            slot.Action.Enable();
            if (_panel != null) Refresh();
        }

        internal static Label Text(string text, string cls) {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            GameFontText.Apply(label);
            return label;
        }
    }
}

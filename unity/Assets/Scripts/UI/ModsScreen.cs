namespace BakAgain.UI {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.ExceptionServices;
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
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using Templates = GameData.Resources.Text.UiTemplates;

    /// <summary>
    /// The Mods screen's edits to <c>mods.json</c> (mod system spec 4.6), kept until <see cref="Save"/>.
    /// Built from the state file and the installed folders, in the order <see cref="ModResolver"/>
    /// reads them: the file's order, with installed mods it does not list on top and enabled.
    /// Language mods are in the list (Save keeps their lines) but the screen leaves them out: they
    /// are chosen in the language setting.
    /// </summary>
    public sealed class ModsDraft {
        /// <summary>One installed mod as the draft has it.</summary>
        public sealed class Entry {
            public Entry(InstalledMod mod, bool enabled) {
                Mod = mod;
                Enabled = enabled;
            }

            public InstalledMod Mod { get; }
            public bool Enabled { get; internal set; }
            internal string Id => Mod.Manifest.Id;
        }

        private readonly List<Entry> _mods;
        private readonly List<InstalledMod> _uninstalled = new();

        private ModsDraft(List<Entry> mods) {
            _mods = mods;
        }

        /// <summary>Highest priority first, without the mods marked for uninstall.</summary>
        public IReadOnlyList<Entry> Mods => _mods;

        /// <summary>The installed mods and their state. A corrupt state file counts as empty, as at start.</summary>
        public static ModsDraft Load() {
            List<InstalledMod> installed = Directory.Exists(ModLayers.ModsRoot) ? ModLayers.ReadInstalled() : new List<InstalledMod>();
            Dictionary<string, InstalledMod> unlisted = installed.GroupBy(m => m.Manifest.Id).ToDictionary(g => g.Key, g => g.First());
            var mods = new List<Entry>();
            foreach (ModStateEntry e in ModLayers.ReadState()) {
                if (unlisted.Remove(e.Id, out InstalledMod mod)) {
                    mods.Add(new Entry(mod, e.Enabled));
                }
            }
            mods.InsertRange(0, unlisted.Values.Select(m => new Entry(m, true)));
            return new ModsDraft(mods);
        }

        private Entry Find(string id) => _mods.First(e => e.Id == id);

        public void Toggle(string id) {
            Entry e = Find(id);
            e.Enabled = !e.Enabled;
        }

        public void MoveUp(string id) => Move(id, -1);

        public void MoveDown(string id) => Move(id, +1);

        /// <summary>Swaps the mod with its neighbour on the screen: language mods are not listed there, so they are stepped over.</summary>
        private void Move(string id, int step) {
            int i = _mods.IndexOf(Find(id));
            int j = i + step;
            while (j >= 0 && j < _mods.Count && _mods[j].Mod.Manifest.IsLanguage) {
                j += step;
            }
            if (j < 0 || j >= _mods.Count) {
                return;
            }
            (_mods[i], _mods[j]) = (_mods[j], _mods[i]);
        }

        /// <summary>Takes the mod off the list; <see cref="Save"/> deletes its folder.</summary>
        public void Uninstall(string id) {
            Entry e = Find(id);
            _mods.Remove(e);
            _uninstalled.Add(e.Mod);
        }

        /// <summary>
        /// Installs a mod zip into <see cref="ModLayers.ModsRoot"/> now (an install cannot wait for Save:
        /// it replaces the folder). A new mod goes to the top, enabled; a re-installed one keeps its place
        /// and its on/off state. Throws <see cref="InvalidDataException"/> listing what is wrong with the zip.
        /// </summary>
        public void Install(string zipPath) {
            InstalledMod mod = ModArchive.Install(zipPath, ModLayers.ModsRoot);
            string folder = Path.GetFullPath(mod.Folder);
            _uninstalled.RemoveAll(m => Path.GetFullPath(m.Folder) == folder);
            int at = _mods.FindIndex(e => e.Id == mod.Manifest.Id);
            if (at < 0) {
                _mods.Insert(0, new Entry(mod, true));
            } else {
                _mods[at] = new Entry(mod, _mods[at].Enabled);
            }
        }

        /// <summary>Why the mod is not active, as resolved at start.</summary>
        public IReadOnlyList<ModProblem> ProblemsFor(string id) =>
            ModLayers.Current.Problems.Where(p => p.ModId == id).ToList();

        /// <summary>
        /// Whether the draft, once saved, would start with other active mods than the running ones:
        /// the change waits for the next start.
        /// </summary>
        public bool Changed {
            get {
                ModResolution next = ModResolver.Resolve(_mods.Select(e => e.Mod).ToList(), State().ToList(), ModLayers.Mode, ModLayers.GameVersion);
                return !next.Active.Select(Key).SequenceEqual(ModLayers.Current.Active.Select(Key));
            }
        }

        private static string Key(InstalledMod m) => m.Manifest.Id + " " + m.Manifest.Version;

        private IEnumerable<ModStateEntry> State() =>
            _mods.Select(e => new ModStateEntry(e.Id, e.Mod.Manifest.Version.ToString(), e.Enabled));

        /// <summary>Deletes an uninstalled mod's folder; tests make it fail.</summary>
        internal Action<string> DeleteFolder { get; set; } = folder => Directory.Delete(folder, true);

        /// <summary>
        /// Deletes the uninstalled mods' folders, then writes <c>mods.json</c>. A folder that could not be
        /// deleted is written disabled: unlisted, it would start enabled at the top. Throws the first
        /// delete failure after writing.
        /// </summary>
        public void Save() {
            string root = Path.GetFullPath(ModLayers.ModsRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var kept = new List<InstalledMod>();
            Exception failure = null;
            foreach (InstalledMod mod in _uninstalled) {
                string folder = Path.GetFullPath(mod.Folder);
                // Only ever a mod folder directly inside the mods folder.
                if (!ModLayers.IsModFolder(folder) || Path.GetDirectoryName(folder) != root || !Directory.Exists(folder)) {
                    continue;
                }
                try {
                    DeleteFolder(folder);
                } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                    kept.Add(mod);
                    failure ??= e;
                }
            }
            ModStateFile.Write(ModLayers.StatePath,
                State().Concat(kept.Select(m => new ModStateEntry(m.Manifest.Id, m.Manifest.Version.ToString(), false))));
            _uninstalled.Clear();
            _uninstalled.AddRange(kept);
            if (failure != null) {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    /// <summary>
    /// The installed mods (mod system spec 4.6), opened from the Enhanced settings: enable, order,
    /// uninstall and, on desktop, install from a zip. In the Enhanced settings' look. Nothing is saved
    /// until Save, and the mod set changes at the next start, which the captions say.
    /// </summary>
    public sealed class ModsScreen : Navigation.ScreenBase {
        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger(nameof(ModsScreen));

        private Navigation.IScreenNavigator _navigator;
        private InputLayerStack _stack;
        private ICursorManager _cursor;
        private NavigableLayer _layer;
        private VisualElement _panel;
        private ScrollView _scroll;
        private Label _error;
        private ModsDraft _draft;
        private bool _pending;
        // The mod whose Uninstall was pressed once; any other press disarms it.
        private string _armed, _armedAtPress;
        private readonly List<(Button Row, Func<string> Caption, Func<bool> Enabled)> _rows = new();
        private readonly Dictionary<Button, Action> _clicks = new();

        [Inject]
        public void Construct(Navigation.IScreenNavigator navigator, InputLayerStack stack, ICursorManager cursor) {
            _navigator = navigator;
            _stack = stack;
            _cursor = cursor;
        }

        /// <summary>A problem's line under its mod: "Needs hd-trees".</summary>
        internal static string ProblemText(ModProblem p) =>
            Templates.Format(p.Kind switch {
                ModProblemKind.MissingDependency => Templates.ModsProblemMissingDependency,
                ModProblemKind.DependencyVersion => Templates.ModsProblemDependencyVersion,
                ModProblemKind.Conflict => Templates.ModsProblemConflict,
                _ => Templates.ModsProblemGameVersion,
            }, ("other", p.Other));

        protected override UniTask BuildWhileHiddenAsync() {
            if (GetComponent<UIDocument>()?.rootVisualElement == null) {
                return UniTask.CompletedTask;
            }
            _draft = ModsDraft.Load();
            _armed = null;
            Build();
            return UniTask.CompletedTask;
        }

        protected override void OnAfterShow() {
            _layer = new NavigableLayer("mods", CaptureMode.Exclusive, Widgets(), CancelOrClosePicker, _cursor);
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

        private List<NavWidget> Widgets() {
            var widgets = new List<NavWidget>();
            // Fixed rows first: a mod row scrolled out of view keeps its rect, under Install or Save, and
            // the layer resolves focus to the FIRST widget under the cursor.
            foreach ((Button row, Func<string> _, Func<bool> _) in _rows.OrderBy(r => _scroll.Contains(r.Row) ? 1 : 0)) {
                Button b = row;
                widgets.Add(new NavWidget(b, b.Q<Label>("caption")?.text ?? string.Empty, () => EnhancedSettingsScreen.CanonicalRect(GetComponent<UIDocument>(), b),
                    () => { if (b.enabledSelf && !FileBrowser.IsOpen) ClickOf(b)?.Invoke(); }, null));
            }
            return widgets;
        }

        private Action ClickOf(Button b) => _clicks.TryGetValue(b, out Action a) ? a : null;

        /// <summary>The list changed shape (a move, an uninstall, an install): new rows, new widgets.</summary>
        private void Rebuild() {
            Build();
            _layer?.SetWidgets(Widgets());
        }

        private void Build() {
            _panel?.RemoveFromHierarchy();
            _rows.Clear();
            _clicks.Clear();
            _panel = MovedElements.Box("enhanced-settings");
            _panel.Add(EnhancedSettingsScreen.Text(Templates.Format(Templates.ModsTitle), "enhanced-settings__title"));

            // The mods scroll; Install, Save and Cancel below them stay in view.
            // The theme does not carry Unity's default ScrollView styles, so the clip is set here; with no
            // scroller style, the wheel, a touch drag and focus do the scrolling.
            _scroll = new ScrollView(ScrollViewMode.Vertical) {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
            };
            _scroll.style.flexGrow = 1;
            _scroll.style.flexBasis = 0; // the space left, not the list's height
            _scroll.contentViewport.style.overflow = Overflow.Hidden;
            VisualElement list = new VisualElement(); // not __column: its zero basis collapses inside a scroll
            _scroll.Add(list);
            _panel.Add(_scroll);

            List<ModsDraft.Entry> shown = _draft.Mods.Where(e => !e.Mod.Manifest.IsLanguage).ToList();
            if (shown.Count == 0) {
                list.Add(EnhancedSettingsScreen.Text(Templates.Format(Templates.ModsNone), "enhanced-settings__label"));
                if (Application.platform == RuntimePlatform.Android) {
                    Label hint = EnhancedSettingsScreen.Text(Templates.Format(Templates.ModsInboxHint), "enhanced-settings__label");
                    hint.style.height = StyleKeyword.Auto; // it wraps
                    list.Add(hint);
                }
            }
            foreach (ModsDraft.Entry entry in shown) {
                ModsDraft.Entry e = entry;
                string id = e.Mod.Manifest.Id;
                Row(list, () => ModCaption(e), () => true, () => _draft.Toggle(id));
                VisualElement buttons = MovedElements.Box("enhanced-settings__buttons");
                Row(buttons, () => Templates.Format(Templates.ModsUp), () => shown[0] != e, () => { _draft.MoveUp(id); Rebuild(); });
                Row(buttons, () => Templates.Format(Templates.ModsDown), () => shown[shown.Count - 1] != e,
                    () => { _draft.MoveDown(id); Rebuild(); });
                Row(buttons, () => Templates.Format(_armed == id ? Templates.ModsUninstallConfirm : Templates.ModsUninstall), () => true,
                    () => Uninstall(id));
                list.Add(buttons);
                foreach (ModProblem p in _draft.ProblemsFor(id)) {
                    list.Add(EnhancedSettingsScreen.Text(ProblemText(p), "enhanced-settings__error"));
                }
            }
            // A row reached by keyboard or gamepad scrolls into view with the lines either side of it: a
            // line still out of view keeps its rect under Install or Save, and focus would jump there.
            foreach ((Button row, Func<string> _, Func<bool> _) in _rows) {
                VisualElement line = row.parent == list ? row : row.parent;
                row.RegisterCallback<FocusInEvent>(_ => {
                    int i = list.IndexOf(line);
                    if (i + 1 < list.childCount) _scroll.ScrollTo(list[i + 1]);
                    if (i > 0) _scroll.ScrollTo(list[i - 1]);
                    _scroll.ScrollTo(line);
                });
            }

            if (EnhancedSettingsScreen.FoldersPickable) {
                Row(_panel, () => Templates.Format(Templates.ModsInstallFile), () => true, InstallFromFile);
            }
            _error = EnhancedSettingsScreen.Text(string.Empty, "enhanced-settings__error");
            _panel.Add(_error);

            VisualElement bottom = MovedElements.Box("enhanced-settings__buttons");
            Row(bottom, () => Pending(Templates.Format(Templates.EnhancedSave)), () => true, Save);
            Row(bottom, () => Templates.Format(Templates.EnhancedCancel), () => true, Cancel);
            _panel.Add(bottom);

            CanonicalStage.GetOrCreate(GetComponent<UIDocument>().rootVisualElement, null).Add(_panel);
            Refresh();
        }

        /// <summary>"<c>{text}</c> (restart)" while the draft would start other mods.</summary>
        private string Pending(string text) =>
            _pending ? Templates.Format(Templates.EnhancedPendingKey, ("text", text)) : text;

        /// <summary>"[x] HD Trees 1.0.0", with "(restart)" while the draft would start other mods.</summary>
        private string ModCaption(ModsDraft.Entry e) =>
            Pending(Templates.Format(Templates.ModsRow, ("on", e.Enabled ? "x" : " "),
                ("name", e.Mod.Manifest.Name), ("version", e.Mod.Manifest.Version.ToString())));

        private void Row(VisualElement parent, Func<string> caption, Func<bool> enabled, Action action) {
            var row = new Button();
            row.AddToClassList("text-button");
            row.AddToClassList("enhanced-settings__row");
            GameFontText.Caption(row, string.Empty);
            Action click = () => {
                _armedAtPress = _armed;
                _armed = null;
                action();
                Refresh();
            };
            row.clicked += click;
            _clicks[row] = click;
            parent.Add(row);
            _rows.Add((row, caption, enabled));
        }

        private void Refresh() {
            _pending = _draft.Changed;
            foreach ((Button row, Func<string> caption, Func<bool> enabled) in _rows) {
                Label label = row.Q<Label>("caption");
                if (label != null) label.text = caption();
                row.SetEnabled(enabled());
            }
        }

        /// <summary>The first press asks, the second marks the mod; Save deletes it.</summary>
        private void Uninstall(string id) {
            if (_armedAtPress != id) {
                _armed = id;
                return;
            }
            _draft.Uninstall(id);
            Rebuild();
        }

        private void InstallFromFile() =>
            MetaMenu.PickZip(Templates.Format(Templates.ModsInstallFile), zip => {
                try {
                    _draft.Install(zip);
                } catch (Exception e) {
                    // Any failure, as at start: a bad entry name can throw ArgumentException or NotSupportedException.
                    Logger.LogError("Mod {Zip} could not be installed: {Errors}", zip, e.Message);
                    _error.text = e.Message;
                    return;
                }
                Rebuild();
            });

        private void Save() {
            try {
                _draft.Save();
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                Logger.LogError("Mods could not be saved: {Reason}", e.Message);
                _error.text = e.Message;
                return;
            }
            _navigator.Pop().Forget();
        }

        private void Cancel() => _navigator.Pop().Forget();

        /// <summary>Escape over an open file picker closes the picker, not the screen under it.</summary>
        private void CancelOrClosePicker() {
            if (FileBrowser.IsOpen) {
                FileBrowser.HideDialog(invokeCancelCallback: true);
                return;
            }
            Cancel();
        }
    }
}

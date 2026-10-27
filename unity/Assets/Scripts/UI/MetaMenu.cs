namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using GameData.Resources.Text;
    using GameData.Setup;
    using Microsoft.Win32;
    using SimpleFileBrowser;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The first-run setup wizard (TASK-1014, spec docs/specs/2026-10-27-game-files-setup-design.md):
    /// one parchment card that renders <see cref="Flow"/>'s step. It runs before anything of the game
    /// exists, so it uses only the port's own font, textures and texts.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MetaMenu : MonoBehaviour {
        private const string ResourceArchiveFilename = "KRONDOR.001";
        private const string GogUrl = "https://www.gog.com/game/betrayal_at_krondor";
        private const string ForumUrl = "https://forum.bakagain.org/viewforum.php?f=4";
        private const string HelpUrl = "https://bakagain.org/setup.html#";
        private const int CopyNeedsMb = 14;

        private readonly AwaitableCompletionSource _completionSource = new();
        private VisualElement _top;
        private VisualElement _body;
        private VisualElement _buttons;
        private VisualElement _links;
        private string _found;  // desktop: an install found without asking, offered on the Find step
        private string _chosen; // the folder Play stores as GamePath
        private bool _done;

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger<MetaMenu>();

        internal SetupFlow Flow { get; } = new();

        internal static string DefaultOverridePath => Path.Join(Application.persistentDataPath, "overrides");

        private static bool Android => Application.platform == RuntimePlatform.Android;

        private static string Original => Path.Combine(Application.persistentDataPath, "original");

        private static string T(string key, params (string Name, object Value)[] args) => LanguagePacks.BootText(key, args);

        private void OnEnable() => FileBrowser.SingleClickMode = true;

        /// <summary>Opens on Welcome, or on Check with <paramref name="existing"/> when the stored folder failed it.</summary>
        public void Show(GameFilesReport existing) {
            Flow.Start(existing);
            _chosen = existing == null ? null : BakResourceSettings.GamePath;
            Render();
        }

        public Awaitable WaitForDialogAsync() => _completionSource.Awaitable;

        /// <summary>
        /// The desktop places an install is looked for, in order: GOG's own record of it on Windows,
        /// then the default install folders (GOG, Heroic, Wine, Lutris).
        /// </summary>
        internal static IReadOnlyList<string> DesktopCandidates() {
            var list = new List<string>();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games\1207660953")) {
                if (key?.GetValue("PATH") is { } path) list.Add(path.ToString());
            }
            list.Add(@"C:\GOG Games\Betrayal at Krondor");
#else
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            list.Add(Path.Combine(home, "GOG Games", "Betrayal at Krondor"));
            list.Add(Path.Combine(home, "Games", "Heroic", "Betrayal at Krondor"));
            list.Add(Path.Combine(home, ".wine", "drive_c", "GOG Games", "Betrayal at Krondor"));
            list.Add(Path.Combine(home, "Games", "betrayal-at-krondor", "drive_c", "GOG Games", "Betrayal at Krondor"));
            list.Add(Path.Combine(home, "Games", "betrayal-at-krondor"));
#endif
            return list;
        }

        private static string FindInstalled() =>
            DesktopCandidates().Select(c => GameFilesSearch.FindGameFolder(c)).FirstOrDefault(f => f != null && GameFilesCheck.Check(f).AllOk);

        internal void Render() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _top = root.Q("Top");
            _body = root.Q<ScrollView>("Body");
            _buttons = root.Q("Buttons");
            _links = root.Q("Links");
            foreach (VisualElement e in new[] { _top, _body, _buttons, _links }) e.Clear();

            int n = Flow.Step switch { SetupStep.Welcome => 1, SetupStep.Find => 2, SetupStep.Check => 3, _ => 0 };
            Label stepLine = root.Q<Label>("StepLine");
            stepLine.style.display = n == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            stepLine.text = n == 3 && Flow.SourceName != null
                ? T(UiTemplates.BootStepOf, ("n", n)) + " · " + T(UiTemplates.BootFrom, ("name", Flow.SourceName))
                : T(UiTemplates.BootStepOf, ("n", n));

            switch (Flow.Step) {
                case SetupStep.Welcome:
                    Text(UiTemplates.BootWelcomeTitle, "setup-title");
                    Text(UiTemplates.BootWelcomeBody);
                    Text(UiTemplates.BootNoGame, "setup-text", "setup-dim");
                    Primary(UiTemplates.BootFindFiles, FindFiles);
                    Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
                    Link(UiTemplates.BootBuyLink, () => Application.OpenURL(GogUrl));
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Find when Android:
                    Text(UiTemplates.BootChooseFolderTitle, "setup-title");
                    Text(UiTemplates.BootChooseFolderBodyAndroid);
                    Text(UiTemplates.BootDownloadNote, "setup-text", "setup-dim");
                    Note(Flow.Message);
                    Primary(UiTemplates.BootChooseFolder, PickAndroidFolder);
                    Link(UiTemplates.BootPhoneHelpLink, Flow.OpenHelp);
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Find when _found != null:
                    Text(UiTemplates.BootFoundTitle, "setup-title");
                    Text(UiTemplates.BootFoundBody);
                    Secondary(_found);
                    Note(Flow.Message);
                    Primary(UiTemplates.BootUseThis, () => UseFolder(Path.GetFileName(_found), _found));
                    AddButton(UiTemplates.BootChooseAnother, "setup-outline", Browse, _buttons);
                    Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Find:
                    Text(UiTemplates.BootChooseFolderTitle, "setup-title");
                    Text(UiTemplates.BootChooseFolderBodyDesktop);
                    Text(UiTemplates.BootCaseNote, "setup-text", "setup-dim");
                    Note(Flow.Message);
                    Primary(UiTemplates.BootBrowse, Browse);
                    Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Check:
                    RenderCheck();
                    break;
                case SetupStep.Help:
                    AddButton(UiTemplates.BootBack, "setup-link", Flow.Back, _top);
                    Text(Android ? UiTemplates.BootHelpTitleAndroid : UiTemplates.BootHelpTitleDesktop, "setup-title");
                    Text(Android ? UiTemplates.BootHelpBodyAndroid
                        : Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor
                            ? UiTemplates.BootHelpBodyWindows : UiTemplates.BootHelpBodyLinux);
                    AddButton(UiTemplates.BootMoreHelpWeb, "setup-outline", () => Application.OpenURL(HelpUrl + HelpAnchor), _buttons);
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Stuck:
                    AddButton(UiTemplates.BootBack, "setup-link", Flow.Back, _top);
                    Text(UiTemplates.BootStuckTitle, "setup-title");
                    Text(UiTemplates.BootStuckBody);
                    Text(UiTemplates.BootVersionCopy, "setup-text", "setup-dim");
                    string version = BuildInfo.Describe(Application.version, Application.consoleLogPath);
                    _body.Add(LinkButton(version, () => GUIUtility.systemCopyBuffer = version));
                    Primary(UiTemplates.BootAskForum, () => Application.OpenURL(ForumUrl));
                    AddButton(Android ? UiTemplates.BootShareLog : UiTemplates.BootOpenLogFolder, "setup-outline", ShareOrOpenLog, _buttons);
                    break;
            }
        }

        private void RenderCheck() {
            GameFilesReport report = Flow.Report;
            int ok = report.Files.Values.Count(s => s == GameFileState.Ok);
            if (report.AllOk) {
                Text(UiTemplates.BootAllSet, "setup-title");
            } else {
                _body.Add(MakeLabel(T(UiTemplates.BootFoundCount, ("found", ok)), "setup-title"));
            }
            foreach (string name in GameFolder.Required) {
                var row = new VisualElement();
                row.AddToClassList("setup-row");
                row.Add(new Mark(report.Files[name] == GameFileState.Ok));
                row.Add(MakeLabel(name, "setup-text"));
                _body.Add(row);
            }
            if (!report.AllOk) {
                Note(ProblemText(report));
                Primary(UiTemplates.BootChooseAgain, () => {
                    Flow.ChooseAgain();
                    _found = null;
                    Render();
                });
            } else {
                if (Android) Text(UiTemplates.BootCopiedNote);
                else if (_chosen != null) Secondary(_chosen);
                Primary(UiTemplates.BootPlay, Play).SetEnabled(Flow.CanPlay);
            }
            Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
        }

        /// <summary>What is wrong, by file name and in words; never a path.</summary>
        private static string ProblemText(GameFilesReport report) {
            List<string> problems = report.Problems.ToList();
            if (problems.Count == GameFolder.Required.Length && problems.All(p => report.Files[p] == GameFileState.Missing)) {
                return T(UiTemplates.BootNothingHere);
            }
            IEnumerable<string> lines = problems.Select(p => T(report.Files[p] switch {
                GameFileState.Missing => UiTemplates.BootMissing,
                GameFileState.Empty => UiTemplates.BootEmpty,
                _ => UiTemplates.BootDamaged,
            }, ("file", p)));
            string text = string.Join(" ", lines);
            return problems.Any(p => report.Files[p] == GameFileState.Missing) ? text + " " + T(UiTemplates.BootMissingHint) : text;
        }

        private static string HelpAnchor => Android ? "android"
            : Application.platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor ? "windows" : "linux";

        private void FindFiles() {
            Flow.Next();
            _found = Android ? null : FindInstalled();
            Render();
        }

        private void Play() {
            if (!Flow.CanPlay) return;
            if (!string.IsNullOrEmpty(_chosen)) BakResourceSettings.GamePath = _chosen;
            // A language pack read before the setup was English.
            LanguagePacks.Reload();
            _done = true;
            _completionSource.SetResult();
        }

        private void Browse() =>
            Pick(_ => true, FileBrowser.PickMode.FilesAndFolders,
                _found ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, T(UiTemplates.BootBrowse), UsePick, () => {
                Flow.Cancelled();
                Render();
            });

        /// <summary>A desktop pick: a folder (searched two levels down), a zip (copied in), or an installer.</summary>
        internal void UsePick(string path) {
            string name = Path.GetFileName(path.TrimEnd('/', '\\'));
            Logger.LogInformation("Setup pick: {Path}", path);
            if (Directory.Exists(path)) {
                UseFolder(name, GameFilesSearch.FindGameFolder(path) ?? path);
                return;
            }
            switch (GameFilesSearch.Classify(new[] { path })) {
                case PickKind.Zip:
                    // ponytail: imported on the main thread; ~14 MB takes well under a second. A worker if a slow disk shows it.
                    ImportResult result = GameFilesImport.FromZip(path, Original);
                    if (result.Error != null) {
                        Logger.LogWarning("Zip import failed: {Error}", result.Error);
                        Flow.PickFailed(ImportErrorText(result.Error));
                    } else {
                        _chosen = Original;
                        Flow.Picked(name, result.Report);
                    }
                    break;
                case PickKind.GogInstaller:
                case PickKind.CdImage:
                    Flow.PickFailed(T(UiTemplates.BootInstallerUnreadable));
                    break;
                default:
                    // One of the game's own files picked: its folder is the pick.
                    if (GameFolder.Required.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                        string folder = Path.GetDirectoryName(path);
                        UseFolder(Path.GetFileName(folder), folder);
                        return;
                    }
                    Flow.PickFailed(T(UiTemplates.BootNothingHere));
                    break;
            }
            Render();
        }

        private static string ImportErrorText(string error) =>
            error == "io" ? T(UiTemplates.BootCopyFailed, ("mb", CopyNeedsMb)) : T(UiTemplates.BootNothingHere);

        /// <summary>A desktop folder is used where it is: nothing is copied.</summary>
        private void UseFolder(string name, string folder) {
            _chosen = folder;
            Flow.Picked(name, GameFilesCheck.Check(folder));
            Render();
        }

        // ponytail: Android stand-in until the system folder picker (Task 8): SimpleFileBrowser's own
        // folder pick, the folder's top level only, copied in through a staging folder.
        private void PickAndroidFolder() =>
            Pick(_ => true, FileBrowser.PickMode.Folders, null, null, T(UiTemplates.BootChooseFolder), CopyAndroidFolder, () => {
                Flow.Cancelled();
                Render();
            });

        private void CopyAndroidFolder(string uri) {
            string staging = Path.Combine(Application.temporaryCachePath, "pick");
            try {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                foreach (FileSystemEntry entry in FileBrowserHelpers.GetEntriesInDirectory(uri, false)) {
                    if (!entry.IsDirectory && (GameFolder.Required.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)
                                               || entry.Name.Equals("RESOURCE.CFG", StringComparison.OrdinalIgnoreCase))) {
                        FileBrowserHelpers.CopyFile(entry.Path, Path.Combine(staging, entry.Name));
                    }
                }
            } catch (Exception e) {
                Logger.LogError(e, "Copying the picked folder failed");
            }
            ImportResult result = GameFilesImport.FromFolder(staging, Original);
            if (result.Error != null) {
                Flow.PickFailed(ImportErrorText(result.Error));
            } else {
                _chosen = Original;
                Flow.Picked(FileBrowserHelpers.GetFilename(uri), result.Report);
            }
            Render();
        }

        /// <summary>
        /// The Still stuck? card's log button: "Open log folder" on desktop. Android's "Share log" needs
        /// the intent bridge that comes with the Android picker, so it does nothing there yet.
        /// </summary>
        private static void ShareOrOpenLog() {
            if (!Android && !string.IsNullOrEmpty(Application.consoleLogPath)) {
                Application.OpenURL("file://" + Path.GetDirectoryName(Application.consoleLogPath));
            }
        }

        private void Text(string key, params string[] classes) => _body.Add(MakeLabel(T(key), classes.Length == 0 ? new[] { "setup-text" } : classes));

        private void Secondary(string text) => _body.Add(MakeLabel(text, "setup-text", "setup-dim"));

        private void Note(string text) {
            if (!string.IsNullOrEmpty(text)) _body.Add(MakeLabel(text, "setup-note"));
        }

        private static Label MakeLabel(string text, params string[] classes) {
            var label = new Label(text) { enableRichText = false };
            foreach (string c in classes) label.AddToClassList(c);
            return label;
        }

        private Button Primary(string key, Action clicked) => AddButton(key, "setup-primary", clicked, _buttons);

        private void Link(string key, Action clicked) => _links.Add(LinkButton(T(key), clicked));

        private Button LinkButton(string text, Action clicked) {
            var link = new Button(() => {
                clicked();
                Render();
            }) { text = "<u>" + text + "</u>" };
            link.AddToClassList("setup-link");
            return link;
        }

        private Button AddButton(string key, string cssClass, Action clicked, VisualElement parent) {
            var button = new Button(() => {
                clicked();
                if (!_done) Render();
            }) { text = T(key) };
            button.AddToClassList(cssClass);
            if (cssClass == "setup-link") button.text = "<u>" + button.text + "</u>";
            parent.Add(button);
            return button;
        }

        /// <summary>A tick or a cross, drawn: shape as well as colour (the USS gives the colour), and Alegreya has neither glyph.</summary>
        private sealed class Mark : VisualElement {
            public Mark(bool ok) {
                AddToClassList("setup-mark");
                AddToClassList(ok ? "setup-ok" : "setup-bad");
                generateVisualContent += ctx => {
                    Painter2D p = ctx.painter2D;
                    float w = contentRect.width, h = contentRect.height;
                    p.strokeColor = resolvedStyle.color;
                    p.lineWidth = 4;
                    p.lineCap = LineCap.Round;
                    p.BeginPath();
                    if (ok) {
                        p.MoveTo(new Vector2(w * 0.1f, h * 0.55f));
                        p.LineTo(new Vector2(w * 0.4f, h * 0.85f));
                        p.LineTo(new Vector2(w * 0.9f, h * 0.15f));
                    } else {
                        p.MoveTo(new Vector2(w * 0.15f, h * 0.15f));
                        p.LineTo(new Vector2(w * 0.85f, h * 0.85f));
                        p.MoveTo(new Vector2(w * 0.85f, h * 0.15f));
                        p.LineTo(new Vector2(w * 0.15f, h * 0.85f));
                    }
                    p.Stroke();
                };
            }
        }

        /// <summary>The desktop game-folder picker, shared with the Enhanced settings: the player picks
        /// KRONDOR.001 and gets its folder. Nothing happens while a picker is already open (a second Show
        /// would replace the first one's callbacks and leave its filter attached).</summary>
        internal static void PickGameArchiveFolder(string initialPath, string title, Action<string> picked) =>
            Pick(FilterGameFiles, FileBrowser.PickMode.Files, initialPath, ResourceArchiveFilename, title,
                path => picked(FileBrowserHelpers.GetDirectoryName(path)));

        /// <summary>A folder picker (the mods folder), with the same one-at-a-time rule.</summary>
        internal static void PickFolder(string initialPath, string title, Action<string> picked) =>
            Pick(FilterDirectoriesOnly, FileBrowser.PickMode.Folders, initialPath, null, title, picked);

        /// <summary>A mod zip picker (the Mods screen), with the same one-at-a-time rule.</summary>
        internal static void PickZip(string title, Action<string> picked) =>
            Pick(FilterZips, FileBrowser.PickMode.Files, null, null, title, picked);

        private static bool FilterZips(FileSystemEntry entry) =>
            entry.IsDirectory || entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        private static void Pick(FileBrowser.FileSystemEntryFilter filter, FileBrowser.PickMode mode, string initialPath,
            string initialFilename, string title, Action<string> picked, Action cancelled = null) {
            if (FileBrowser.IsOpen) {
                return;
            }
            FileBrowser.DisplayedEntriesFilter -= filter; // never twice
            FileBrowser.DisplayedEntriesFilter += filter;
            FileBrowser.ShowLoadDialog(paths => {
                    FileBrowser.DisplayedEntriesFilter -= filter;
                    if (paths is { Length: > 0 }) picked(paths[0]);
                }, () => {
                    FileBrowser.DisplayedEntriesFilter -= filter;
                    cancelled?.Invoke();
                },
                mode, false, initialPath, initialFilename, title);
        }

        private static bool FilterGameFiles(FileSystemEntry entry) {
            return entry.IsDirectory || string.Equals(entry.Name, ResourceArchiveFilename, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool FilterDirectoriesOnly(FileSystemEntry entry) {
            return entry.IsDirectory;
        }
    }
}

namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using Cysharp.Threading.Tasks;
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
        private static readonly string[] OtherArchives = { ".rar", ".7z", ".tar", ".gz", ".tgz" };

        private readonly AwaitableCompletionSource _completionSource = new();
        private VisualElement _top;
        private VisualElement _body;
        private VisualElement _buttons;
        private VisualElement _links;
        private string _found;  // desktop: an install found without asking, offered on the Find step
        private bool _foundGog; // ...and whether it is GOG's own install (not a Heroic, Wine or Lutris one)
        private string _chosen; // the folder Play stores as GamePath
        private bool _done;
        private bool _copied; // the Still stuck? version line was just copied
        private bool _picking; // Android: a system picker or its copy is under way
        private bool _fromFiles; // Android: the pick was loose files, so there is no one name to show

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger<MetaMenu>();

        internal SetupFlow Flow { get; } = new();

        internal static string DefaultOverridePath => Path.Join(Application.persistentDataPath, "overrides");

        internal static RuntimePlatform? PlatformForTest;

        private static RuntimePlatform Platform => PlatformForTest ?? Application.platform;

        private static bool Android => Platform == RuntimePlatform.Android;

        private static string Original => Path.Combine(Application.persistentDataPath, "original");

        private static string T(string key, params (string Name, object Value)[] args) => LanguagePacks.BootText(key, args);

        private void OnEnable() {
            FileBrowser.SingleClickMode = true;
            if (Application.platform == RuntimePlatform.Android) KeepTouchSizeOnSmallPhones();
        }

        /// <summary>
        /// The panel scales with the screen height, so on a small phone (1280x720 at 320 dpi) a 64-unit
        /// button is only 43dp. Scale the panel up until one unit is at least 48/63 dp: the layout then
        /// has fewer units to fill, and the body scrolls.
        /// </summary>
        private void KeepTouchSizeOnSmallPhones() {
            PanelSettings panel = GetComponent<UIDocument>().panelSettings;
            if (Screen.dpi <= 0 || panel.scaleMode != PanelScaleMode.ScaleWithScreenSize) return;
            float pxPerUnit = Math.Min(Screen.width, Screen.height) / (float)panel.referenceResolution.y;
            float wantedPxPerUnit = 48f / 63f * Screen.dpi / 160f;
            panel.scale = Math.Max(1f, wantedPxPerUnit / pxPerUnit);
            Logger.LogInformation("Setup panel: {Width}x{Height} at {Dpi} dpi, scale {Scale}", Screen.width, Screen.height, Screen.dpi, panel.scale);
        }

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

        /// <summary>GOG's own install: the registry's folder or a "GOG Games" folder, not one inside a Wine prefix.</summary>
        private static bool IsGogInstall(string candidate) =>
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            true;
#else
            candidate == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GOG Games", "Betrayal at Krondor");
#endif

        private void FindInstalled() {
            (_found, _foundGog) = (null, false);
            foreach (string candidate in DesktopCandidates()) {
                string folder = GameFilesSearch.FindGameFolder(candidate);
                if (folder != null && GameFilesCheck.Check(folder).AllOk) {
                    (_found, _foundGog) = (folder, IsGogInstall(candidate));
                    return;
                }
            }
        }

        internal void Render() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _top = root.Q("Top");
            _body = root.Q<ScrollView>("Body");
            _buttons = root.Q("Buttons");
            _links = root.Q("Links");
            foreach (VisualElement e in new[] { _top, _body, _buttons, _links }) e.Clear();
            // On a phone two links side by side are two small targets next to each other: one per row.
            _links.EnableInClassList("setup-links-stacked", Android);

            int n = Flow.Step switch { SetupStep.Welcome => 1, SetupStep.Find => 2, SetupStep.Check => 3, _ => 0 };
            Label stepLine = root.Q<Label>("StepLine");
            stepLine.style.display = n == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            string from = _fromFiles ? T(UiTemplates.BootFromFiles)
                : Flow.SourceName != null ? T(UiTemplates.BootFrom, ("name", Flow.SourceName)) : null;
            stepLine.text = n == 3 && from != null
                ? T(UiTemplates.BootStepOf, ("n", n)) + " · " + from
                : T(UiTemplates.BootStepOf, ("n", n));

            switch (Flow.Step) {
                case SetupStep.Welcome:
                    Text(UiTemplates.BootWelcomeTitle, "setup-title");
                    Text(UiTemplates.BootWelcomeBody);
                    Text(UiTemplates.BootNoGame, "setup-text", "setup-dim");
                    Primary(UiTemplates.BootFindFiles, FindFiles);
                    // On a phone the links stack, one row each: the help one becomes the outline button instead.
                    if (Android) AddButton(UiTemplates.BootWhereLink, "setup-outline", Flow.OpenHelp, _buttons);
                    else Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
                    Link(UiTemplates.BootBuyLink, () => Application.OpenURL(GogUrl));
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Find when Android:
                    Text(UiTemplates.BootChooseFolderTitle, "setup-title");
                    // What just happened goes first: the body scrolls, and a note below it was cut off on a phone.
                    // A copy under way replaces the last pick's note, and a note replaces the dim side remark.
                    if (_picking) Text(UiTemplates.BootCopying);
                    else Note(Flow.Message);
                    Text(UiTemplates.BootChooseFolderBodyAndroid);
                    if (KeepSideRemark) Text(UiTemplates.BootDownloadNote, "setup-text", "setup-dim");
                    Primary(UiTemplates.BootChooseFolder, () => PickAndroidAsync(AndroidSetupPicker.PickFolderAsync, UiTemplates.BootNoFolderPicker, false).Forget());
                    // Most players do not have the files on the phone yet: the way there is a button, not a link.
                    AddButton(UiTemplates.BootPhoneHelpLink, "setup-outline", Flow.OpenHelp, _buttons);
                    Link(UiTemplates.BootPickOtherAndroid, () => PickAndroidAsync(AndroidSetupPicker.PickFilesAsync, UiTemplates.BootNoFilePicker, true).Forget());
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    // Nothing to tap while a pick is copied in: the result renders the next step.
                    foreach (VisualElement e in _buttons.Children().Concat(_links.Children())) e.SetEnabled(!_picking);
                    break;
                case SetupStep.Find when _found != null:
                    Text(UiTemplates.BootFoundTitle, "setup-title");
                    Note(Flow.Message);
                    Text(_foundGog ? UiTemplates.BootFoundBody : UiTemplates.BootFoundBodyOther);
                    Secondary(_found);
                    Primary(UiTemplates.BootUseThis, () => UseFolder(Path.GetFileName(_found), _found));
                    AddButton(UiTemplates.BootChooseAnother, "setup-outline", Browse, _buttons);
                    Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Find:
                    Text(UiTemplates.BootChooseFolderTitle, "setup-title");
                    Note(Flow.Message);
                    Text(UiTemplates.BootChooseFolderBodyDesktop);
                    if (KeepSideRemark) Text(UiTemplates.BootCaseNote, "setup-text", "setup-dim");
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
                    HelpBody(Android ? UiTemplates.BootHelpBodyAndroid
                        : Platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor
                            ? UiTemplates.BootHelpBodyWindows : UiTemplates.BootHelpBodyLinux);
                    AddButton(UiTemplates.BootMoreHelpWeb, "setup-outline", () => Application.OpenURL(HelpUrl + HelpAnchor), _buttons);
                    Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
                    break;
                case SetupStep.Stuck:
                    AddButton(UiTemplates.BootBack, "setup-link", Flow.Back, _top);
                    Text(UiTemplates.BootStuckTitle, "setup-title");
                    Text(UiTemplates.BootStuckBody);
                    Text(UiTemplates.BootVersionCopy, "setup-text", "setup-dim");
                    // Android has no log file, and "adb logcat" means nothing to a player: Share log sends it instead.
                    string version = Android ? T(UiTemplates.BootVersion, ("version", Application.version))
                        : BuildInfo.Describe(Application.version, Application.consoleLogPath);
                    _body.Add(LinkButton(_copied ? T(UiTemplates.BootCopied) : version, () => {
                        GUIUtility.systemCopyBuffer = version;
                        _copied = true;
                        _body.schedule.Execute(() => {
                            _copied = false;
                            if (Flow.Step == SetupStep.Stuck) Render();
                        }).StartingIn(1500);
                    }));
                    Primary(UiTemplates.BootAskForum, () => Application.OpenURL(ForumUrl));
                    if (Android || !string.IsNullOrEmpty(Application.consoleLogPath)) {
                        AddButton(Android ? UiTemplates.BootShareLog : UiTemplates.BootOpenLogFolder, "setup-outline", ShareOrOpenLog, _buttons);
                    }
                    break;
            }
            // A rule under "‹ Back": the scrolled text slides under it instead of leaving half a line loose.
            _top.EnableInClassList("setup-top-rule", _top.childCount > 0);
        }

        private void RenderCheck() {
            GameFilesReport report = Flow.Report;
            int ok = report.Files.Values.Count(s => s == GameFileState.Ok);
            if (report.AllOk) {
                Text(UiTemplates.BootAllSet, "setup-title");
            } else {
                // A file that is there but cut short is not "found": count what is OK instead.
                bool broken = report.Files.Values.Any(s => s is GameFileState.Damaged or GameFileState.Empty);
                _body.Add(MakeLabel(broken ? T(UiTemplates.BootOkCount, ("ok", ok)) : T(UiTemplates.BootFoundCount, ("found", ok)), "setup-title"));
            }
            foreach (string name in GameFolder.Required) {
                var row = new VisualElement();
                row.AddToClassList("setup-row");
                row.Add(new Mark(report.Files[name] == GameFileState.Ok));
                row.Add(MakeLabel(name, "setup-text"));
                _body.Add(row);
            }
            if (!report.AllOk) {
                Note(ProblemText(report, InPlace));
                Primary(UiTemplates.BootChooseAgain, () => {
                    Flow.ChooseAgain();
                    _found = null;
                    Render();
                });
                if (Android) AddButton(UiTemplates.BootPhoneHelpLink, "setup-outline", Flow.OpenHelp, _buttons);
                else Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
            } else {
                if (Android) Text(UiTemplates.BootCopiedNote);
                else if (_chosen != null) Secondary(_chosen);
                Primary(UiTemplates.BootPlay, Play).SetEnabled(Flow.CanPlay);
                // The files are in: on a phone, where to get them is no longer the question (and the stacked links need the room).
                if (!Android) Link(UiTemplates.BootWhereLink, Flow.OpenHelp);
            }
            Link(UiTemplates.BootStuckLink, Flow.OpenStuck);
        }

        /// <summary>A desktop folder used where it is: nothing was copied, so "copy it again" means nothing.</summary>
        private bool InPlace => !Android && _chosen != null && _chosen != Original;

        /// <summary>The dim side remark stays under a note that it still helps with.</summary>
        private bool KeepSideRemark => string.IsNullOrEmpty(Flow.Message)
            || Flow.Message == T(UiTemplates.BootPermission) || Flow.Message == T(UiTemplates.BootNothingHere);

        /// <summary>What is wrong, by file name and in words; never a path.</summary>
        private static string ProblemText(GameFilesReport report, bool inPlace) {
            List<string> problems = report.Problems.ToList();
            if (problems.Count == GameFolder.Required.Length && problems.All(p => report.Files[p] == GameFileState.Missing)) {
                return T(UiTemplates.BootNothingHere);
            }
            IEnumerable<string> lines = problems.Select(p => T(report.Files[p] switch {
                GameFileState.Missing => UiTemplates.BootMissing,
                _ when inPlace => UiTemplates.BootDamagedInPlace,
                GameFileState.Empty => UiTemplates.BootEmpty,
                _ => UiTemplates.BootDamaged,
            }, ("file", p)));
            string text = string.Join(" ", lines);
            return problems.Any(p => report.Files[p] == GameFileState.Missing) ? text + " " + T(UiTemplates.BootMissingHint) : text;
        }

        private static string HelpAnchor => Android ? "android"
            : Platform is RuntimePlatform.WindowsPlayer or RuntimePlatform.WindowsEditor ? "windows" : "linux";

        private void FindFiles() {
            Flow.Next();
            if (Android) _found = null;
            else FindInstalled();
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
                        Logger.LogInformation("Zip import failed: {Error}", result.Error);
                        Flow.PickFailed(_found != null && result.Error == "io" && !Android
                            ? T(UiTemplates.BootCopyErrorFound) : ImportErrorText(result.Error));
                    } else {
                        _chosen = Original;
                        Flow.Picked(name, result.Report);
                    }
                    break;
                case PickKind.GogInstaller:
                case PickKind.CdImage:
                    Flow.PickFailed(T(Android ? UiTemplates.BootInstallerUnreadableAndroid : UiTemplates.BootInstallerUnreadable));
                    break;
                default:
                    // One of the game's own files picked: its folder is the pick.
                    if (GameFolder.Required.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                        string folder = Path.GetDirectoryName(path);
                        UseFolder(Path.GetFileName(folder), folder);
                        return;
                    }
                    Flow.PickFailed(T(OtherArchives.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
                        ? UiTemplates.BootNotAZip : UiTemplates.BootNothingHere));
                    break;
            }
            Render();
        }

        internal static string ImportErrorText(string error) => error switch {
            "corrupt-zip" => T(UiTemplates.BootZipDamaged),
            "too-large" => T(UiTemplates.BootZipTooLarge),
            "space" => T(Android ? UiTemplates.BootCopyFailed : UiTemplates.BootCopyFailedDesktop, ("mb", CopyNeedsMb)),
            // "io" and anything else: a read or write failed, cause unknown (a full disk reports "space" only from Android's copy).
            _ => T(Android ? UiTemplates.BootCopyError : UiTemplates.BootCopyErrorDesktop),
        };

        /// <summary>A desktop folder is used where it is: nothing is copied.</summary>
        private void UseFolder(string name, string folder) {
            _chosen = folder;
            Flow.Picked(name, GameFilesCheck.Check(folder));
            Render();
        }

        /// <summary>Android: the system picker, its copy into the staging folder, then the import from there.</summary>
        private async UniTaskVoid PickAndroidAsync(Func<UniTask<AndroidSetupPicker.PickResult>> pick, string noPickerKey, bool looseFiles) {
            if (_picking) return;
            _picking = true;
            try {
                AndroidSetupPicker.PickResult picked = await pick();
                if (picked == null) {
                    Flow.Cancelled();
                } else if (picked.Error == "no-picker") {
                    Flow.PickFailed(T(noPickerKey));
                } else {
                    UseAndroidPick(picked, looseFiles);
                }
            } catch (Exception e) { // whatever the Java side or the copy throws ends as a message, never a wizard left waiting
                Logger.LogError(e, "The Android pick failed");
                Flow.PickFailed(ImportErrorText("io"));
            } finally {
                _picking = false;
                try {
                    AndroidSetupPicker.ClearStaging();
                } catch (IOException e) {
                    Logger.LogWarning(e, "The setup staging folder stays behind");
                }
            }
            Render();
        }

        /// <summary>
        /// What the Java side copied: the game's files (imported, then checked), or one other file, a zip
        /// or an installer, used like a desktop pick. A failed copy imports nothing, unless only the
        /// optional RESOURCE.CFG failed after all three game files were copied. <paramref name="looseFiles"/>:
        /// picked as loose files, whose display name is only the first file's.
        /// </summary>
        internal void UseAndroidPick(AndroidSetupPicker.PickResult picked, bool looseFiles = false) {
            Logger.LogInformation("Setup pick: {Name}, copy error {Error}", picked.DisplayName, picked.Error);
            _fromFiles = false;
            switch (picked.Error) {
                case "permission":
                    Flow.PickFailed(T(UiTemplates.BootPermission));
                    return;
                case "io" when GameFolder.Required.All(picked.Copied.Contains):
                    break;
                case "space":
                case "io":
                    Flow.PickFailed(ImportErrorText(picked.Error));
                    return;
            }
            string[] files = Directory.GetFiles(picked.StagingDir);
            if (files.Length == 1 && !GameFolder.Required.Contains(Path.GetFileName(files[0]))
                                  && Path.GetFileName(files[0]) != "RESOURCE.CFG") {
                UsePick(files[0]);
                return;
            }
            ImportResult result = GameFilesImport.FromFolder(picked.StagingDir, Original);
            if (result.Error != null) {
                Flow.PickFailed(ImportErrorText(result.Error));
            } else {
                _chosen = Original;
                _fromFiles = looseFiles;
                Flow.Picked(picked.DisplayName, result.Report);
            }
        }

        /// <summary>
        /// The Still stuck? card's log button: "Share log" on Android, "Open log folder" on desktop.
        /// </summary>
        private static void ShareOrOpenLog() {
            if (Android) {
                try {
                    AndroidSetupPicker.ShareLog();
                } catch (Exception e) when (e is AndroidJavaException or IOException) {
                    Logger.LogError(e, "Sharing the log failed");
                }
            } else if (!string.IsNullOrEmpty(Application.consoleLogPath)) {
                Application.OpenURL("file://" + Path.GetDirectoryName(Application.consoleLogPath));
            }
        }

        /// <summary>A help body: a numbered section's first line ("1. With a USB cable") is its heading.</summary>
        private void HelpBody(string key) {
            foreach (string section in T(key).Split("\n\n")) {
                int br = section.IndexOf('\n');
                if (br > 0 && char.IsDigit(section[0])) {
                    _body.Add(MakeLabel(section[..br], "setup-text", "setup-help-head"));
                    _body.Add(MakeLabel(section[(br + 1)..], "setup-text"));
                } else {
                    _body.Add(MakeLabel(section, "setup-text"));
                }
            }
        }

        private void Text(string key, params string[] classes) => _body.Add(MakeLabel(T(key), classes.Length == 0 ? new[] { "setup-text" } : classes));

        /// <summary>The folder, as secondary detail under the main message (desktop only).</summary>
        private void Secondary(string folder) => _body.Add(MakeLabel(T(UiTemplates.BootFolder, ("path", folder)), "setup-text", "setup-dim"));

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

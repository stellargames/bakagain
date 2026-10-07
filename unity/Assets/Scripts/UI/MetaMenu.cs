namespace BakAgain.UI {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
#endif
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using GameData.Resources.Text;
    using Microsoft.Win32;
    using SimpleFileBrowser;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    [RequireComponent(typeof(UIDocument))]
    public class MetaMenu : MonoBehaviour {
        private const string ResourceArchiveFilename = "KRONDOR.001";
        private readonly AwaitableCompletionSource _completionSource = new();
        private Button _continueButton;
        private TextField _gameFileTextField;
        private TextField _overrideTextField;
        private Toggle _toggle;
        private Button _gameFileBrowseButton;
        private Button _overrideBrowseButton;

        private static readonly ILogger Logger = LogManager.LoggerFactory.CreateLogger<MetaMenu>();

        private static string DefaultOverridePath => Path.Join(Application.persistentDataPath, "overrides");

        private static string DefaultGameFilePath {
            get {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games\1207660953");
                object value = key?.GetValue("PATH");
                if (value != null) {
                    return value.ToString();
                }
                key?.Close();
#endif
                return @"c:\games\betrayal at krondor";
            }
        }

        private Label _gamePathError;

        private static readonly HashSet<string> RequiredGameFileNames = new(StringComparer.OrdinalIgnoreCase) {
            "KRONDOR.001", "FRP.SX", "RESOURCE.CFG", "STARTUP.GAM"
        };

        private void OnEnable() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _gamePathError = root.Q<Label>("LabelGamePathError");
            // Start hidden from CODE, not from the UXML's inline style: instantiating the tree and
            // reading the label back shows the inline `display: none` does not survive, which would
            // leave an empty error box sitting in the layout on every boot.
            ShowGamePathError(null);
            _toggle = root.Q<Toggle>("ToggleEnableOverrides");
            _overrideTextField = root.Q<TextField>("TextFieldOverridePath");
            _gameFileTextField = root.Q<TextField>("TextFieldGameFilePath");
            _continueButton = root.Q<Button>("ButtonContinue");
            // The screen's words come from the pack system like every other screen's (TASK-775).
            root.Q<Label>("LabelDataFiles").text = LanguagePacks.BootText(UiTemplates.BootDataFiles);
            _toggle.label = LanguagePacks.BootText(UiTemplates.BootEnableOverrides);
            _overrideTextField.label = LanguagePacks.BootText(UiTemplates.BootOverrideDirectory);
            _gameFileTextField.label = LanguagePacks.BootText(UiTemplates.BootGameDirectory);
            _continueButton.text = LanguagePacks.BootText(UiTemplates.BootContinue);
            _gameFileBrowseButton = _gameFileTextField.parent.Q<Button>("ButtonBrowseGameFilePath");
            _overrideBrowseButton = _overrideTextField.parent.Q<Button>("ButtonBrowseOverridePath");

            ConfigureOverrideElements();
            ConfigureGamePathElements();
            ConfigureContinueButton();

            // Subscribe to events
            _continueButton.clicked += OnContinueButtonClicked;
            _gameFileTextField.RegisterValueChangedCallback(OnGameFilePathChanged);
            _gameFileTextField.RegisterCallback<FocusOutEvent>(OnGameFilePathFocusOut);
            _gameFileBrowseButton.clicked += ShowFileBrowser;
            _toggle.RegisterValueChangedCallback(OnToggleValueChanged);
            _overrideBrowseButton.clicked += ShowOverrideFileBrowser;
        }

        private void OnDisable() {
            // Unsubscribe from events
            _continueButton.clicked -= OnContinueButtonClicked;
            _gameFileTextField.UnregisterValueChangedCallback(OnGameFilePathChanged);
            _gameFileTextField.UnregisterCallback<FocusOutEvent>(OnGameFilePathFocusOut);
            _gameFileBrowseButton.clicked -= ShowFileBrowser;
            _toggle.UnregisterValueChangedCallback(OnToggleValueChanged);
            _overrideBrowseButton.clicked -= ShowOverrideFileBrowser;
        }

        public Awaitable WaitForDialogAsync() {
            return _completionSource.Awaitable;
        }

        private void ConfigureContinueButton() {
            _continueButton.SetEnabled(AreRequiredGameFilesPresentIn(_gameFileTextField.value));
        }

        private async void OnContinueButtonClicked() {
#if UNITY_ANDROID && !UNITY_EDITOR
            string internalGamePath = Path.Combine(Application.persistentDataPath, "original");

            bool allFilesInInternalPath = RequiredGameFileNames.All(requiredFile => File.Exists(Path.Combine(internalGamePath, requiredFile)));

            if (!allFilesInInternalPath) {
                Directory.CreateDirectory(internalGamePath);
                string sourceDirUriString = _gameFileTextField.value;

                try {
                    FileSystemEntry[] filesInSourceDir = FileBrowserHelpers.GetEntriesInDirectory(sourceDirUriString, false);
                    var sourceFileMap = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (FileSystemEntry entry in filesInSourceDir) {
                        if (!entry.IsDirectory) {
                            sourceFileMap[entry.Name] = entry;
                        }
                    }

                    int successfullyProcessedCount = 0;
                    foreach (string requiredFileName in RequiredGameFileNames) {
                        string targetPath = Path.Combine(internalGamePath, requiredFileName);
                        if (File.Exists(targetPath)) {
                            successfullyProcessedCount++;
                            continue;
                        }

                        if (sourceFileMap.TryGetValue(requiredFileName, out FileSystemEntry sourceEntry)) {
                            Logger.LogInformation($"Copying {sourceEntry.Name} from {sourceEntry.Path} to {targetPath}");
                            FileBrowserHelpers.CopyFile(sourceEntry.Path, targetPath);
                            successfullyProcessedCount++;
                        } else {
                            Logger.LogError($"Required file '{requiredFileName}' not found in selected source directory '{sourceDirUriString}' during copy operation.");
                            ShowMissingFilesErrorDialog(sourceDirUriString, $"Missing '{requiredFileName}' in source.");
                            return;
                        }
                    }

                    if (successfullyProcessedCount != RequiredGameFileNames.Count) {
                        Logger.LogError("Failed to process all required game files. Expected {ExpectedCount}, processed {ProcessedCount}. Source: {SourceDirUriString}", RequiredGameFileNames.Count, successfullyProcessedCount, sourceDirUriString);
                        // Show error to user
                        // TODO: Implement a user-facing error message
                    } else {
                        Logger.LogInformation("All required files processed successfully for Android.");
                    }
                } catch (Exception e) {
                    Logger.LogError(e, "Failed to copy game files from '{SourceDirUriString}': {ExceptionType} - {ExceptionMessage}", sourceDirUriString, e.GetType().Name, e.Message);
                    ShowMissingFilesErrorDialog(sourceDirUriString, "Error during file copy.");
                    return;
                }
            }

            BakResourceSettings.GamePath = internalGamePath;
#else
            if (AreRequiredGameFilesPresentIn(_gameFileTextField.value)) {
                BakResourceSettings.GamePath = _gameFileTextField.value;
            }
#endif

            BakResourceSettings.OverrideEnabled = _toggle.value;
            try {
                if (!Directory.Exists(_overrideTextField.value)) {
                    Directory.CreateDirectory(_overrideTextField.value);
                }
                BakResourceSettings.OverridePath = _overrideTextField.value;
                // The pack lives under this folder; read before it was set, it was English.
                LanguagePacks.Reload();
            } catch (Exception e) {
                Logger.LogError(e, "Error setting override path");
            }

            _completionSource.SetResult();
        }

        private void ConfigureGamePathElements() {
            _gameFileTextField.value = string.IsNullOrWhiteSpace(BakResourceSettings.GamePath) ? DefaultGameFilePath : BakResourceSettings.GamePath;
            FileBrowser.SingleClickMode = true;
        }

        private void OnGameFilePathChanged(ChangeEvent<string> evt) {
            bool isValid = AreRequiredGameFilesPresentIn(evt.newValue);
            _continueButton.SetEnabled(isValid);
            if (isValid) ClearMissingFilesError();
        }

        private void OnGameFilePathFocusOut(FocusOutEvent evt) {
            bool isValid = AreRequiredGameFilesPresentIn(_gameFileTextField.value);
            _continueButton.SetEnabled(isValid);
            if (isValid) ClearMissingFilesError();
        }

        private void ShowFileBrowser() {
            try {
                string initialPath = !string.IsNullOrEmpty(_gameFileTextField.value) ? _gameFileTextField.value : Application.persistentDataPath;

#if UNITY_ANDROID && !UNITY_EDITOR
                FileBrowser.ShowLoadDialog(onSuccess: paths => {
                        if (paths is {Length: > 0}) {
                            string selectedPath = paths[0];
                            _gameFileTextField.value = selectedPath;
                            bool filesValid = AreRequiredGameFilesPresentIn(selectedPath);
                            _continueButton.SetEnabled(filesValid);
                            if (!filesValid) {
                                ShowMissingFilesErrorDialog(selectedPath);
                            } else {
                                ClearMissingFilesError();
                            }
                        }
                    }, onCancel: () => {
                        // Optionally, handle cancel if you need to clear errors or reset state
                        ClearMissingFilesError();
                    }, pickMode: FileBrowser.PickMode.Folders, 
                    allowMultiSelection: false, initialPath: initialPath,
                    title: "Select Game Folder", 
                    loadButtonText: "Select Folder");
#else
                FileBrowser.DisplayedEntriesFilter += FilterGameFiles;

                FileBrowser.ShowLoadDialog(onSuccess: paths => {
                        // Clean up the filter
                        FileBrowser.DisplayedEntriesFilter -= FilterGameFiles;
                        if (paths is {Length: > 0}) {
                            string selectedPath = FileBrowserHelpers.GetDirectoryName(paths[0]);
                            _gameFileTextField.value = selectedPath;
                            _continueButton.SetEnabled(AreRequiredGameFilesPresentIn(_gameFileTextField.value));
                        }
                    }, onCancel: () => {
                        // Clean up the filter on cancel too
                        FileBrowser.DisplayedEntriesFilter -= FilterGameFiles;
                    }, pickMode: FileBrowser.PickMode.Files, allowMultiSelection: false, initialPath: initialPath,
                    // initialFilename: ResourceArchiveFilename,
                    title: "Select Game Resource File", loadButtonText: "Select");
#endif
            } catch (Exception ex) {
                Logger.LogError(ex, "Error showing file browser");
            }
        }

        private static bool FilterGameFiles(FileSystemEntry entry) {
            return entry.IsDirectory || string.Equals(entry.Name, ResourceArchiveFilename, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool AreRequiredGameFilesPresentIn(string directoryPath) {
            if (string.IsNullOrEmpty(directoryPath)) {
                return false;
            }

            int foundCount = 0;
            try {
                if (directoryPath.StartsWith("content://")) {
#if UNITY_ANDROID && !UNITY_EDITOR
                    FileSystemEntry[] entries = FileBrowserHelpers.GetEntriesInDirectory(directoryPath, false);
                    foreach (FileSystemEntry entry in entries) {
                        if (!entry.IsDirectory && RequiredGameFileNames.Contains(entry.Name)) {
                            foundCount++;
                        }
                    }
                    Logger.LogInformation("Game folder {DirectoryPath}: {Found} of {Required} required files among {Entries} entries",
                        directoryPath, foundCount, RequiredGameFileNames.Count, entries.Length);

                    // A content:// URI is a Storage Access Framework document, not a filesystem
                    // path: Directory.Exists below is always false for it, so falling through
                    // refused every folder the Android picker returned, files or not.
                    return foundCount == RequiredGameFileNames.Count;
#else
                    Logger.LogWarning("Attempting to check a 'content://' URI on a non-Android platform.");

                    return false;
#endif
                }

                if (!Directory.Exists(directoryPath))
                    return false;
                foundCount += RequiredGameFileNames.Count(requiredFile => File.Exists(Path.Combine(directoryPath, requiredFile)));

                return foundCount == RequiredGameFileNames.Count;
            } catch (Exception e) {
                Logger.LogError(e, "Error validating game folder '{DirectoryPath}'", directoryPath);

                return false;
            }
        }

        private void ShowMissingFilesErrorDialog(string pathAttempted, string specificError = null) {
            string baseMessage =
                $"The selected folder '{pathAttempted}' is missing one or more required game files (e.g., {string.Join(", ", RequiredGameFileNames)}). Please select the correct Betrayal at Krondor game folder.";
            string errorMessage = string.IsNullOrEmpty(specificError) ? baseMessage : $"{baseMessage} Specific issue: {specificError}";
            Logger.LogError(errorMessage);
            ShowGamePathError(errorMessage);
        }

        private void ClearMissingFilesError() {
            Logger.LogInformation("Clearing missing files error state.");
            ShowGamePathError(null);
        }

        /// <summary>
        /// Shows or clears the game-path error, on the screen rather than only in the log.
        /// </summary>
        /// <remarks>
        /// <b>This screen is the one place a log is no use.</b> It is the game-path prompt: it runs
        /// before anything else, and the player it is talking to is the one who has just pointed it
        /// at the wrong folder. Sending "missing KRONDOR.001" only to the Unity console left them
        /// with a Continue button that stays disabled and no stated reason.
        ///
        /// <para>The label is hidden rather than emptied when there is nothing to say, so it takes
        /// no layout space in the normal case.</para>
        /// </remarks>
        private void ShowGamePathError(string message) {
            if (_gamePathError == null) {
                return;
            }

            bool hasMessage = !string.IsNullOrEmpty(message);
            _gamePathError.text = hasMessage ? message : string.Empty;
            _gamePathError.style.display = hasMessage ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ConfigureOverrideElements() {
            _overrideTextField.value = string.IsNullOrWhiteSpace(BakResourceSettings.OverridePath) ? DefaultOverridePath : BakResourceSettings.OverridePath;

            _toggle.value = BakResourceSettings.OverrideEnabled;
            _overrideTextField.enabledSelf = _toggle.value;
            _overrideBrowseButton.enabledSelf = _toggle.value;
        }

        private void OnToggleValueChanged(ChangeEvent<bool> evt) {
            _overrideTextField.enabledSelf = evt.newValue;
            _overrideBrowseButton.enabledSelf = evt.newValue;
        }

        private void ShowOverrideFileBrowser() {
            FileBrowser.DisplayedEntriesFilter += FilterDirectoriesOnly;
            FileBrowser.ShowLoadDialog(onSuccess: paths => {
                FileBrowser.DisplayedEntriesFilter -= FilterDirectoriesOnly;
                _overrideTextField.value = paths[0];
            }, onCancel: () => {
                FileBrowser.DisplayedEntriesFilter -= FilterDirectoriesOnly;
            }, pickMode: FileBrowser.PickMode.Folders, allowMultiSelection: false, initialPath: _overrideTextField.value, initialFilename: null, title: "Select Override Path");
        }

        private static bool FilterDirectoriesOnly(FileSystemEntry entry) {
            return entry.IsDirectory;
        }
    }
}
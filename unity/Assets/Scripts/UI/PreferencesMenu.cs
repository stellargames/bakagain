namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.ResourceManagement.Loaders;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Config;
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// Controller for the faithful Preferences screen (REQ_PREF.DAT over
    /// OPTIONS2.SCX), the Unity counterpart of dialog_Preferences @ 0x6f33f.
    /// Edits a working copy of <see cref="IPreferencesService.Current"/> and
    /// commits it on OK; Cancel discards. Radio/checkbox state is surfaced to
    /// <see cref="UserInterfaceLoader"/> via <see cref="IMenuStateProvider"/>.
    /// A navigator-managed screen: pushed by <see cref="MenuActionHandler"/> from either menu;
    /// OK/Cancel pop back to whoever pushed it (the stack remembers).
    /// </summary>
    [RequireComponent(typeof(UserInterfaceLoader))]
    public class PreferencesMenu : BakAgain.UI.Navigation.ScreenBase, IActionHandler, IMenuStateProvider {
        // --- REQ_PREF action ids (verified against REQ_PREF.json + the switch
        //     at 0x6f5e8). Radio groups carry contiguous ids; value = id - base. ---
        private const int StepSizeBase = 192;     // 192..194 (3)
        private const int TurnSizeBase = 195;     // 195..197 (3)
        private const int DetailBase = 198;       // 198..201 (4)
        private const int TextSpeedBase = 202;    // 202..204 (3)

        // Checkboxes -> flag bits (205..209).
        private const int ToggleSound = 205;        // flag 0x01
        private const int ToggleGameMusic = 206;    // flag 0x02
        private const int ToggleCombatMusic = 207;  // flag 0x04
        private const int ToggleIntroduction = 208; // flag 0x08
        private const int ToggleCdMusic = 209;      // flag 0x10 (CD only)

        // Buttons.
        private const int ButtonOk = 24;
        private const int ButtonCancel = 46;
        private const int ButtonCancelAlt = 1;
        private const int ButtonDefaults = 32;

        // Right-click help DDX ids (DIAL_Z00), from dialog_Preferences.
        private const int HelpOk = 140;
        private const int HelpCancel = 141;
        private const int HelpDefaults = 142;
        private const int ConfirmDefaults = 114;

        private ILogger _logger;
        private IPreferencesService _preferences;
        private IDialogManager _dialogManager;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private UserInterfaceLoader _loader;

        private Preferences _working;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<PreferencesMenu>();
            _loader = GetComponent<UserInterfaceLoader>();
        }

        [Inject]
        public void Construct(IPreferencesService preferences, IDialogManager dialogManager,
            BakAgain.UI.Navigation.IScreenNavigator navigator) {
            _preferences = preferences;
            _dialogManager = dialogManager;
            _navigator = navigator;
        }

        // Copy-on-entry working set (mirrors the stack copy of pConfiguration). Runs BEFORE the
        // GameObject activates: activation triggers UserInterfaceLoader.OnEnable, which reads our
        // toggle states via IMenuStateProvider — so the working copy must already be set.
        protected override async UniTask OnBeforeShowAsync() {
            await _preferences.EnsureLoadedAsync();
            _working = _preferences.Current.Clone();
        }

        // Which build and where its log is, for bug reports. Re-added on every show: the
        // document's tree is rebuilt whenever the screen is reactivated. Placed by the theme's
        // .build-info rule.
        protected override void OnAfterShow() {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            if (root.Q<Label>(BuildInfoName) == null) {
                var label = new Label(BuildInfo.Describe(Application.version, Application.consoleLogPath)) {
                    name = BuildInfoName,
                    pickingMode = PickingMode.Ignore,
                };
                label.AddToClassList("build-info");
                root.Add(label);
            }
        }

        private const string BuildInfoName = "build-info";

        private void Close() => _navigator.Pop().Forget();

        public void PrimaryAction(int menuEntryActionId) {
            if (_working == null) {
                return;
            }
            switch (menuEntryActionId) {
                case >= StepSizeBase and <= StepSizeBase + 2:
                    _working.StepSize = (StepSize)(menuEntryActionId - StepSizeBase);
                    break;
                case >= TurnSizeBase and <= TurnSizeBase + 2:
                    _working.TurnSize = (TurnSize)(menuEntryActionId - TurnSizeBase);
                    break;
                case >= DetailBase and <= DetailBase + 3:
                    _working.DetailLevel = (DetailLevel)(menuEntryActionId - DetailBase);
                    break;
                case >= TextSpeedBase and <= TextSpeedBase + 2:
                    _working.TextSpeed = (TextSpeed)(menuEntryActionId - TextSpeedBase);
                    break;
                case ToggleSound:
                    _working.Sound = !_working.Sound;
                    break;
                case ToggleGameMusic:
                    _working.GameMusic = !_working.GameMusic;
                    break;
                case ToggleCombatMusic:
                    _working.CombatMusic = !_working.CombatMusic;
                    break;
                case ToggleIntroduction:
                    _working.Introduction = !_working.Introduction;
                    break;
                case ToggleCdMusic:
                    _working.CdMusic = !_working.CdMusic;
                    break;
                case ButtonOk:
                    _preferences.Apply(_working);
                    Close();
                    break;
                case ButtonCancel:
                case ButtonCancelAlt:
                    Close();
                    break;
                case ButtonDefaults:
                    RestoreDefaults().Forget();
                    break;
                default:
                    _logger.LogDebug("Unhandled preferences action {ActionId}", menuEntryActionId);
                    break;
            }
        }

        public async Awaitable SecondaryAction(int menuEntryActionId) {
            // Right-click shows the button's help text, as in dialog_Preferences.
            int helpId = menuEntryActionId switch {
                ButtonOk => HelpOk,
                ButtonCancel => HelpCancel,
                ButtonCancelAlt => HelpCancel,
                ButtonDefaults => HelpDefaults,
                _ => -1
            };
            if (helpId < 0) {
                return;
            }
            await _dialogManager.ShowById(helpId).AsTask();
        }

        public bool GetToggleState(int actionId) {
            if (_working == null) {
                return false;
            }
            return actionId switch {
                // Radios: selected when the model's value matches this id's index.
                >= StepSizeBase and <= StepSizeBase + 2 => (int)_working.StepSize == actionId - StepSizeBase,
                >= TurnSizeBase and <= TurnSizeBase + 2 => (int)_working.TurnSize == actionId - TurnSizeBase,
                >= DetailBase and <= DetailBase + 3 => (int)_working.DetailLevel == actionId - DetailBase,
                >= TextSpeedBase and <= TextSpeedBase + 2 => (int)_working.TextSpeed == actionId - TextSpeedBase,
                // Checkboxes.
                ToggleSound => _working.Sound,
                ToggleGameMusic => _working.GameMusic,
                ToggleCombatMusic => _working.CombatMusic,
                ToggleIntroduction => _working.Introduction,
                ToggleCdMusic => _working.CdMusic,
                _ => false
            };
        }

        private async UniTaskVoid RestoreDefaults() {
            // Confirm via DDX 114 (the original's yes/no box). Only reset on Yes.
            bool confirmed = await _dialogManager.ShowConfirmById(ConfirmDefaults);
            if (!confirmed) {
                return;
            }
            Preferences defaults = await _preferences.GetDefaultsAsync();
            _working = defaults.Clone();
            _loader.RefreshToggles();
        }
    }
}

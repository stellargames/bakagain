namespace BakAgain.UI {
    using UnityEngine;
    using UnityEngine.UI;
    using BakAgain.Core.States;
    using BakAgain.Core.States.Debug;
    using TMPro;

    /// <summary>
    /// Simple UI for WorldTestState to navigate between test zones.
    /// </summary>
    public class WorldTestUI : MonoBehaviour {
        [Header("UI References")]
        public TextMeshProUGUI zoneInfoText;
        public Button previousZoneButton;
        public Button nextZoneButton;
        public Button[] quickZoneButtons;
        public TextMeshProUGUI controlsText;
        public Button toggleTriggersButton;
        public TextMeshProUGUI triggerInfoText;

        [Header("Quick Zone Buttons")]
        public int[] quickZoneNumbers = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        private WorldTestState _worldTestState;

        /// <summary>
        /// Set the WorldTestState reference (called by WorldTestState)
        /// </summary>
        public void SetWorldTestState(WorldTestState worldTestState) {
            _worldTestState = worldTestState;
        }

        public void SetupListeners() {
            // Setup zone navigation buttons
            if (previousZoneButton != null) {
                previousZoneButton.onClick.AddListener(() => {
                    _worldTestState?.PreviousZone();
                });
            }

            if (nextZoneButton != null) {
                nextZoneButton.onClick.AddListener(() => {
                    _worldTestState?.NextZone();
                });
            }

            // Setup quick zone buttons
            if (quickZoneButtons != null && quickZoneNumbers != null) {
                for (int i = 0; i < System.Math.Min(quickZoneButtons.Length, quickZoneNumbers.Length); i++) {
                    int zoneNumber = quickZoneNumbers[i];
                    if (quickZoneButtons[i] != null) {
                        int index = i; // Capture for closure
                        quickZoneButtons[i].onClick.AddListener(() => {
                            _worldTestState?.LoadZone(zoneNumber);
                            UpdateZoneText($"Zone {zoneNumber}");
                        });
                        
                        // Set button text
                        var buttonText = quickZoneButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                        if (buttonText != null) {
                            buttonText.text = $"Zone {zoneNumber}";
                        }
                    }
                }
            }

            if (toggleTriggersButton != null) {
                toggleTriggersButton.onClick.AddListener(() => {
                    _worldTestState?.ToggleTriggerOverlay();
                });
            }

            // Setup controls text
            if (controlsText != null) {
                controlsText.text = "Controls:\nWASD - Move\nMouse - Look\nClick buttons to change zones";
            }

            UpdateZoneText("World Test Mode");
        }

        public void UpdateZoneText(string text) {
            if (zoneInfoText != null) {
                zoneInfoText.text = text;
            }
        }

        /// <summary>Trigger-overlay summary and colour legend.</summary>
        public void UpdateTriggerText(string text) {
            if (triggerInfoText != null) {
                triggerInfoText.text = text;
            }
        }

        private void OnDestroy() {
            // Clean up button listeners
            if (previousZoneButton != null) {
                previousZoneButton.onClick.RemoveAllListeners();
            }

            if (nextZoneButton != null) {
                nextZoneButton.onClick.RemoveAllListeners();
            }

            if (toggleTriggersButton != null) {
                toggleTriggersButton.onClick.RemoveAllListeners();
            }

            if (quickZoneButtons != null) {
                foreach (var button in quickZoneButtons) {
                    if (button != null) {
                        button.onClick.RemoveAllListeners();
                    }
                }
            }
        }
    }
}

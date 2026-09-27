namespace BakAgain.Core.States.Debug {
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using BakAgain.World;
    using Cysharp.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Test state for world loading and rendering.
    /// Loads and displays different zones for testing purposes.
    /// </summary>
    public class WorldTestState {
        private readonly ILogger<WorldTestState> _logger;
        private readonly ZoneSceneBuilder _zoneSceneBuilder;
        private readonly IResourceProviderService _resourceProvider;
        private GameObject _currentZone;
        private Camera _camera;
        private WorldExplorerController _controller;
        private GameObject _ui;
        private WorldTestUI _worldTestUI;
        private int _currentZoneIndex = 0;
        private readonly List<int> _testZones = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        private World.Hotspots.WorldTriggerOverlay _triggerOverlay;
        private bool _triggersVisible = true;

        /// <summary>
        /// Chapter whose trigger block the overlay draws. Triggers are chapter-specific — the same
        /// ground fires different things as the story moves on — so the overlay has to pick one.
        /// Chapter 1 is what a fresh game sees.
        /// </summary>
        private const int TriggerChapter = 1;

        /// <summary>Height above the terrain to float the trigger outlines at.</summary>
        private const float TriggerOverlayHeight = 3f;

        public WorldTestState(
            ILogger<WorldTestState> logger,
            ZoneSceneBuilder zoneSceneBuilder,
            IResourceProviderService resourceProvider) {
            _logger = logger;
            _zoneSceneBuilder = zoneSceneBuilder;
            _resourceProvider = resourceProvider;
        }

        public async UniTask RunAsync() {
            _logger.LogInformation("Entering WorldTestState - testing world loading and rendering");

            // Setup camera and controller
            await SetupCameraAndController();

            // Setup UI
            SetupUI();

            // Load first test zone
            await LoadTestZone(_currentZoneIndex);
        }

        private async UniTask SetupCameraAndController() {
            // Disable all existing cameras so they don't render over ours
            foreach (var cam in Camera.allCameras) {
                cam.enabled = false;
            }

            // Create dedicated world test camera
            var cameraGO = new GameObject("WorldTestCamera");
            _camera = cameraGO.AddComponent<Camera>();
            cameraGO.tag = "MainCamera";

            // Position camera at good starting point
            _camera.transform.position = new Vector3(0, 50, -100);
            _camera.transform.rotation = Quaternion.Euler(15, 0, 0);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 10000f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.2f, 0.2f, 0.25f);

            // Add world explorer controller
            _controller = _camera.gameObject.AddComponent<WorldExplorerController>();

            _logger.LogInformation("Camera and controller setup complete");
        }

        private void SetupUI() {
            // Create UI canvas
            var canvasGO = new GameObject("WorldTestUI");
            canvasGO.transform.SetParent(null);
            
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            
            var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasGO.AddComponent<GraphicRaycaster>();

            // Create UI panel
            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            
            var panel = panelGO.AddComponent<Image>();
            panel.color = new Color(0, 0, 0, 0.8f);
            
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0.7f);
            panelRect.anchorMax = new Vector2(0.3f, 1f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Add WorldTestUI component BEFORE CreateUIElements so GetComponent finds it
            _worldTestUI = canvasGO.AddComponent<WorldTestUI>();
            _worldTestUI.SetWorldTestState(this);

            // Create basic UI elements (assigns references into _worldTestUI)
            CreateUIElements(canvasGO);

            // Wire button listeners now that all references are assigned
            _worldTestUI.SetupListeners();
            _ui = canvasGO;

            _logger.LogInformation("UI setup complete");
        }

        private void CreateUIElements(GameObject canvasGO) {
            // Create zone info text
            var zoneInfoGO = new GameObject("ZoneInfoText");
            zoneInfoGO.transform.SetParent(canvasGO.transform, false);
            var zoneInfoText = zoneInfoGO.AddComponent<TextMeshProUGUI>();
            zoneInfoText.text = "World Test Mode";
            zoneInfoText.fontSize = 18;
            zoneInfoText.color = Color.white;
            
            var zoneInfoRect = zoneInfoGO.GetComponent<RectTransform>();
            zoneInfoRect.anchorMin = new Vector2(0.02f, 0.9f);
            zoneInfoRect.anchorMax = new Vector2(0.28f, 0.95f);
            zoneInfoRect.offsetMin = Vector2.zero;
            zoneInfoRect.offsetMax = Vector2.zero;

            // Create controls text
            var controlsGO = new GameObject("ControlsText");
            controlsGO.transform.SetParent(canvasGO.transform, false);
            var controlsText = controlsGO.AddComponent<TextMeshProUGUI>();
            controlsText.text = "Controls:\nWASD - Move, Space/Ctrl - Up/Down\nRight-click + Mouse - Look\nShift - Fast, Scroll - Speed";
            controlsText.fontSize = 12;
            controlsText.color = Color.white;
            
            var controlsRect = controlsGO.GetComponent<RectTransform>();
            controlsRect.anchorMin = new Vector2(0.02f, 0.7f);
            controlsRect.anchorMax = new Vector2(0.28f, 0.85f);
            controlsRect.offsetMin = Vector2.zero;
            controlsRect.offsetMax = Vector2.zero;

            // Create previous zone button
            var prevButtonGO = new GameObject("PreviousZoneButton");
            prevButtonGO.transform.SetParent(canvasGO.transform, false);
            var prevButton = prevButtonGO.AddComponent<Button>();
            var prevButtonImage = prevButtonGO.AddComponent<Image>();
            prevButtonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            
            var prevButtonRect = prevButtonGO.GetComponent<RectTransform>();
            prevButtonRect.anchorMin = new Vector2(0.02f, 0.6f);
            prevButtonRect.anchorMax = new Vector2(0.13f, 0.65f);
            prevButtonRect.offsetMin = Vector2.zero;
            prevButtonRect.offsetMax = Vector2.zero;

            var prevButtonTextGO = new GameObject("Text");
            prevButtonTextGO.transform.SetParent(prevButtonGO.transform, false);
            var prevButtonText = prevButtonTextGO.AddComponent<TextMeshProUGUI>();
            prevButtonText.text = "Previous Zone";
            prevButtonText.fontSize = 12;
            prevButtonText.color = Color.white;
            prevButtonText.alignment = TextAlignmentOptions.Center;

            var prevButtonTextRect = prevButtonTextGO.GetComponent<RectTransform>();
            prevButtonTextRect.anchorMin = Vector2.zero;
            prevButtonTextRect.anchorMax = Vector2.one;
            prevButtonTextRect.offsetMin = Vector2.zero;
            prevButtonTextRect.offsetMax = Vector2.zero;

            // Create next zone button
            var nextButtonGO = new GameObject("NextZoneButton");
            nextButtonGO.transform.SetParent(canvasGO.transform, false);
            var nextButton = nextButtonGO.AddComponent<Button>();
            var nextButtonImage = nextButtonGO.AddComponent<Image>();
            nextButtonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            
            var nextButtonRect = nextButtonGO.GetComponent<RectTransform>();
            nextButtonRect.anchorMin = new Vector2(0.17f, 0.6f);
            nextButtonRect.anchorMax = new Vector2(0.28f, 0.65f);
            nextButtonRect.offsetMin = Vector2.zero;
            nextButtonRect.offsetMax = Vector2.zero;

            var nextButtonTextGO = new GameObject("Text");
            nextButtonTextGO.transform.SetParent(nextButtonGO.transform, false);
            var nextButtonText = nextButtonTextGO.AddComponent<TextMeshProUGUI>();
            nextButtonText.text = "Next Zone";
            nextButtonText.fontSize = 12;
            nextButtonText.color = Color.white;
            nextButtonText.alignment = TextAlignmentOptions.Center;

            var nextButtonTextRect = nextButtonTextGO.GetComponent<RectTransform>();
            nextButtonTextRect.anchorMin = Vector2.zero;
            nextButtonTextRect.anchorMax = Vector2.one;
            nextButtonTextRect.offsetMin = Vector2.zero;
            nextButtonTextRect.offsetMax = Vector2.zero;

            // Create quick zone buttons (zones 01-12, 4 rows x 3 cols)
            var quickZoneButtons = new Button[12];
            for (int i = 0; i < 12; i++) {
                var quickButtonGO = new GameObject($"QuickZoneButton_{i}");
                quickButtonGO.transform.SetParent(canvasGO.transform, false);
                var quickButton = quickButtonGO.AddComponent<Button>();
                var quickButtonImage = quickButtonGO.AddComponent<Image>();
                quickButtonImage.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
                
                var quickButtonRect = quickButtonGO.GetComponent<RectTransform>();
                quickButtonRect.anchorMin = new Vector2(0.02f + (i % 3) * 0.09f, 0.5f - (i / 3f) * 0.05f);
                quickButtonRect.anchorMax = new Vector2(0.11f + (i % 3) * 0.09f, 0.54f - (i / 3f) * 0.05f);
                quickButtonRect.offsetMin = Vector2.zero;
                quickButtonRect.offsetMax = Vector2.zero;

                var quickButtonTextGO = new GameObject("Text");
                quickButtonTextGO.transform.SetParent(quickButtonGO.transform, false);
                var quickButtonText = quickButtonTextGO.AddComponent<TextMeshProUGUI>();
                quickButtonText.text = $"Zone {i + 1:D2}";
                quickButtonText.fontSize = 10;
                quickButtonText.color = Color.white;
                quickButtonText.alignment = TextAlignmentOptions.Center;

                var quickButtonTextRect = quickButtonTextGO.GetComponent<RectTransform>();
                quickButtonTextRect.anchorMin = Vector2.zero;
                quickButtonTextRect.anchorMax = Vector2.one;
                quickButtonTextRect.offsetMin = Vector2.zero;
                quickButtonTextRect.offsetMax = Vector2.zero;

                quickZoneButtons[i] = quickButton;
            }

            // Trigger-overlay toggle (below the quick-zone grid)
            var triggerButtonGO = new GameObject("ToggleTriggersButton");
            triggerButtonGO.transform.SetParent(canvasGO.transform, false);
            var triggerButton = triggerButtonGO.AddComponent<Button>();
            var triggerButtonImage = triggerButtonGO.AddComponent<Image>();
            triggerButtonImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            var triggerButtonRect = triggerButtonGO.GetComponent<RectTransform>();
            triggerButtonRect.anchorMin = new Vector2(0.02f, 0.28f);
            triggerButtonRect.anchorMax = new Vector2(0.15f, 0.33f);
            triggerButtonRect.offsetMin = Vector2.zero;
            triggerButtonRect.offsetMax = Vector2.zero;

            var triggerButtonTextGO = new GameObject("Text");
            triggerButtonTextGO.transform.SetParent(triggerButtonGO.transform, false);
            var triggerButtonText = triggerButtonTextGO.AddComponent<TextMeshProUGUI>();
            triggerButtonText.text = "Toggle Triggers";
            triggerButtonText.fontSize = 12;
            triggerButtonText.color = Color.white;
            triggerButtonText.alignment = TextAlignmentOptions.Center;

            var triggerButtonTextRect = triggerButtonTextGO.GetComponent<RectTransform>();
            triggerButtonTextRect.anchorMin = Vector2.zero;
            triggerButtonTextRect.anchorMax = Vector2.one;
            triggerButtonTextRect.offsetMin = Vector2.zero;
            triggerButtonTextRect.offsetMax = Vector2.zero;

            // Trigger summary + colour legend
            var triggerInfoGO = new GameObject("TriggerInfoText");
            triggerInfoGO.transform.SetParent(canvasGO.transform, false);
            var triggerInfoText = triggerInfoGO.AddComponent<TextMeshProUGUI>();
            triggerInfoText.text = "Triggers: —";
            triggerInfoText.fontSize = 11;
            triggerInfoText.color = Color.white;

            var triggerInfoRect = triggerInfoGO.GetComponent<RectTransform>();
            triggerInfoRect.anchorMin = new Vector2(0.02f, 0.08f);
            triggerInfoRect.anchorMax = new Vector2(0.28f, 0.27f);
            triggerInfoRect.offsetMin = Vector2.zero;
            triggerInfoRect.offsetMax = Vector2.zero;

            // Set up the WorldTestUI component references
            var worldTestUI = canvasGO.GetComponent<WorldTestUI>();
            if (worldTestUI != null) {
                worldTestUI.zoneInfoText = zoneInfoText;
                worldTestUI.controlsText = controlsText;
                worldTestUI.previousZoneButton = prevButton;
                worldTestUI.nextZoneButton = nextButton;
                worldTestUI.quickZoneButtons = quickZoneButtons;
                worldTestUI.toggleTriggersButton = triggerButton;
                worldTestUI.triggerInfoText = triggerInfoText;
            }
        }

        private async UniTask LoadTestZone(int zoneIndex) {
            if (zoneIndex < 0 || zoneIndex >= _testZones.Count) {
                _logger.LogWarning("Invalid zone index: {ZoneIndex}", zoneIndex);
                return;
            }

            int zoneNumber = _testZones[zoneIndex];
            _logger.LogInformation("Loading test zone {ZoneNumber} (index {ZoneIndex})", zoneNumber, zoneIndex);

            // Update UI
            if (_worldTestUI != null) {
                _worldTestUI.UpdateZoneText($"Loading Zone {zoneNumber}...");
            }

            // Clean up previous zone
            if (_currentZone != null) {
                Object.Destroy(_currentZone);
                _currentZone = null;
            }

            try {
                // Load new zone
                _currentZone = await _zoneSceneBuilder.BuildZoneAsync(zoneNumber);
                
                if (_currentZone != null) {
                    _logger.LogInformation("Successfully loaded zone {ZoneNumber}", zoneNumber);

                    // Sky band + horizon mountains: clear the camera to the zone's sky colour and
                    // bind it so the Z##H.BMX backdrop follows + parallax-scrolls with heading.
                    _currentZone.GetComponent<ZoneEnvironment>()?.ApplyToCamera(_camera);

                    // Position camera near first terrain quad at eye level
                    var terrainParent = _currentZone.transform.Find("Terrain");
                    var firstTerrain = terrainParent?.GetComponentInChildren<Renderer>();
                    if (firstTerrain != null) {
                        _camera.transform.position = firstTerrain.bounds.center + new Vector3(0, 20f, 0);
                        _camera.transform.rotation = Quaternion.Euler(30, 45, 0);
                    } else {
                        var bounds = GetBounds(_currentZone.transform);
                        if (bounds.size != Vector3.zero) {
                            _camera.transform.position = bounds.center + new Vector3(0, 20f, 0);
                            _camera.transform.rotation = Quaternion.Euler(30, 0, 0);
                        }
                    }

                    await BuildTriggerOverlay(zoneNumber);

                    // Update UI
                    if (_worldTestUI != null) {
                        _worldTestUI.UpdateZoneText($"Zone {zoneNumber} Loaded");
                    }
                } else {
                    _logger.LogError("Failed to load zone {ZoneNumber} - null result", zoneNumber);
                    if (_worldTestUI != null) {
                        _worldTestUI.UpdateZoneText($"Failed to load Zone {zoneNumber}");
                    }
                }
            } catch (System.Exception ex) {
                _logger.LogError(ex, "Error loading zone {ZoneNumber}", zoneNumber);
                if (_worldTestUI != null) {
                    _worldTestUI.UpdateZoneText($"Error loading Zone {zoneNumber}");
                }
            }
        }

        /// <summary>
        /// Draws the zone's world triggers. The overlay is parented to the zone, so switching zones
        /// destroys it along with everything else and the next load rebuilds it.
        /// </summary>
        private async UniTask BuildTriggerOverlay(int zoneNumber) {
            _triggerOverlay = null;
            try {
                // Float the outlines a little above the terrain rather than at y=0: zone geometry
                // does not sit on the origin plane, so 0 would bury them.
                var terrainParent = _currentZone.transform.Find("Terrain");
                var firstTerrain = terrainParent?.GetComponentInChildren<Renderer>();
                float height = (firstTerrain != null
                    ? firstTerrain.bounds.center.y
                    : GetBounds(_currentZone.transform).center.y) + TriggerOverlayHeight;

                _triggerOverlay = await World.Hotspots.WorldTriggerOverlay.BuildAsync(
                    _currentZone, _zoneSceneBuilder.LoadedChunks, TriggerChapter, height,
                    _resourceProvider, this, _logger);

                _triggerOverlay.SetVisible(_triggersVisible);
                _worldTestUI?.UpdateTriggerText(TriggerLegend(_triggerOverlay));
            } catch (System.Exception ex) {
                // A missing trigger table must not cost us the zone — the viewer's job is the world.
                _logger.LogWarning(ex, "Could not build the trigger overlay for zone {Zone}", zoneNumber);
                _worldTestUI?.UpdateTriggerText("Triggers: unavailable");
            }
        }

        /// <summary>Shows or hides the trigger outlines.</summary>
        public void ToggleTriggerOverlay() {
            _triggersVisible = !_triggersVisible;
            _triggerOverlay?.SetVisible(_triggersVisible);
            if (_triggerOverlay != null) {
                _worldTestUI?.UpdateTriggerText(TriggerLegend(_triggerOverlay));
            }
        }

        private static string TriggerLegend(World.Hotspots.WorldTriggerOverlay overlay) {
            var sb = new System.Text.StringBuilder();
            sb.Append(overlay.Summary());
            sb.Append(overlay.Visible ? "" : "  (hidden)");
            foreach (var pair in overlay.CountsByType) {
                Color c = World.Hotspots.WorldTriggerOverlay.ColorFor(pair.Key);
                // TMP rich-text swatch so the legend matches what is drawn in the world.
                sb.Append($"\n<color=#{ColorUtility.ToHtmlStringRGB(c)}>■</color> {pair.Key}: {pair.Value}");
            }
            return sb.ToString();
        }

        private Bounds GetBounds(Transform transform) {
            var renderers = transform.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) {
                return new Bounds(transform.position, Vector3.zero);
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        // Public methods for UI/controls to call
        public void NextZone() {
            _currentZoneIndex = (_currentZoneIndex + 1) % _testZones.Count;
            LoadTestZone(_currentZoneIndex).Forget();
        }

        public void PreviousZone() {
            _currentZoneIndex = (_currentZoneIndex - 1 + _testZones.Count) % _testZones.Count;
            LoadTestZone(_currentZoneIndex).Forget();
        }

        public void LoadZone(int zoneNumber) {
            int existingIndex = _testZones.IndexOf(zoneNumber);
            if (existingIndex >= 0) {
                _currentZoneIndex = existingIndex;
            } else {
                _testZones.Add(zoneNumber);
                _currentZoneIndex = _testZones.Count - 1;
            }
            LoadTestZone(_currentZoneIndex).Forget();
        }
    }
}

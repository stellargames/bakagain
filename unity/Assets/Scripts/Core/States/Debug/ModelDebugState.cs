namespace BakAgain.Core.States.Debug {
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Converters;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using BakAgain.World.Rendering;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Palette;
    using GameData.Resources.World;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using System.IO;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;
    using Color = UnityEngine.Color;

    /// <summary>
    /// Debug state for viewing individual 3D models from zone TBL files.
    /// Allows selecting zone, model type, and viewing with rotation controls.
    /// </summary>
    public class ModelDebugState {
        private readonly ILogger<ModelDebugState> _logger;
        private readonly IResourceProviderService _resourceProvider;
        private readonly WorldRenderModeService _renderModeService;
        private readonly IResourceCache _resourceCache;

        private WorldEntityRenderContext _renderContext;
        private WorldModelLoader _modelLoader;
        private GameObject _currentModel;
        private GameObject _ui;
        private Camera _camera;
        private GameObject _modelRoot;
        private GameObject _bboxObject;
        private bool _bboxVisible = true;
        private static Material _bboxMaterial;

        private string _currentZone = "Z01";
        private ZoneTable _currentTbl;
        private PaletteResource _currentPalette;
        private Color[] _unityPalette;
        private List<int> _validModelIndices = new();
        private int _currentModelIndex = 0;

        private TextMeshProUGUI _zoneText;
        private TextMeshProUGUI _modelText;
        private TextMeshProUGUI _modelInfoText;

        // Zone dropdown — built programmatically. The button shows the current
        // zone label; clicking it toggles a panel of 16 zone buttons that
        // closes itself on selection. Avoids TMP_Dropdown scaffolding overhead
        // for a small dev-tool list.
        private TextMeshProUGUI _zoneDropdownLabel;
        private GameObject _zoneDropdownPanel;

        // All 16 .TBL files: 12 base zones + 3 alternate-state ("M") zones + COMBAT.
        // COMBAT.TBL has no matching .PAL; the original game renders it with whichever
        // zone palette is loaded at the time, so the debug viewer falls back to Z01.PAL.
        // Z##M.TBL similarly reuse the matching Z##.PAL.
        private readonly List<string> _availableZones = new() {
            "Z01", "Z02", "Z03", "Z04", "Z05", "Z06",
            "Z07", "Z08", "Z09", "Z10", "Z11", "Z12",
            "Z10M", "Z11M", "Z12M", "COMBAT"
        };

        public ModelDebugState(
            ILogger<ModelDebugState> logger,
            IResourceProviderService resourceProvider,
            WorldRenderModeService renderModeService,
            IResourceCache resourceCache) {
            _logger = logger;
            _resourceProvider = resourceProvider;
            _renderModeService = renderModeService;
            _resourceCache = resourceCache;
        }

        public async UniTask RunAsync() {
            _logger.LogInformation("Entering ModelDebugState - single model viewer");

            SetupCamera();
            SetupUI();
            SetupModelRoot();

            await LoadZone(_currentZone);
        }


        private void SetupCamera() {
            foreach (var cam in Camera.allCameras) {
                cam.enabled = false;
            }

            var cameraGO = new GameObject("ModelDebugCamera");
            _camera = cameraGO.AddComponent<Camera>();
            cameraGO.tag = "MainCamera";
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 2000f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
        }

        private void SetupModelRoot() {
            _modelRoot = new GameObject("ModelRoot");
            _modelRoot.transform.position = Vector3.zero;
        }

        private void SetupUI() {
            var canvasGO = new GameObject("ModelDebugUI");
            canvasGO.transform.SetParent(null);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);

            canvasGO.AddComponent<GraphicRaycaster>();

            // Background panel
            var panelGO = new GameObject("Panel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panel = panelGO.AddComponent<Image>();
            panel.color = new Color(0, 0, 0, 0.85f);
            var panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(0.3f, 1f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Zone text
            _zoneText = CreateText(canvasGO, "ZoneText", _currentZone, 20,
                new Vector2(0.02f, 0.92f), new Vector2(0.28f, 0.97f));

            // Model text
            _modelText = CreateText(canvasGO, "ModelText", "Model: -", 16,
                new Vector2(0.02f, 0.85f), new Vector2(0.28f, 0.90f));

            // Model info text — 4 sections (Entity / LODs / Geometry / GID) plus header + bounds.
            _modelInfoText = CreateText(canvasGO, "ModelInfoText", "Type: -\nVertices: -\nFaces: -", 11,
                new Vector2(0.02f, 0.42f), new Vector2(0.28f, 0.83f));

            // Controls text — compact two-line summary so the info panel can grow.
            CreateText(canvasGO, "ControlsText",
                "← → rotate  drag orbit  scroll zoom\n" +
                "[ ] prev/next model    - + prev/next zone",
                11, new Vector2(0.02f, 0.31f), new Vector2(0.28f, 0.35f));

            // Buttons
            BuildZoneDropdown(canvasGO);

            CreateButton(canvasGO, "PrevModel", "Prev Model", new Vector2(0.02f, 0.18f), new Vector2(0.13f, 0.23f), PreviousModel);
            CreateButton(canvasGO, "NextModel", "Next Model", new Vector2(0.17f, 0.18f), new Vector2(0.28f, 0.23f), NextModel);

            CreateButton(canvasGO, "ResetView", "Reset View", new Vector2(0.02f, 0.11f), new Vector2(0.28f, 0.16f), ResetView);
            CreateButton(canvasGO, "ToggleWireframe", "Wire", new Vector2(0.02f, 0.04f), new Vector2(0.10f, 0.09f), ToggleWireframe);
            CreateButton(canvasGO, "ToggleBBox", "BBox", new Vector2(0.11f, 0.04f), new Vector2(0.19f, 0.09f), ToggleBBox);
            CreateButton(canvasGO, "ToggleGid", "GID", new Vector2(0.20f, 0.04f), new Vector2(0.28f, 0.09f), ToggleGid);

            _ui = canvasGO;

            // Add input component to camera
            var input = _camera.gameObject.AddComponent<ModelDebugInput>();
            input.Initialize(this);
        }

        private TextMeshProUGUI CreateText(GameObject parent, string name, string text, int fontSize, Vector2 anchorMin, Vector2 anchorMax) {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return tmp;
        }

        private void CreateButton(GameObject parent, string name, string text, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick) {
            var go = new GameObject(name + "Button");
            go.transform.SetParent(parent.transform, false);
            var button = go.AddComponent<Button>();
            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            button.onClick.AddListener(onClick);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 12;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void BuildZoneDropdown(GameObject canvasGO) {
            // Header button: shows current zone, toggles the panel.
            var headerGO = new GameObject("ZoneDropdownHeader");
            headerGO.transform.SetParent(canvasGO.transform, false);
            var headerImage = headerGO.AddComponent<Image>();
            headerImage.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
            var headerRect = headerGO.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0.02f, 0.25f);
            headerRect.anchorMax = new Vector2(0.28f, 0.30f);
            headerRect.offsetMin = Vector2.zero;
            headerRect.offsetMax = Vector2.zero;
            var headerButton = headerGO.AddComponent<Button>();
            headerButton.onClick.AddListener(ToggleZoneDropdown);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(headerGO.transform, false);
            _zoneDropdownLabel = labelGO.AddComponent<TextMeshProUGUI>();
            _zoneDropdownLabel.text = $"Zone: {_currentZone} ▾";
            _zoneDropdownLabel.fontSize = 12;
            _zoneDropdownLabel.color = Color.white;
            _zoneDropdownLabel.alignment = TextAlignmentOptions.Center;
            var labelRect = labelGO.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            // Panel: hidden by default, anchored ABOVE the header so the list
            // grows up into screen space rather than off the bottom.
            _zoneDropdownPanel = new GameObject("ZoneDropdownPanel");
            _zoneDropdownPanel.transform.SetParent(canvasGO.transform, false);
            var panelImage = _zoneDropdownPanel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
            var panelRect = _zoneDropdownPanel.GetComponent<RectTransform>();
            // 16 entries * 5% screen height = 80%; place between header (30%) and
            // controls text (~31%) up to near the top, so the list always fits.
            panelRect.anchorMin = new Vector2(0.02f, 0.30f);
            panelRect.anchorMax = new Vector2(0.28f, 0.95f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            _zoneDropdownPanel.SetActive(false);

            // One button per zone, stacked top-to-bottom inside the panel.
            int n = _availableZones.Count;
            for (int i = 0; i < n; i++) {
                string zoneId = _availableZones[i];
                float yMax = 1f - (float)i / n;
                float yMin = 1f - (float)(i + 1) / n;
                CreateZoneEntry(_zoneDropdownPanel, zoneId,
                    new Vector2(0f, yMin), new Vector2(1f, yMax));
            }
        }

        private void CreateZoneEntry(GameObject parent, string zoneId, Vector2 anchorMin, Vector2 anchorMax) {
            var go = new GameObject($"Entry_{zoneId}");
            go.transform.SetParent(parent.transform, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
            var button = go.AddComponent<Button>();
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(2f, 1f);
            rect.offsetMax = new Vector2(-2f, -1f);
            button.onClick.AddListener(() => SelectZone(zoneId));

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = zoneId;
            tmp.fontSize = 12;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void ToggleZoneDropdown() {
            if (_zoneDropdownPanel != null) {
                _zoneDropdownPanel.SetActive(!_zoneDropdownPanel.activeSelf);
            }
        }

        private void SelectZone(string zoneId) {
            if (_zoneDropdownPanel != null) {
                _zoneDropdownPanel.SetActive(false);
            }
            LoadZone(zoneId).Forget();
        }

        private async UniTask LoadZone(string zoneId) {
            _logger.LogInformation("Loading zone {Zone} for model debug", zoneId);
            _currentZone = zoneId;

            // Clean up previous
            if (_currentModel != null) {
                Object.Destroy(_currentModel);
                _currentModel = null;
            }
            if (_modelRoot != null) {
                _resourceProvider.ReleaseAssets(_modelRoot);
            }

            string paletteId = ResolvePaletteId(zoneId);

            try {
                _currentTbl = await _resourceProvider.LoadAssetAsync<ZoneTable>($"{zoneId}.TBL", _modelRoot);
                _currentPalette = await _resourceProvider.LoadAssetAsync<PaletteResource>($"{paletteId}.PAL", _modelRoot);
                _unityPalette = _currentPalette.Colors.ToUnity();

                // Same render context the game builds (terrain/polygon materials), minus fog and
                // sprites — the viewer only lists polygon-geometry models (never sprites), and a
                // centred close-up model needs no distance fog. Every model then renders through the
                // shared WorldEntityBuilder, exactly as it appears in-world.
                _renderContext = WorldEntityRenderContext.Create(
                    _unityPalette, _renderModeService.CreateProfile());

                // Slot-bitmap texturing: the extractor bakes each textured face's resource key
                // directly, so the viewer's loader just needs the shared resource cache to render
                // textured quads exactly as in-world.
                _renderContext.SlotLoader = new SlotBitmapTextureLoader(_resourceCache);
                _renderContext.ResourceProvider = _resourceProvider;

                _modelLoader?.Dispose();
                _modelLoader = new WorldModelLoader(_currentTbl, _renderContext);

                // Find all valid 3D models (polygon entities with renderable LOD-0 geometry)
                _validModelIndices.Clear();
                for (int i = 0; i < _currentTbl.Entries.Count; i++) {
                    var entry = _currentTbl.Entries[i];
                    if (TblMeshConverter.HasPolygonGeometry(entry.Dat)) {
                        _validModelIndices.Add(i);
                    }
                }

                _logger.LogInformation("Zone {Zone} loaded: {Count} models with geometry", zoneId, _validModelIndices.Count);

                if (_zoneText != null) {
                    _zoneText.text = $"{zoneId} ({_validModelIndices.Count} models)";
                }

                if (_zoneDropdownLabel != null) {
                    _zoneDropdownLabel.text = $"Zone: {zoneId} ▾";
                }

                if (_validModelIndices.Count > 0) {
                    _currentModelIndex = 0;
                    await LoadCurrentModel();
                } else {
                    if (_modelText != null) {
                        _modelText.text = "No 3D models found";
                    }
                    if (_modelInfoText != null) {
                        _modelInfoText.text = "This zone has no polygon models.\nTry another zone.";
                    }
                }
            } catch (System.Exception ex) {
                _logger.LogError(ex, "Failed to load zone {Zone}", zoneId);
                if (_zoneText != null) {
                    _zoneText.text = $"{zoneId} (ERROR)";
                }
                if (_zoneDropdownLabel != null) {
                    _zoneDropdownLabel.text = $"Zone: {zoneId} ▾";
                }
            }
        }

        // Z##M.TBL variants share their base zone's palette; COMBAT.TBL has no
        // dedicated palette of its own and is rendered against whichever zone
        // palette is active at combat time — fall back to Z01.PAL for the viewer.
        private static string ResolvePaletteId(string zoneId) {
            if (zoneId == "COMBAT") return "Z01";
            if (zoneId.EndsWith("M") && zoneId.Length >= 4) {
                return zoneId.Substring(0, zoneId.Length - 1);
            }
            return zoneId;
        }

        private async UniTask LoadCurrentModel(bool resetView = true) {
            if (_validModelIndices.Count == 0 || _currentModelIndex >= _validModelIndices.Count) {
                return;
            }

            int entryIndex = _validModelIndices[_currentModelIndex];
            var entry = _currentTbl.Entries[entryIndex];
            var dat = entry.Dat;

            _logger.LogInformation("Loading model {ModelIndex}/{Total}: {ModelName} (TypeId {TypeId})",
                _currentModelIndex + 1, _validModelIndices.Count, entry.Name, entryIndex);

            // Clean up previous model
            if (_currentModel != null) {
                Object.Destroy(_currentModel);
            }

            // Render via the same path the game uses (classify → convert → materials), so the viewer
            // shows depth-sorted houses / per-pen terrain exactly as they appear in-world.
            _currentModel = await WorldEntityBuilder.Build(entry, entryIndex,
                Vector3.zero, Quaternion.identity,
                _modelRoot.transform, _modelRoot.transform, _renderContext, _modelLoader);
            if (_currentModel == null) {
                _logger.LogWarning("Model {Name} (TypeId {Id}) is not renderable", entry.Name, entryIndex);
                if (_modelText != null) _modelText.text = $"{entry.Name}: not renderable";
                if (_modelInfoText != null) _modelInfoText.text = "Entity has no renderable geometry.";
                return;
            }
            var mesh = _currentModel.GetComponent<MeshFilter>().sharedMesh;

            // Center and scale the model
            var bounds = mesh.bounds;
            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float targetSize = 10f;
            if (maxDim > 0) {
                float scale = targetSize / maxDim;
                _currentModel.transform.localScale = new Vector3(scale, scale, scale);
            }
            _currentModel.transform.localPosition = -bounds.center * _currentModel.transform.localScale.x;

            // Old _bboxObject / _wireframeObject were children of the destroyed
            // _currentModel — clear dangling references and rebuild.
            _bboxObject = null;
            if (_bboxVisible) BuildBBoxRenderer(dat);
            _wireframeObject = null;
            if (_wireframe) BuildWireframeOverlay();
            _gidObject = null;
            if (_gidVisible) BuildGidOverlay(entry.Gid, dat);

            if (resetView) ResetView();

            // Update UI
            if (_modelText != null) {
                _modelText.text = $"Model {_currentModelIndex + 1}/{_validModelIndices.Count}: {entry.Name}";
            }

            if (_modelInfoText != null) {
                _modelInfoText.text = BuildModelInfoText(entry, entryIndex, bounds);
            }
        }

        public void ToggleBBox() {
            _bboxVisible = !_bboxVisible;
            if (_bboxVisible) {
                if (_validModelIndices.Count == 0) return;
                int entryIndex = _validModelIndices[_currentModelIndex];
                BuildBBoxRenderer(_currentTbl.Entries[entryIndex].Dat);
            } else if (_bboxObject != null) {
                Object.Destroy(_bboxObject);
                _bboxObject = null;
            }
        }

        private void BuildBBoxRenderer(TableDatInfo dat) {
            if (_currentModel == null || dat?.Min == null || dat.Max == null) return;

            // BBox ships pre-scaled by the extractor, exactly like the mesh vertices in
            // TblMeshConverter, so both need only the Y/Z swap to share local space.
            var a = BakCoordinateConverter.ConvertPosition(dat.Min.X, dat.Min.Y, dat.Min.Z);
            var b = BakCoordinateConverter.ConvertPosition(dat.Max.X, dat.Max.Y, dat.Max.Z);
            var lo = Vector3.Min(a, b);
            var hi = Vector3.Max(a, b);

            var corners = new Vector3[8] {
                new(lo.x, lo.y, lo.z), new(hi.x, lo.y, lo.z),
                new(hi.x, hi.y, lo.z), new(lo.x, hi.y, lo.z),
                new(lo.x, lo.y, hi.z), new(hi.x, lo.y, hi.z),
                new(hi.x, hi.y, hi.z), new(lo.x, hi.y, hi.z)
            };
            var indices = new int[] {
                0,1, 1,2, 2,3, 3,0,   // -Z face
                4,5, 5,6, 6,7, 7,4,   // +Z face
                0,4, 1,5, 2,6, 3,7    // connecting edges
            };

            var mesh = new Mesh { name = "BBoxWire" };
            mesh.vertices = corners;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("BBox");
            go.transform.SetParent(_currentModel.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = GetBBoxMaterial();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            _bboxObject = go;
        }

        private static Material GetBBoxMaterial() {
            if (_bboxMaterial == null) {
                // URP-friendly unlit; falls back to Hidden/Internal-Colored if URP shader
                // isn't available (e.g. tests).
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Hidden/Internal-Colored");
                _bboxMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                if (_bboxMaterial.HasProperty(BaseColor))
                    _bboxMaterial.SetColor(BaseColor, Color.cyan);
                else
                    _bboxMaterial.color = Color.cyan;
            }
            return _bboxMaterial;
        }

        private static string BuildModelInfoText(ZoneTableEntry entry, int entryIndex, Bounds bounds) {
            var dat = entry.Dat;
            var sb = new System.Text.StringBuilder(1024);

            sb.Append("Name: ").Append(entry.Name).Append('\n');
            sb.Append("TypeId: ").Append(entryIndex).Append('\n');

            sb.Append("\n[Entity]\n");
            sb.Append("Type/DrawPri/Scale: ")
                .Append(dat.EntityType).Append(" / ")
                .Append(dat.DrawPriority).Append(" / ")
                .Append(dat.VertexScale).Append('\n');
            sb.Append("Flags: 0x").Append(dat.EntityFlags.ToString("X2"))
                .Append(" (").Append(FlagsToString(dat)).Append(")\n");
            sb.Append("Extent: ").Append(dat.Extent).Append('\n');
            if (dat.Unknown04 != 0 || dat.Unknown06 != 0) {
                sb.Append("Unk04/06: ").Append(dat.Unknown04).Append(" / ")
                    .Append(dat.Unknown06).Append('\n');
            }
            if (dat.Min != null && dat.Max != null) {
                sb.Append("BBox: (").Append(dat.Min.X).Append(',').Append(dat.Min.Y).Append(',').Append(dat.Min.Z)
                    .Append(")..(").Append(dat.Max.X).Append(',').Append(dat.Max.Y).Append(',').Append(dat.Max.Z)
                    .Append(")\n");
            } else {
                sb.Append("BBox: omitted (UNBOUNDED)\n");
            }

            sb.Append("\n[LODs] ").Append(dat.Lods.Count).Append(" level(s)\n");
            for (int li = 0; li < dat.Lods.Count; li++) {
                var lod = dat.Lods[li];
                sb.Append(' ').Append(li)
                    .Append(": thr=").Append(lod.Threshold)
                    .Append(" meshes=").Append(lod.Meshes.Count)
                    .Append(" [").Append(SummarizeFaceTypes(lod)).Append("]\n");
            }

            // Geometry stats — walk LOD 0 (what TblMeshConverter renders).
            int totalVertices = 0;
            int totalFaces = 0;
            int validFaces = 0;
            int degenerateFaces = 0;
            int lineFaces = 0;
            int invalidIndexFaces = 0;
            int minVertsPerFace = int.MaxValue;
            int maxVertsPerFace = 0;
            if (dat.Lods.Count > 0) {
                var lod0 = dat.Lods[0];
                // Sum unique pool sizes — submeshes share pools, so this reflects actual
                // geometry rather than over-counting shared verts.
                foreach (var pool in lod0.VertexPools) totalVertices += pool.Count;
                foreach (var meshRec in lod0.Meshes) {
                    if (meshRec.MeshFaces.Count == 0
                        || !(meshRec.MeshFaces[0] is PolygonMeshFace polyFace)) {
                        continue;
                    }
                    int poolSize = (meshRec.VertexPoolIndex >= 0
                                    && meshRec.VertexPoolIndex < lod0.VertexPools.Count)
                        ? lod0.VertexPools[meshRec.VertexPoolIndex].Count
                        : 0;
                    foreach (var face in polyFace.Faces) {
                        totalFaces++;
                        // A two-vertex face is a LINE, which the converter now renders (see
                        // TblMeshConverter.IsRenderableLine). Counting it as degenerate said the
                        // catapult had 15 broken faces when it has 15 ropes.
                        if (face.VertexIndices.Count == 2) {
                            lineFaces++;
                            continue;
                        }
                        if (face.VertexIndices.Count < 3) {
                            degenerateFaces++;
                            continue;
                        }
                        bool hasInvalid = false;
                        foreach (int vi in face.VertexIndices) {
                            if (vi < 0 || vi >= poolSize) {
                                hasInvalid = true;
                                break;
                            }
                        }
                        if (hasInvalid) {
                            invalidIndexFaces++;
                            continue;
                        }
                        validFaces++;
                        minVertsPerFace = Mathf.Min(minVertsPerFace, face.VertexIndices.Count);
                        maxVertsPerFace = Mathf.Max(maxVertsPerFace, face.VertexIndices.Count);
                    }
                }
            }
            if (minVertsPerFace == int.MaxValue) minVertsPerFace = 0;

            sb.Append("\n[Geometry] (LOD 0)\n");
            sb.Append("Verts: ").Append(totalVertices).Append('\n');
            sb.Append("Faces: ").Append(totalFaces)
                .Append(" (ok=").Append(validFaces)
                .Append(" line=").Append(lineFaces)
                .Append(" deg=").Append(degenerateFaces)
                .Append(" bad=").Append(invalidIndexFaces).Append(")\n");
            sb.Append("Verts/Face: ").Append(minVertsPerFace).Append('-').Append(maxVertsPerFace).Append('\n');
            int spriteIdx = TblMeshConverter.FindSpriteBitmapIndex(dat);
            sb.Append("Sprite: ").Append(spriteIdx).Append('\n');

            var gid = entry.Gid;
            {
                sb.Append("\n[GID]\n");
                sb.Append("Radius: ").Append(gid.XRadius).Append("x").Append(gid.YRadius).Append('\n');
                sb.Append("Flags: 0x").Append(gid.Flags.ToString("X2"));
                if (gid.IsSloped) sb.Append(" SLOPED");
                sb.Append('\n');
                int totalSubedges = 0;
                foreach (var region in gid.Regions) totalSubedges += region.Subedges.Count;
                sb.Append("Regions: ").Append(gid.Regions.Count)
                    .Append(" (subedges=").Append(totalSubedges).Append(")\n");
            }

            sb.Append("\nUnity bounds: ").Append(bounds.size.ToString("F2"));
            return sb.ToString();
        }

        private static string FlagsToString(TableDatInfo dat) {
            if (dat.EntityFlags == 0) return "none";
            var parts = new List<string>();
            if (dat.IsUnbounded) parts.Add("UNBOUNDED");
            if (dat.IsDepthSorted) parts.Add("DEPTH_SORTED");
            byte other = (byte)(dat.EntityFlags & ~0x60);   // debug: any unexpected raw bits
            if (other != 0) parts.Add($"0x{other:X2}");
            return string.Join("|", parts);
        }

        private static string SummarizeFaceTypes(LodLevel lod) {
            int poly = 0, spriteA = 0, spriteB = 0, empty = 0;
            foreach (var meshRec in lod.Meshes) {
                if (meshRec.MeshFaces.Count == 0) { empty++; continue; }
                switch (meshRec.MeshFaces[0]) {
                    case PolygonMeshFace _: poly++; break;
                    case SpriteAMeshFace _: spriteA++; break;
                    case SpriteBMeshFace _: spriteB++; break;
                    default: empty++; break;
                }
            }
            var parts = new List<string>();
            if (poly > 0) parts.Add($"{poly}P");
            if (spriteA > 0) parts.Add($"{spriteA}sA");
            if (spriteB > 0) parts.Add($"{spriteB}sB");
            if (empty > 0) parts.Add($"{empty}-");
            return parts.Count > 0 ? string.Join(" ", parts) : "-";
        }

        public void PreviousZone() {
            int idx = _availableZones.IndexOf(_currentZone);
            if (idx < 0) idx = 0;
            idx = (idx - 1 + _availableZones.Count) % _availableZones.Count;
            LoadZone(_availableZones[idx]).Forget();
        }

        public void NextZone() {
            int idx = _availableZones.IndexOf(_currentZone);
            if (idx < 0) idx = 0;
            idx = (idx + 1) % _availableZones.Count;
            LoadZone(_availableZones[idx]).Forget();
        }

        public void PreviousModel() {
            if (_validModelIndices.Count == 0) return;
            _currentModelIndex = (_currentModelIndex - 1 + _validModelIndices.Count) % _validModelIndices.Count;
            LoadCurrentModel().Forget();
        }

        public void NextModel() {
            if (_validModelIndices.Count == 0) return;
            _currentModelIndex = (_currentModelIndex + 1) % _validModelIndices.Count;
            LoadCurrentModel().Forget();
        }

        public void ResetView() {
            if (_camera != null) {
                _camera.transform.position = new Vector3(0, 5, -15);
                _camera.transform.rotation = Quaternion.Euler(15, 0, 0);
            }
            if (_modelRoot != null) {
                _modelRoot.transform.rotation = Quaternion.identity;
            }
        }

        public void RotateModel(float deltaY) {
            if (_modelRoot != null) {
                _modelRoot.transform.Rotate(Vector3.up, deltaY, Space.World);
            }
        }

        private bool _wireframe;
        private GameObject _wireframeObject;
        private static Material _wireframeMaterial;

        public void ToggleWireframe() {
            _wireframe = !_wireframe;
            if (_wireframe) {
                BuildWireframeOverlay();
            } else if (_wireframeObject != null) {
                Object.Destroy(_wireframeObject);
                _wireframeObject = null;
            }
        }

        private void BuildWireframeOverlay() {
            if (_currentModel == null) return;
            var filter = _currentModel.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;

            var srcMesh = filter.sharedMesh;
            var srcTris = srcMesh.triangles;

            // Extract unique edges from triangle list
            var edgeSet = new System.Collections.Generic.HashSet<long>();
            var lineIndices = new List<int>();
            for (int i = 0; i < srcTris.Length; i += 3) {
                int a = srcTris[i], b = srcTris[i + 1], c = srcTris[i + 2];
                AddEdge(edgeSet, lineIndices, a, b);
                AddEdge(edgeSet, lineIndices, b, c);
                AddEdge(edgeSet, lineIndices, c, a);
            }

            var mesh = new Mesh { name = "Wireframe" };
            mesh.vertices = srcMesh.vertices;
            mesh.SetIndices(lineIndices.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Wireframe");
            go.transform.SetParent(_currentModel.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = GetWireframeMaterial();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            _wireframeObject = go;
        }

        private static void AddEdge(System.Collections.Generic.HashSet<long> edgeSet,
            List<int> lineIndices, int a, int b) {
            long key = a < b ? ((long)a << 32 | (uint)b) : ((long)b << 32 | (uint)a);
            if (edgeSet.Add(key)) {
                lineIndices.Add(a);
                lineIndices.Add(b);
            }
        }

        private static Material GetWireframeMaterial() {
            if (_wireframeMaterial == null) {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Hidden/Internal-Colored");
                _wireframeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                if (_wireframeMaterial.HasProperty("_BaseColor"))
                    _wireframeMaterial.SetColor("_BaseColor", Color.yellow);
                else
                    _wireframeMaterial.color = Color.yellow;
            }
            return _wireframeMaterial;
        }

        public void OrbitCamera(float deltaX, float deltaY) {
            if (_camera != null && _currentModel != null) {
                _camera.transform.RotateAround(Vector3.zero, Vector3.up, deltaX);
                _camera.transform.RotateAround(Vector3.zero, _camera.transform.right, -deltaY);
            }
        }

        public void ZoomCamera(float delta) {
            if (_camera != null) {
                _camera.transform.position += _camera.transform.forward * delta;
            }
        }

        // GID overlay — draws each region's polygon footprint at its base elevation
        // (or per-vertex sloped elevation), so the collision/elevation grid can be
        // eyeballed alongside the rendered mesh. See docs/FileFormats/ZoneTable-DAT.md §0b.
        private bool _gidVisible = true;
        private GameObject _gidObject;
        private static Material _gidMaterial;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void ToggleGid() {
            _gidVisible = !_gidVisible;
            if (_gidVisible) {
                if (_validModelIndices.Count == 0) return;
                int entryIndex = _validModelIndices[_currentModelIndex];
                var entry = _currentTbl.Entries[entryIndex];
                BuildGidOverlay(entry.Gid, entry.Dat);
            } else if (_gidObject != null) {
                Object.Destroy(_gidObject);
                _gidObject = null;
            }
        }

        private void BuildGidOverlay(TableGidInfo gid, TableDatInfo dat) {
            if (_currentModel == null || gid == null || gid.Regions.Count == 0) return;

            // GID polygons live in entity-local map space (X, Y planar; elevation as BaK Z) and,
            // unlike DAT vertices/bbox, are NOT pre-scaled by the extractor — so the 2^VertexScale
            // shift is still applied here. Elevations already carry the ×1.2 world-up bake, and
            // ×1.2-then-shift equals shift-then-×1.2, so the overlay stays aligned with the mesh.
            int vertexScale = 1 << dat.VertexScale;
            bool sloped = gid.IsSloped;

            var verts = new List<Vector3>();
            var indices = new List<int>();

            foreach (var region in gid.Regions) {
                int n = region.Subedges.Count;
                if (n < 2) continue;
                int baseIdx = verts.Count;
                for (int i = 0; i < n; i++) {
                    var se = region.Subedges[i];
                    int z = region.BaseElevation;
                    if (sloped && region.Slope != null) {
                        // Same formula as computeSlopedRegionElevation (0x2a05d).
                        int dx = region.Slope.AnchorX - se.AnchorX;
                        int dy = region.Slope.AnchorY - se.AnchorY;
                        int gradient = (region.Slope.A * dx + region.Slope.B * dy) * region.SlopeShift;
                        z = region.BaseElevation + (gradient >> 12);
                    }
                    verts.Add(BakCoordinateConverter.ConvertPosition(
                        se.AnchorX * vertexScale, se.AnchorY * vertexScale, z * vertexScale));
                }
                for (int i = 0; i < n; i++) {
                    indices.Add(baseIdx + i);
                    indices.Add(baseIdx + (i + 1) % n);
                }
            }

            if (verts.Count == 0) return;

            var mesh = new Mesh { name = "GidOverlay" };
            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts.ToArray();
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("GidOverlay");
            go.transform.SetParent(_currentModel.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = GetGidMaterial();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            _gidObject = go;
        }

        private static Material GetGidMaterial() {
            if (_gidMaterial == null) {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Hidden/Internal-Colored");
                _gidMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                var color = new Color(0.2f, 1f, 0.4f, 1f);
                if (_gidMaterial.HasProperty("_BaseColor"))
                    _gidMaterial.SetColor("_BaseColor", color);
                else
                    _gidMaterial.color = color;
            }
            return _gidMaterial;
        }
    }
}

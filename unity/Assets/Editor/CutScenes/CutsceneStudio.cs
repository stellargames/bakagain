namespace BakAgain.Editor.Cutscenes {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Palette;
    using JetBrains.Annotations;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.ResourceLocations;
    using UnityEngine.UIElements;
    using Color = UnityEngine.Color;
    using Image = UnityEngine.UIElements.Image;

    public class CutsceneStudio : EditorWindow {
        [SerializeField]
        private VisualTreeAsset visualTree;

        [SerializeField]
        private StyleSheet styleSheet;

        private int _cutsceneTreeViewId;
        private CutsceneState _cutsceneState;
        private ICutsceneFrameProcessor _frameProcessor;
        private static CancellationTokenSource _cancellation;
        private VisualElement _highlightRect;
        private Image[] _bufferImages;
        private Label[] _bufferStatusLabels;
        private Toggle _stepToggle;

        private static OverrideResourceProvider _overrideResourceProvider;
        private static BakResourceProvider _bakResourceProvider;
        private static OverrideResourceLocator _overrideResourceLocator;
        private static BakResourceLocator _bakResourceLocator;

        private static OverrideResourceProvider OverrideResourceProvider {
            get => _overrideResourceProvider ??= new OverrideResourceProvider();
        }

        private static BakResourceProvider BakResourceProvider {
            get => _bakResourceProvider ??= new BakResourceProvider();
        }

        private static OverrideResourceLocator OverrideResourceLocator {
            get => _overrideResourceLocator ??= new OverrideResourceLocator();
        }

        private static BakResourceLocator BakResourceLocator {
            get => _bakResourceLocator ??= new BakResourceLocator();
        }

        public void CreateGUI() {
            visualTree.CloneTree(rootVisualElement);
            rootVisualElement.styleSheets.Add(styleSheet);

            // *** HERE, NOT ONLY IN THE MENU ITEM. *** _cancellation is static, so every domain
            // reload — a recompile, entering or leaving play mode — nulls it, while the EditorWindow
            // itself is serialised and comes back. Selecting a frame then dereferences
            // _cancellation.Token and throws NullReferenceException BEFORE any command runs, so the
            // studio draws nothing and stays black. The throw is inside a UniTaskVoid, so it
            // surfaces only as an unobserved-exception log behind the selection change, which is
            // exactly how this read as "the frame commands do nothing".
            //
            // Reproduced 2026-09-06 (TASK-345): with the window restored after a reload,
            // OnFrameSelectedAsync threw at the ProcessFrameEditorAsync call with a null
            // _cancellation. CreateGUI runs on every reload, which is the point.
            //
            // Same shape as the preview Texture2D fixed above: a thing the window depends on that
            // does not survive a reload the window does survive.
            // Not `??=`: OnDisable CANCELS it, and a docked tab going inactive and active again
            // would then leave a live-but-cancelled source, so every later frame would be cancelled
            // instead of drawn — the same black window by a second route.
            if (_cancellation == null || _cancellation.IsCancellationRequested) {
                _cancellation = new CancellationTokenSource();
            }

            _ = InitializeAsync();
            BakResourceProvider.ClearCaches();
        }

        [MenuItem("BaK Again/Cutscene Studio")]
        public static void Init() {
            if (!Addressables.ResourceManager.ResourceProviders.OfType<OverrideResourceProvider>().Any()) {
                Addressables.ResourceManager.ResourceProviders.Add(OverrideResourceProvider);
            }
            if (!Addressables.ResourceManager.ResourceProviders.OfType<BakResourceProvider>().Any()) {
                Addressables.ResourceManager.ResourceProviders.Add(BakResourceProvider);
            }
            if (!Addressables.ResourceLocators.OfType<OverrideResourceLocator>().Any()) {
                Addressables.AddResourceLocator(OverrideResourceLocator);
            }
            if (!Addressables.ResourceLocators.OfType<BakResourceLocator>().Any()) {
                Addressables.AddResourceLocator(BakResourceLocator);
            }

            var wnd = GetWindow<CutsceneStudio>();
            wnd.titleContent = new GUIContent("Cutscene Studio");

            var size = new Vector2(1200, 800);
            wnd.minSize = size;
            wnd.maxSize = size;
            _cancellation = new CancellationTokenSource();
        }

        private async UniTaskVoid InitializeAsync() {
            // Poor-man's DI for the editor window
            var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
            ILogger<CutsceneFrameProcessor> frameProcessorLogger =
                ConditionalLoggingExtensions.CreateLogger<CutsceneFrameProcessor>(loggerFactory);
            var resourceCache = new ResourceCache();
            _frameProcessor = new CutsceneFrameProcessor(frameProcessorLogger, resourceCache);

            var canvas = rootVisualElement.Q<Image>("PreviewImage");
            if (!canvas.image) {
                var texture2D = new Texture2D(640, 400, TextureFormat.RGBA32, false, false) {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    // *** WITHOUT THIS THE PREVIEW DIES ON EVERY DOMAIN RELOAD. *** A Texture2D
                    // made here is owned by nothing that survives a reload, so entering play mode
                    // or any recompile destroys it — while the EditorWindow IS serialised and comes
                    // back still referencing it. CutsceneState.Initialize then reads
                    // ContainerImage.Texture.width and throws MissingReferenceException, so no
                    // buffers are built and the studio renders nothing, with the exception buried
                    // behind a selection change. HideAndDontSave is the idiom for a texture an
                    // editor window owns.
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture2D.Apply();
                canvas.image = texture2D;
            }

            var defaultPalette = await Addressables.LoadAssetAsync<PaletteResource>("options.pal");
            var uiImage = new UiImage(canvas);
            _cutsceneState = new CutsceneState(uiImage, defaultPalette.Colors.ToUnity());

            await InitAdsFilePickerTreeView();

            // Register key event callback for frameList
            var frameList = rootVisualElement.Q<ScrollView>("FrameList");
            frameList.focusable = true;
            frameList.Focus();
            frameList.RegisterCallback<KeyDownEvent>(OnFrameListKeyDown);

            var highlightColor = new Color(1, 1, 0, 0.5f);
            _highlightRect = new VisualElement {
                style = {
                    position = Position.Absolute,
                    borderTopColor = highlightColor,
                    borderBottomColor = highlightColor,
                    borderLeftColor = highlightColor,
                    borderRightColor = highlightColor,
                    borderTopWidth = 0.5f,
                    borderBottomWidth = 0.5f,
                    borderLeftWidth = 0.5f,
                    borderRightWidth = 0.5f,
                    visibility = Visibility.Hidden
                }
            };
            canvas.parent.Add(_highlightRect);

            _bufferImages = new Image[4];
            _bufferStatusLabels = new Label[4];
            for (var i = 0; i < 4; i++) {
                _bufferImages[i] = rootVisualElement.Q<Image>($"BufferImage{i}");
                _bufferStatusLabels[i] = rootVisualElement.Q<Label>($"BufferStatus{i}");
            }

            _stepToggle = rootVisualElement.Q<Toggle>("StepToggle");
        }

        private async UniTask InitAdsFilePickerTreeView() {
            var adsFilePickerTreeView = rootVisualElement.Q<TreeView>("AdsFilePickerTreeView");
            List<TreeViewItemData<object>> rootItems = await GetAdsTreeViewData();
            adsFilePickerTreeView.SetRootItems(rootItems);
            adsFilePickerTreeView.makeItem = () => new Label();
            adsFilePickerTreeView.bindItem = (element, index) => {
                object itemDataForIndex = adsFilePickerTreeView.GetItemDataForIndex<object>(index);
                ((Label)element).text = itemDataForIndex switch {
                    IResourceLocator locator => locator is OverrideResourceLocator ? "Overrides" : "Originals",
                    AnimatorResource resource => resource.Id,
                    null => "null",
                    _ => throw new InvalidOperationException($"Unknown item type {itemDataForIndex.GetType()}")
                };
            };
            adsFilePickerTreeView.selectionType = SelectionType.Single;
            adsFilePickerTreeView.selectionChanged += items => _ = OnAdsFilePickerTreeViewSelectionChangedAsync(items);
        }

        private void OnFrameListKeyDown(KeyDownEvent evt) {
            var frameList = rootVisualElement.Q<ScrollView>("FrameList");
            var selectedFrame = frameList.Children().FirstOrDefault(child => child.ClassListContains("selected"));

            if (selectedFrame == null)
                return;

            int selectedIndex = frameList.IndexOf(selectedFrame);
            switch (evt.keyCode) {
                case KeyCode.RightArrow when selectedIndex < frameList.childCount - 1:
                    {
                        var nextFrame = frameList[selectedIndex + 1];
                        frameList.ScrollTo(nextFrame);
                        _ = OnFrameSelectedAsync(nextFrame);

                        break;
                    }
                case KeyCode.LeftArrow when selectedIndex > 0:
                    {
                        var previousFrame = frameList[selectedIndex - 1];
                        frameList.ScrollTo(previousFrame);
                        _ = OnFrameSelectedAsync(previousFrame);

                        break;
                    }
            }
        }

        private async UniTaskVoid OnAdsFilePickerTreeViewSelectionChangedAsync(IEnumerable<object> selectedItems) {
            IEnumerable<object> items = selectedItems as object[] ?? selectedItems.ToArray();
            if (!items.Any()) {
                return;
            }
            var treeView = rootVisualElement.Q<TreeView>("AdsFilePickerTreeView");
            object selectedItem = items.First();
            int selectedId = treeView.selectedIds.First();

            switch (selectedItem) {
                case IResourceLocator locator:
                    Debug.Log($"Selected locator: {locator.LocatorId}");
                    treeView.CollapseAll();
                    treeView.ExpandItem(selectedId);

                    break;
                case AnimatorResource animatorResource:
                    int parentId = treeView.GetParentIdForIndex(treeView.selectedIndex);
                    object parentData = treeView.GetItemDataForId<object>(parentId);

                    // Set the appropriate provider and locator based on parent branch
                    SetResourceLocatorAndProvider(parentData);
                    await AnimatorResourceSelected(animatorResource);

                    break;
            }
        }

        private static void SetResourceLocatorAndProvider(object parentData) {
            if (parentData == OverrideResourceLocator) {
                Addressables.RemoveResourceLocator(BakResourceLocator);
                Addressables.ResourceManager.ResourceProviders.Remove(BakResourceProvider);
                Addressables.AddResourceLocator(OverrideResourceLocator);
                Addressables.ResourceManager.ResourceProviders.Add(OverrideResourceProvider);
            } else if (parentData == BakResourceLocator) {
                Addressables.RemoveResourceLocator(OverrideResourceLocator);
                Addressables.ResourceManager.ResourceProviders.Remove(OverrideResourceProvider);
                Addressables.AddResourceLocator(BakResourceLocator);
                Addressables.ResourceManager.ResourceProviders.Add(BakResourceProvider);
            }
        }

        private async UniTask<List<TreeViewItemData<object>>> GetAdsTreeViewData() {
            _cutsceneTreeViewId = 0;
            var adsExtension = nameof(ResourceType.ADS);
            IEnumerable<object> overrideKeys = OverrideResourceLocator.Keys.Where(key => key.ToString().EndsWith(adsExtension));
            IEnumerable<object> bakKeys = BakResourceLocator.Keys.Where(key => key.ToString().EndsWith(adsExtension));
            List<TreeViewItemData<object>> overrideTreeViewItems = await LoadAssetsIntoTreeViewItems(overrideKeys, OverrideResourceLocator);
            List<TreeViewItemData<object>> bakTreeViewItems = await LoadAssetsIntoTreeViewItems(bakKeys, BakResourceLocator);

            var rootItems = new List<TreeViewItemData<object>> {
                new(_cutsceneTreeViewId++, OverrideResourceLocator, overrideTreeViewItems),
                new(_cutsceneTreeViewId++, BakResourceLocator, bakTreeViewItems)
            };

            return rootItems;
        }

        private async UniTask AnimatorResourceSelected(AnimatorResource animatorResource) {
            _cutsceneState.Reset();
            UpdateBufferViews();
            Debug.Log($"Selected resource: {animatorResource.Id}");

            string ttmFile = animatorResource.ResourceFiles.First().Value;
            var animationResource = await Addressables.LoadAssetAsync<AnimationResource>(ttmFile);

            InitAnimationsList(animatorResource);

            var frameList = rootVisualElement.Q<ScrollView>("FrameList");
            frameList.Clear();
            for (var index = 1; index <= animationResource.Frames.Count; index++) {
                var frame = animationResource.Frames[index - 1];
                string tagLabel = null;
                if (frame.Tag != null) {
                    animationResource.Tags.TryGetValue(frame.Tag.Value, out string tagName);
                    tagLabel = $"{frame.Tag}:  \"{tagName}\"";
                }
                var frameContainer = MakeFrameContainer(index, frame, tagLabel);
                frameList.Add(frameContainer);
            }
            _cutsceneState.CurrentImageSlot = 0;
            _cutsceneState.ImageSlots.Clear();
        }

        private void InitAnimationsList(AnimatorResource animatorResource) {
            var animationsList = rootVisualElement.Q<ListView>("AdsAnimationsList");
            animationsList.Clear();
            animationsList.itemsSource = animatorResource.Animations;
            animationsList.makeItem = () => new Label();
            animationsList.bindItem = (element, index) => {
                if (index >= animatorResource.Animations.Count) {
                    return;
                }
                var label = (Label)element;
                label.text = animatorResource.Animations[index].Id + ": " + animatorResource.Animations[index].Tag;
            };
            animationsList.selectionType = SelectionType.Single;
            animationsList.selectionChanged += OnAnimationsListSelectionChanged;
            string script = animatorResource.Animations[0].Script;
            SetAdsFileContent(script);
        }

        private void SetAdsFileContent(string script) {
            var adsFileContent = rootVisualElement.Q<ScrollView>("AdsFileContent");
            adsFileContent.Clear();
            adsFileContent.Add(new Label(script));
        }

        private void OnAnimationsListSelectionChanged(IEnumerable<object> objects) {
            IEnumerable<object> selectedItems = objects as object[] ?? objects.ToArray();
            if (!selectedItems.Any()) {
                return;
            }
            object selectedItem = selectedItems.First();
            if (selectedItem is not AnimatorScript animation) {
                return;
            }
            SetAdsFileContent(animation.Script);
        }

        private VisualElement MakeFrameContainer(int frameIndex, Frame frame, [CanBeNull] string tagLabel) {
            var frameContainer = new VisualElement();
            frameContainer.AddToClassList("frame-container");
            frameContainer.AddToClassList(frameIndex % 2 == 0 ? "even" : "odd");
            frameContainer.userData = frame.Commands;

            frameContainer.RegisterCallback<ClickEvent>(FrameSelectCallback);
            var label = new Label($"{tagLabel}\nFrame {frameIndex}");
            frameContainer.Add(label);
            if (frameContainer.userData is List<FrameCommand> frameCommands) {
                _frameProcessor.PreloadFrameResourcesEditor(frameCommands, _cutsceneState);
            }

            return frameContainer;
        }

        private void FrameSelectCallback(ClickEvent evt) {
            if (evt.currentTarget is VisualElement frameContainer) {
                _ = OnFrameSelectedAsync(frameContainer);
            }
        }

        private async UniTaskVoid OnFrameSelectedAsync(VisualElement frame) {
            var frameList = rootVisualElement.Q<ScrollView>("FrameList");
            MarkAsSelected(frame, frameList);
            _highlightRect.style.visibility = Visibility.Hidden;

            var frameCommands = (List<FrameCommand>)frame.userData;
            var listView = rootVisualElement.Q<ListView>("CommandList");
            listView.itemsSource = frameCommands;
            listView.makeItem = () => new Label();
            listView.bindItem = (element, index) => {
                if (index >= frameCommands.Count) {
                    return;
                }
                var text = frameCommands[index].ToString();
                var label = (Label)element;

                label.style.color = text.StartsWith("Unknown") ? Color.red : Color.white;
                label.text = text;
            };
            listView.selectionChanged += OnCommandListSelectionChanged;
            listView.ClearSelection();

            if (_stepToggle is {value: false}) {
                await _frameProcessor.ProcessFrameEditorAsync(frameCommands, _cutsceneState, _cancellation.Token);
                UpdateBufferViews();
            }
        }

        private static void MarkAsSelected(VisualElement element, VisualElement container) {
            foreach (var child in container.Children()) {
                child.RemoveFromClassList("selected");
            }
            element.AddToClassList("selected");
        }

        private async UniTask<List<TreeViewItemData<object>>> LoadAssetsIntoTreeViewItems(IEnumerable<object> keys,
            IResourceLocator resourceLocator) {
            var list = new List<TreeViewItemData<object>>();
            foreach (object key in keys) {
                if (resourceLocator.Locate(key, typeof(AnimatorResource), out IList<IResourceLocation> locations)) {
                    var location = locations.First();
                    var resource = await Addressables.LoadAssetAsync<AnimatorResource>(location);
                    list.Add(new TreeViewItemData<object>(_cutsceneTreeViewId++, resource));
                }
            }

            return list;
        }

        private void OnDisable() {
            _cancellation?.Cancel();
        }

        private void OnDestroy() {
            _cutsceneState?.Dispose();
            BakResourceProvider?.ClearCaches();
        }

        private void OnCommandListSelectionChanged(IEnumerable<object> selectedItems) {
            _ = OnCommandListSelectionChangedAsync(selectedItems);
        }

        private async UniTaskVoid OnCommandListSelectionChangedAsync(IEnumerable<object> selectedItems) {
            _highlightRect.style.visibility = Visibility.Hidden;

            object[] items = selectedItems.ToArray();
            if (!items.Any()) {
                return;
            }

            if (items.First() is not FrameCommand command) {
                return;
            }

            if (_stepToggle is not {value: false}) {
                await _frameProcessor.ProcessFrameEditorAsync(new List<FrameCommand> {
                    command
                }, _cutsceneState, _cancellation.Token);
                UpdateBufferViews();
            }
            Rect? area;
            switch (command) {
                case IArea areaCommand:
                    area = new Rect(areaCommand.X, areaCommand.Y, areaCommand.Width, areaCommand.Height);

                    break;
                case DrawImageBase imageCommand:
                    var image = _cutsceneState.GetImage(imageCommand.ImageSlot, imageCommand.ImageNumber);
                    System.Diagnostics.Debug.Assert(image != null, nameof(image) + " != null");
                    area = new Rect(imageCommand.X, imageCommand.Y, image.Width, image.Height);

                    break;
                case DrawAreaFromBuffer bufferCommand:
                    var bufferArea = _cutsceneState.SavedRectArea(bufferCommand.BufferNumber);
                    area = bufferArea == null
                        ? null
                        : new Rect(bufferArea.X, bufferArea.Y, bufferArea.Width, bufferArea.Height);

                    break;
                default:
                    area = null;

                    break;
            }

            if (!area.HasValue)
                return;
            var canvas = rootVisualElement.Q<Image>("PreviewImage");
            var imageRect = canvas.contentRect;
            const float originalWidth = 320;
            const float originalHeight = 240;

            float scaleX = imageRect.width / originalWidth;
            float scaleY = imageRect.height / originalHeight;

            _highlightRect.style.left = area.Value.x * scaleX;
            _highlightRect.style.top = area.Value.y * scaleY;
            _highlightRect.style.width = area.Value.width * scaleX;
            _highlightRect.style.height = area.Value.height * scaleY;
            _highlightRect.style.visibility = Visibility.Visible;
        }

        /// <summary>
        /// Push the buffers to the on-screen images — and ask for a repaint, which is the whole
        /// reason the studio appeared to work only in play mode.
        /// </summary>
        /// <remarks>
        /// <b>The drawing was never broken in edit mode.</b> Measured 2026-09-06 with the editor
        /// STOPPED, driving the real path (Addressables load -> CutsceneFrameProcessor
        /// .ProcessFrameEditorAsync over C11.TTM frames 0-5 -> CutsceneState): the indexed buffer
        /// holds 68240 non-zero texels, the OutputBuffer 93332 non-black pixels, and the preview
        /// Texture2D — read back through the GPU, not <c>GetPixels32</c>, which returns the stale
        /// CPU side — holds the same 93332 across 157 distinct colours. Every stage is correct.
        ///
        /// <para>What was missing is that nothing repainted the window. <c>RenderOutput</c> ends in
        /// <c>Graphics.CopyTexture</c>, a GPU-side copy into a texture the <see cref="Image"/>
        /// already references, so UI Toolkit has no way to know the contents changed and the panel
        /// keeps the pixels it last painted. Play mode drives continuous EditorWindow repaints,
        /// which is exactly why entering it "fixed" the studio and leaving it "broke" it again with
        /// no code change in between.</para>
        ///
        /// <para>The recorded theory — UniTask continuations not being pumped by the PlayerLoop in
        /// edit mode — is false: <c>UniTask.Yield</c> and <c>UniTask.Delay</c> both resume with
        /// <c>Application.isPlaying == false</c>, and the frame commands above ran to completion
        /// there.</para>
        /// </remarks>
        private void UpdateBufferViews() {
            for (var i = 0; i < 4; i++) {
                if (_bufferImages[i] == null || _cutsceneState == null)
                    continue;
                _bufferImages[i].image = _cutsceneState.GetIndexedBuffer(i);

                if (_bufferStatusLabels[i] == null)
                    continue;
                var statuses = new List<string>();
                if (i == _cutsceneState.CurrentDrawBufferIndex) {
                    statuses.Add("Draw");
                }

                if (i == _cutsceneState.BackgroundBufferIndex) {
                    statuses.Add("BackGnd");
                }

                if (i == _cutsceneState.TargetBufferIndex) {
                    statuses.Add("Target");
                }

                _bufferStatusLabels[i].text = string.Join(" / ", statuses);
            }

            // The GPU-side copy leaves UI Toolkit believing nothing changed — see the remarks.
            rootVisualElement.Q<Image>("PreviewImage")?.MarkDirtyRepaint();
            for (var i = 0; i < 4; i++) {
                _bufferImages[i]?.MarkDirtyRepaint();
            }
            Repaint();
        }
    }
}
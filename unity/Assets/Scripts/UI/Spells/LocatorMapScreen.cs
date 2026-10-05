namespace BakAgain.UI.Spells {
    using BakAgain.Core;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Navigation;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Spells;
    using GameData.Resources.World;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The locator spells' map inset — <c>CastLocatorSpell</c> (IDA 0x6d062), shared by Eyes of
    /// Ishap, The Unseen and Nacre Cicatrix.
    /// </summary>
    /// <remarks>
    /// <b>It is not a map screen; it is the world seen from above through a smaller hole.</b> The
    /// original shrinks the render viewport to an inset, lifts the camera to the zone's maximum map
    /// height, draws the map into the clipped region, overlays REQ_CMAP and puts everything back on
    /// the way out. So this is the overhead map's machinery in a different rect, with dots on it and
    /// none of the controls.
    ///
    /// <para><b>The player's own map zoom survives by construction here.</b> The original has to save
    /// and restore it by hand, because its shared leave-map-view routine writes the CURRENT camera
    /// height back into the remembered zoom — which on this screen is the maximum. This screen never
    /// writes <see cref="GameSession.MapCameraZ"/> at all, so there is nothing to undo.</para>
    ///
    /// <para><b>It is an overlay, not a navigator screen.</b> The navigator shows one screen at a
    /// time by design, so pushing this hid the travel HUD and left black holes where its compass,
    /// its portraits and its main world view had been — the same mistake, and the same fix, as
    /// <see cref="BakAgain.UI.CampMenu"/>. The original never swaps screens here either: it shrinks
    /// the render viewport and draws REQ_CMAP over the live travel screen.</para>
    ///
    /// <para><b>The main viewport freezes while this is up, and that is faithful.</b> There is one
    /// world camera; taking it for the inset leaves the travel view's render texture holding its
    /// last frame, which is exactly what the original's shrunken render viewport does to the pixels
    /// outside it.</para>
    ///
    /// <para><see cref="LocatorMap"/> carries the rules; this class is the wiring.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class LocatorMapScreen : MonoBehaviour, ILocatorMapView, IActionHandler, IScreenInput {
        /// <summary>The REQ entry that closes the screen — the one the original's loop tests for.</summary>
        /// <remarks>
        /// Left-click returns to the world; right-click describes the button
        /// (<see cref="CloseHelpDialog"/>). REQ_CMAP's other five entries are the travel HUD's
        /// button cluster and do nothing here, exactly as in the original.
        /// </remarks>
        private const int CloseActionId = 18;

        /// <summary>ddx 236 — "Left clicking on this button will return you to the world view."</summary>
        private const int CloseHelpDialog = 236;

        private ILogger _logger;
        private GameSession _session;
        private WorldRuntime _world;
        private IDialogManager _dialogs;
        private IWorldViewport _worldViewport;
        private GameViewportRegistry _viewportRegistry;

        private UIDocument _document;
        private UserInterfaceLoader _loader;
        private WorldViewportView _worldView;
        private UniTaskCompletionSource _closed;
        private FieldSpells.LocatorTarget _target;

        /// <summary>Showing the Brass Spyglass's view rather than a locator spell's inset.</summary>
        private bool _spyglass;

        /// <summary>The Spyglass's sound as its view opens (ITEMUSE.C:71, audio_sfx_play_n_times(0x3b)).</summary>
        private const int SpyglassCue = 0x3b;
        private readonly List<VisualElement> _markers = new List<VisualElement>();

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<LocatorMapScreen>();
            _document = GetComponent<UIDocument>();
            _loader = GetComponent<UserInterfaceLoader>();
        }

        [Inject]
        public void Construct(GameSession session, WorldRuntime world, IDialogManager dialogs,
            IWorldViewport worldViewport, GameViewportRegistry viewportRegistry) {
            _session = session;
            _world = world;
            _dialogs = dialogs;
            _worldViewport = worldViewport;
            _viewportRegistry = viewportRegistry;
        }

        /// <inheritdoc />
        /// <remarks>
        /// The same machinery as a locator spell, with the differences ITEMUSE.C:33-78 makes: the
        /// whole world viewport (20 rows taller) instead of the inset, the camera at 98% of the
        /// zone's maximum height, no REQ_CMAP over it, and any arrow key or button closes it
        /// (<c>dialog_poll_arrow_or_button</c>). Its markers are the valuables search's own scan,
        /// <c>proxscan_paged_dispatch_all</c>, with no contents check.
        /// </remarks>
        public async UniTask RunSpyglassAsync() {
            _spyglass = true;
            // Over the inventory it is used from, as the original draws into the open screen; still
            // under the dialog overlay. Screens share one panel at order 0.
            // GetComponent, not _document: Awake has not run on a prefab that was never active.
            UIDocument document = GetComponent<UIDocument>();
            float order = document != null ? document.sortingOrder : 0f;
            if (document != null) {
                document.sortingOrder = SpyglassSortingOrder;
            }
            try {
                Audio.MenuSoundService.Instance?.Play(SpyglassCue);
                await RunAsync(FieldSpells.LocatorTarget.Valuables);
            } finally {
                _spyglass = false;
                if (document != null) {
                    document.sortingOrder = order;
                }
            }
        }

        private const float SpyglassSortingOrder = 1f;

        /// <inheritdoc />
        public async UniTask RunAsync(FieldSpells.LocatorTarget target) {
            if (target == FieldSpells.LocatorTarget.None) {
                return;
            }
            _target = target;
            _closed = new UniTaskCompletionSource();
            gameObject.SetActive(true);
            try {
                await _closed.Task;
            } finally {
                gameObject.SetActive(false);
            }
        }

        private void OnEnable() {
            if (_loader == null) {
                return;
            }
            if (_loader.IsBuilt) {
                AttachContent();
            } else {
                _loader.Built += OnLoaderBuilt;
            }
            ApplyCamera();
            // The inset is SHORTER than the travel viewport, so it needs its own FOV even though it
            // shares the zone's focal length.
            _world?.Environment?.SetOverheadMapMode(
                _world.WorldCamera, on: true, cameraHeight: CameraHeight(),
                mapViewHeight: InsetRect().height,
                focalLength: _world.ZoneDefinition?.FocalLength ?? _worldViewport.FocalLength);
        }

        private void OnLoaderBuilt(IReadOnlyList<NavWidget> widgets) {
            _loader.Built -= OnLoaderBuilt;
            AttachContent();
        }

        private void AttachContent() {
            VisualElement root = _document?.rootVisualElement;
            if (root == null || _worldView != null) {
                return;
            }
            _worldView = new WorldViewportView(_worldViewport, _viewportRegistry, _logger);
            _worldView.SetWorldCamera(_world?.WorldCamera);
            // REQ_CMAP carries no ClickArea for the inset, so the rect comes from the spell's own
            // data rather than from the layout — the one place this screen has coordinates at all.
            _worldView.Attach(root, InsetRect());
            if (_spyglass) {
                // No REQ_CMAP over the Spyglass, and any click on the view closes it. Its widgets
                // are the stage's imagebutton_N elements, beside the world view's host.
                root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("imagebutton_"))
                    .ForEach(e => {
                        e.style.display = DisplayStyle.None;
                        _hiddenForSpyglass.Add(e);
                    });
                root.RegisterCallback<PointerDownEvent>(OnSpyglassPointerDown, TrickleDown.TrickleDown);
            }
            // *** NOT NOW — the inset has no resolved size yet. *** TryProject divides by the host's
            // resolvedStyle width and height, which are 0 until UI Toolkit has laid the screen out,
            // so placing markers here would put every dot in the top-left corner (or reject the lot).
            // One-shot: the party cannot move while this is up, so the first layout is the only one
            // whose geometry matters.
            VisualElement host = _worldView.Element;
            if (host == null) {
                return;
            }
            AttachPartyMarker(host);
            host.RegisterCallback<GeometryChangedEvent>(OnInsetLaidOut);
        }

        /// <summary>The inset the world is shown in, in canonical space.</summary>
        private Rect InsetRect() {
            (int x, int y, int width, int height) = ViewRect();
            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// The camera pose: over the party, straight down, at the zone's <b>maximum</b> map height.
        /// </summary>
        /// <remarks>
        /// Maximum, not the remembered zoom — the locator always shows the widest view whatever the
        /// player left the overhead map at (<see cref="LocatorMap.OpensAtMaximumZoom"/>). The yaw is
        /// the map's, so the inset turns with the party unless north-up is on, and the markers are
        /// placed by this same camera so they cannot disagree with it.
        /// </remarks>
        private void ApplyCamera() {
            Camera cam = _world?.WorldCamera;
            if (cam == null || _session == null) {
                return;
            }
            cam.transform.position = BakCoordinateConverter.ConvertPosition(
                _session.PositionX, _session.PositionY, (int)CameraHeight());
            int yaw = LocalMapScreen.MapRendersWithYaw(
                unchecked((ushort)_session.Rotation), GameOptions.NorthUpMap);
            cam.transform.rotation = BakCoordinateConverter.ConvertRotation(
                unchecked((ushort)LocalMapScreen.TopDownPitch), 0, unchecked((ushort)yaw));
        }

        private float CameraHeight() => _world?.ZoneDefinition == null
            ? 0f
            : _spyglass
                ? _world.ZoneDefinition.MapMaxZ * FieldSpells.SpyglassHeightPercent / 100f
                : _world.ZoneDefinition.MapMaxZ;

        private (int X, int Y, int Width, int Height) ViewRect() =>
            _spyglass ? FieldSpells.SpyglassViewport : FieldSpells.LocatorViewport;

        private readonly List<VisualElement> _hiddenForSpyglass = new List<VisualElement>();

        private void OnSpyglassPointerDown(PointerDownEvent evt) => Close();

        /// <summary>
        /// Puts a dot on every marked thing.
        /// </summary>
        /// <remarks>
        /// Computed once rather than per frame: the party cannot move while this is up, so nothing
        /// a marker depends on changes. The dots are positioned by the world camera's own
        /// projection — see <see cref="LocatorMap.MarkersUseTheCameraProjection"/>, which is why
        /// there is no fixed-point arithmetic here to keep in step with the render.
        ///
        /// <para><b>Known divergence:</b> the original runs a third scan over its fixed-object list
        /// under a different rule, and skips that scan entirely for Eyes of Ishap. Our world has no
        /// separate fixed-object entity list to scan — every placed thing is a
        /// <see cref="WorldEntity"/> — so only the list-scan rule
        /// (<see cref="LocatorMap.Marks"/>) is applied here. See TASK-190.</para>
        /// </remarks>
        private void OnInsetLaidOut(GeometryChangedEvent evt) {
            if (evt.target is VisualElement host) {
                host.UnregisterCallback<GeometryChangedEvent>(OnInsetLaidOut);
            }
            PlaceMarkers();
        }

        private void PlaceMarkers() {
            ClearMarkers();
            Camera cam = _world?.WorldCamera;
            VisualElement host = _worldView?.Element;
            if (cam == null || host == null || _session == null) {
                return;
            }
            Vector3 party = BakCoordinateConverter.ConvertPosition(
                _session.PositionX, _session.PositionY, 0);

            foreach (WorldEntity entity in
                Object.FindObjectsByType<WorldEntity>(FindObjectsSortMode.None)) {
                if (!IsMarked(entity, party)) {
                    continue;
                }
                if (TryProject(cam, host, entity.transform.position, out Vector2 at)) {
                    host.Add(MakeDot(at));
                }
            }
        }

        private bool IsMarked(WorldEntity entity, Vector3 party) {
            Vector3 p = entity.transform.position;
            // Across the ground, as the original measures it: the height difference is not part of
            // the range, so a thing on a ledge is as near as the same thing on the flat.
            float ground = Vector2.Distance(new Vector2(party.x, party.z), new Vector2(p.x, p.z));
            long bakDistance = (long)(ground * BakCoordinateConverter.WorldScale);
            bool holdsFood = false;
            bool holdsMagic = false;
            if (LocatorMap.ChecksContents(_target)) {
                (int bakX, int bakY) = BakCoordinateConverter.ToBakXY(p);
                IEnumerable<int> items = ContentsAt(bakX, bakY);
                holdsFood = LocatorMap.ContentsSatisfy(FieldSpells.LocatorTarget.Food, items);
                holdsMagic = LocatorMap.ContentsSatisfy(FieldSpells.LocatorTarget.Magic, items);
            }

            return LocatorMap.Marks(_target, entity.EntityType, bakDistance,
                (long)entity.WorldExtent, holdsFood, holdsMagic);
        }

        /// <summary>What the container standing at a position holds, or nothing.</summary>
        /// <remarks>
        /// The LIVE container, not the save snapshot — the original opens the fixed-object record at
        /// the item's position and reads its item list, and a container the party has already
        /// changed this session exists only in the runtime layer.
        /// </remarks>
        private IEnumerable<int> ContentsAt(int bakX, int bakY) {
            GameData.Resources.Inventory.RuntimeContainer container =
                _session.GetLiveContainerAt(_session.CurrentZone, bakX, bakY);
            if (container?.Items == null) {
                yield break;
            }
            foreach (GameData.Resources.Inventory.RuntimeItem item in container.Items) {
                yield return item.ObjectId;
            }
        }

        /// <summary>Projects a world position into the inset, or reports that it falls outside.</summary>
        /// <remarks>
        /// The bounds test is widened by the dot's own radius
        /// (<see cref="LocatorMap.MarkerClipSlack"/>) so a thing just off the edge shows as a half
        /// dot rather than vanishing, which is what the original's clip does.
        /// </remarks>
        private static bool TryProject(Camera cam, VisualElement host, Vector3 world,
            out Vector2 at) {
            at = default;
            Vector3 view = cam.WorldToViewportPoint(world);
            if (view.z < 0f) {
                return false;
            }
            float w = host.resolvedStyle.width;
            float h = host.resolvedStyle.height;
            // Viewport Y runs up from the bottom; UI Toolkit runs down from the top.
            at = new Vector2(view.x * w, (1f - view.y) * h);
            float slack = DotHeight * 0.5f;

            return at.x >= -slack && at.x <= w + slack && at.y >= -slack && at.y <= h + slack;
        }

        // The dot is radius 2 in the original's pixels. Canonical space stretches x5 horizontally
        // and x6 vertically, so a round dot there is a slightly tall one here — the same stretch
        // every other piece of original art gets.
        private const float DotWidth = LocatorMap.MarkerWidth;

        private const float DotHeight = LocatorMap.MarkerHeight;

        private VisualElement MakeDot(Vector2 at) {
            var dot = new VisualElement {
                name = "BakLocatorMarker",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = at.x - (DotWidth * 0.5f), top = at.y - (DotHeight * 0.5f),
                    width = DotWidth, height = DotHeight,
                    backgroundColor = MarkerColor,
                    borderTopLeftRadius = Length.Percent(50),
                    borderTopRightRadius = Length.Percent(50),
                    borderBottomLeftRadius = Length.Percent(50),
                    borderBottomRightRadius = Length.Percent(50),
                },
            };
            _markers.Add(dot);

            return dot;
        }

        // Pen 111. Hard-coded rather than resolved through a palette because it is the same
        // (215, 0, 0) in OPTIONS.PAL and in all twelve zone palettes — see LocatorMap.MarkerPen.
        private static readonly Color MarkerColor = new Color(215f / 255f, 0f, 0f, 1f);

        /// <summary>
        /// The party's arrow at the centre, as the overhead map draws it: the original renders these
        /// views through the map's path, which draws the MAPICONS arrow (seen in its locator and
        /// Spyglass views, 2026-10-02).
        /// </summary>
        private void AttachPartyMarker(VisualElement viewport) {
            var centre = new VisualElement {
                name = "BakMapMarker",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, top = 0, right = 0, bottom = 0,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                },
            };
            int icon = OverheadMapMarker.IconIndexFor(unchecked((ushort)_session.Rotation), GameOptions.NorthUpMap);
            centre.Add(new ArchiveImage { pickingMode = PickingMode.Ignore, address = $"MAPICONS.BMX#{icon}" });
            viewport.Add(centre);   // not a search marker: PlaceMarkers' clear must leave it
        }

        private void ClearMarkers() {
            foreach (VisualElement marker in _markers) {
                marker.RemoveFromHierarchy();
            }
            _markers.Clear();
        }

        private void OnDisable() {
            if (_loader != null) {
                _loader.Built -= OnLoaderBuilt;
            }
            ClearMarkers();
            foreach (VisualElement child in _hiddenForSpyglass) {
                child.style.display = StyleKeyword.Null;
            }
            _hiddenForSpyglass.Clear();
            _document?.rootVisualElement?.UnregisterCallback<PointerDownEvent>(
                OnSpyglassPointerDown, TrickleDown.TrickleDown);
            _worldView?.Dispose();
            _worldView = null;
            // Sky, horizon, fog and far plane before the camera, so the travel view is whole the
            // moment it is shown again.
            _world?.Environment?.SetOverheadMapMode(
                _world.WorldCamera, on: false, cameraHeight: 0f,
                mapViewHeight: InsetRect().height,
                focalLength: _world.ZoneDefinition?.FocalLength ?? _worldViewport.FocalLength);
            _world?.Movement?.SyncToCamera();
        }

        // --- IActionHandler ---

        public void PrimaryAction(int menuEntryActionId) {
            if (_spyglass || menuEntryActionId == CloseActionId) {
                Close();
            }
        }

        public async Awaitable SecondaryAction(int menuEntryActionId) {
            if (menuEntryActionId != CloseActionId || _dialogs == null) {
                return;
            }
            await _dialogs.ShowById(CloseHelpDialog).AsTask();
        }

        // --- IScreenInput (REQ_CMAP is an InteractiveScreen) ---

        public bool WantsText => false;

        public bool OnDirection(NavDirection dir, bool ctrl) {
            if (!_spyglass) {
                return false;
            }
            Close();   // the Spyglass ends on any arrow key
            return true;
        }

        public bool OnTab(bool shift) => false;

        /// <summary>E (0x12) closes the map like its Exit button (SPELLFX.C:345) — TASK-809.</summary>
        public void OnText(char c) {
            if (GameData.Resources.World.KeyScancode.Of(c) == CloseActionId) {
                PrimaryAction(CloseActionId);
            }
        }

        public bool OnEdit(EditKey key) => false;

        public void OnSubmit() => Close();

        public void OnCancel() => Close();

        private void Close() => _closed?.TrySetResult();
    }
}

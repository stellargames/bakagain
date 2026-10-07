namespace BakAgain.UI.InGame {
    using BakAgain.Core;
    using BakAgain.ResourceManagement;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI.FullMap;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Navigation;
    using BakAgain.World;
    using BakAgain.World.Converters;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.World;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The overhead map — <c>sub_ovr180_11F</c> (0x6d49f) over <c>REQ_MAP.DAT</c>, opened and closed
    /// by the travel HUD's map button.
    /// </summary>
    /// <remarks>
    /// <b>It is the same world through the same hole in the same chrome, seen from above.</b> There
    /// is no map render and no second camera: showing this screen tips the world camera straight
    /// down (<see cref="LocalMapScreen.TopDownPitch"/>) and lifts it to the remembered zoom; hiding
    /// it hands the camera back to <see cref="PartyMovement.SyncToCamera"/>, which re-derives the
    /// travel pose from the session. The arrows still walk the party, so the movement driver is the
    /// travel screen's own — <see cref="ClassicMovementDriver"/>, held-key repeat and all.
    ///
    /// <para><see cref="LocalMapScreen"/> carries the rules; this class is the wiring.</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OverheadMapScreen : ScreenBase, IActionHandler, IScreenInput, IMenuStateProvider {
        // The three portrait ClickAreas REQ_MAP shares with REQ_MAIN. Not in LocalMapScreen.ActionFor
        // because they are not the map's own controls — they do on this screen exactly what they do
        // on the travel HUD.
        private const int ActionPartyMember1 = 2;

        private ILogger _logger;
        private GameSession _session;
        private WorldRuntime _world;
        private IDialogManager _dialogs;
        private IScreenNavigator _navigator;
        private IResourceProviderService _resources;
        private IWorldViewport _worldViewport;
        private CampMenu _campMenu;
        private IFullMapView _fullMap;
        private BakAgain.UI.Inventory.InventoryMenu _inventoryMenu;
        private BakAgain.UI.Character.CharacterSheetScreen _characterSheet;
        private IPointer _pointer;
        private IGameplayInput _gameplay;
        private IMapOptionInput _mapOptions;

        private UIDocument _document;
        private UserInterfaceLoader _loader;
        private MenuLayerHost _layerHost;
        private WorldViewportView _worldView;
        private CompassView _compass;
        private PartyHeadsView _partyHeads;
        private IMovementDriver _movementDriver;
        private ArchiveImage _marker;
        private int _markerIcon = -1;
        private bool _zoomUpLive = true;
        private bool _zoomDownLive = true;

        // The option, read through one property so the camera and the marker can never disagree
        // about which of them is carrying the heading.
        private static bool NorthUp => GameOptions.NorthUpMap;

        private PartyMovement Movement => _world?.Movement;

        private void Awake() {
            _logger = LogManager.LoggerFactory.CreateLogger<OverheadMapScreen>();
            _document = GetComponent<UIDocument>();
            _loader = GetComponent<UserInterfaceLoader>();
            _layerHost = GetComponent<MenuLayerHost>();
            // No SetActive(false) here: the PREFAB is saved inactive (ScreenPrefabsStartInactiveTests
            // enforces it), so this Awake only runs when the navigator shows the screen — and
            // deactivating here would undo that same activation.
        }

        [Inject]
        public void Construct(GameSession session, WorldRuntime world, IDialogManager dialogs,
            IScreenNavigator navigator, IResourceProviderService resources,
            IWorldViewport worldViewport,
            CampMenu campMenu, IFullMapView fullMap,
            BakAgain.UI.Inventory.InventoryMenu inventoryMenu,
            BakAgain.UI.Character.CharacterSheetScreen characterSheet,
            IPointer pointer, IGameplayInput gameplay, IMapOptionInput mapOptions,
            ICheatInput cheat, BakAgain.UI.Cheats.ChestCheatScreen cheatChest) {
            _cheat = cheat;
            _cheatChest = cheatChest;
            _session = session;
            _world = world;
            _dialogs = dialogs;
            _navigator = navigator;
            _resources = resources;
            _worldViewport = worldViewport;
            _campMenu = campMenu;
            _fullMap = fullMap;
            _inventoryMenu = inventoryMenu;
            _characterSheet = characterSheet;
            _pointer = pointer;
            _gameplay = gameplay;
            _mapOptions = mapOptions;
        }

        protected override void OnAfterShow() {
            _closingForWorldExit = false;
            if (_loader == null) {
                return;
            }
            if (_loader.IsBuilt) {
                AttachContent();
            } else {
                _loader.Built += OnLoaderBuilt;
            }
            _movementDriver = new ClassicMovementDriver(_document, Movement, _gameplay, _pointer);
            ApplyMapCamera();
            ApplyMapBackdrop();
        }

        private void OnLoaderBuilt(System.Collections.Generic.IReadOnlyList<NavWidget> widgets) {
            _loader.Built -= OnLoaderBuilt;
            AttachContent();
        }

        // Idempotent: SetEntryState re-raises Built to rebuild the input layer, so this can be
        // reached again while the screen is already up.
        private void AttachContent() {
            VisualElement root = _document?.rootVisualElement;
            if (root == null || _worldView != null) {
                return;
            }
            _worldView = new WorldViewportView(_worldViewport, _logger);
            _worldView.SetWorldCamera(_world?.WorldCamera);
            // REQ_MAP has no viewport element of its own; the view falls back to the RE-verified
            // canonical rect, which is the same hole the travel screen's hotspot_192 sits in.
            _worldView.Attach(root);

            if (_loader.TryGetElementRect(
                    GameData.Resources.Menu.UserInterface.CompassWindowActionId, out Rect compassRect)) {
                _compass = new CompassView(_session, _resources);
                _compass.BuildAsync(CanonicalStage.GetOrCreate(root, _loader.Frame), compassRect, this).Forget();
            }

            _partyHeads = new PartyHeadsView(_session, _resources);
            _partyHeads.Attach(root);
            _partyHeads.RenderAsync().Forget();

            AttachMarker(root);

            // The Android pads, in the side bars as on the travel screen: the arrows still walk the
            // party here, and the driver above reads the same held pad (TASK-820).
            if (BakAgain.UI.InputCore.TouchInputState.Instance != null) {
                _touchControls = new TouchControlsView(BakAgain.UI.InputCore.TouchInputState.Instance,
                    _pointer, new GameData.Resources.Layout.TouchControlsLayout());
                _touchControls.Build(root, CanonicalStage.GetOrCreate(root, _loader.Frame));
            }
            // Registered here rather than in OnAfterShow because the root only exists once the REQ
            // has built, and this method is the one place that is true. The guard above makes it
            // run once per show, so the callback is not stacked on a rebuild.
            root.RegisterCallback<WheelEvent>(OnWheel);
            _wheelRoot = root;
            RefreshZoomButtons(force: true);
        }

        private VisualElement _wheelRoot;

        /// <summary>A fight took the screen from the map; reopen it when the fight is done.</summary>
        public bool ReopenAfterFight { get; set; }
        private TouchControlsView _touchControls;

        /// <summary>
        /// Mouse wheel zooms the map, one step a notch.
        /// </summary>
        /// <remarks>
        /// <b>An addition, not a port.</b> The original binds nothing to the wheel — it is a 1993
        /// DOS game — so this exists because it is the gesture a player reaches for, and it
        /// deliberately changes nothing else: the REQ zoom buttons, PageUp/PageDown and the
        /// remembered <see cref="GameSession.MapCameraZ"/> all keep working exactly as they did.
        ///
        /// <para><b>Why the explicit Can-check rather than just calling Zoom.</b> The one-step arms
        /// do NOT clamp themselves — <see cref="LocalMapScreen.ClampsItsOwnZoom"/> is false for
        /// them precisely because they have buttons, and the button enable gate is what keeps them
        /// in range (see <see cref="RefreshZoomButtons"/>). A wheel notch has no button and so no
        /// gate, so without this it would walk the camera straight past MapMinZ/MapMaxZ. The five-
        /// step arms would self-clamp, but five steps a notch is not a zoom, it is a jump.
        ///
        /// <para>So this asks the same two predicates the gate asks, and then goes through the same
        /// <see cref="Zoom"/> the buttons use — one zoom path, one clamp rule.</para></para>
        ///
        /// <para>Wheel sign: UI Toolkit reports scrolling AWAY from the user as negative
        /// <c>delta.y</c>, which is the "zoom in" direction everywhere else, and zooming in on a
        /// top-down map means lowering the camera.</para>
        /// </remarks>
        private void OnWheel(WheelEvent evt) {
            ZoneDefinition def = _world?.ZoneDefinition;
            if (def == null || _session == null || Mathf.Approximately(evt.delta.y, 0f)) {
                return;
            }

            if (LocalMapScreen.TryWheelZoom(evt.delta.y, _session.MapCameraZ, def.MapZoomStep,
                    def.MapMinZ, def.MapMaxZ, out LocalMapScreen.MapAction action)) {
                Zoom(action);
            }
            // Stopped either way: at the end of the range the wheel does nothing, exactly as a
            // greyed zoom button does nothing, but it is still the map's wheel.
            evt.StopPropagation();
        }

        /// <summary>
        /// Puts the party marker in the middle of the world view.
        /// </summary>
        /// <remarks>
        /// <b>The party is drawn, not implied by the camera</b> — see
        /// <see cref="OverheadMapMarker"/>. A child of the viewport element rather than a rect
        /// computed here, so it inherits the one hole the world is rendered into and cannot drift
        /// from it; the centring is the flex box's, which is the same answer as
        /// <see cref="OverheadMapMarker.TopLeftFor"/> without carrying the icon's size around.
        /// </remarks>
        private void AttachMarker(VisualElement root) {
            VisualElement viewport = root?.Q(name: "WorldViewport");
            if (viewport == null) {
                _logger.LogWarning("OverheadMapScreen: no world view, so no party marker.");

                return;
            }
            var host = new VisualElement {
                name = "BakMapMarker",
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = 0, top = 0, right = 0, bottom = 0,
                    alignItems = Align.Center,
                    justifyContent = Justify.Center,
                },
            };
            _marker = new ArchiveImage { pickingMode = PickingMode.Ignore };
            host.Add(_marker);
            viewport.Add(host);
            RefreshMarker();
        }

        /// <summary>Points the marker at the party's heading, or leaves it alone if it cannot move.</summary>
        /// <remarks>
        /// Assigning the same address is a no-op inside <see cref="ArchiveImage"/>, but the icon is
        /// cached here too so a turning map never builds the string at all — this runs every frame.
        /// </remarks>
        private void RefreshMarker() {
            if (_marker == null) {
                return;
            }
            int icon = OverheadMapMarker.IconIndexFor(unchecked((ushort)_session.Rotation), NorthUp);
            if (icon == _markerIcon) {
                return;
            }
            _markerIcon = icon;
            _marker.address = $"MAPICONS.BMX#{icon}";
        }

        private void OnDisable() {
            if (_loader != null) {
                _loader.Built -= OnLoaderBuilt;
            }
            if (_wheelRoot != null) {
                _wheelRoot.UnregisterCallback<WheelEvent>(OnWheel);
                _wheelRoot = null;
            }
            _worldView?.Dispose();
            _worldView = null;
            _partyHeads?.Dispose();
            _partyHeads = null;
            _compass = null;
            // The marker goes with the viewport element it hangs off; drop the handle so a re-show
            // rebuilds it rather than pointing at a detached element.
            _marker = null;
            _markerIcon = -1;
            _movementDriver = null;
            _touchControls?.Dispose();
            _touchControls = null;
            // Put the sky, the horizon, the fog and the far plane back before the camera, so the
            // travel view is whole the moment it is shown again.
            _world?.Environment?.SetOverheadMapMode(
                _world.WorldCamera, on: false, cameraHeight: 0f,
                mapViewHeight: _worldViewport.CanonicalRect.Height,
                focalLength: _world?.ZoneDefinition?.FocalLength ?? _worldViewport.FocalLength);
            // ...and put the world's own entities back if this was a dungeon automap.
            _world?.Automap?.Hide();
            // Hand the camera back. SyncToCamera re-derives height, pitch and heading from the
            // session, so nothing needs saving on the way in — and leaving it looking down would
            // return the player to a world view pointed at the ground.
            Movement?.SyncToCamera();
        }

        private ICheatInput _cheat;
        private BakAgain.UI.Cheats.ChestCheatScreen _cheatChest;
        private float _cheatHeldSince = -1f;

        /// <summary>
        /// RShift+Alt+` HELD opens CHEAT CENTRAL's cipher chest (MAP.C:423-433). The modifiers are
        /// tested at the press; letting go of ` before the hold is up cancels it.
        /// </summary>
        private void OpenCheatChestOnALongHold(bool ownsInput) {
            if (!ownsInput || _cheat == null || _cheatChest == null) {
                _cheatHeldSince = -1f;
                return;
            }
            if (_cheat.CheatKeyPressed && _cheat.CheatChordHeld) {
                _cheatHeldSince = Time.unscaledTime;
            }
            if (_cheatHeldSince < 0f) {
                return;
            }
            if (!_cheat.CheatKeyHeld) {
                _cheatHeldSince = -1f;
                return;
            }
            if (Time.unscaledTime - _cheatHeldSince < GameData.Resources.World.CheatCentral.ChestHoldSeconds) {
                return;
            }
            _cheatHeldSince = -1f;
            _cheatChest.OpenWithCipherAsync().Forget();
        }

        private bool _closingForWorldExit;

        /// <summary>
        /// A world-loop exit request ends the map, checked every pass as the map loop does
        /// (MAP.C:447) — the cheat chest's chapter skip raises one under this screen.
        /// </summary>
        private void CloseOnAWorldExitRequest(bool ownsInput) {
            if (!ownsInput || _closingForWorldExit || _session == null || _session.ChapterTransitionPending == 0) {
                return;
            }
            _closingForWorldExit = true;
            _navigator?.Pop().Forget();
        }

        /// <summary>
        /// A fight that starts while the map is up — the party walked into an encounter on it —
        /// takes the screen. The original runs the fight full-screen from inside the map loop
        /// (hotspotevt_type1_encounter_run via hotspotevt_disp_pending_events, MAP.C:214); here the
        /// map closed nothing and the fight played out unseen beneath it. The original's loop then
        /// carries on with the map, so <see cref="ReopenAfterFight"/> asks the travel HUD to bring it
        /// back once the fight is over.
        /// </summary>
        private void CloseWhenAFightStarts() {
            if (_closingForWorldExit || _world == null || !_world.FightInProgress) {
                return;
            }
            _closingForWorldExit = true;
            ReopenAfterFight = true;
            _navigator?.Pop().Forget();
        }

        private void Update() {
            if (_worldView == null) {
                return;
            }
            // Same gate the travel HUD uses: a dialog over the map is resolved above this screen's
            // layer, so the party stops while it is up.
            bool ownsInput = _layerHost != null && _layerHost.IsInputActive;
            _touchControls?.Refresh(inFight: false);
            _movementDriver?.Tick(ownsInput);
            ToggleNorthUpIfAsked(ownsInput);
            OpenCheatChestOnALongHold(ownsInput);
            CloseOnAWorldExitRequest(ownsInput);
            CloseWhenAFightStarts();
            // After the driver, because a step re-syncs the camera to the travel pose.
            ApplyMapCamera();
            RefreshMarker();
            _compass?.Refresh();
            _worldView.Tick();
            RefreshZoomButtons(force: false);
        }

        /// <summary>
        /// 'N' — hold north at the top, or let the map turn with the party.
        /// </summary>
        /// <remarks>
        /// Keyboard only: the original gives it no REQ_MAP button and no help record, and it is in
        /// the CD build alone. The backdrop is left as it is — the option changes what the camera
        /// points at, not what map mode means.
        ///
        /// <para>Gated on the same flag as the movement driver, and for the same reason: the
        /// original reads this key from the map's own input loop, which is not running while a
        /// dialog sits above the map. Ungated, a key pressed at a dialog would turn the map under
        /// it.</para>
        /// </remarks>
        private void ToggleNorthUpIfAsked(bool ownsInput) {
            if (!ownsInput || _mapOptions == null || !_mapOptions.ToggleNorthUpMap) {
                return;
            }
            GameOptions.NorthUpMap = !GameOptions.NorthUpMap;
        }

        /// <summary>
        /// Puts the map pose on the world camera: the party's position, the remembered height, and
        /// the straight-down pitch.
        /// </summary>
        /// <remarks>
        /// Re-applied every frame rather than once on show, because every step and turn runs
        /// <see cref="PartyMovement.SyncToCamera"/>, which writes the travel height and pitch back.
        /// The yaw is the party's, unless north-up is on — see
        /// <see cref="LocalMapScreen.MapRendersWithYaw"/>, which is the other half of the same
        /// decision <see cref="RefreshMarker"/> makes about the icon.
        /// </remarks>
        private void ApplyMapCamera() {
            Camera cam = _world?.WorldCamera;
            if (cam == null || _session == null) {
                return;
            }
            ClampZoomIntoRange();
            cam.transform.position = BakCoordinateConverter.ConvertPosition(
                _session.PositionX, _session.PositionY, (int)_session.MapCameraZ);
            int yaw = LocalMapScreen.MapRendersWithYaw(unchecked((ushort)_session.Rotation), NorthUp);
            cam.transform.rotation = BakCoordinateConverter.ConvertRotation(
                unchecked((ushort)LocalMapScreen.TopDownPitch), 0, unchecked((ushort)yaw));
        }

        /// <summary>
        /// Puts the world's backdrop into map mode at the current height.
        /// </summary>
        /// <remarks>
        /// Applied on show and on every zoom rather than per frame: it writes global render settings,
        /// and only the height it is keyed to ever changes. <see cref="ZoneEnvironment"/> owns what
        /// map mode actually means.
        /// </remarks>
        private void ApplyMapBackdrop() {
            Camera cam = _world?.WorldCamera;
            if (cam == null) {
                return;
            }

            // The map reuses the travel viewport, so its rect — and therefore its FOV — is that one's.
            _world.Environment?.SetOverheadMapMode(
                cam, on: true, cameraHeight: cam.transform.position.y,
                mapViewHeight: _worldViewport.CanonicalRect.Height,
                focalLength: _world?.ZoneDefinition?.FocalLength ?? _worldViewport.FocalLength);

            // Underground, the map is not the world seen from above — it is the dungeon automap:
            // the same placements drawn from the map model table, of which only the ones the party
            // has walked past appear. Above ground Automap is null and this does nothing.
            DungeonAutomapView automap = _world.Automap;
            if (automap != null) {
                int shown = automap.Show(_session?.AutomapVisits, _session?.CurrentZone ?? 0);
                _logger?.LogInformation("Dungeon automap: showing {Shown} of {Mapped} mapped placements.",
                    shown, automap.PlacementCount);
            }
        }

        // The height is seeded per zone by WorldRuntime; this only catches a session that never got
        // a seed (a world built before this screen existed, or a test), so the first open still
        // frames something instead of sitting at ground level.
        private void ClampZoomIntoRange() {
            ZoneDefinition def = _world?.ZoneDefinition;
            if (def == null) {
                return;
            }
            if (_session.MapCameraZ < def.MapMinZ) {
                _session.MapCameraZ = def.MapMinZ;
            } else if (_session.MapCameraZ > def.MapMaxZ) {
                _session.MapCameraZ = def.MapMaxZ;
            }
        }

        private void Zoom(LocalMapScreen.MapAction action) {
            ZoneDefinition def = _world?.ZoneDefinition;
            if (def == null || _session == null) {
                return;
            }
            _session.MapCameraZ = LocalMapScreen.CameraZAfter(
                action, _session.MapCameraZ, def.MapZoomStep, def.MapMinZ, def.MapMaxZ);
            ApplyMapCamera();
            // The fog and far plane are keyed to the height, so a zoom moves them too.
            ApplyMapBackdrop();
            RefreshZoomButtons(force: false);
        }

        /// <summary>
        /// Greys out a zoom button when a whole step no longer fits.
        /// </summary>
        /// <remarks>
        /// <b>This is what keeps the one-step zooms in range at all</b> — they clamp nothing
        /// themselves (<see cref="LocalMapScreen.ClampsItsOwnZoom"/>), so the gate is the guard.
        /// Re-evaluated every frame like the original's loop, but written only on a change:
        /// SetEntryState re-raises Built, and rebuilding the REQ every frame would be a stutter.
        /// </remarks>
        private void RefreshZoomButtons(bool force) {
            ZoneDefinition def = _world?.ZoneDefinition;
            if (def == null || _loader == null || !_loader.IsBuilt) {
                return;
            }
            bool up = LocalMapScreen.CanZoomUp(_session.MapCameraZ, def.MapZoomStep, def.MapMaxZ);
            bool down = LocalMapScreen.CanZoomDown(_session.MapCameraZ, def.MapZoomStep, def.MapMinZ);
            if (!force && up == _zoomUpLive && down == _zoomDownLive) {
                return;
            }
            _zoomUpLive = up;
            _zoomDownLive = down;
            _loader.SetEntryState(ZoomUpActionId, visible: true, navigable: up);
            _loader.SetEntryState(ZoomDownActionId, visible: true, navigable: down);
            // The original's gate is a picture too: a zoom that no longer fits is drawn as the bare
            // stone (MAP.C:126-135 sets wEnable_gate). Without this the map opened at its top
            // height still showed the zoom-out icon, under camp as well (TASK-791).
            _loader.SetEntryGate(ZoomUpActionId, !up);
            _loader.SetEntryGate(ZoomDownActionId, !down);
        }

        private const int ZoomUpActionId = 0x49;
        private const int ZoomDownActionId = 0x51;

        public void PrimaryAction(int menuEntryActionId) {
            if (IsPortrait(menuEntryActionId)) {
                // With Shift held, the character sheet (MAP.C:409-411); else the inventory.
                int slot = menuEntryActionId - ActionPartyMember1;
                if (BakAgain.UI.InputCore.InputDriver.ShiftHeld) {
                    if (slot < _session.ActivePartyIndices.Length) {
                        _characterSheet?.RunAsync(slot).Forget();
                    }
                } else if (_inventoryMenu != null && _inventoryMenu.SetMember(slot)) {
                    _navigator.Push(_inventoryMenu).Forget();
                }
                return;
            }

            LocalMapScreen.MapAction action = LocalMapScreen.ActionFor(menuEntryActionId);
            switch (action) {
                case LocalMapScreen.MapAction.MoveForward: Movement?.MoveForward(); break;
                case LocalMapScreen.MapAction.MoveBackward: Movement?.MoveBackward(); break;
                case LocalMapScreen.MapAction.TurnLeft: Movement?.TurnLeft(); break;
                case LocalMapScreen.MapAction.TurnRight: Movement?.TurnRight(); break;
                case LocalMapScreen.MapAction.ZoomUpOneStep:
                case LocalMapScreen.MapAction.ZoomDownOneStep:
                case LocalMapScreen.MapAction.ZoomUpFiveSteps:
                case LocalMapScreen.MapAction.ZoomDownFiveSteps:
                    Zoom(action);
                    break;
                case LocalMapScreen.MapAction.ToggleFollowRoad: ToggleFollowRoad(); break;
                case LocalMapScreen.MapAction.ShowFullMap: ShowFullMap(); break;
                // Opened directly, not pushed: the camp panel covers the viewport and leaves the
                // chrome around it, the same way it does over the travel HUD.
                case LocalMapScreen.MapAction.Encamp: _campMenu?.Open(); break;
                case LocalMapScreen.MapAction.Close: _navigator?.Pop().Forget(); break;
                default:
                    _logger.LogDebug("OverheadMapScreen unhandled primary action {ActionId}.",
                        menuEntryActionId);
                    break;
            }
        }

        public async Awaitable SecondaryAction(int menuEntryActionId) {
            // Right-click / Shift on a portrait opens the character sheet where a plain click opens
            // the inventory — the travel HUD's idiom, on the same three click areas.
            if (IsPortrait(menuEntryActionId) && _characterSheet != null) {
                await _characterSheet.RunAsync(menuEntryActionId - ActionPartyMember1).AsTask();

                return;
            }

            int helpId = LocalMapScreen.DescribeDialogFor(LocalMapScreen.ActionFor(menuEntryActionId));
            if (helpId == 0 || _dialogs == null) {
                return;
            }
            await _dialogs.ShowById(helpId).AsTask();
        }

        private static bool IsPortrait(int actionId) =>
            actionId >= ActionPartyMember1 && actionId <= ActionPartyMember1 + 2;

        private void ToggleFollowRoad() {
            if (Movement == null) {
                return;
            }
            if (Movement.IsTravelling) {
                Movement.DisengageTravel();
            } else {
                Movement.TryEngageTravel();
            }
        }

        private void ShowFullMap() => OpenFullMapAsync(_fullMap, _session, _resources, _navigator, this).Forget();

        /// <summary>
        /// The player's full map — from the local map's button or F there, and F on the travel HUD
        /// (fmap_screen_run from MAP.C:383 and WORLDLP.C:324).
        /// </summary>
        internal static async UniTaskVoid OpenFullMapAsync(IFullMapView fullMap, GameSession session,
                IResourceProviderService resources, IScreenNavigator navigator, object owner) {
            if (fullMap == null) {
                return;
            }
            await session.PlaceMapMarkerAsync(resources, owner);
            fullMap.SetMarker(session.MapMarkerVisible, session.MapMarkerXPercent,
                session.MapMarkerYPercent, session.MapMarkerIcon);
            // The player opened this one, so it needs REQ_FMAP's Exit widget. The loading-screen
            // caller passes null instead and is dismissed by the flow behind it; without this the
            // map was a dead end, closable by nothing at all.
            fullMap.SetExitAffordance(() => navigator.Pop().Forget());
            navigator.Push(fullMap).Forget();
        }

        // --- IMenuStateProvider ---

        /// <summary>Reads the movement, not a local flag — travel ends for reasons the player did
        /// not ask for, so a click-toggled flag would go stale (same as the travel HUD).</summary>
        public bool GetToggleState(int actionId) =>
            LocalMapScreen.ActionFor(actionId) == LocalMapScreen.MapAction.ToggleFollowRoad
            && Movement != null && Movement.IsTravelling;

        // --- IScreenInput (REQ_MAP is an InteractiveScreen: the screen owns arrows and Enter) ---

        public bool WantsText => false;

        /// <summary>
        /// The coarse directions are the zoom; the arrows are deliberately NOT handled here.
        /// </summary>
        /// <remarks>
        /// A held arrow already reaches <see cref="ClassicMovementDriver"/> through the gameplay
        /// axis, which is what gives the original's held-key repeat. Stepping here as well would
        /// move the party twice for one press.
        /// </remarks>
        public bool OnDirection(NavDirection dir, bool ctrl) {
            switch (dir) {
                case NavDirection.PageUp: Zoom(LocalMapScreen.MapAction.ZoomUpOneStep); return true;
                case NavDirection.PageDown: Zoom(LocalMapScreen.MapAction.ZoomDownOneStep); return true;
                // Home/End — the five-step jumps, which have no button of their own.
                case NavDirection.First: Zoom(LocalMapScreen.MapAction.ZoomUpFiveSteps); return true;
                case NavDirection.Last: Zoom(LocalMapScreen.MapAction.ZoomDownFiveSteps); return true;
                default: return false;
            }
        }

        public bool OnTab(bool shift) => false;

        /// <summary>
        /// A letter on the map screen — which is how the map CLOSES on the same key that opened it.
        /// </summary>
        /// <remarks>
        /// <b>One action does both halves in the original.</b> The travel loop sends action 0x32 to
        /// <c>map_main_loop()</c> (WORLDLP.C:316) and the map screen's own dispatch for 0x32 sets
        /// <c>keep_running = 0</c> (MAP.C:398) — and 0x32 is simply M's DOS scancode, which is why
        /// no key table exists on either side. <see cref="LocalMapScreen.ActionFor"/> already maps
        /// 0x32 to Close; nothing was ever delivering it from a key (TASK-584).
        ///
        /// <para>Routed through <see cref="PrimaryAction"/> rather than popping directly, so a
        /// letter reaches exactly the handler its button would and the screen keeps one dispatch.</para>
        /// </remarks>
        public void OnText(char c) {
            int action = GameData.Resources.World.TravelHotkeys.ActionFor(c);
            if (action != GameData.Resources.World.TravelHotkeys.NoAction) {
                PrimaryAction(action);
            }
        }

        public bool OnEdit(EditKey key) => false;

        public void OnSubmit() { }

        public void OnCancel() => _navigator?.Pop().Forget();
    }
}

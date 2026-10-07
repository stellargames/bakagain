namespace BakAgain.Core.DI {
    using BakAgain.Audio;
    using BakAgain.Book;
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.Core.States;
#if UNITY_EDITOR
    using BakAgain.Core.States.Debug;
#endif
    using BakAgain.CutScenes;
    using BakAgain.ResourceManagement;
    using BakAgain.UI;
    using BakAgain.UI.Cursor;
    using BakAgain.UI.FullMap;
    using BakAgain.UI.InGame;
    using BakAgain.UI.InputCore;
    using BakAgain.UI.Inventory;
    using BakAgain.World;
    using BakAgain.World.Rendering;
    using Microsoft.Extensions.Logging;
    using UnityEngine;
    using VContainer;
    using VContainer.Unity;

    public class RootLifetimeScope : LifetimeScope {
        [SerializeField]
        private MidiPlaybackManager midiPlaybackManagerPrefab;

        [SerializeField]
        private CutSceneView cutSceneViewPrefab;

        [SerializeField]
        private MainMenu mainMenuPrefab;

        [SerializeField]
        private InGameMenu inGameMenuPrefab;

        [SerializeField]
        private PreferencesMenu preferencesMenuPrefab;

        [SerializeField]
        private LoadGameMenu loadGameMenuPrefab;

        [SerializeField]
        private SaveGameMenu saveGameScreenPrefab;

        [SerializeField]
        private ContentsMenu contentsMenuPrefab;

        [SerializeField]
        private BakAgain.World.Scenes.LocationScreen locationScreenPrefab;

        [SerializeField]
        private BakAgain.UI.Spells.CastScreen castScreenPrefab;

        [SerializeField]
        private BakAgain.UI.Teleport.TeleportScreen teleportScreenPrefab;

        [SerializeField]
        private BakAgain.UI.Character.TempleHealScreen templeHealScreenPrefab;

        [SerializeField]
        private BakAgain.UI.Character.CharacterSheetScreen characterSheetScreenPrefab;

        [SerializeField]
        private BakAgain.UI.CampMenu campMenuPrefab;

        /// <summary>
        /// The combat action HUD. Registered even though nothing opens it yet: it is an overlay on
        /// the travel HUD like <see cref="campMenuPrefab"/>, and leaving it unregistered means the
        /// eventual opener cannot resolve it.
        /// </summary>
        [SerializeField]
        private BakAgain.UI.Combat.CombatMenu combatMenuPrefab;

        /// <summary>
        /// The quarrel picker SHOOT.DAT draws, raised by the melee menu's Shoot button. Registered
        /// and guarded exactly like <see cref="combatMenuPrefab"/>.
        /// </summary>
        [SerializeField]
        private BakAgain.UI.Combat.ShootMenu shootMenuPrefab;

        [SerializeField]
        private BakAgain.UI.Rest.InnScreen innScreenPrefab;

        [SerializeField]
        private BakAgain.UI.RiftMapScreen riftMapScreenPrefab;

        [SerializeField]
        private BakAgain.UI.Puzzle.PuzzleScreen puzzleScreenPrefab;

        /// <summary>The hidden CHEAT CENTRAL menus (TASK-692): REQ_KNOC from travel, REQ_CHET from the map.</summary>
        [SerializeField]
        private BakAgain.UI.Cheats.KnockKnockCheatScreen knockKnockCheatScreenPrefab;

        [SerializeField]
        private BakAgain.UI.Cheats.ChestCheatScreen chestCheatScreenPrefab;

        [SerializeField]
        private BookView bookViewPrefab;

        [SerializeField]
        private DialogManager dialogManagerPrefab;

        [SerializeField]
        private FullMapView fullMapViewPrefab;

        // Intro credits scroll (attract loop). Assigned by claude-pc once the
        // CreditsView prefab exists (see docs/unity-editor-tasks.md).
        [SerializeField]
        private CreditsView creditsViewPrefab;

        // Unified in-game/travel screen (FRAME.SCR chrome + REQ_MAIN + world viewport +
        // compass + heads on one UIDocument) — supersedes the InGameHud + InGameMenu pair.
        // Null-guarded so the container builds while the prefab is pending.
        [SerializeField]
        private InGameScreen inGameScreenPrefab;

        [SerializeField]
        private OverheadMapScreen overheadMapScreenPrefab;

        // The locator spells' map inset (REQ_CMAP over the world at the zone's maximum map height).
        // Instantiated inactive; a successful Eyes of Ishap / The Unseen / Nacre Cicatrix runs it.
        // Null-guarded like the overhead map, so the container still builds before it is assigned —
        // FieldSpellCaster falls back to its warning when the view is missing.
        [SerializeField]
        private BakAgain.UI.Spells.LocatorMapScreen locatorMapScreenPrefab;

        // Loot/inventory screen (REQ_INV.DAT over INVENTOR.SCX). Opened by
        // WorldInteractionController after the corpse loot dialog (Task 5). Assigned in the
        // RootLifetimeScope scene object; the container registers it as a plain singleton.
        [SerializeField]
        private InventoryMenu inventoryScreenPrefab;

        // Software mouse cursor overlay (POINTER/POINTERG sets). Assigned once the
        // CursorOverlay prefab exists (UIDocument, highest Sort Order, clearColor=false).
        // Null-guarded below so the container builds — and Phase-4 hover/warp consumers
        // still resolve ICursorManager — while the prefab is pending.
        [SerializeField]
        private CursorManager cursorManagerPrefab;

        // The PanelSettings every game screen's UIDocument shares (not MetaPanelSettings, which only
        // the loading screen and the game-folder prompt use). GameInitializationService installs
        // the runtime-built game font on it.
        [SerializeField]
        private UnityEngine.UIElements.PanelSettings gamePanelSettings;

        protected override void Configure(IContainerBuilder builder) {
            builder.RegisterInstance(LogManager.LoggerFactory);
            builder.Register(typeof(Logger<>), Lifetime.Transient).As(typeof(ILogger<>));

            // Input-ownership core (InputLayerStack + IUiCommands) + agent/test UiDriver hook.
            InputCoreInstaller.RegisterInputCore(builder);

            // Screen navigation: the single owner of "which screen is visible" — one stack of
            // IScreens; screens never SetActive each other. See
            // docs/superpowers/specs/2026-07-12-screen-navigation-architecture-design.md.
            // *** THE NAVIGATOR NO LONGER FADES EVERY TRANSITION. *** TASK-214 put a fade on every
            // Push/Pop/ResetTo/Replace. In practice it collided with the transitions that already
            // fade themselves — cutscene scripts open with their own FadeOut/FadeIn — and produced a
            // string of reported defects: a white screen fading in before the intro, black to
            // background to black between the intro and the menu, a stale intro frame on New Game,
            // an extra darken between the menu and the chapter animation, and another between the
            // chapter animation and the book. Removed at the owner's request, 2026-09-06.
            //
            // ScreenFade and IScreenFade are KEPT and still registered: the world and map reload
            // fades in HotspotService and LocationScenePlayer are self-paired, faithful to
            // WORLDLP.C:102-139 and MAP.C:136-167, and are unaffected. Re-enabling the global layer
            // is one line — hand the resolved IScreenFade back to the navigator's Fade property,
            // which defaults to NullScreenFade.
            builder.Register<BakAgain.UI.Navigation.IScreenFade,
                BakAgain.UI.Navigation.ScreenFade>(Lifetime.Singleton);
            builder.Register(_ => new BakAgain.UI.Navigation.ScreenNavigator(),
                Lifetime.Singleton).As<BakAgain.UI.Navigation.IScreenNavigator>();

            builder.Register<AddressableResourceProviderService>(Lifetime.Singleton).As<IResourceProviderService>();

            builder.RegisterComponentInNewPrefab(midiPlaybackManagerPrefab, Lifetime.Singleton).As<MidiPlaybackManager>();
            builder.Register<BakAgain.Audio.MenuSoundService>(Lifetime.Singleton);
            // Publish the static composition seam so UserInterfaceLoader (a prefab sibling
            // VContainer's RegisterComponentInNewPrefab does not inject) can reach it without
            // a runtime container lookup. Mirrors the UiDriver seam below.
            builder.RegisterBuildCallback(container =>
                BakAgain.Audio.MenuSoundService.Instance = container.Resolve<BakAgain.Audio.MenuSoundService>());
            builder.RegisterComponentInNewPrefab(cutSceneViewPrefab, Lifetime.Singleton).As<ICutsceneView>();
            builder.RegisterComponentInNewPrefab(mainMenuPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(inGameMenuPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(preferencesMenuPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(loadGameMenuPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(saveGameScreenPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(contentsMenuPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(campMenuPrefab, Lifetime.Singleton);
            if (combatMenuPrefab != null) {
                // Guarded because an unassigned prefab reference makes RegisterComponentInNewPrefab
                // throw at container build, taking the whole game down rather than just combat.
                builder.RegisterComponentInNewPrefab(combatMenuPrefab, Lifetime.Singleton);
            }
            if (shootMenuPrefab != null) {
                builder.RegisterComponentInNewPrefab(shootMenuPrefab, Lifetime.Singleton);
            }
            builder.RegisterComponentInNewPrefab(riftMapScreenPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(innScreenPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(puzzleScreenPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(knockKnockCheatScreenPrefab, Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(chestCheatScreenPrefab, Lifetime.Singleton);
            builder.Register<BakAgain.UI.Puzzle.PuzzleService>(Lifetime.Singleton);
            builder.Register<BakAgain.UI.Spells.FieldSpellCaster>(Lifetime.Singleton);
            builder.Register<BakAgain.World.WorldLightingService>(Lifetime.Singleton);
            builder.RegisterComponentInNewPrefab(bookViewPrefab, Lifetime.Singleton).As<IBookView>();
            builder.RegisterComponentInNewPrefab(dialogManagerPrefab, Lifetime.Singleton).As<IDialogManager>();
            builder.RegisterComponentInNewPrefab(fullMapViewPrefab, Lifetime.Singleton).As<IFullMapView>();
            builder.RegisterComponentInNewPrefab(creditsViewPrefab, Lifetime.Singleton);

            // World viewport geometry, read from START.DAT on first use (not at construction:
            // the game path may still be unset here). Plain singleton, no scene object — safe to
            // register unconditionally.
            builder.Register<WorldViewport>(Lifetime.Singleton).As<IWorldViewport>();

            // Unified in-game/travel screen (supersedes the InGameHud + InGameMenu pair).
            // InGameState depends on IInGameScreen; a no-op NullInGameScreen keeps it
            // resolvable while the prefab is pending.
            if (inGameScreenPrefab != null) {
                builder.RegisterComponentInNewPrefab(inGameScreenPrefab, Lifetime.Singleton).As<IInGameScreen>();
            } else {
                builder.Register<NullInGameScreen>(Lifetime.Singleton).As<IInGameScreen>();
            }

            // Loot/inventory screen. Registered when the prefab is assigned; null-guarded so a
            // pending assignment doesn't break container build (nothing resolves it until the
            // Task-5 WorldInteractionController wiring lands).
            if (inventoryScreenPrefab != null) {
                builder.RegisterComponentInNewPrefab(inventoryScreenPrefab, Lifetime.Singleton);
            }

            // Software cursor overlay. Prefab-backed CursorManager when assigned, else a
            // no-op NullCursorManager so ICursorManager stays resolvable (the OS cursor
            // shows until the overlay prefab is wired).
            if (cursorManagerPrefab != null) {
                builder.RegisterComponentInNewPrefab(cursorManagerPrefab, Lifetime.Singleton).As<ICursorManager>();
                // Eagerly instantiate at container build. ICursorManager is otherwise only
                // resolved by UserInterfaceLoader, which lives on a couple of sub-screens
                // (ContentsScreen / LoadGameScreen) — so on the main menu, intro and in-game the
                // lazy singleton was never created, leaving the OS arrow visible. Forcing
                // resolution here makes the software cursor (and OS-cursor hiding) active app-wide
                // from startup; states can still call ICursorManager.Hide() for cutscenes.
                builder.RegisterBuildCallback(container => container.Resolve<ICursorManager>());
            } else {
                builder.Register<NullCursorManager>(Lifetime.Singleton).As<ICursorManager>();
            }

            builder.Register<BookPresenter>(Lifetime.Transient).As<IBookPresenter>();

            builder.Register<GameSession>(Lifetime.Singleton);
            builder.Register<IGameClock, GameClock>(Lifetime.Singleton);
            // Resolved eagerly: it works purely by subscribing to the clock, so nothing would ever
            // ask for it and a lazy singleton would never be built.
            builder.Register<PartyUpkeepService>(Lifetime.Singleton);
            builder.Register<BakAgain.World.Scenes.InnService>(Lifetime.Singleton);
            builder.RegisterBuildCallback(container => container.Resolve<PartyUpkeepService>());
            builder.Register<IGameStateLoader, GameStateLoader>(Lifetime.Singleton);
            builder.Register<IPreferencesService, PreferencesService>(Lifetime.Singleton);
            builder.Register<ISaveGameDirectoryService, SaveGameDirectoryService>(Lifetime.Singleton);
            builder.Register<ISaveGameService, SaveGameService>(Lifetime.Singleton);

            // Game-logic services + the transition flows (there is no state machine — see the
            // 2026-07-12 screen-navigation architecture doc §3.1: state is GameSession data,
            // WorldRuntime owns the world lifecycle, GameFlow methods are the edges).
            builder.Register<DialogExecutor>(Lifetime.Singleton);
            builder.Register<ChapterScenesPlayer>(Lifetime.Singleton);
            builder.Register<BakAgain.World.Scenes.GdsSceneLoader>(Lifetime.Singleton);
            // The teleport hand-off slot. A GLOBAL in the original (teleportationData
            // @0x3dc31) and shared state here too: the location loop takes its scene half
            // and the world flow takes its location half, so both must see one instance.
            builder.Register<GameData.Resources.Location.PendingTeleport>(Lifetime.Singleton);
            builder.Register<BakAgain.World.Scenes.LocationScenePlayer>(Lifetime.Singleton);
            // The location hotspot overlay. Instantiated inactive (the prefab asset is saved that
            // way) and switched on by LocationScenePlayer once the entry animation has played.
            builder.RegisterComponentInNewPrefab(locationScreenPrefab, Lifetime.Singleton);
            // The casting screen. Instantiated inactive; shown by whoever opens it.
            builder.RegisterComponentInNewPrefab(castScreenPrefab, Lifetime.Singleton);
            // The temple rift map. Instantiated inactive; a temple's teleport arm runs it.
            builder.RegisterComponentInNewPrefab(teleportScreenPrefab, Lifetime.Singleton);
            // The temple healing service. Instantiated inactive; a temple's heal arm runs it.
            builder.RegisterComponentInNewPrefab(templeHealScreenPrefab, Lifetime.Singleton);
            // The character screen. Instantiated inactive; a right-click on a portrait runs it.
            builder.RegisterComponentInNewPrefab(characterSheetScreenPrefab, Lifetime.Singleton);
            if (overheadMapScreenPrefab != null) {
                builder.RegisterComponentInNewPrefab(overheadMapScreenPrefab, Lifetime.Singleton);
            }
            if (locatorMapScreenPrefab != null) {
                builder.RegisterComponentInNewPrefab(locatorMapScreenPrefab, Lifetime.Singleton)
                    .As<BakAgain.UI.Spells.ILocatorMapView>();
            } else {
                // Bound to null rather than left unregistered: FieldSpellCaster takes the view as a
                // constructor dependency, and an unregistered interface would fail the whole
                // container instead of the one spell. It already has a warning for "cast succeeded
                // but there is nothing to show it on".
                builder.Register<BakAgain.UI.Spells.ILocatorMapView>(
                    _ => null, Lifetime.Singleton);
            }
            builder.Register<WorldRuntime>(Lifetime.Singleton);
            builder.Register<GameFlow>(Lifetime.Singleton).As<IGameFlow>();
            builder.Register<ICutscenePlayerFactory, CutscenePlayerFactory>(Lifetime.Singleton);
            builder.Register<ICutsceneFrameProcessor, CutsceneFrameProcessor>(Lifetime.Singleton);
#if UNITY_EDITOR
            // Debug-only entry scripts (selected from the StartStateSelector overlay).
            // Excluded from player builds so they never ship.
            builder.Register<BookTestState>(Lifetime.Singleton);
            builder.Register<WorldTestState>(Lifetime.Singleton);
            builder.Register<ModelDebugState>(Lifetime.Singleton);
            builder.Register<TestCutsceneState>(Lifetime.Singleton);
            builder.Register<PlayCutsceneState>(Lifetime.Singleton);
#endif

            // World rendering
            builder.Register<WorldRenderModeService>(Lifetime.Singleton);
            builder.Register<ZoneSceneBuilder>(Lifetime.Transient);
            // Ground bags outlive any one zone build (the session owns the containers), so the
            // spawner is a singleton that each build re-points at the zone currently on screen.
            builder.Register<GroundBagService>(Lifetime.Singleton).AsSelf().As<IGroundBagSpawner>();
            builder.Register<BakAgain.World.DoorVisualService>(Lifetime.Singleton);

            new CutsceneInstaller().Install(builder);

            // Start-state selector (debug boot menu) + its static MCP/test seam (mirrors UiDriver).
            builder.Register<StartStateSelector>(Lifetime.Singleton);
            builder.RegisterBuildCallback(container => DebugStart.Active = container.Resolve<StartStateSelector>());

            // Register GameManager
            builder.Register<GameManager>(Lifetime.Singleton);

            builder.RegisterInstance(gamePanelSettings);
            builder.Register<GameInitializationService>(Lifetime.Singleton).AsImplementedInterfaces();
            builder.Register<GlobalDiagnosticService>(Lifetime.Singleton).AsImplementedInterfaces();
        }
    }
}
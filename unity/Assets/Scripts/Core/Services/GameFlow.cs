namespace BakAgain.Core.Services {
    using BakAgain.Core.States;
    using BakAgain.CutScenes;
    using BakAgain.UI;
    using BakAgain.UI.FullMap;
    using BakAgain.UI.InGame;
    using BakAgain.UI.Navigation;
    using BakAgain.World;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.GameState;
    using GameData.Resources.Location;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Threading;
    using VContainer;

    /// <summary>
    /// The game's transition scripts — the edges of the (implicit) state graph. There is no state
    /// machine: the state is data (<see cref="GameSession"/>), the world lifecycle is
    /// <see cref="WorldRuntime"/>, which screen shows is the <see cref="IScreenNavigator"/>, and
    /// each method here is one awaited edge that sequences them and lands in a configuration.
    /// Serialized: one flow at a time (a second trigger while one runs is ignored) — the only
    /// invariant the old StateMachine actually provided. See the 2026-07-12 architecture doc §3.1.
    /// </summary>
    public interface IGameFlow {
        /// <summary>The intro attract loop (INTRO cutscene ↔ credits until input), then the main menu.</summary>
        UniTask Boot();

        /// <summary>Land on the main menu (tearing down the world if one is built).</summary>
        UniTask ShowMainMenu();

        /// <summary>New game: chapter scenes → hydrate STARTUP.GAM + CHAPx.DAT → chapter-setup
        /// dialog (party order) → full map + chapter-description dialog → the world.
        /// <paramref name="playChapterScenes"/> false skips the scenes (debug fast path).</summary>
        UniTask StartNewGame(bool playChapterScenes = true);

        /// <summary>Restore: hydrate the save at <paramref name="path"/> → full map + chapter
        /// dialog → the world. False when hydration fails (caller stays where it is).</summary>
        UniTask<bool> LoadSave(string path);

        /// <summary>
        /// Move the running game to another chapter: scenes → apply CHAPx.DAT → clear the
        /// transition's globals → chapter-setup dialog → map → the world.
        /// </summary>
        /// <remarks>
        /// <b>Not <see cref="StartNewGame"/> with a number.</b> It deliberately does not hydrate —
        /// re-reading STARTUP.GAM would reset the party, inventories and story flags — and it
        /// clears the 400..5199 global window, which a new game has no need to.
        /// </remarks>
        UniTask GoToChapter(int chapter);

        /// <summary>Enter the world from the current session (debug direct-boot hydrates a new
        /// game first when no session is active).</summary>
        UniTask EnterWorld();

        /// <summary>
        /// Act on a queued teleport destination, and report what it turned out to mean.
        /// </summary>
        /// <remarks>
        /// The world half only. A <see cref="ZoneTransitionKind.SceneOnly"/> destination is
        /// reported and NOT acted on: running a GDS scene belongs to the scene player, which the
        /// callers of this already hold, and injecting one here would give GameFlow a second job it
        /// does not otherwise have. The two kinds that touch the world — reposition and change zone
        /// — are exactly the ones only GameFlow may perform, since it alone sequences WorldRuntime.
        /// </remarks>
        UniTask<ZoneTransitionKind> TransitionTo(GameData.Resources.Location.Location destination);
    }

    public sealed class GameFlow : IGameFlow {
        private readonly ILogger<GameFlow> _logger;
        private readonly BakAgain.UI.Navigation.IScreenFade _screenFade;
        private readonly GameData.Resources.Location.PendingTeleport _teleport;
        private readonly BakAgain.World.Scenes.LocationScenePlayer _locations;
        private bool _drainingTeleport;
        private readonly IPreferencesService _preferences;
        private readonly IScreenNavigator _navigator;
        private readonly GameSession _session;
        private readonly IGameStateLoader _gameStateLoader;
        private readonly DialogExecutor _dialogExecutor;
        private readonly ChapterScenesPlayer _scenes;
        private readonly WorldRuntime _world;
        private readonly FullMapView _fullMap;
        private readonly IDialogManager _dialogManager;
        private readonly CutscenePresenter _cutscenePresenter;
        private readonly ICutsceneView _cutsceneView;
        private readonly CreditsView _credits;
        private readonly BakAgain.UI.InputCore.InputLayerStack _inputStack;
        // MainMenu and the travel screen are resolved lazily: their prefabs' [Inject] methods
        // reach back into IGameFlow (MainMenu directly; InGameScreen via its InGameMenu sibling),
        // so constructor dependencies here would be DI cycles.
        private readonly IObjectResolver _resolver;
        private MainMenu _mainMenu;
        private InGameScreen _travelScreen;

        private bool _busy;

        public GameFlow(
            ILogger<GameFlow> logger,
            IScreenNavigator navigator,
            GameSession session,
            IGameStateLoader gameStateLoader,
            DialogExecutor dialogExecutor,
            ChapterScenesPlayer scenes,
            WorldRuntime world,
            FullMapView fullMap,
            IDialogManager dialogManager,
            CutscenePresenter cutscenePresenter,
            ICutsceneView cutsceneView,
            CreditsView credits,
            BakAgain.UI.InputCore.InputLayerStack inputStack,
            IPreferencesService preferences,
            IObjectResolver resolver,
            // Optional so a GameFlow built without a screen to darken simply cuts. TASK-214.
            BakAgain.UI.Navigation.IScreenFade screenFade = null,
            // Optional for the same reason: a GameFlow with no teleport slot simply never has one
            // to drain.
            GameData.Resources.Location.PendingTeleport teleport = null,
            // Optional too: without it a queued destination's SCENE half cannot be run, and the
            // drain below says so rather than silently dropping it.
            BakAgain.World.Scenes.LocationScenePlayer locations = null) {
            _teleport = teleport;
            _locations = locations;
            _screenFade = screenFade ?? new BakAgain.UI.Navigation.NullScreenFade();
            _logger = logger;
            _preferences = preferences;
            _navigator = navigator;
            _session = session;
            _gameStateLoader = gameStateLoader;
            _dialogExecutor = dialogExecutor;
            _scenes = scenes;
            _world = world;
            _fullMap = fullMap;
            _dialogManager = dialogManager;
            _cutscenePresenter = cutscenePresenter;
            _cutsceneView = cutsceneView;
            _credits = credits;
            _inputStack = inputStack;
            _resolver = resolver;
        }

        private MainMenu MainMenu => _mainMenu ??= _resolver.Resolve<MainMenu>();
        private InGameScreen TravelScreen => _travelScreen ??= _resolver.Resolve<InGameScreen>();

        public UniTask Boot() => RunExclusive(nameof(Boot), async () => {
            // The intro attract loop, faithful to PlayIntro (KRONDOR.EXE 0x20bbc): INTRO.ADS plays,
            // then the credits scroll, then the whole thing repeats until the player presses any
            // key / clicks — which exits to the main menu. The attract-pass counter is 1-based so
            // the credits' cycle-gated easter eggs (cycle % 30 == 8) line up with word_dseg_5E6.
            // Skippable: the original gates the whole intro on a preference bit —
            // `if (g_engine_prefs->flags & 8) gmain_play_intro_animation()` (GMAIN.C:725), checked
            // once at startup before the menu loop. Turning it off lands straight on the menu.
            bool playIntro = _preferences?.Current?.Introduction ?? true;
            if (!playIntro) {
                _logger.LogInformation("Intro disabled in Preferences; going straight to the main menu.");
            }

            int cycleIndex = 0;
            if (playIntro && _cutscenePresenter != null && _cutsceneView != null) {
                while (true) {
                    _logger.LogInformation("Playing INTRO cutscene (attract pass {Pass}).", cycleIndex + 1);
                    await _navigator.Push(_cutsceneView);
                    // *** POP TO CONTINUE, CLEAR TO LEAVE. *** Pop fades back IN once the screen is
                    // gone, which is right when the attract loop carries on to the credits and wrong
                    // when it is handing over to the main menu: ShowMainMenuCore's ResetTo fades too,
                    // so the player saw black, then the empty background faded up, then black again,
                    // then the menu. Clear is the navigator's own answer to that — it deliberately
                    // does not fade "because the ResetTo that follows it does". Reported from play.
                    var cutsceneComplete = false;
                    try {
                        cutsceneComplete = await _cutscenePresenter.PlayCutsceneAsync(
                            "INTRO", _cutsceneView, attractMode: true);
                    } finally {
                        if (cutsceneComplete) {
                            await _navigator.Pop();
                        } else {
                            await _navigator.Clear();
                        }
                    }
                    if (!cutsceneComplete) {
                        break; // cancelled by input during the animation — exit the attract loop
                    }
                    if (_credits == null) {
                        _logger.LogWarning("CreditsView was not injected; skipping credits.");
                        break;
                    }
                    cycleIndex++;
                    await _navigator.Push(_credits);
                    // Same rule as the cutscene above: interrupting the credits leaves the attract
                    // loop for the menu, so clear rather than fade back in over nothing.
                    var creditsComplete = false;
                    try {
                        creditsComplete = await _credits.PlayAsync(cycleIndex, CancellationToken.None);
                    } finally {
                        if (creditsComplete) {
                            await _navigator.Pop();
                        } else {
                            await _navigator.Clear();
                        }
                    }
                    if (!creditsComplete) {
                        break; // player interrupted the credits — exit to the main menu
                    }
                    if (!await PauseBeforeRepeatAsync()) {
                        break; // input during the pause — exit to the main menu
                    }
                }
            } else {
                _logger.LogError("CutscenePresenter or ICutsceneView was not injected; skipping intro.");
            }
            await ShowMainMenuCore();
        });

        /// <summary>
        /// The attract loop's rest between passes — <b>140 timer ticks</b>, about 2.37 seconds.
        /// </summary>
        /// <remarks>
        /// The original sets <c>deadline = ticks + 0x8C</c> after the credits return and spins on it
        /// while polling the keyboard and BOTH mouse buttons, so the pause is interruptible exactly
        /// like every other point in the sequence — which is why this returns whether it ran to the
        /// end rather than just awaiting. Without it the loop restarts the moment the credits stop,
        /// so the title screen never rests on the fade-to-black at the end of a pass.
        ///
        /// <para>The wall-clock length is <see cref="GameTick"/>'s to give: a tick count only became
        /// a duration once the clock's rate was recovered.</para>
        /// </remarks>
        /// <returns>False if input cut the pause short, meaning leave for the menu.</returns>
        private async UniTask<bool> PauseBeforeRepeatAsync() {
            var interrupted = false;
            // Any key or either button, same as the cutscene's attract-mode layer: anyIntentActivates
            // routes every intent here, and both callbacks mean the same thing.
            var layer = new BakAgain.UI.InputCore.ActionLayer(
                "attract-pause", () => interrupted = true, () => interrupted = true,
                onMove: null, anyIntentActivates: true);
            _inputStack.Push(layer);
            try {
                double seconds = GameTick.Seconds(AttractLoop.PauseTicksBeforeRepeat);
                double start = UnityEngine.Time.realtimeSinceStartupAsDouble;
                while (!interrupted && UnityEngine.Time.realtimeSinceStartupAsDouble - start < seconds) {
                    await UniTask.Yield();
                }
            } finally {
                _inputStack.Remove(layer);
            }

            return !interrupted;
        }

        public UniTask ShowMainMenu() => RunExclusive(nameof(ShowMainMenu), ShowMainMenuCore);

        public UniTask StartNewGame(bool playChapterScenes = true) => RunExclusive(nameof(StartNewGame), async () => {
            await LeaveCurrentAsync();

            if (playChapterScenes) {
                await _scenes.PlayAsync(1, ChapterScenesMode.StartOnly);
            }

            // Hydrate STARTUP.GAM; its stored chapter selects the CHAPx.DAT applied over the
            // template (the DOS go_to_chapter).
            bool hydrated = await _gameStateLoader.LoadNewGameAsync();
            if (!hydrated) {
                _logger.LogError("New-game hydration failed; returning to main menu.");
                await ShowMainMenuCore();
                return;
            }

            // go_to_chapter's tail runs the chapter-setup dialog (DIAL_Z20 #2000023) right after
            // applying CHAPx.DAT — it sets the runtime party/head ORDER via its ChangeParty action
            // (chapter 1 = Locklear, Owyn, Gorath), which STARTUP.GAM's [0,1,2] template does not.
            // GMAIN.C:203 runs the same savegame_chapter_start_dispatch for a new game.
            BeginChapterStart(_session.Chapter);
            ApplyChapterSetupArm(_session.Chapter);
            await _dialogExecutor.RunChapterSetupAsync(_session.Chapter);
            _session.PartyDirtyFlags = 0;

            await ShowMapAndChapterDialogAsync();
            await EnterWorldCoreAsync();
        });

        /// <summary>
        /// Move the running game to another chapter — the transition half of <c>go_to_chapter</c>.
        /// </summary>
        /// <remarks>
        /// <b>StartNewGame minus the hydration, plus the clear.</b> The two differ in exactly two
        /// places and both matter: this must NOT call <see cref="IGameStateLoader.LoadNewGameAsync"/>
        /// (that re-reads STARTUP.GAM and would reset the party, inventories and story flags — a
        /// chapter change is not a new game), and it must clear the transition's global window,
        /// which a new game does not need because there is nothing there yet.
        ///
        /// <para><b>The order is the original's: apply, then clear, then the setup dialog.</b>
        /// <c>ClearGlobalVars_400_5200</c> runs after the file apply and before the per-chapter arm,
        /// so a chapter's own setup can write into the range it just cleared. Clearing first would
        /// erase what CHAPx.DAT applied; clearing last would erase what the setup dialog set.</para>
        ///
        /// <para><b>The pending flag is cleared as the transition starts, not when it finishes.</b>
        /// It is what asked for this call; leaving it set would have the next dialog ask again.</para>
        /// </remarks>
        public UniTask GoToChapter(int chapter) => RunExclusive(nameof(GoToChapter), () => GoToChapterCore(chapter));

        /// <summary>
        /// A chapter is finished — the original's mode 5 (GMAIN.C:743-752): the finished chapter's
        /// closing scenes, then either the next chapter or, after chapter 9, the main menu.
        /// </summary>
        /// <remarks>
        /// <b>The close plays FIRST, for every chapter.</b> <c>gmain_play_chapter_cutscene(n, 2, 1)</c>
        /// runs before the chapter-9 test, so the game's ending is C92.BOK and C92.ADS. This went
        /// straight to the next chapter's opening (and, after chapter 9, straight to the menu), so no
        /// chapter's second part — C12, C32, C52, the ending — was ever shown on a playthrough.
        /// </remarks>
        private UniTask FinishChapter(int finished) => RunExclusive(nameof(FinishChapter), async () => {
            _session.ChapterTransitionPending = 0;
            await LeaveCurrentAsync();
            await _scenes.PlayAsync(finished, ChapterScenesMode.EndOnly);
            if (ChapterTransition.EndsTheGame(finished)) {
                await ShowMainMenuCore();
                return;
            }
            await GoToChapterCore(ChapterTransition.NextChapter(finished));
        });

        private async UniTask GoToChapterCore(int chapter) {
            _session.ChapterTransitionPending = 0;
            await LeaveCurrentAsync();
            await _scenes.PlayAsync(chapter, ChapterScenesMode.StartOnly);

            BeginChapterStart(chapter);
            if (!await _gameStateLoader.ApplyChapterStartAsync(chapter)) {
                _logger.LogError("Chapter {Chapter} data failed to apply; staying where we are.",
                    chapter);
                return;
            }

            _session.ClearChapterGlobals();
            ApplyChapterSetupArm(chapter);
            await _dialogExecutor.RunChapterSetupAsync(chapter);
            _session.PartyDirtyFlags = 0;
            await ShowMapAndChapterDialogAsync();
            await EnterWorldCoreAsync();
        }


        /// <summary>
        /// The head of <c>savegame_chapter_start_dispatch</c> (canassa SAVEGAME.C:135-157): record the
        /// purse for this chapter and run the timer pool down, before the CHAP file is applied.
        /// </summary>
        /// <remarks>
        /// <b>Eighty ticks of 30000 without moving the clock.</b> The game time is rounded to the next
        /// day separately (ApplyChapterStart), so this only expires the pool and fires the set/clear
        /// flag writes of whatever runs out — <see cref="GameClock.TickTimersOnly"/>, not Advance.
        /// </remarks>
        private void BeginChapterStart(int chapter) {
            _session.ChapterFinishingGold.RecordStartOf(chapter, _session.PartyGold);
            GameClock clock = _resolver.Resolve<GameClock>();
            for (int i = 0; i < ChapterStartTimerTicks; i++) {
                clock.TickTimersOnly(ChapterStartTimerTickSize);
            }
        }

        /// <summary><c>for (i = 0; i &lt; 0x50; i++) timerpool_tick(30000);</c></summary>
        private const int ChapterStartTimerTicks = 0x50;

        private const long ChapterStartTimerTickSize = 30000;

        /// <summary>
        /// The switch and the tail of <c>savegame_chapter_start_dispatch</c> (SAVEGAME.C:180-244), after
        /// the CHAP file is applied: the per-chapter arm, the chapter 3 and 7 event writes, and the
        /// party heal every chapter gets.
        /// </summary>
        /// <remarks>
        /// ponytail: chapter 4's palette fade (<c>palette_fade_run_scheduled</c>) is presentation and is
        /// not played. The setup dialog (0x1e8497) is RunChapterSetupAsync, already called after this.
        /// </remarks>
        private void ApplyChapterSetupArm(int chapter) {
            switch (GameData.Resources.GameState.ChapterTransition.ArmFor(chapter)) {
                case GameData.Resources.GameState.ChapterSetupArm.LocklearInventoryToZone15:
                    CloneInventory(_session.GetActorInventory(CharacterLocklear),
                        _session.GetRuntimeContainerAt(15, 2, 2));
                    break;
                case GameData.Resources.GameState.ChapterSetupArm.OwynAndGorathInventoryToZone12:
                    CloneInventory(_session.GetActorInventory(CharacterOwyn),
                        _session.GetRuntimeContainerAt(12, 0xa9a10, 0xab180));
                    CloneInventory(_session.GetActorInventory(CharacterGorath),
                        _session.GetRuntimeContainerAt(12, 0xaa820, 0xaa1e0));
                    // for (i = 1; i <= 2; i++): characters[1] is Gorath and gets the lit one.
                    GiveOnlyATorch(_session.GetActorInventory(CharacterGorath), lit: true);
                    GiveOnlyATorch(_session.GetActorInventory(CharacterOwyn), lit: false);
                    _session.PartyGold = 0;
                    break;
                case GameData.Resources.GameState.ChapterSetupArm.ZoneZeroContainerIntoLocklearsPack: {
                    // Locklear's pack is refilled from the zone-0 container at (10, 0), verbatim.
                    GameData.Resources.Inventory.RuntimeContainer source = _session.GetRuntimeContainerAt(0, 10, 0);
                    GameData.Resources.Inventory.RuntimeContainer locklear = _session.GetActorInventory(CharacterLocklear);
                    if (source != null && locklear != null) {
                        locklear.Items.Clear();
                        foreach (GameData.Resources.Inventory.RuntimeItem item in source.Items) {
                            locklear.Items.Add(item.Clone());
                        }
                        locklear.Dirty = true;
                    }
                    break;
                }
                case GameData.Resources.GameState.ChapterSetupArm.TwoZoneZeroContainersToZone15:
                    CloneInventory(_session.GetRuntimeContainerAt(0, 20, 1), _session.GetRuntimeContainerAt(15, 0x3c, 3));
                    CloneInventory(_session.GetRuntimeContainerAt(0, 30, 1), _session.GetRuntimeContainerAt(15, 0x40, 3));
                    // #ifdef V102CD, the build we target.
                    _session.SetGlobalFlag(0x1959, false);
                    _session.SetGlobalFlag(0x195a, false);
                    break;
            }

            // Cases 5 and 6 have no break and fall into case 7: case 8:, so all four restore the purse.
            _session.PartyGold = _session.ChapterFinishingGold.PurseAfterRestore(chapter, _session.PartyGold);

            if (chapter == 3) {
                _session.SetGlobalFlag(0x1fbc, false);
            }
            if (chapter == 7) {
                _session.SetGlobalFlag(0x1ab1, true);
            }

            // stat_party_heal_all(100): the active party, each through stat_combatant_heal.
            _session.HealActiveParty(GameData.Resources.Character.CharacterHeal.FullHealAmount);
        }

        /// <summary>
        /// <c>itemuse_actor_spawn_clone_inv</c> (ITEMUSE.C:555): the target container's items become a
        /// copy of the source's, each with <see cref="GameData.ItemFlags.Equipped"/> cleared. The source
        /// keeps its own items.
        /// </summary>
        private static void CloneInventory(GameData.Resources.Inventory.RuntimeContainer source,
            GameData.Resources.Inventory.RuntimeContainer target) {
            if (source == null || target == null) {
                return;
            }
            target.Items.Clear();
            foreach (GameData.Resources.Inventory.RuntimeItem item in source.Items) {
                target.Items.Add(new GameData.Resources.Inventory.RuntimeItem(item.ObjectId, item.Variable,
                    (ushort)(item.ItemFlags & ~(ushort)GameData.ItemFlags.Equipped)));
            }
            target.Dirty = true;
        }

        /// <summary>Chapter 4's prison kit: the pack emptied to one torch (object 0x54) at condition 6.</summary>
        private static void GiveOnlyATorch(GameData.Resources.Inventory.RuntimeContainer pack, bool lit) {
            if (pack == null) {
                return;
            }
            pack.Items.Clear();
            pack.Items.Add(new GameData.Resources.Inventory.RuntimeItem(ChapterFourTorch, ChapterFourTorchCondition,
                lit ? (ushort)GameData.ItemFlags.Lit : (ushort)0));
            pack.Dirty = true;
        }

        private const byte ChapterFourTorch = 0x54;

        private const byte ChapterFourTorchCondition = 6;

        /// <summary>canassa GMAIN.H: CHR_LOCKLEAR 0, CHR_GORATH 1, CHR_OWYN 2 — the character index, not the roster slot.</summary>
        private const int CharacterLocklear = 0;

        private const int CharacterGorath = 1;

        private const int CharacterOwyn = 2;

        public async UniTask<bool> LoadSave(string path) {
            if (_busy) {
                _logger.LogWarning("GameFlow busy; ignoring LoadSave.");
                return false;
            }
            _busy = true;
            try {
                bool ok = await _gameStateLoader.LoadFromFileAsync(path);
                if (!ok) {
                    return false; // caller (LoadGameMenu) stays on its screen
                }
                // A loaded game lives where it was loaded from, so a later bookmark writes slot 0 of
                // that same directory. Derived from the path rather than threaded through the call,
                // and refused rather than guessed if the file is not a slot file — mistaking one for
                // slot 0 would overwrite the bookmark.
                string loadedDir = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
                if (!string.IsNullOrEmpty(loadedDir)
                    && GameData.Resources.Data.BookmarkSave.TryParseSlot(
                        System.IO.Path.GetFileName(path), out int loadedSlot)) {
                    _session.SetSaveLocation(loadedDir, loadedSlot);
                }
                // Faithful to StartGameOrLoadSave @ 0x20835 (mode 3): restore shows the same
                // fullmap + chapter dialog as a new game, but skips the chapter scenes and
                // go_to_chapter — the loaded save already carries full state.
                await LeaveCurrentAsync();
                await ShowMapAndChapterDialogAsync();
                await EnterWorldCoreAsync();
                return true;
            } catch (Exception e) {
                _logger.LogError(e, "Error in flow LoadSave");
                return false;
            } finally {
                _busy = false;
            }
        }

        public UniTask EnterWorld() => RunExclusive(nameof(EnterWorld), async () => {
            await LeaveCurrentAsync();
            // Debug direct-boot: hydrate a new game when no session is active (chapter from
            // STARTUP.GAM), mirroring the old InGameState guard. The normal flows arrive with an
            // active session, making this a no-op.
            if (!_session.IsActive) {
                _logger.LogInformation("EnterWorld with an un-hydrated session (debug direct-boot); loading new game first.");
                if (!await _gameStateLoader.LoadNewGameAsync()) {
                    _logger.LogError("Direct-boot new-game hydration failed; aborting EnterWorld.");
                    return;
                }
                BeginChapterStart(_session.Chapter);
                ApplyChapterSetupArm(_session.Chapter);
                await _dialogExecutor.RunChapterSetupAsync(_session.Chapter);
                _session.PartyDirtyFlags = 0;
            }
            await EnterWorldCoreAsync();
        });

        /// <inheritdoc/>
        public async UniTask<ZoneTransitionKind> TransitionTo(GameData.Resources.Location.Location destination) {
            ZoneTransitionKind kind = ZoneTransition.KindOf(destination,
                _session.CurrentZone, _session.WorldX, _session.WorldY, _session.Rotation);

            // Neither of these touches the world, so neither takes the busy latch: there is
            // nothing to serialise against. It also means a caller that is mid-scene can ask what a
            // freshly queued destination means without being told GameFlow is busy.
            if (kind == ZoneTransitionKind.None || kind == ZoneTransitionKind.SceneOnly) {
                return kind;
            }

            if (_busy) {
                _logger.LogWarning("GameFlow busy; ignoring TransitionTo.");
                return ZoneTransitionKind.None;
            }
            _busy = true;
            try {
                if (kind == ZoneTransitionKind.Reposition) {
                    // Same zone: the scene, its collision and its chunk tables all stay. Moving the
                    // party is a state write plus a camera resync, and rebuilding the world for it
                    // would throw away everything that is still valid.
                    _session.PlaceAt(destination);
                    _world.Movement?.SyncToCamera();

                    return kind;
                }

                // Different zone: dispose, relocate, load — the original's order, and the reason
                // the session write sits BETWEEN the two halves. BuildAsync reads the zone off the
                // session, so placing the party before the teardown would build the new zone with
                // the old world still up, and placing it after would build the old one again.
                await LeaveCurrentAsync();
                _session.PlaceAt(destination);
                await EnterWorldCoreAsync();

                return kind;
            } catch (Exception e) {
                _logger.LogError(e, "Error in flow TransitionTo");

                return ZoneTransitionKind.None;
            } finally {
                _busy = false;
            }
        }

        // --- shared sequence pieces ---

        // Leave whatever configuration is current: pop every screen, and tear down the world if
        // one is built — the old per-state Exit semantics, centralized.
        private async UniTask LeaveCurrentAsync() {
            // A location loop is started with `.Forget()`, so clearing the navigator does not end it
            // — and a surviving one still answers sub-scene transitions with ITS scene number. See
            // LocationScenePlayer.Abandon.
            _locations?.Abandon();
            await _navigator.Clear();
            if (_world.IsBuilt) {
                await _world.TeardownAsync();
            }
        }

        private async UniTask ShowMainMenuCore() {
            await LeaveCurrentAsync();
            await _navigator.ResetTo(MainMenu);
        }

        /// <summary>
        /// FULLMAP at the party position with the chapter-description dialog over it (DDX
        /// chapter + 293, mirroring <c>StartGameOrLoadSave</c>'s <c>add ax, 293</c>) — and
        /// <b>the map is deliberately LEFT UP</b>.
        /// </summary>
        /// <remarks>
        /// <b>*** THE MAP IS THE LOADING SCREEN. ***</b> It used to pop itself as soon as the
        /// chapter dialog was dismissed, and <see cref="EnterWorldCoreAsync"/>'s
        /// <c>BuildAsync</c> — the zone geometry, the collision tables, the hotspot tables, the
        /// party placement — ran <b>afterwards</b>, with nothing on screen. So the player watched
        /// the map disappear and then waited on an empty screen for the part that actually takes
        /// the time. Reported from play, 2026-08-25.
        ///
        /// <para>Leaving it up costs no extra mechanism: <c>ResetTo</c> hides the outgoing screen
        /// and shows the new one, so the map is swapped for the travel screen at the moment the
        /// world is ready. The dismissal is therefore gated on READINESS rather than on the
        /// dialog's lifetime, which is the whole of the fix.</para>
        ///
        /// <para><b>Zone crossings still show nothing</b> — <c>TransitionTo</c> clears the stack
        /// and rebuilds with no screen up. Same defect, different flow, and whether a crossing
        /// should show the map at all is a question about the original rather than about this bug.
        /// </para>
        /// </remarks>
        private async UniTask ShowMapAndChapterDialogAsync() {
            _fullMap.SetMarker(
                _session.MapMarkerVisible,
                _session.MapMarkerXPercent,
                _session.MapMarkerYPercent,
                _session.MapMarkerIcon);
            // No way out on purpose: this is the loading mask, and EnterWorldCoreAsync's ResetTo
            // takes it down once the world is ready. Set explicitly rather than left over from a
            // previous show, which is why the setter is called on both paths.
            _fullMap.SetExitAffordance(null);
            await _navigator.Push(_fullMap);
            int chapterDialogId = _session.Chapter + 293;
            _logger.LogInformation("Showing chapter description dialog DDX {DialogId}.", chapterDialogId);
            await _dialogManager.ShowById(chapterDialogId);
            // No Pop: see the remarks. The caller's EnterWorldCoreAsync swaps the map out once the
            // world behind it is built.
        }

        /// <summary>
        /// Builds the world and shows the travel screen.
        /// </summary>
        /// <remarks>
        /// <b>The <c>ResetTo</c> at the end is what dismisses the loading screen</b>, so everything
        /// the player needs must be finished before it: <c>BuildAsync</c> awaits the zone scene,
        /// its collision and the hotspot tables, and the camera and movement are handed to the
        /// travel screen BEFORE it shows so the first frame and the first keypress find them ready.
        /// Anything added here that the world needs belongs above the <c>ResetTo</c>, not after it.
        /// </remarks>
        /// <summary>
        /// What flow owes the world loop each iteration. Called from the travel screen's update.
        /// </summary>
        /// <remarks>
        /// <b>The chapter transition is noticed here because that is where the original notices
        /// it.</b> A dialog requests one by writing the <c>WorldLoopExitRequest</c> field, which
        /// lands in <see cref="GameSession.ChapterTransitionPending"/>; <c>MainGameLoop</c> then
        /// exits on the flag at the top of its next pass.
        ///
        /// <para><b>Acting on the WRITE instead would break it.</b> The write happens inside the
        /// dialog, and <see cref="RunExclusive"/> drops a flow call while one is running — so an
        /// event-driven transition would either be logged away as busy or tear the world down under
        /// a live dialog. A frame's delay is not a compromise here; it is the mechanism.</para>
        ///
        /// <para><b>*** THE CHAPTER IS NOT THE NEW ONE, AND NOTHING WRITES IT. *** This said "the
        /// same dialog writes Field.Chapter, so the session carries the destination" and that is
        /// false.</b> Across all 32 shipped DDX files the dialogs write Vars 0, 4, 14, 15, 16 and
        /// 17 — never Var 7, the chapter field. The original does not expect one either: it stores
        /// the CURRENT chapter at the exit (<c>g_nChapterAtLoopExit = g_gameState.nChapter</c>,
        /// WORLDLP.C:403) and every consumer adds one itself (GMAIN.C:112, 205, 753). Passing the
        /// session's own chapter here re-applied the chapter the party was already in, so a
        /// transition restarted it — measured live on 2026-09-07: raising the flag in chapter 1
        /// landed back at chapter 1, zone 1, tile (10,16) instead of chapter 2, zone 11,
        /// tile (11,11).</para>
        ///
        /// <para><b>And the flag is not a boolean.</b> Only value 1 advances; 2 is a different exit
        /// that plays a cutscene and stays put. Six shipped dialogs write 1 and one writes 2 — see
        /// <see cref="ChapterTransition.Advances"/>.</para>
        /// </remarks>
        private bool _openingGroundPile;

        /// <summary>"We can barely manage with what we have" -- ITEMUSE.C:587.</summary>
        private const int GroundPileDialogId = 0x1b775b;

        /// <summary>
        /// <c>itemuse_ground_pile_open_inv</c> (ITEMUSE.C:570-590): move the pile into a container on
        /// the party's spot, say so, and open the loot screen on it.
        /// </summary>
        /// <remarks>
        /// The container is found or claimed the way a discard's is
        /// (<see cref="GameSession.ResolveGroundDropTarget"/>, the same objfixed-then-enc_location
        /// order), shown in the world, and settled by the loot screen's own exit -- a bag the player
        /// empties is freed, one they leave items in stays on the ground to come back to.
        /// </remarks>
        private async UniTaskVoid OpenGroundPileAsync() {
            _openingGroundPile = true;
            try {
                GameData.Resources.Inventory.RuntimeContainer pile = _session.GroundPile;
                GroundDropTarget target = _session.ResolveGroundDropTarget(
                    _session.CurrentZone, _session.PositionX, _session.PositionY);
                if (!target.Resolved) {
                    _logger.LogWarning("Ground pile of {Count} item(s) has no container to go to in zone "
                        + "{Zone}; left on the pile.", pile.Items.Count, _session.CurrentZone);
                    return;
                }
                foreach (GameData.Resources.Inventory.RuntimeItem item in pile.Items) {
                    target.Container.Items.Add(item.Clone());
                }
                pile.Items.Clear();
                pile.Dirty = true;
                target.Container.Dirty = true;
                GameData.Resources.Inventory.GroundContainerPool.RecomputeHoldsProtectedItem(
                    target.Container, _session.ObjectInfo);
                _resolver.Resolve<BakAgain.World.IGroundBagSpawner>().SpawnAsync(target.Container).Forget();

                await _dialogManager.ShowById(GroundPileDialogId);
                var loot = _resolver.Resolve<BakAgain.UI.Inventory.InventoryMenu>();
                loot.SetContainer(target.Container, GameData.Resources.World.WorldEntityType.Bag);
                await _navigator.PushAndWaitAsync(loot);
            } finally {
                _openingGroundPile = false;
            }
        }

        private void PumpWorldLoop() {
            // *** NOT WHILE A CONVERSATION IS OPEN. *** The original reads the exit request at the
            // bottom of a loop pass (WORLDLP.C:402) and the dialog that wrote it runs INSIDE that
            // pass, so the whole conversation plays first. The travel screen stays visible under a
            // dialog and ticks this every frame, so Finn's first page (which writes Var 17) tore the
            // world down and started chapter 5's scenes under the rest of his conversation.
            if (_busy || _session.DialogsPlaying > 0) {
                return;
            }

            // *** WHAT NO PACK COULD TAKE WAS DROPPED AT THE PARTY'S FEET -- AND NEVER SHOWN. ***
            // A dialog that hands out more than the packs hold cascades the rest onto the ground
            // pile (DialogExecutor.CascadeAround); the original's world loop empties it on its next
            // pass (WORLDLP.C:177, itemuse_ground_pile_open_inv). Nothing here did, so those items
            // were simply gone.
            if (!_openingGroundPile && _session.GroundPile is { Items: { Count: > 0 } }) {
                OpenGroundPileAsync().Forget();

                return;
            }

            // `modalscreen_pending_scene_trans` (MODALSCR.C:51), which the original's world loop
            // calls at the top of every pass while the record is set. A dialog that queued a move
            // with no location screen open — the sewer ladder, a tunnel — is drained here; one
            // queued from INSIDE a location is already gone by now, taken by LocationScenePlayer as
            // its loop unwound.
            //
            // *** THE SCENE HALF RUNS FIRST, AND THE MOVE WAITS FOR IT. *** MODALSCR.C:71-75 is
            // `while (record[7] != 0) townscene_main_loop(...)`, and only line 80 onward moves the
            // party. Krondor's sewer exit (destination 18) names GDS2E, so the original comes up
            // INSIDE the northern-gate scene; draining the world half alone put the party outside
            // it. The scene half is taken HERE rather than inside the loop because this runs every
            // frame — leaving it set until RunAsync got round to it would re-enter every frame.
            if (!_drainingTeleport && _teleport != null
                && _teleport.TryTakeScene(out int sceneNumber, out int sceneLetter)) {
                if (_locations == null) {
                    _logger.LogWarning(
                        "Teleport wants location scene GDS{Scene}/{Letter} and no scene player is "
                        + "wired; the world half will be applied without it.",
                        sceneNumber, sceneLetter);
                } else {
                    RunQueuedLocationAsync(sceneNumber, sceneLetter).Forget();

                    return;
                }
            }

            if (!_drainingTeleport) {
                GameData.Resources.Location.Location where = _teleport?.TakeLocation();
                if (where != null) {
                    TransitionTo(where).Forget();

                    return;
                }
            }

            if (_session.ChapterTransitionPending == 0) {
                return;
            }

            int request = _session.ChapterTransitionPending;
            if (!ChapterTransition.Advances(request)) {
                // The other exit: it is not a chapter change, and leaving the flag set would have
                // this fire again every frame. Nothing else is ported for it yet.
                _session.ChapterTransitionPending = 0;
                _logger.LogInformation(
                    "World loop exit request {Request} is not a chapter advance; cleared.", request);

                return;
            }

            if (ChapterTransition.EndsTheGame(_session.Chapter)) {
                // Chapter 9 finishing is the END OF THE GAME, not chapter 10 — there is no
                // CHAP10.DAT. The original plays the ending and then goes to the main menu
                // (GMAIN.C:745-751); FinishChapter does both.
                _logger.LogInformation("Chapter {Chapter} finished: that is the end of the game.",
                    _session.Chapter);
            }

            FinishChapter(_session.Chapter).Forget();
        }

        /// <summary>
        /// Runs a queued destination's location scene, then lets its own tail apply the move.
        /// </summary>
        /// <remarks>
        /// The latch is what stops the every-frame seam re-entering while the scene is open: the
        /// world half is still in the slot the whole time, and taking it mid-scene would tear the
        /// world down under a location that is still drawing — the case
        /// <c>LocationScenePlayer.ApplyQueuedTeleportAsync</c> is written to avoid.
        /// </remarks>
        private async UniTask RunQueuedLocationAsync(int sceneNumber, int sceneLetter) {
            _drainingTeleport = true;
            try {
                await _locations.RunAsync(sceneNumber, sceneLetter);
            } catch (Exception e) {
                _logger.LogError(e, "Error running queued location GDS{Scene}/{Letter}",
                    sceneNumber, sceneLetter);
            } finally {
                _drainingTeleport = false;
            }
        }

        private async UniTask EnterWorldCoreAsync() {
            // The zone build used to be covered by a fade (TASK-214), cleared by the ResetTo
            // below. The navigator no longer fades, so darkening here would leave the cover opaque
            // with nothing to lift it. If the build's visual jolt is worth covering again, the fade
            // has to be PAIRED here rather than relying on the navigator.
            await _world.BuildAsync();
            // Wire the built camera + movement into the travel screen before it shows, so the first
            // frame/keypress finds them ready.
            TravelScreen.SetWorldCamera(_world.WorldCamera);
            TravelScreen.SetMovement(_world.Movement);
            TravelScreen.SetWorldLoopSeam(PumpWorldLoop);
            // The chapter goal is drawn over the loading map with SkipWait (GMAIN.C:178), so nothing
            // dismissed it: the world taking the screen is what ends it (TASK-496). Cleared BEFORE
            // the swap, not after: ResetTo hides the map first and then awaits the travel screen's
            // show, and in that gap the caption sat over the bare world -- on a chapter-end load for
            // the whole run-up to the closing scenes, where the original shows the world without it
            // (TASK-725).
            _dialogManager.ClearDialog();
            await _navigator.ResetTo(TravelScreen);
        }

        private async UniTask RunExclusive(string name, Func<UniTask> body) {
            if (_busy) {
                _logger.LogWarning("GameFlow busy; ignoring {Flow}.", name);
                return;
            }
            _busy = true;
            try {
                await body();
            } catch (Exception e) {
                _logger.LogError(e, "Error in flow {Flow}", name);
            } finally {
                _busy = false;
            }
        }
    }
}

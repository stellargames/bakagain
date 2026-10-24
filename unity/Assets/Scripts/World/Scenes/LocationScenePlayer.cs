namespace BakAgain.World.Scenes {
    using BakAgain.CutScenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Menu;
    using GameData.Resources.Scene;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// Shows an interactive location — the Unity half of <c>GDS_RunScene</c>'s presentation.
    ///
    /// <para>An awaited script rather than a screen, in the shape of
    /// <see cref="Core.Services.ChapterScenesPlayer"/>. It deliberately does <b>not</b> touch the
    /// navigator: the caller that pushes the cutscene screen is the one that pops it, so there is no
    /// way for a failure in here to strand a pushed screen. When the interaction loop lands it will
    /// own the push/interact/pop as one balanced unit.</para>
    ///
    /// <para><b>A location's picture is its ADS animation, not a background image</b> — the original
    /// loads the scene file and immediately calls its animation player, and the only SCX the scene
    /// loop touches is the dialogue frame. So "showing" a location means playing its entry animation
    /// and holding on the last frame.</para>
    /// </summary>
    public sealed class LocationScenePlayer {
        private readonly ILogger<LocationScenePlayer> _logger;
        private readonly CutscenePresenter _presenter;
        private readonly ICutsceneView _view;
        private readonly GdsSceneLoader _loader;
        private readonly IResourceCache _resources;
        private readonly LocationScreen _screen;
        private readonly GameData.Resources.Location.PendingTeleport _teleport;
        private readonly VContainer.IObjectResolver _resolver;
        private CancellationTokenSource _showing;
        private UniTask? _animation;

        /// <summary>The layout the scene's hotspots are written into.</summary>
        /// <summary>
        /// Covers the world while a location replaces it.
        /// </summary>
        /// <remarks>
        /// <b>A location does NOT go through the screen navigator</b> — it activates its screen
        /// directly — so the fade the navigator applies to every other screen transition would miss
        /// exactly the case the original names first: entering a town or a location. Faded here
        /// instead, which works because this method is already async and can hold the transition
        /// open. TASK-214 settled that the remake fades rather than cuts, and this is one of the
        /// three owners that implement it — the others being ScreenNavigator for ordinary screens
        /// and HotspotService.SwapArenaThroughFade for entering and leaving a fight.
        /// A change between sub-scenes of one location is the exception and CUTS (<see
        /// cref="UI.Navigation.IScreenFade.CutAsync"/>), as the original does (owner, 2026-10-21).
        /// </remarks>
        public UI.Navigation.IScreenFade Fade { get; set; }

        public const string HotspotLayout = "REQ_GDS.DAT";

        /// <summary>The image every location is drawn on top of.</summary>
        /// <remarks>
        /// The scene files never name it — the original loads it once per run and re-blits it before
        /// every replay, so the animations assume it is already there. The <c>.scr</c> spelling at
        /// the original's call site is a red herring: its loader rewrites the last character of the
        /// filename to <c>x</c>, and there is no <c>.SCR</c> in the game data.
        /// </remarks>
        public const string Backdrop = "DIALOG.SCX";

        public LocationScenePlayer(ILogger<LocationScenePlayer> logger, CutscenePresenter presenter,
            ICutsceneView view, GdsSceneLoader loader, IResourceCache resources,
            LocationScreen screen, GameData.Resources.Location.PendingTeleport teleport = null,
            // IGameFlow is resolved lazily: it depends on WorldRuntime, which depends on this, so a
            // constructor dependency would be a DI cycle. Same treatment GameFlow gives its screens.
            VContainer.IObjectResolver resolver = null,
            // Optional so a player built in a test cuts instead of needing a screen to darken.
            UI.Navigation.IScreenFade fade = null) {
            Fade = fade ?? new UI.Navigation.NullScreenFade();
            _logger = logger;
            _presenter = presenter;
            _view = view;
            _loader = loader;
            _resources = resources;
            _screen = screen;
            _teleport = teleport;
            _resolver = resolver;
        }

        /// <summary>
        /// Loads a location and shows it, holding on the entry animation's last frame.
        /// </summary>
        /// <param name="sceneNumber">The scene's own number — not the story chapter.</param>
        /// <param name="sub">Sub-scene, 1-based; 1 is the location's main view.</param>
        /// <returns>The scene that was shown, or null when it could not be.</returns>
        /// <remarks>
        /// The caller must already have the cutscene screen showing — this drives the presenter and
        /// nothing else. The hotspot interaction loop is not here yet; what this establishes is the
        /// load → resolve → play-and-hold path the loop will sit inside.
        /// </remarks>
        /// <summary>
        /// Runs a location until the player leaves it, following sub-scene transitions.
        /// </summary>
        /// <remarks>
        /// <b>A location is a loop over sub-scenes, not one screen.</b> A transition hotspot names
        /// another letter of the same scene number and the loop reloads it; a letter of zero or less
        /// is how an exit is authored, and ends the whole thing. The picture, hotspots and dialogs
        /// are rebuilt per sub-scene because each is its own GDS file.
        /// </remarks>
        /// <summary>
        /// Bumped by every <see cref="RunAsync"/> and by <see cref="Abandon"/>; a loop whose captured
        /// value no longer matches stops and touches nothing.
        /// </summary>
        private int _generation;

        /// <summary>
        /// End whatever location loop is running — what a save load and a return to the menu need,
        /// because the loop is started fire-and-forget and outlives both otherwise.
        /// </summary>
        /// <remarks>
        /// <b>Takes the screen down here rather than leaving it to the loop's `finally`.</b> The loop
        /// is parked in <see cref="WaitForTransitionAsync"/>, which only completes when a hotspot
        /// fires — so on a load it would never unwind, and a stale generation makes it skip the
        /// teardown when it finally does. The first version of this bumped the counter and nothing
        /// else, and the abandoned location's screen stayed up over the newly loaded game: the party
        /// was at the loaded position while GDS1C was still drawn. Same day, one fix later.
        ///
        /// <para><see cref="Hide"/> is idempotent, so calling it here and again from the loop costs
        /// nothing.</para>
        /// </remarks>
        public void Abandon() {
            _generation++;
            IsOpen = false;
            Hide();
        }

        /// <summary>
        /// Whether a location loop is running, its leaving fade and move included — while it is, the
        /// world loop does not run (GameFlow's pump checks this).
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// The location a screen has been raised over, while it is up: what that screen's push hides
        /// and its pop reveals in the travel HUD's place (see <see cref="LocationScreen.RaisedOver"/>).
        /// Null otherwise.
        /// </summary>
        public LocationScreen RaisedOver => _screen != null && _screen.RaisedOver ? _screen : null;

        /// <param name="leaving">Run once the player has left, while the location is still on screen
        /// and before any queued teleport: what the world loop's dispatch pass does after the town
        /// returns (the gate's post key and the step's time, HOTSPOT.C:643-646 and WORLDLP.C:191-198).
        /// Not run for a loop a load or a newer run has abandoned.</param>
        public async UniTask RunAsync(int sceneNumber, int sub, Action leaving = null) {
            // A high byte on the number IS the sub-scene letter, and it overrides the argument —
            // GDS_RunScene does this before anything else. Every Bkgr world trigger in the game names
            // a packed value, so skipping it asks for scenes that do not exist.
            (sceneNumber, sub) = GdsSceneRules.UnpackScene(sceneNumber, sub);

            // *** A LOOP FROM AN ABANDONED GAME MUST NOT STILL BE LISTENING. ***
            // HotspotService starts this with `.Forget()`, so nothing holds it and nothing used to
            // end it except the player walking out. Loading a save while a location was open left
            // the old loop alive: on 2026-09-13 a Silden run (scene 11) survived a load, the party
            // entered town 1, and the first sub-scene transition was taken by the STALE loop —
            // "showing scene 11/3" — so clicking the Fletcher's Post opened GDS11C instead of GDS1C.
            // The screen had already drawn GDS1A, which is what made it read as a wrong-destination
            // bug rather than as a leak.
            int generation = ++_generation;
            IsOpen = true;

            // *** A LOCATION PLAYS ITS OWN SONG AND HANDS THE WORLD'S BACK ON THE WAY OUT. ***
            // GDS_RunScene queries the playing track on entry (TOWNSCN.C:329), plays each sub-scene's
            // song when it has one (368-371, see ShowAsync), and restores the queried track when the
            // loop ends (671). A letter whose Song is 0 keeps whatever is already playing (TASK-512).
            int trackBefore = await PlayTrackAsync(GameData.Resources.Audio.MusicPlayback.QueryOnly);

            int next = sub;
            // *** ONLY ARRIVING FADES; A SUB-SCENE CUTS. *** fadeFlag starts at 1 (TOWNSCN.C:325), is
            // tested at :376 and cleared at :398 after the first scene's fade-in, so every later scene
            // of the visit (and a temple teleport's destination scene) is presented with a plain
            // screen_frame_present (:409): the new picture and its description at once. The owner
            // chose that cut for the remake on 2026-10-21; TASK-214's fades stay for the world.
            bool cut = false;
            bool left = false;
            try {
                while (generation == _generation
                       && !GdsActionDispatch.TransitionLeavesTheLocation(next)) {
                    _pendingTransition = null;
                    GdsScene scene = await ShowAsync(sceneNumber, next, cut);
                    if (scene == null) {
                        return;
                    }
                    cut = true;
                    // TOWNSCN.C:384-392: a hotspot flagged to run on arrival is the action, with no
                    // click (Queen's Row opens on Ivan's conversation). PresentAsync left the
                    // description out for it; the hotspot's own redraw puts it up afterwards.
                    if (_loader.ArrivalHotspot(scene) is int arrival) {
                        _screen?.PrimaryAction(GdsSceneRules.ActionIdFor(arrival));
                    }
                    next = await WaitForTransitionAsync();

                    // A teleport redirects the loop to a DIFFERENT LOCATION rather than to another
                    // letter of this one, so it moves the scene number too. This is what makes a
                    // temple teleport arrive inside the destination temple instead of outside it.
                    if (_teleport != null && _teleport.TryTakeScene(out int number, out int letter)) {
                        sceneNumber = number;
                        next = letter;
                    }
                }
                left = true;
            } finally {
                if (generation == _generation) {
                    // The town has returned into the world loop's dispatch pass, which finishes the
                    // step there and then, with the town still on screen (WORLDLP.C:191-198) — before
                    // the next pass moves any queued teleport (:94-95).
                    try {
                        leaving?.Invoke();
                    } catch (Exception e) {
                        _logger.LogError(e, "LocationScenePlayer: error finishing the visit to scene {Scene}.", sceneNumber);
                    }

                    // *** LEAVING FADES, ONCE, AND A TELEPORT MOVES UNDER IT. *** The loop exit
                    // raises g_nSceneReloadPending (TOWNSCN.C:680). The world loop's next pass moves
                    // a queued teleport first (WORLDLP.C:94-95, modalscreen_pending_scene_trans,
                    // MODALSCR.C:51-92) and only then fades out from the town, repaints the world at
                    // wherever the party now is, and fades in (WORLDLP.C:102-139): one fade, and the
                    // destination is what fades in.
                    // TAKEN BEFORE THE HUD COMES BACK: the restored travel screen pumps the world
                    // loop (GameFlow.PumpWorldLoop), which drains a waiting move by itself, fire-
                    // and-forget. Left in the slot, the move was made by that pump in the middle of
                    // this fade-in, the zone torn down and rebuilt on screen, and the step charged
                    // after it (measured at the Krondor sewer door, TASK-906 item 3).
                    GameData.Resources.Location.Location destination = left ? _teleport?.TakeLocation() : null;
                    await Fade.FadeOutAsync();
                    try {
                        await HideAsync();
                        PlayTrackAsync(trackBefore).Forget();
                        if (destination != null) {
                            await ApplyTeleportAsync(destination);
                        }
                    } finally {
                        await Fade.FadeInAsync();
                        IsOpen = false;
                    }
                }
            }
        }

        /// <summary>
        /// Moves the party to the world destination leaving the location left queued.
        /// </summary>
        /// <remarks>
        /// <b>After the loop, never during it.</b> The original runs ProcessTeleportation from the
        /// world loop once GDS_RunScene has returned — so a destination that also names a scene has
        /// already had its scene half taken by the loop above, and what is left here is only the
        /// move. Doing it mid-loop would tear down the world under a location that is still drawing.
        /// </remarks>
        private async UniTask ApplyTeleportAsync(GameData.Resources.Location.Location destination) {
            if (_resolver == null) {
                return;
            }

            var flow = (Core.Services.IGameFlow)_resolver.Resolve(typeof(Core.Services.IGameFlow));
            await flow.TransitionTo(destination);
        }

        private int? _pendingTransition;

        /// <summary>
        /// Music through the shared player, or nothing when no player is registered — a player built
        /// in a test has no resolver.
        /// </summary>
        private UniTask<int> PlayTrackAsync(int track) {
            BakAgain.Audio.MidiPlaybackManager midi = ResolveOptional<BakAgain.Audio.MidiPlaybackManager>();
            BakAgain.ResourceManagement.IResourceProviderService songs =
                ResolveOptional<BakAgain.ResourceManagement.IResourceProviderService>();
            return midi == null || songs == null
                ? UniTask.FromResult(GameData.Resources.Audio.MusicPlayback.NoTrack)
                : midi.PlayTrackAsync(track, songs, owner: this);
        }

        private T ResolveOptional<T>() where T : class {
            if (_resolver == null) {
                return null;
            }
            try {
                return (T)_resolver.Resolve(typeof(T));
            } catch (Exception) {
                return null;
            }
        }

        private async UniTask<int> WaitForTransitionAsync() {
            await UniTask.WaitUntil(() => _pendingTransition.HasValue);
            return _pendingTransition.Value;
        }

        /// <param name="cut">Change to the scene in a cut rather than through a fade: every scene of a
        /// visit after the first (see RunAsync).</param>
        public async UniTask<GdsScene> ShowAsync(int sceneNumber, int sub, bool cut = false) {
            if (_presenter == null || _view == null || _loader == null) {
                _logger.LogError("LocationScenePlayer: presenter, view or loader not injected; cannot show {Scene}/{Sub}.",
                    sceneNumber, sub);
                return null;
            }

            _logger.LogInformation("LocationScenePlayer: showing scene {Scene}/{Sub}.", sceneNumber, sub);
            GdsScene scene = await _loader.LoadAsync(sceneNumber, sub);
            if (scene == null) {
                _logger.LogError("LocationScenePlayer: scene {Scene}/{Sub} did not resolve.", sceneNumber, sub);
                return null;
            }

            if (scene.Song > 0) {
                PlayTrackAsync(scene.Song).Forget();
            }

            // g_dialog_in_scene: a party speaker stands on the script's slot-5 backdrop (TASK-810).
            var dialogs = ResolveOptional<UI.IDialogManager>();
            if (dialogs != null) {
                dialogs.SceneSpeakerBackdrop = () =>
                    _presenter.TryGetSlotResources(GameData.Resources.Dialog.PartySpeakerBackdrop.Slot,
                        out string image, out string palette)
                        ? (image, palette)
                        : null;
            }

            List<string> tags = await ResolveTagsAsync(scene);
            if (tags.Count == 0) {
                _logger.LogError(
                    "LocationScenePlayer: {Resource} has no scripts for scene {SceneId} (entry {Entry}, idle {Idle}); nothing to show.",
                    scene.AnimationResource, scene.Id, scene.EntryAnimationTag, scene.IdleAnimationTag);
                return null;
            }

            // *** DARK FIRST, THEN EVERYTHING ELSE (TASK-904). *** The original's order is
            // TOWNSCN.C:378-404: fade out, clear, play the idle animation, paint the description
            // bubble WHILE THE PALETTE IS STILL BLACK, then fade in — so the description arrives with
            // the picture and is never seen over the world. (It plays the ENTRY script before its
            // fade-out, :356, but that script only loads — see ResolveTagsAsync — and measured at
            // LaMut nothing of the town shows before the screen goes black.)
            // Starting the animation before this fade let its onHeld paint "In many ways the town
            // looked like..." over the travel view for the whole fade-out, and on a sub-scene put
            // the new picture and its text up a second before fading them out and back in.
            // Everything from here to the fade-in is under the cover, and the finally clears it on
            // every exit, or a failure would leave it black.
            //
            // *** A SUB-SCENE CUTS INSTEAD (owner, 2026-10-21). *** The same build runs under a
            // still of the frame on screen, which is dropped in one frame once the new picture and
            // its description are both up: the original's single screen_frame_present (TOWNSCN.C:409)
            // after painting both. No black frame, and no frame of one without the other.
            if (cut) {
                // Under the still, the page a hotspot left drawn (TASK-914) goes: the new picture
                // covers the whole screen in the original, and a sub-scene with no description
                // (GDS40L, after GDS40K hotspot 0) would otherwise keep the old page over it.
                await Fade.CutAsync(() => {
                    _screen?.ClearDescription();
                    return PresentAsync(scene, tags, sceneNumber, sub);
                });
            } else {
                await Fade.FadeOutAsync();
                try {
                    await PresentAsync(scene, tags, sceneNumber, sub);
                } finally {
                    await Fade.FadeInAsync();
                }
            }

            return scene;
        }

        /// <summary>
        /// Puts a loaded scene on screen behind whatever covers the change: the held picture, the
        /// hotspots and the description, returning once all three are up (or the hold timed out).
        /// </summary>
        private async UniTask PresentAsync(GdsScene scene, List<string> tags, int sceneNumber, int sub) {
            // The hold runs until the location closes, so give it a token this player owns.
            //
            // Cancelling the previous hold lets the PREVIOUS PlayCutsceneTagsAsync resume, and
            // its tail calls view.Hide(). Await it here, before starting the next scene, or that
            // teardown lands after the new sub-scene has drawn and blanks it — which is exactly
            // what moving between sub-scenes did.
            _showing?.Cancel();
            if (_animation.HasValue) {
                await _animation.Value.SuppressCancellationThrow();
                _animation = null;
            }
            _showing?.Dispose();
            _showing = new CancellationTokenSource();

            // *** THE TRAVEL HUD MUST GO DOWN, OR IT PAINTS OVER THE LOCATION. ***
            // A location's picture is drawn by the cutscene view, which is a uGUI Canvas; every
            // screen here is a UI Toolkit panel, and those render ABOVE that canvas whatever
            // their sortingOrder says. The navigator gets this right for an ordinary cutscene by
            // enabling only the stack top (EnsureTopShownAsync), and a location deliberately does
            // NOT go through the navigator — so nothing was taking the HUD down and it covered the
            // town completely. Measured: with the HUD alone deactivated by hand, the location
            // appeared.
            await HideTravelScreenAsync();

            // *** THE HOTSPOTS DO NOT WAIT FOR THE ENTRY ANIMATION. ***
            // The original plays it and then ends the wait itself (FinishDialogWait) before
            // building the menu and polling — it never blocks on the animation running out.
            // Awaiting it here hung forever against the shipped data: the hold that follows the
            // scripts only ends when the location closes, so the location never became clickable
            // and there was no error to say why. What IS awaited below is the moment the scripts
            // reach that hold, which is bounded.
            // Preserve(): a UniTask is single-consumption, and this one is stored to be awaited
            // by the next sub-scene. PlayEntryAnimation swallows its own errors, so nothing is
            // lost by not observing it here.
            var held = new UniTaskCompletionSource();
            _heldAnimationId = scene.IdleAnimationTag;
            _animation = PlayEntryAnimation(scene, tags, () => held.TrySetResult()).Preserve();
            await ShowHotspotsAsync(scene, sceneNumber, sub);

            // What the place says when you arrive goes ON the held picture, so it waits for the
            // picture: every script clears the dialog panel as it ends. After the hotspots, so
            // their build (which clears the stage) cannot wipe the sign off it.
            // The SCENE, not the screen's binding: see LocationScreen.ShowSceneDescriptionAsync.
            // Not when a hotspot runs on arrival (TOWNSCN.C:393): the scene opens on its dialog.
            UniTask described = _loader.ArrivalHotspot(scene) != null
                ? held.Task
                : held.Task.ContinueWith(() => _screen?.ShowSceneDescriptionAsync(scene) ?? UniTask.CompletedTask);
            // ponytail: a fixed bound, so a script that never reaches its hold (or fails) fades
            // in without its description rather than staying black; its text still lands if
            // the hold comes later.
            // NOT _animation in this WhenAny: Preserve() only memoizes a FINISHED task, so a
            // continuation registered here on the still-holding animation made the next
            // sub-scene's await of it throw "Already continuation registered".
            await UniTask.WhenAny(described,
                UniTask.Delay(TimeSpan.FromSeconds(HoldTimeoutSeconds), ignoreTimeScale: true));
        }

        /// <summary>The scene animation the held picture was last drawn with.</summary>
        private int _heldAnimationId;

        /// <summary>
        /// Redraws the held picture with another of the scene's animations: a hotspot's own, played
        /// before its dialog, or the idle one, which puts the scene back afterwards.
        /// </summary>
        /// <remarks>
        /// <b>The keeper leaves the counter while you talk to him.</b> TOWNSCN.C:462 plays the
        /// hotspot's animation channel (<see cref="GdsHotspot.AnimationSceneId"/>) before its dialog,
        /// and the loop tail replays the idle one (:630). TVRN2's "BW-SUMANI GONE" and "BW-DUBAL
        /// GONE" are the Blue Wheel drawn without the man you are talking to, who is the speaker
        /// on the dialog instead (TASK-981). Like the first show, the picture needs the entry script
        /// that loads it before the one that draws it (see ResolveTagsAsync). A no-op when that
        /// picture is already up.
        /// </remarks>
        public async UniTask ShowAnimationAsync(GdsScene scene, int animationId) {
            if (scene == null || _presenter == null || _view == null || _resources == null
                || !GdsSceneRules.HasAnimation(animationId) || animationId == _heldAnimationId) {
                return;
            }
            var ads = await _resources.GetOrLoadAsync<AnimatorResource>($"{scene.AnimationResource}.ADS");
            if (ads == null) {
                return;
            }
            var tags = new List<string>(2);
            AddTag(tags, ads, scene.EntryAnimationTag);
            int loading = tags.Count;
            AddTag(tags, ads, animationId);
            if (tags.Count == loading) {
                return;
            }

            _showing?.Cancel();
            if (_animation.HasValue) {
                await _animation.Value.SuppressCancellationThrow();
                _animation = null;
            }
            _showing?.Dispose();
            _showing = new CancellationTokenSource();
            _heldAnimationId = animationId;
            var held = new UniTaskCompletionSource();
            _animation = PlayEntryAnimation(scene, tags, () => held.TrySetResult()).Preserve();
            await UniTask.WhenAny(held.Task,
                UniTask.Delay(TimeSpan.FromSeconds(HoldTimeoutSeconds), ignoreTimeScale: true));
        }

        /// <summary>How long the fade-in waits for the scripts to reach their hold.</summary>
        private const float HoldTimeoutSeconds = 3f;

        // Drives the entry animation and holds on its last frame. Not awaited by the caller — see
        // ShowAsync — so its errors are logged here rather than propagating.
        private async UniTask PlayEntryAnimation(GdsScene scene, List<string> tags, Action onHeld) {
            try {
                // holdUntil = this player's token: the picture is held WITHOUT an input layer, so
                // the hotspot overlay owns the clicks. The default hold pushes an Exclusive layer
                // (right for a cutscene, whose only interaction is skip) and would swallow every
                // click on the location.
                await _presenter.PlayCutsceneTagsAsync(scene.AnimationResource, _view,
                    tags, holdRenderingAfter: true, holdUntil: _showing.Token, backdrop: Backdrop,
                    onHeld: onHeld);
            } catch (Exception e) {
                _logger.LogError(e, "LocationScenePlayer: error animating scene {SceneId}.", scene.Id);
            }
        }

        /// <summary>
        /// Builds the scene's live hotspots into the click overlay above the held frame.
        /// </summary>
        /// <remarks>
        /// The visibility pass belongs to <see cref="GdsSceneLoader.VisibleHotspots"/> and is not
        /// repeated here; this only converts what it returns. Runs under the fade cover
        /// (<see cref="ShowAsync"/>), so nothing is clickable before the location is on screen.
        /// </remarks>
        private async UniTask ShowHotspotsAsync(GdsScene scene, int sceneNumber, int sub) {
            if (_screen == null) {
                return;
            }

            var frame = await _resources.GetOrLoadAsync<UserInterface>(HotspotLayout);
            if (frame == null) {
                _logger.LogError("LocationScenePlayer: {Layout} did not load; {Scene} has no hotspots.",
                    HotspotLayout, scene.Id);
                return;
            }

            UiElement[] elements = _loader.VisibleHotspots(scene)
                .Select(live => GdsSceneMenu.ElementFor(live.Index, live.Hotspot, frame))
                .ToArray();

            _screen.Bind(scene, sceneNumber, sub, letter => _pendingTransition = letter);
            _screen.gameObject.SetActive(true);

            // Activating the screen only STARTS the loader's async REQ load; it is not finished when
            // SetActive returns. Setting the entries straight away finds no layout and silently
            // builds nothing, so wait for the layout first.
            var ui = _screen.GetComponent<ResourceManagement.Loaders.UserInterfaceLoader>();
            await UniTask.WaitUntil(() => ui.IsBuilt || !_screen.gameObject.activeInHierarchy);
            if (!ui.IsBuilt) {
                _logger.LogError("LocationScenePlayer: {Layout} never built; {Scene} has no hotspots.",
                    HotspotLayout, scene.Id);
                return;
            }

            await ui.SetMenuEntries(elements);
            _logger.LogInformation("LocationScenePlayer: {Scene} showing {Count} hotspots.",
                scene.Id, elements.Length);
        }

        /// <summary>
        /// The travel HUD, hidden while a location is up and restored when it closes.
        /// </summary>
        /// <remarks>
        /// Resolved lazily through the container for the same reason IGameFlow is: taking it as a
        /// constructor dependency would close a DI cycle. Null when there is no HUD to hide (tests),
        /// which is why every use is guarded.
        /// </remarks>
        private UI.InGame.InGameScreen TravelScreen() {
            if (_resolver == null) {
                return null;
            }
            try {
                return (UI.InGame.InGameScreen)_resolver.Resolve(typeof(UI.InGame.InGameScreen));
            } catch (System.Exception) {
                return null;
            }
        }

        /// <remarks>
        /// <b>Hide unconditionally, and key the RESTORE on <c>IsVisible</c> rather than the hide.</b>
        /// This used to skip the call entirely when <c>IsVisible</c> was false, which cannot take
        /// down a HUD whose GameObject is still active — and those two disagree for real:
        /// <c>InGameScreen.HideAsync</c> clears <c>_visible</c> BEFORE awaiting its fade and only
        /// calls <c>SetActive(false)</c> after it, so there is a window where the HUD is on screen
        /// and <c>IsVisible</c> reads false. Measured 2026-09-23 in Romney: <c>active=True
        /// IsVisible=False</c>, and in that state the location could not hide it, so REQ_MAIN's
        /// button cluster painted over the description exactly as TASK-620's screenshots show
        /// (LocationScreen is sortingOrder -3 against the HUD's -2).
        ///
        /// <para><c>HideAsync</c> is a no-op on an already-hidden screen, so calling it always costs
        /// nothing; <c>_hidTravelScreen</c> still records only a HUD this player genuinely took
        /// down, so <see cref="RestoreTravelScreenAsync"/> does not raise one it never hid.</para>
        /// </remarks>
        private async UniTask HideTravelScreenAsync() {
            UI.InGame.InGameScreen travel = TravelScreen();
            if (travel == null) {
                return;
            }
            // *** LATCH IT, NEVER CLEAR IT HERE. *** This method runs on EVERY sub-scene show, not
            // just the first: the RunAsync loop calls ShowAsync once per letter, so walking from
            // Romney into the Black Sheep Tavern hides an already-hidden HUD. A plain assignment
            // then set the flag back to false on that second pass and the final Hide() restored
            // nothing — the party came out of the location with no compass, no buttons and no way
            // to camp. Only RestoreTravelScreenAsync clears it, when it actually puts the HUD back.
            _hidTravelScreen |= travel.IsVisible;
            await travel.HideAsync();
        }

        /// <summary>
        /// Puts the HUD back, and ONLY if this player was the one that took it down.
        /// </summary>
        /// <remarks>
        /// Showing it unconditionally would raise the travel screen over whatever replaced the
        /// location — a chapter transition or a zone crossing can close a location by moving the
        /// party somewhere else entirely, and the screen it left up is not ours to overrule.
        /// </remarks>
        private async UniTask RestoreTravelScreenAsync() {
            if (!_hidTravelScreen) {
                return;
            }
            _hidTravelScreen = false;
            UI.InGame.InGameScreen travel = TravelScreen();
            if (travel != null) {
                await travel.ShowAsync();
            }
        }

        private bool _hidTravelScreen;

        /// <summary>Takes the location down: ends the held picture and hides the click overlay.</summary>
        // Fire-and-forget: Hide() is the synchronous teardown path and cannot await. The guard
        // inside RestoreTravelScreenAsync means a second call is a no-op rather than a second Show.
        public void Hide() => HideAsync().Forget();

        private UniTask HideAsync() {
            var dialogs = ResolveOptional<UI.IDialogManager>();
            if (dialogs != null) {
                dialogs.SceneSpeakerBackdrop = null;
            }
            _showing?.Cancel();
            _showing?.Dispose();
            _showing = null;
            _animation = null;
            if (_screen != null) {
                // Before the GameObject goes: the description panel lives on the dialog overlay's
                // own UIDocument, so deactivating this screen does not take it with it.
                _screen.ClearDescription();
                _screen.gameObject.SetActive(false);
            }
            return RestoreTravelScreenAsync();
        }

        /// <summary>
        /// The animation scripts that make a location appear, in the order they must run.
        /// </summary>
        /// <remarks>
        /// <b>A location's picture needs BOTH its animations, and neither one alone shows anything.</b>
        /// The entry script only loads: it selects palette and image slot 0 and reads the town's
        /// <c>.PAL</c> and <c>.bmp</c> into them, then ends without drawing. The idle script is what
        /// draws — a single <c>DrawImage</c> of slot 0 plus the frame border. That is exactly why
        /// every town shares idle 13 ("DISPLAY"): the load is per-town and the draw is generic.
        ///
        /// <para>Playing only the entry script leaves the screen untouched and reports no error,
        /// which is precisely what it did. The original runs the same pair in the same order
        /// (<c>GDS_RunScene</c>: entry animation, then the idle/steady animation, then build the
        /// hotspot menu and poll).</para>
        ///
        /// <para>The ids are ADS script <i>ids</i> and the presenter matches tag <i>strings</i>, so
        /// each is translated by <see cref="GdsSceneRules.AnimationTagFor"/>; an id of zero means the
        /// scene has no such script and is skipped rather than looked up.</para>
        /// </remarks>
        private async UniTask<List<string>> ResolveTagsAsync(GdsScene scene) {
            var tags = new List<string>(2);
            if (!GdsSceneRules.HasAnimation(scene.EntryAnimationTag)
                && !GdsSceneRules.HasAnimation(scene.IdleAnimationTag)) {
                return tags;
            }

            // The same load the presenter does internally; going through the cache rather than
            // widening CutscenePresenter for one caller.
            var ads = await _resources.GetOrLoadAsync<AnimatorResource>($"{scene.AnimationResource}.ADS");
            if (ads == null) {
                return tags;
            }

            AddTag(tags, ads, scene.EntryAnimationTag);
            AddTag(tags, ads, scene.IdleAnimationTag);
            return tags;
        }

        private void AddTag(List<string> tags, AnimatorResource ads, int animationId) {
            if (!GdsSceneRules.HasAnimation(animationId)) {
                return;
            }
            string tag = GdsSceneRules.AnimationTagFor(ads.Animations, animationId);
            if (tag == null) {
                _logger.LogWarning("LocationScenePlayer: {Resource} has no script with id {Id}.",
                    ads.Id, animationId);
                return;
            }
            tags.Add(tag);
        }
    }
}

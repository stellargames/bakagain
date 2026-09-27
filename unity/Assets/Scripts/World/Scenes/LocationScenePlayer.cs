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
        private readonly ICutscenePresenter _presenter;
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

        public LocationScenePlayer(ILogger<LocationScenePlayer> logger, ICutscenePresenter presenter,
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
            Hide();
        }

        public async UniTask RunAsync(int sceneNumber, int sub) {
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

            // *** A LOCATION PLAYS ITS OWN SONG AND HANDS THE WORLD'S BACK ON THE WAY OUT. ***
            // GDS_RunScene queries the playing track on entry (TOWNSCN.C:329), plays each sub-scene's
            // song when it has one (368-371, see ShowAsync), and restores the queried track when the
            // loop ends (671). A letter whose Song is 0 keeps whatever is already playing (TASK-512).
            int trackBefore = await PlayTrackAsync(GameData.Resources.Audio.MusicPlayback.QueryOnly);

            int next = sub;
            try {
                while (generation == _generation
                       && !GdsActionDispatch.TransitionLeavesTheLocation(next)) {
                    _pendingTransition = null;
                    GdsScene scene = await ShowAsync(sceneNumber, next);
                    if (scene == null) {
                        return;
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
            } finally {
                if (generation == _generation) {
                    Hide();
                    PlayTrackAsync(trackBefore).Forget();
                }
            }

            if (generation != _generation) {
                return;   // a newer run owns the screen now
            }

            await ApplyQueuedTeleportAsync();
        }

        /// <summary>
        /// Moves the party, if leaving the location left a world destination queued.
        /// </summary>
        /// <remarks>
        /// <b>After the loop, never during it.</b> The original runs ProcessTeleportation from the
        /// world loop once GDS_RunScene has returned — so a destination that also names a scene has
        /// already had its scene half taken by the loop above, and what is left here is only the
        /// move. Doing it mid-loop would tear down the world under a location that is still drawing.
        /// </remarks>
        private async UniTask ApplyQueuedTeleportAsync() {
            GameData.Resources.Location.Location destination = _teleport?.TakeLocation();
            if (destination == null || _resolver == null) {
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

        public async UniTask<GdsScene> ShowAsync(int sceneNumber, int sub) {
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

            List<string> tags = await ResolveTagsAsync(scene);
            if (tags.Count == 0) {
                _logger.LogError(
                    "LocationScenePlayer: {Resource} has no scripts for scene {SceneId} (entry {Entry}, idle {Idle}); nothing to show.",
                    scene.AnimationResource, scene.Id, scene.EntryAnimationTag, scene.IdleAnimationTag);
                return null;
            }

            // The hold runs until the location closes, so give it a token this player owns.
            //
            // Cancelling the previous hold lets the PREVIOUS PlayCutsceneTagsAsync resume, and its
            // tail calls view.Hide(). Await it here, before starting the next scene, or that
            // teardown lands after the new sub-scene has drawn and blanks it — which is exactly what
            // moving between sub-scenes did.
            _showing?.Cancel();
            if (_animation.HasValue) {
                await _animation.Value.SuppressCancellationThrow();
                _animation = null;
            }
            _showing?.Dispose();
            _showing = new CancellationTokenSource();

            // *** THE HOTSPOTS DO NOT WAIT FOR THE ENTRY ANIMATION. ***
            // The original plays it and then ends the wait itself (FinishDialogWait) before building
            // the menu and polling — it never blocks on the animation running out. Awaiting it here
            // hung forever against the shipped data: the entry script does not terminate on its own,
            // so the location never became clickable and there was no error to say why.
            // Preserve(): a UniTask is single-consumption, and this one is stored to be awaited by
            // the next sub-scene. PlayEntryAnimation swallows its own errors, so nothing is lost by
            // not observing it here.
            _animation = PlayEntryAnimation(scene, tags).Preserve();
            await ShowHotspotsAsync(scene, sceneNumber, sub);
            return scene;
        }

        // Drives the entry animation and holds on its last frame. Not awaited by the caller — see
        // ShowAsync — so its errors are logged here rather than propagating.
        private async UniTask PlayEntryAnimation(GdsScene scene, List<string> tags) {
            try {
                // holdUntil = this player's token: the picture is held WITHOUT an input layer, so
                // the hotspot overlay owns the clicks. The default hold pushes an Exclusive layer
                // (right for a cutscene, whose only interaction is skip) and would swallow every
                // click on the location.
                await _presenter.PlayCutsceneTagsAsync(scene.AnimationResource, _view,
                    tags, holdRenderingAfter: true, holdUntil: _showing.Token, backdrop: Backdrop,
                    // What the place says when you arrive goes ON the held picture, so it waits
                    // for the picture: every script clears the dialog panel as it ends.
                    // The SCENE, not the screen's binding: this fires before ShowHotspotsAsync
                    // has bound it. See LocationScreen.ShowSceneDescriptionAsync.
                    onHeld: () => _screen?.ShowSceneDescriptionAsync(scene).Forget());
            } catch (Exception e) {
                _logger.LogError(e, "LocationScenePlayer: error animating scene {SceneId}.", scene.Id);
            }
        }

        /// <summary>
        /// Builds the scene's live hotspots into the click overlay above the held frame.
        /// </summary>
        /// <remarks>
        /// The visibility pass belongs to <see cref="GdsSceneLoader.VisibleHotspots"/> and is not
        /// repeated here; this only converts what it returns. The hotspots go up <b>after</b> the
        /// entry animation, so nothing is clickable while the arrival is still playing.
        /// </remarks>
        private async UniTask ShowHotspotsAsync(GdsScene scene, int sceneNumber, int sub) {
            if (_screen == null) {
                return;
            }

            var frame = await _resources.GetOrLoadAsync<UserInterface>(HotspotLayout);
            if (frame == null) {
                // No fade to clear: this exit is BEFORE the fade-out below.
                _logger.LogError("LocationScenePlayer: {Layout} did not load; {Scene} has no hotspots.",
                    HotspotLayout, scene.Id);
                return;
            }

            UiElement[] elements = _loader.VisibleHotspots(scene)
                .Select(live => GdsSceneMenu.ElementFor(live.Index, live.Hotspot, frame))
                .ToArray();

            // Darken BEFORE the world is replaced, in the order the original fades: out, repaint,
            // in. The fade-in waits until the hotspots are on the screen, at the end of this method.
            await Fade.FadeOutAsync();
            // *** THE TRAVEL HUD MUST GO DOWN, OR IT PAINTS OVER THE LOCATION. ***
            // A location's picture is drawn by the cutscene view, which is a uGUI Canvas; every
            // screen here is a UI Toolkit panel, and those render ABOVE that canvas whatever their
            // sortingOrder says. The navigator gets this right for an ordinary cutscene by enabling
            // only the stack top (EnsureTopShownAsync), and a location deliberately does NOT go
            // through the navigator — so nothing was taking the HUD down and it covered the town
            // completely. Measured: with the HUD alone deactivated by hand, the location appeared.
            await HideTravelScreenAsync();
            _screen.Bind(scene, sceneNumber, sub, letter => _pendingTransition = letter);
            _screen.gameObject.SetActive(true);

            // Activating the screen only STARTS the loader's async REQ load; it is not finished when
            // SetActive returns. Setting the entries straight away finds no layout and silently
            // builds nothing, so wait for the layout first.
            var ui = _screen.GetComponent<ResourceManagement.Loaders.UserInterfaceLoader>();
            await UniTask.WaitUntil(() => ui.IsBuilt || !_screen.gameObject.activeInHierarchy);
            if (!ui.IsBuilt) {
                // *** CLEAR THE COVER ON EVERY EXIT AFTER THE FADE-OUT. *** A location that fails to
                // build its hotspots would otherwise leave the screen black with no way back.
                await Fade.FadeInAsync();
                _logger.LogError("LocationScenePlayer: {Layout} never built; {Scene} has no hotspots.",
                    HotspotLayout, scene.Id);
                return;
            }


            await ui.SetMenuEntries(elements);
            // The build above CLEARS the stage, and the sign lives on it — see
            // LocationScreen.RestoreSign for why that is a race and not an ordering bug.
            _screen?.RestoreSign();
            await Fade.FadeInAsync();
            _logger.LogInformation("LocationScenePlayer: {Scene} showing {Count} hotspots.",
                scene.Id, elements.Length);
        }

        /// <summary>
        /// The travel HUD, hidden while a location is up and restored when it closes.
        /// </summary>
        /// <remarks>
        /// Resolved lazily through the container for the same reason IGameFlow is: taking it as a
        /// constructor dependency would close a DI cycle. Null when there is no HUD to hide (tests,
        /// or the NullInGameScreen before the prefab exists), which is why every use is guarded.
        /// </remarks>
        private UI.InGame.IInGameScreen TravelScreen() {
            if (_resolver == null) {
                return null;
            }
            try {
                return (UI.InGame.IInGameScreen)_resolver.Resolve(typeof(UI.InGame.IInGameScreen));
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
            UI.InGame.IInGameScreen travel = TravelScreen();
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
            UI.InGame.IInGameScreen travel = TravelScreen();
            if (travel != null) {
                await travel.ShowAsync();
            }
        }

        private bool _hidTravelScreen;

        /// <summary>Takes the location down: ends the held picture and hides the click overlay.</summary>
        public void Hide() {
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
            // Fire-and-forget: Hide() is the synchronous teardown path and cannot await. The guard
            // inside means a second call is a no-op rather than a second Show.
            RestoreTravelScreenAsync().Forget();
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
            // widening ICutscenePresenter for one caller.
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

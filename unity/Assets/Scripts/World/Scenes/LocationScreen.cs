namespace BakAgain.World.Scenes {
    using BakAgain.UI;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Character;
    using GameData.Resources.Data;
    using GameData.Resources.Dialog;
    using GameData.Resources.Scene;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The clickable half of an interactive location: turns a click on a hotspot into its dialog and
    /// its action — the click arms of <c>GDS_RunScene</c>.
    ///
    /// <para>Deliberately not a screen. The picture is the cutscene view's held animation frame and
    /// this is a hotspot overlay above it, in the shape <see cref="UI.DialogManager"/> already uses to
    /// put UI Toolkit over a cutscene: canonical 1600x1200 px through the shared panel scaler, which
    /// lands on the same centred 4:3 box the cutscene occupies.</para>
    /// </summary>
    [RequireComponent(typeof(ResourceManagement.Loaders.UserInterfaceLoader))]
    public class LocationScreen : MonoBehaviour, IActionHandler {
        private ILogger _logger;
        private IDialogManager _dialogs;
        private UI.InputCore.IPointer _pointer;
        private Core.GameSession _session;
        private GameData.Resources.Location.PendingTeleport _teleport;
        private UI.Teleport.TeleportScreen _teleportScreen;
        private UI.Character.TempleHealScreen _healScreen;
        private InnService _inn;
        private CutScenes.IResourceCache _resources;
        private UI.Inventory.InventoryMenu _inventoryMenu;
        private UI.Navigation.IScreenNavigator _navigator;
        private VContainer.IObjectResolver _resolver;

        /// <summary>
        /// The travel HUD, which the navigator re-reveals when a raised screen pops.
        /// </summary>
        /// <remarks>
        /// <b>Resolved lazily, and it has to be</b> — exactly as
        /// <see cref="LocationScenePlayer"/> resolves it, and for the same reason its note gives:
        /// taking `InGameScreen` as a constructor dependency closes a DI cycle, because the HUD
        /// depends on the location player which depends on this screen. Tried it as a constructor
        /// parameter on 2026-09-12 and VContainer refused the whole container with
        /// "Circular dependency detected!", so the game did not boot at all.
        ///
        /// <para>Null when there is no HUD (tests), which is why the use is guarded.</para>
        /// </remarks>
        /// <summary>A service from the container, or null without one (tests) or when it fails.</summary>
        private T ResolveOptional<T>() where T : class {
            if (_resolver == null) {
                return null;
            }
            try {
                return (T)_resolver.Resolve(typeof(T));
            } catch (System.Exception) {
                return null;
            }
        }

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
        private GdsScene _scene;
        private Action<int> _onTransition;
        private readonly Dictionary<int, int> _visits = new();

        [VContainer.Inject]
        public void Construct(IDialogManager dialogs, UI.InputCore.IPointer pointer,
            Core.GameSession session, GameData.Resources.Location.PendingTeleport teleport,
            UI.Teleport.TeleportScreen teleportScreen,
            UI.Character.TempleHealScreen healScreen, InnService inn,
            CutScenes.IResourceCache resources, UI.Inventory.InventoryMenu inventoryMenu,
            UI.Navigation.IScreenNavigator navigator,
            VContainer.IObjectResolver resolver = null) {
            _dialogs = dialogs;
            _pointer = pointer;
            _session = session;
            _teleport = teleport;
            _teleportScreen = teleportScreen;
            _healScreen = healScreen;
            _inn = inn;
            _resources = resources;
            _inventoryMenu = inventoryMenu;
            _navigator = navigator;
            // Optional: a bare harness has no container to resolve through. See TravelScreen().
            _resolver = resolver;
            _logger = Core.LogManager.LoggerFactory.CreateLogger<LocationScreen>();
        }

        /// <summary>The scene whose hotspots are currently built into this screen.</summary>
        /// <param name="onTransition">
        /// Called with the sub-scene letter an action wants to move to; zero or less means leave the
        /// location. The screen does not load scenes itself — the player owns that loop.
        /// </param>
        public void Bind(GdsScene scene, int sceneNumber, int sceneLetter,
            Action<int> onTransition = null) {
            _scene = scene;
            _sceneNumber = sceneNumber;
            // Examine is right-click-only (see SecondaryAction): on touch it is a long-press.
            if (GetComponent<ResourceManagement.Loaders.UserInterfaceLoader>() is { } ui) {
                ui.TouchLongPressIsSecondary = true;
            }
            _sceneLetter = sceneLetter;
            _onTransition = onTransition;
            // Visit counts are per run of a scene: gds_loadSceneFile zeroes the counter on load, so
            // the value the file ships is scratch and re-entering starts from zero again.
            _visits.Clear();
            MarkSceneVisited();
            RestockBardingFund();
        }

        /// <summary>wFlags bit that says this scene records a visit — TOWNSCN.C:364.</summary>
        private const int SceneVisitedGateBit = 0x80;

        /// <summary>The low bits of wFlags carrying the town index — TOWNSCN.C:366.</summary>
        private const int SceneVisitedIndexMask = 0x7f;

        /// <summary>
        /// Record that the party has been here — <c>TOWNSCN.C</c>:364-367, the entry block that also
        /// starts the scene's music:
        /// <code>
        /// if ((g_pCurrentTownScene->wFlags &amp; 0x80) != 0)
        ///     gstate_event_write(TOWN_VISITED(g_pCurrentTownScene->wFlags &amp; 0x7f), 1);
        /// </code>
        /// with <c>#define TOWN_VISITED(idx) ((idx) + 6480)</c> (GSTATE.H:79).
        /// </summary>
        /// <remarks>
        /// <b>*** THE 0x80 BIT IS A GATE, NOT PART OF THE INDEX. ***</b> The temples carry 129-138
        /// (0x81-0x8A), which records towns 1-10; LaMut's street carries 1 and the Hawk's Hollow
        /// smith 0, and those scenes record nothing at all. Writing the word unmasked would set
        /// flags 129-138 — a band that belongs to something else entirely.
        ///
        /// <para><b>Nothing consumed this field.</b> <c>GdsSceneExtractor</c> fills
        /// <see cref="GdsScene.EntryFlagWord"/> and no reader existed, so every temple stayed
        /// unvisited for ever: <c>TeleportScreen.IsVisited</c> reads
        /// <see cref="GameData.Resources.Location.TeleportMenu.VisitedFlagFor"/> and always got 0,
        /// so the rift map refused with <c>NoOtherTemplesDialog</c> without opening and the whole
        /// teleport subsystem was unreachable. Measured inside GDS70B (temple 2) with 6481-6492 all
        /// clear after two temples had been walked into. TASK-561.</para>
        ///
        /// <para>Towns and temples share the one flag table — <c>MODALSCR.C</c>:183/222 builds the
        /// rift map's pins from the same <c>TOWN_VISITED</c> reads — which is why this is written
        /// for every scene that carries the bit, not only for temples.</para>
        /// </remarks>
        private void MarkSceneVisited() {
            int flags = _scene?.EntryFlagWord ?? 0;
            if ((flags & SceneVisitedGateBit) == 0 || _session == null) {
                return;
            }

            _session.SetGlobalFlag(
                GameData.Resources.Location.TeleportMenu.VisitedFlagFor(flags & SceneVisitedIndexMask),
                true);
        }

        /// <summary>
        /// Refills the scene container's barding fund once per chapter, as the scene loads.
        /// </summary>
        /// <remarks>
        /// TOWNSCN.C:146-161, <c>townscene_load</c>'s tail: without it a tavern played dry stayed dry
        /// for the rest of the game (TASK-527). Flushed through <c>Dirty</c>, the engine's
        /// <c>needsFlush</c>, so the refill and its chapter stamp reach the save.
        /// </remarks>
        private void RestockBardingFund() {
            GameData.Resources.Inventory.RuntimeContainer container = LocationContainer();
            SaveGameContainerShopData shop = container?.Shop;
            if (shop == null || _session == null) {
                return;
            }
            SaveGameContainerShopData restocked = shop.WithBardingFundRestockedFor(_session.Chapter);
            if (!ReferenceEquals(restocked, shop)) {
                container.Shop = restocked;
                container.Dirty = true;
            }
        }

        /// <summary>Which scene this is, which is also where its container is filed.</summary>
        private int _sceneNumber;

        /// <inheritdoc cref="_sceneNumber"/>
        private int _sceneLetter;

        /// <summary>
        /// Shows the location's own description, and the sign over it.
        /// </summary>
        /// <remarks>
        /// <b>This is where the signs are.</b> 109 of the shipped scenes open with a
        /// <c>#Name#</c> block — "Three Hillmen Pawn", "Nia's Goods" — and NONE of the hotspot
        /// examines do, so a port that split the name off an examine would have written code no
        /// shipped data reaches. It belongs to the scene's own dialog, which is what a location
        /// says when you arrive.
        ///
        /// <para>The description STAYS UP while the location does, rather than being dismissed like
        /// an examine: the original re-renders it after every action.</para>
        /// </remarks>
        /// <param name="scene">
        /// The scene to describe, for a caller that has it before this screen has been bound to it.
        /// Defaults to the bound one.
        /// </param>
        public async UniTask ShowSceneDescriptionAsync(GdsScene scene = null) {
            // *** THE ENTRY ANIMATION REACHES ITS HOLD BEFORE Bind RUNS. ***
            // LocationScenePlayer.ShowAsync starts PlayEntryAnimation and only then awaits
            // ShowHotspotsAsync, which loads a layout, fades twice and takes the travel HUD down
            // before it binds this screen. The animation's onHeld fires in that gap, so reading
            // _scene there described the location the party had just LEFT: arriving in Romney
            // printed LaMut's "All who visit LaMut are equal" over Romney's market square
            // (measured 2026-09-09). The caller that has the scene passes it.
            scene ??= _scene;
            if (_dialogs == null || (scene?.SceneDialogId ?? 0) == 0) {
                return;
            }
            DialogPlay play = await _dialogs.ResolveById(scene.SceneDialogId);
            DialogEntry entry = play?.Entry;
            if (entry == null) {
                return;
            }

            (string sign, string description) = GdsSceneInteraction.SplitExamineText(entry.Text);
            // A copy, because the dialog loader caches entries: writing the stripped text back onto
            // the shared one would leave the sign missing the second time this shop is entered, and
            // nothing would say why.
            await _dialogs.DisplayEntry(sign == null
                ? play
                : new DialogPlay(entry.WithText(description), play.Slots, play.Context, play.Dialog, play.Pushed));
            // The original paints the description into the scene (TOWNSCN.C:216): the hotspots under
            // it stay live. GDS6A's exit covers the whole text strip, and a pickable panel left the
            // party unable to leave Romney at the start of chapter 3.
            _dialogs.LetClicksThroughPanel();
            ShowSign(sign, sign == null ? null : await _dialogs.ResolvePaletteAsync());
        }

        /// <summary>
        /// Take the location's description panel down.
        /// </summary>
        /// <remarks>
        /// <b>The description outlives the location otherwise.</b> It is shown through
        /// <see cref="IDialogManager.DisplayEntry"/>, which returns as soon as the panel is up and
        /// leaves it there until something clears it — that is what "stays up while the location
        /// does" means in <see cref="ShowSceneDescriptionAsync"/>. Nothing did the clearing, so
        /// walking out of LaMut left "Undoubtedly the newest tavern in town…" painted across the
        /// travel HUD, over the party portraits and the button strip, for the rest of the session.
        /// Measured 2026-09-09: after leaving, the input stack top was <c>InGameScreen</c> and the
        /// manager's panel was still present.
        /// </remarks>
        public void ClearDescription() => _dialogs?.ClearDialog();

        /// <summary>
        /// Left click — run the hotspot's action.
        /// </summary>
        /// <remarks>
        /// The hotspot's own dialog is shown first unless the data says otherwise (see
        /// <see cref="GdsSceneInteraction.ShowsActionDialogFirst"/>), and the dialog's result can
        /// then override which action runs — <see cref="GdsSceneRules.OutcomeFor"/>.
        /// </remarks>
        public void PrimaryAction(int menuEntryActionId) {
            _ = Act(menuEntryActionId);
        }

        /// <summary>
        /// Right click — describe the hotspot.
        /// </summary>
        /// <remarks>
        /// Not a convenience: examine is right-click-only in the original, so this is the one path to
        /// every description in the game.
        /// </remarks>
        public async Awaitable SecondaryAction(int menuEntryActionId) {
            GdsHotspot hotspot = HotspotFor(menuEntryActionId);
            if (!GdsSceneInteraction.HasExamine(hotspot)) {
                // Faithful: a hotspot with no examine dialog says nothing at all.
                return;
            }

            await Examine(hotspot).AsTask();
        }

        /// <summary>
        /// Shows a hotspot's description in whichever of the two presentations its entry asks for.
        /// </summary>
        /// <remarks>
        /// <b>The choice is per entry, not per screen.</b> A type-6 (<c>PlainFullScreen</c>) entry or
        /// one with branches opens the dialog window; everything else is drawn over the location
        /// while the picture stays up. Sending every description to the window is what left the
        /// location blank with unboxed text on it.
        /// </remarks>
        private async UniTask Examine(GdsHotspot hotspot) {
            // TOWNSCN.C:426-437: the right-click arm loads the scene's repair mask into Var 18 BEFORE
            // the examine dialog, as the primary arm does at :475. Six menders' 1800039 branch on it.
            if (GdsSceneInteraction.ExaminePublishesRepairCategories) {
                PublishRepairCategories();
            }
            DialogPlay play = await _dialogs.ResolveById(hotspot.ExamineDialogId);
            DialogEntry entry = play?.Entry;
            if (entry == null) {
                return;
            }

            var style = GdsSceneInteraction.ExamineStyleFor((int)entry.DialogType, entry.Branches?.Count ?? 0);
            if (style == GdsSceneInteraction.ExamineStyle.DialogWindow) {
                await _dialogs.ShowEntry(play);
                return;
            }

            // In-scene: render without blocking so the held picture stays visible underneath, then
            // wait the way the original does — see DismissAsync.
            await _dialogs.DisplayEntry(play);
            await DismissAsync();
            _dialogs.ClearDialog();
        }

        /// <summary>
        /// Puts the establishment's name on its sign above the description.
        /// </summary>
        /// <remarks>
        /// The original draws a pill-shaped bubble for it — the same one a keyword dialog puts its
        /// "asked about" prompt in. Rendered as its own element over the location so it comes down
        /// with the description rather than living in the dialog's text.
        /// </remarks>
        private void ShowSign(string sign, Color[] palette = null) {
            ClearSign();
            if (string.IsNullOrEmpty(sign)) {
                return;
            }
            _sign = sign;
            _signPalette = palette ?? _signPalette;
            VisualElement stage = Stage();
            if (stage == null) {
                return;
            }
            // dialog_draw_speech_bubble — the speaker's name pill (TOWNSCN.C:213).
            var holder = new VisualElement {
                name = SignName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = Length.Percent(50),
                    top = GdsSceneInteraction.SignTop,
                    translate = new StyleTranslate(new Translate(Length.Percent(-50), 0f)),
                },
            };
            if (_signPalette != null) {
                holder.Add(BakAgain.UI.DialogPanelBuilder.BuildNamePill(sign, null, _signPalette));
            } else {
                var label = new Label(sign) {
                    pickingMode = PickingMode.Ignore,
                    style = {
                        paddingLeft = GdsSceneInteraction.SignPadding,
                        paddingRight = GdsSceneInteraction.SignPadding,
                        backgroundColor = SignFill,
                        color = SignInk,
                    },
                };
                BakAgain.UI.GameFontText.Apply(label);
                holder.Add(label);
            }
            foreach (VisualElement child in holder.Query<VisualElement>().ToList()) {
                child.pickingMode = PickingMode.Ignore;
            }
            stage.Add(holder);
        }

        private Color[] _signPalette;

        private void ClearSign() {
            _sign = null;
            Stage()?.Q(SignName)?.RemoveFromHierarchy();
        }

        /// <summary>
        /// Puts the sign back after the REQ loader has rebuilt the stage.
        /// </summary>
        /// <remarks>
        /// <b>The loader clears the stage wholesale</b> (<c>UserInterfaceLoader</c>: <c>stage.Clear()</c>
        /// before it builds the hotspot widgets), and the sign is a child of that same stage. Which
        /// of the two runs last is a race: the description — and with it the sign — is raised from
        /// the entry animation's <c>onHeld</c>, while the hotspots are built by
        /// <c>LocationScenePlayer.ShowHotspotsAsync</c> on a path that awaits a layout load and two
        /// fades. On a cold first entry that load is slow and the sign wins; on every later
        /// sub-scene it is cached and the sign is wiped a frame after it appears.
        ///
        /// <para>Measured at Romney 2026-09-12: the FIRST scene shown carried its sign ("Romney",
        /// then "Port Exchange"), and every sub-scene after that carried none — while the original
        /// shows one on all of them, "The Black Sheep Tavern" included. Calling
        /// <see cref="ShowSceneDescriptionAsync"/> by hand afterwards put the sign straight back,
        /// which is what ruled out the text, the split and the scene data.</para>
        ///
        /// <para>Re-applying is the right shape rather than teaching the loader to spare foreign
        /// children: the stage is the loader's to rebuild, and the sign is this screen's to own.</para>
        /// </remarks>
        public void RestoreSign() {
            if (!string.IsNullOrEmpty(_sign)) {
                ShowSign(_sign);
            }
        }

        private string _sign;

        /// <summary>The canonical stage this screen draws into — the hotspots' own.</summary>
        private VisualElement Stage() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;

            return root == null
                ? null
                : BakAgain.UI.CanonicalStage.GetOrCreate(root,
                    GetComponent<ResourceManagement.Loaders.UserInterfaceLoader>()?.Frame);
        }

        private const string SignName = "BakLocationSign";

        private static readonly Color SignFill = new(0.08f, 0.07f, 0.06f);
        private static readonly Color SignInk = new(244f / 255f, 196f / 255f, 164f / 255f);

        /// <summary>
        /// Waits for the player to be done with an in-scene description.
        /// </summary>
        /// <remarks>
        /// <b>Moving the mouse dismisses it</b> — it is not click-to-continue. The original polls for
        /// dialog input and otherwise measures the pointer against where it was when the text
        /// appeared, ending once the summed absolute movement passes
        /// <see cref="GdsSceneInteraction.ExamineDismissMouseDrift"/>. Waiting only for a click would
        /// leave descriptions up far longer than the original ever did.
        ///
        /// <para>Drift is measured in the pointer's screen px; the original's threshold is in its own
        /// mouse units, so the feel is matched rather than the raw number carried across.</para>
        /// </remarks>
        private async UniTask DismissAsync() {
            if (_pointer == null) {
                return;
            }
            Vector2 start = _pointer.ScreenPosition;
            while (true) {
                await UniTask.Yield();
                // The same leak as the service loop below: this ends only on a pointer gesture, so a
                // location torn down while a description is up leaves it spinning for the rest of
                // the session, holding whatever awaits it. The teardown deactivates this GameObject,
                // which is the signal (TASK-574).
                if (!isActiveAndEnabled) {
                    return;
                }
                bool clicked = _pointer.Primary.PressedThisFrame || _pointer.Secondary.PressedThisFrame;
                Vector2 now = _pointer.ScreenPosition;
                int dx = Mathf.RoundToInt(now.x - start.x);
                int dy = Mathf.RoundToInt(now.y - start.y);
                if (GdsSceneInteraction.ExamineEndsOn(clicked, dx, dy)) {
                    return;
                }
            }
        }

        private async UniTask Act(int menuEntryActionId) {
            GdsHotspot hotspot = HotspotFor(menuEntryActionId);
            if (hotspot == null) {
                // *** ACTION 1 IS "LEAVE SCENE", AND IT ARRIVES HERE ALREADY. *** MenuLayerHost
                // maps cancel to PrimaryAction(CancelActionId = 1), which is the same 1 the
                // original's menupage_run answers when a click lands on no hotspot. Dropping it
                // silently is what trapped the party in the Oracle of Aal (GDS40L): its only
                // hotspot is the consultation dialog, its way back is the SCENE's NextSceneLetter,
                // and nothing invoked it. Four shipped scenes have no exit hotspot at all.
                if (menuEntryActionId == GdsActionDispatch.LeaveSceneActionId) {
                    _onTransition?.Invoke(_scene?.NextSceneLetter ?? 0);
                }

                return;
            }

            PublishVisitCount(GdsSceneInteraction.HotspotIndexFor(menuEntryActionId));

            int outcome = hotspot.ActionCode;
            // *** A HOTSPOT DIALOG THAT QUEUES A TELEPORT ENDS THE LOCATION. *** TOWNSCN.C:460
            // snapshots abTeleportRecord[0] before dialog_play_record and :510 compares it after:
            // a changed id (and not the 0xff "none" marker) sets exitFlag, so the loop closes and
            // ProcessTeleportation moves the party. Without it Krondor's sewer door queued the
            // move and the town scene carried on drawing over a party already in zone 11.
            int? teleportBefore = _teleport?.QueuedId;
            bool dialogQueuedATeleport = false;
            if (GdsSceneInteraction.ShowsActionDialogFirst(hotspot)) {
                // *** THE SHOPKEEPER'S LINE BRANCHES ON WHAT HIS MENDER CAN FIX. ***
                // TOWNSCN.C:566 loads bInvreq_arg_x — the shop block's repair-category mask — into
                // lEvtArgAuxValue before dialog_play_record, and dialog 1800038 dispatches on it as
                // Var 18: 1 "sharpens swords", 2 "fixes armor", 4 "repairs crossbows", and the sums.
                // Its DEFAULT arm has no text and no branches, so leaving the global unwritten made
                // the whole exchange silent and dropped the player straight into the screen.
                PublishRepairCategories();
                // *** THE HOUSE'S FUND IS LENT TO THE DIALOG AND TAKEN BACK. *** TOWNSCN.C:467-508
                // loads it out of the speaking actor before dialog_play_record and writes it back
                // clamped afterwards, which is how a sub-action moves money the establishment owes
                // without having any handle on the container. Bracketing the one place a location
                // plays a hotspot dialog is the same seam.
                LoadEstablishmentFund();
                // *** THE SIGN GOES WITH THE PICTURE THE HOTSPOT REPLAYS. *** TOWNSCN.C:462 plays the
                // actor's animation channel before the dialog, and townscene_anim_channel_play_sync
                // opens with a full-screen copy of the clean picture — the sign bubble drawn over it
                // is gone, so a speaker's name plate never sits on "The Black Sheep Tavern". The
                // loop tail's description redraw below puts it back. TASK-504.
                ClearSign();
                int said;
                try {
                    said = await _dialogs.ShowById(hotspot.ActionDialogId);
                } finally {
                    StoreEstablishmentFund();
                }

                // *** THE DIALOG'S ANSWER CAN OVERRIDE WHICH ACTION RUNS. *** Five return values are
                // translated and everything else leaves the code alone, so a dialog that merely said
                // something falls through — see GdsSceneRules.OutcomeFor, whose table is neither the
                // identity nor ordered and so has to be carried rather than derived. It was modelled
                // with nothing able to reach it until SetReturnValue was wired (TASK-313).
                // *** THE ONE NUMBER THAT DECIDES WHICH ACTION RUNS, AND NOTHING LOGGED IT. ***
                // Chasing why Northwarden's Great Hall hotspot opened the hall in chapter 5 instead
                // of taking its -4 sub-scene meant probing globals for side effects of entries the
                // walk may or may not have visited. The answer is one int; print it.
                int before = outcome;
                outcome = GdsSceneRules.OutcomeFor(said, outcome);
                _logger?.LogDebug(
                    $"GDS hotspot dialog {hotspot.ActionDialogId} answered {said}; "
                    + $"action {before} -> {outcome}.");

                // Scoped to the DIALOG branch, as :510 is: the code-11 teleport screen queues a
                // destination too, and its whole point is that the loop stays open and switches to
                // the destination's scene instead of closing.
                dialogQueuedATeleport =
                    _teleport?.QueuedId != null && _teleport.QueuedId != teleportBefore;

                // GdsSceneRules.InvalidatesPalette(said) is the -2 arm's extra effect — it clears
                // the current-palette pointer so whatever runs next reloads it. Not applied here:
                // this screen holds no such pointer to clear, and inventing a field to satisfy the
                // rule would be worse than leaving it unmodelled and named. See TASK-313.
            }

            int index = GdsSceneInteraction.HotspotIndexFor(menuEntryActionId);
            var kind = GdsActionDispatch.KindOf(outcome);
            await Dispatch(hotspot, index, outcome);

            if (dialogQueuedATeleport) {
                // Letter 0 is how an exit is authored, the same signal EndChapter raises. The world
                // half is drained by LocationScenePlayer once the loop has actually unwound.
                _onTransition?.Invoke(0);

                return;
            }

            // *** THE LOCATION SAYS WHAT IT IS AGAIN AFTER EVERY ACTION. *** The original's loop
            // tail replays the idle animation and re-renders the scene's own dialog text, so the
            // description is always there — a screen that opened over it (a shop, a container)
            // leaves the location silent otherwise. A TRANSITION is the exception: the scene it
            // would redraw is the one being left.
            if (GdsActionDispatch.RedrawsTheLocationAfterwards(StaysInTheScene(kind))) {
                await ShowSceneDescriptionAsync();
            }
        }

        /// <summary>Whether an action leaves the party where they were.</summary>
        /// <remarks>
        /// Every arm but the two that move them: a sub-scene transition and the end of a chapter.
        /// Barding can become a transition on the way through, and says so by asking for one.
        /// </remarks>
        private static bool StaysInTheScene(GdsActionDispatch.ActionKind kind) =>
            kind != GdsActionDispatch.ActionKind.SubScene
            && kind != GdsActionDispatch.ActionKind.EndChapter;

        /// <summary>
        /// Runs the hotspot's action code.
        /// </summary>
        /// <remarks>
        /// Only the arms that need nothing beyond this screen are wired: moving between sub-scenes,
        /// leaving, doing nothing, the temple's rift map and the inn's overnight rest. The rest
        /// each open a screen of their own — container, shop, barding — and are logged rather than
        /// silently swallowed so a play session shows which ones the scenes actually reach.
        /// </remarks>
        /// <param name="actionCode">
        /// The code to run, which is the hotspot's own UNLESS its dialog returned one of the five
        /// values <c>GdsSceneRules.OutcomeFor</c> translates. Taking it from the hotspot here would
        /// quietly ignore the override.
        /// </param>
        private async UniTask Dispatch(GdsHotspot hotspot, int index, int actionCode) {
            var kind = GdsActionDispatch.KindOf(actionCode);
            switch (kind) {
                case GdsActionDispatch.ActionKind.DialogOnly:
                    return;

                case GdsActionDispatch.ActionKind.SubScene: {
                    // Code 3 takes the SCENE's next letter and code 4 the HOTSPOT's — reading one
                    // field for both would collapse every exit onto the same destination.
                    int letter = GdsActionDispatch.TransitionLetter(
                        actionCode, _scene?.NextSceneLetter ?? 0, hotspot.NextSceneLetter);
                    _onTransition?.Invoke(letter);
                    return;
                }

                case GdsActionDispatch.ActionKind.EndChapter:
                    // *** IT RAISES THE WORLD-LOOP EXIT, AND THAT IS THE WHOLE ACTION. ***
                    // TOWNSCN.C:569 is two lines — `nWorldLoopExitRequest = 1; exitFlag = 1;` — so
                    // leaving the scene is only the second half. Without the first the click reads
                    // as an ordinary exit and the chapter never turns: GDS6A is the ONLY scene in
                    // the shipped data carrying code 15, and its ActionDialogId is 0, so there is
                    // no dialog behind it to raise the request instead.
                    if (_session != null) {
                        _session.ChapterTransitionPending =
                            (byte)GameData.Resources.GameState.ChapterTransition.AdvanceRequest;
                    }
                    _onTransition?.Invoke(0);
                    return;

                case GdsActionDispatch.ActionKind.Teleport:
                    await RunTeleportAsync();
                    return;

                case GdsActionDispatch.ActionKind.ShopScreen:
                    await RunShopAsync(hotspot);
                    return;

                case GdsActionDispatch.ActionKind.Barding:
                    await RunBardingAsync();
                    return;

                case GdsActionDispatch.ActionKind.Container:
                    await OpenContainerAsync();
                    return;

                case GdsActionDispatch.ActionKind.ShopServices:
                    await RunTempleServicesAsync(hotspot);
                    return;

                case GdsActionDispatch.ActionKind.Inn:
                    // The shop block is read as an inn ONLY because the action code says so — the
                    // same bytes are markup and haggling for a shop.
                    await _inn.RunAsync(InnBlock());
                    return;

                default:
                    _logger.LogInformation(
                        "Location {Scene}: hotspot {Index} action {Kind} (code {Code}) has no screen yet.",
                        _scene?.Id, index, kind, hotspot.ActionCode);
                    return;
            }
        }

        /// <summary>
        /// Playing the lute at a tavern for coin.
        /// </summary>
        /// <remarks>
        /// <b>The experience is handed out before the outcome is known</b>, and to the whole party
        /// — so being thrown out still teaches them something. It is applied as skill USE rather
        /// than as points added: the original's third argument is read as a change mode by the
        /// routine it reaches, whatever its name says. See <see cref="Barding.ExperienceMode"/>.
        ///
        /// <para><b>The fund is spent, once.</b> A performance that earns anything zeroes it on the
        /// container, so coming back finds the tavern tapped out — and one that earns nothing
        /// leaves it to try again.</para>
        /// </remarks>
        private async UniTask RunBardingAsync() {
            GameData.Resources.Inventory.RuntimeContainer tavern = LocationContainer();
            SaveGameContainerShopData shop = tavern?.Shop;
            if (shop == null) {
                _logger.LogWarning("Location {Scene}: barding hotspot with no container.",
                    _scene?.Id);

                return;
            }

            // *** THE PERFORMER IS REMEMBERED, NOT DISCARDED. *** stat_party_find_extreme's answer
            // goes into nEvtArgActor0 (TOWNSCN.C:262), which is what a dialog sub-action means by
            // "the actor" — sub-action 15 boosts a skill on whoever this names. Reading it back out
            // was the missing half; the lookup itself was already right.
            int skill = _session.PartyExtreme(GameData.ActorAttribute.Barding, out int performer);
            _session.EventActor = performer;
            int fund = shop.BardingReward;
            int difficulty = shop.BardingDifficulty;

            GrantBardingExperience(Barding.ExperienceFor(fund, difficulty, skill));

            int reward = Barding.Reward(fund, difficulty, skill);
            if (Barding.SpendsTheFund(reward)) {
                _session.PartyGold += reward;
                // The reward reaches the tavern keeper's line through the same global every other
                // quoted amount does.
                _session.SetGlobalValue(DialogSlotPopulator.QuotedAmountGlobalKey, reward);
                tavern.Shop = shop.WithBardingReward(0);
                tavern.Dirty = true;
            }

            // *** THE PERFORMANCE HAS ITS OWN SONG, FOR THE OUTCOME LINE ONLY. ***
            // townscene_resolv_enc_outcome (TOWNSCN.C:298-300) switches to a track picked by the
            // performer's Barding, plays the outcome dialog, and puts the previous track back. A
            // tapped-out tavern (fund 0) plays its line with no song change (TASK-509).
            BakAgain.Audio.MidiPlaybackManager midi = ResolveOptional<BakAgain.Audio.MidiPlaybackManager>();
            BakAgain.ResourceManagement.IResourceProviderService songs =
                ResolveOptional<BakAgain.ResourceManagement.IResourceProviderService>();
            bool performs = fund != 0 && midi != null && songs != null;
            int previousTrack = GameData.Resources.Audio.MusicPlayback.NoTrack;
            if (performs) {
                previousTrack = await midi.PlayTrackAsync(
                    GameData.Resources.Audio.MusicSelection.ForTavernPerformance(skill), songs, owner: this);
            }
            await _dialogs.ShowById(Barding.DialogFor(fund, difficulty, skill));
            if (performs) {
                midi.PlayTrackAsync(previousTrack, songs, owner: this).Forget();
            }

            if (Barding.ThrownOut(fund, difficulty, skill)) {
                // A failed performance becomes a TRANSITION: the party is walked out through the
                // scene's own exit rather than left standing where they were.
                _onTransition?.Invoke(GdsActionDispatch.TransitionLetter(
                    GdsActionDispatch.ActionAfterBarding(bardingSucceeded: false),
                    _scene?.NextSceneLetter ?? 0, 0));
            }
        }

        /// <summary>Hands the dialog the fund this establishment is holding.</summary>
        private void LoadEstablishmentFund() {
            _session.EstablishmentFund = LocationContainer()?.Shop?.BardingReward ?? 0;
        }

        /// <summary>
        /// Takes it back, clamped, and clears the conversation-scoped copy.
        /// </summary>
        /// <remarks>
        /// <b>Clamped to 0xfa, and the original picks that rather than 0xff.</b> The value lives in
        /// a byte, and a dialog that ran the fund past the ceiling parks it one step below the top
        /// rather than wrapping. The clear afterwards matters as much: the global must not leak into
        /// the next conversation, which is exactly what the original does with it.
        ///
        /// <para>Written back only when it CHANGED, so a location with no container, or a dialog
        /// that never touched the fund, does not mark the record dirty for nothing.</para>
        ///
        /// <para><b>Verified live on scene 7B</b> (a tavern holding 55): opening its hotspot dialog
        /// lent the working copy 55 while the container still read 55, a sub-action raising the copy
        /// to 200 left the container untouched, and closing the dialog cleared the copy and wrote
        /// 200 onto the container. The working copy is what a dialog moves; the container is where
        /// it lands.</para>
        ///
        /// <para><b>The sub-scene letter counts from ONE</b> — <c>GdsSceneContainer.LocationY</c>
        /// says so and this passes <c>_sceneLetter</c> straight through. Reading it as a 0-based
        /// index picks the neighbouring sub-scene's container, which during this verification looked
        /// briefly like the lookup was wrong when it was the probe that was.</para>
        /// </remarks>
        private void StoreEstablishmentFund() {
            int value = Mathf.Clamp(_session.EstablishmentFund, 0, BakAgain.Core.GameSession.EstablishmentFundMax);
            _session.EstablishmentFund = 0;

            GameData.Resources.Inventory.RuntimeContainer container = LocationContainer();
            SaveGameContainerShopData shop = container?.Shop;
            if (shop == null || shop.BardingReward == value) {
                return;
            }

            container.Shop = shop.WithBardingReward((byte)value);
            container.Dirty = true;
            _logger.LogInformation("Location {Scene}: dialog left the house fund at {Value}.",
                _scene?.Id, value);
        }

        /// <summary>The Barding advancement every active member gets from a performance.</summary>
        private void GrantBardingExperience(int uses) {
            if (uses <= 0 || _session?.ActivePartyIndices == null) {
                return;
            }
            foreach (byte character in _session.ActivePartyIndices) {
                // Through ModifyStatOf so the sheet mark follows the change (TASK-611).
                _session.ModifyStatOf(character, GameData.ActorAttribute.Barding, uses,
                    Barding.ExperienceMode,
                    _session.StudyBonusFor(character, GameData.ActorAttribute.Barding));
            }
        }

        /// <summary>
        /// Opens the location's own container on the inventory screen.        /// <summary>
        /// Opens the location's own container on the inventory screen.
        /// </summary>
        /// <remarks>
        /// <b>Three action codes, one behaviour.</b> 5, 6 and 8 are byte-for-byte the same arm in
        /// the original (0x4e406, 0x4e367, 0x4e39d): the same call with the same arguments, the same
        /// backdrop reload afterwards and the same two flags. A port that goes looking for the
        /// difference between them will not find one — whatever the codes meant to the authors, the
        /// engine does not distinguish them.
        ///
        /// <para>It is the SCENE's container — the one keyed by scene number and letter, which is
        /// also where a temple reads its prices from — not something at the party's feet.</para>
        /// </remarks>
        private async UniTask OpenContainerAsync() {
            GameData.Resources.Inventory.RuntimeContainer container = LocationContainer();
            if (container == null || _inventoryMenu == null) {
                _logger.LogWarning("Location {Scene}: container hotspot with no container.",
                    _scene?.Id);

                return;
            }

            // The chest image: this is a fixed thing in a scene, which is what the original's
            // container image resolution comes down to for a location.
            _inventoryMenu.SetContainer(container, GameData.Resources.World.WorldEntityType.Container);
            await RaiseScreenAsync(_inventoryMenu);
        }

        /// <summary>
        /// The temple's service counter — healing and weapon blessing.
        /// </summary>
        /// <remarks>
        /// <b>It is a loop, not a screen.</b> The hotspot's own dialog is re-shown after every
        /// service and only a result of 3 ends it, so a player buys several in one visit without the
        /// location redrawing between them. That is also why this action code skips its dialog on
        /// the way in — it shows it here instead, repeatedly.
        ///
        /// <para>The temple's identity is published before the dialog so its wording can name the
        /// god, and the two services read <b>different bytes of the same union</b>: healing takes
        /// the shopkeeper's skill as its price percentage, blessing takes markup, haggling and
        /// mark-down as fee, percentage and tier. They are only those things because the action code
        /// says so.</para>
        /// </remarks>
        private async UniTask RunTempleServicesAsync(GdsHotspot hotspot) {
            GameData.Resources.Data.SaveGameContainerShopData shop = ShopBlock();
            if (shop == null) {
                _logger.LogWarning("Location {Scene}: service hotspot with no container shop block.",
                    _scene?.Id);

                return;
            }

            _session?.SetGlobalValue(DialogSubjectGlobal, shop.ShopType);
            int result;
            do {
                result = await _dialogs.ShowChoiceIndexById(hotspot.ActionDialogId);
                if (result == GdsActionDispatch.HealingService) {
                    // *** THROUGH THE SAME BRACKET THE BLESSING USES. *** The heal screen pushes
                    // itself, so calling it bare skipped ClearSign/ClearDialog and left the
                    // location's picture, its sign and its description painted UNDER the priest's
                    // parchment — the Chapel of Ishap's name ran straight through the middle of the
                    // quote. Measured 2026-09-13; the same shape TASK-407 fixed for the shop.
                    await RaiseOverLocationAsync(
                        () => _healScreen.RunAsync(shop.ShopkeeperSkill, shop.ShopType));
                } else if (result == GdsActionDispatch.BlessingService) {
                    await RunBlessingAsync(shop);
                }
            }
            // *** AND THE LOCATION MUST STILL BE OPEN. *** This loop re-raises the hotspot's own
            // dialog, and it is started fire-and-forget (`_ = Act(...)`), so nothing but this
            // condition can stop it: `LocationScenePlayer.Hide()` deactivates this very GameObject
            // and a running UniTask does not notice. A service loop that outlived its temple kept
            // raising 1300072 WITH THE PARTY IN THE WORLD, and each raise opens with
            // `RemovePanel(mine: null)`, which tears down whatever dialog is up -- the Silden gate
            // prompt among them, whose caller then waits for an answer that can never come and the
            // town cannot be entered for the rest of the session (TASK-574).
            //
            // `isActiveAndEnabled` rather than a generation counter: the teardown already flips it,
            // so this needs no new state to keep in step. It is the same fix TASK-491 gave the OUTER
            // scene loop, at the one nesting level that task did not reach.
            while (GdsActionDispatch.ServiceMenuContinues(result) && isActiveAndEnabled);
        }

        /// <summary>
        /// The temple's weapon blessing.
        /// </summary>
        /// <remarks>
        /// <b>Not a screen of its own.</b> The original sets an inventory-screen MODE and runs the
        /// ordinary inventory screen, where using an item offers to bless it — so this pushes that
        /// screen rather than building a second one for one changed verb.
        ///
        /// <para>It opens on the FIRST member's pack; the screen's own portraits move between
        /// members from there, which is how a player blesses someone else's sword.</para>
        /// </remarks>
        private async UniTask RunBlessingAsync(
            GameData.Resources.Data.SaveGameContainerShopData shop) {
            if (_inventoryMenu == null || !_inventoryMenu.SetBlessing(shop, FirstPortraitSlot)) {
                _logger.LogWarning("Location {Scene}: the blessing screen would not open.",
                    _scene?.Id);

                return;
            }

            await RaiseScreenAsync(_inventoryMenu);
        }

        /// <summary>
        /// Raises a screen over this location and redraws the location when it closes.
        /// </summary>
        /// <remarks>
        /// <b>The location's description must come DOWN while a screen is up.</b> Measured against
        /// the original at Fletcher's Post, LaMut (2026-09-07): its shop screen shows nothing of the
        /// location — clean portraits, the "Shop" detail panel, the page arrow, Exit and the purse.
        /// Ours painted the description across the portraits and the purse and left the sign over
        /// the middle of the item grid, hiding one cell's name outright. A location is not on the
        /// navigator's stack, so nothing was taking it down.
        ///
        /// <para><b>And it comes back afterwards.</b> Every screen arm in <c>TOWNSCN.C</c> reloads
        /// the backdrop and sets <c>needRefresh = fadeFlag = 1</c> on return, so the location is
        /// redrawn — description included — rather than merely uncovered. That is also why the
        /// description survives an action at all.</para>
        /// </remarks>
        private UniTask RaiseScreenAsync(UI.Navigation.IScreen screen) =>
            RaiseOverLocationAsync(() => _navigator.PushAndWaitAsync(screen));

        /// <summary>
        /// Run something full-screen over this location and put the location back afterwards.
        /// </summary>
        /// <remarks>
        /// <see cref="RaiseScreenAsync"/>'s body, for the one caller that pushes its own screen —
        /// the temple's heal menu, which owns its push/pop and so cannot be handed to the navigator
        /// here.
        /// </remarks>
        private async UniTask RaiseOverLocationAsync(System.Func<UniTask> run) {
            _dialogs?.ClearDialog();
            ClearSign();

            await run();

            // *** THE POP BRINGS THE TRAVEL HUD BACK, AND IT HAS TO GO STRAIGHT DOWN AGAIN. ***
            // `ScreenNavigator.Pop` ends in `EnsureTopShownAsync`, which shows the NAVIGATOR's own
            // previous top — and a location is deliberately not on that stack, so what gets revealed
            // is the HUD `LocationScenePlayer.HideTravelScreenAsync` took down on the way in. It
            // then paints over the location: InGameScreen sits at -2 and LocationScreen at -3.
            //
            // Measured at Romney 2026-09-12: after closing the shop with its own Exit
            // (REQ_INV action 1), the location was STILL ACTIVE and still on GDS6C with the travel
            // HUD drawn over it. The original returns to the Port Exchange scene — picture, sign
            // and description — with no HUD in sight. TASK-407.
            //
            // Taking it down here rather than teaching the navigator about locations is the smaller
            // of the two fixes; putting the location on the navigator's stack is the structural one
            // and is still worth doing.
            UI.InGame.InGameScreen travel = TravelScreen();
            if (travel != null && travel.IsVisible) {
                await travel.HideAsync();
            }

            // Re-shown, not restored: the description is re-resolved because an action can have
            // changed what the place says about itself.
            await ShowSceneDescriptionAsync();
        }

        /// <summary>The portrait the blessing screen opens on.</summary>
        private const int FirstPortraitSlot = 0;

        /// <summary>
        /// Opens the temple's rift map and queues whatever it picks.
        /// </summary>
        /// <remarks>
        /// <b>Nothing here moves the party.</b> The chosen destination goes into the shared
        /// hand-off slot, and its two halves are taken by two different loops afterwards: the scene
        /// half redirects this location loop to the destination temple, and the world half is
        /// applied once that loop finally exits. Applying the move from here would drop the party
        /// outside the temple it just teleported into.
        ///
        /// <para>The three numbers the screen needs come from the location's container, whose shop
        /// block is a <b>type-discriminated union</b> — these same bytes mean markup and haggling
        /// for a shop. Reading them as a temple is only correct because the action code says so.</para>
        /// </remarks>
        private async UniTask RunTeleportAsync() {
            GameData.Resources.Data.SaveGameContainerShopData shop = ShopBlock();
            if (shop == null || _teleportScreen == null) {
                _logger.LogWarning("Location {Scene}: teleport hotspot with no container shop block.",
                    _scene?.Id);

                return;
            }

            int row = await _teleportScreen.RunAsync(
                currentTemple: shop.ShopType,
                baseCost: (int)shop.ShopCategories,
                costPerUnit: shop.TeleportParam);
            if (row < 0) {
                return;
            }

            var destinations = await _resources.GetOrLoadAsync<
                GameData.Resources.Location.TeleportDestinationSet>("TELEPORT.DAT");
            GameData.Resources.Location.TeleportDestination destination = destinations?.ById(row);
            if (destination == null) {
                _logger.LogError("Location {Scene}: teleport row {Row} is not in TELEPORT.DAT.",
                    _scene?.Id, row);

                return;
            }

            _teleport?.Queue(destination);
            // Leave this sub-scene either way. With a scene queued the loop redirects to it; without
            // one it exits and the world move lands.
            _onTransition?.Invoke(0);
        }

        /// <summary>The shop block of the container the party is standing at, or null.</summary>
        /// <summary>
        /// Opens the shopkeeper's stock.
        /// </summary>
        /// <remarks>
        /// <b>There is no shop screen.</b> The original runs the ordinary inventory screen with the
        /// shopkeeper's container as the displayed one and the mode flag set — the same flag the
        /// picklock screen uses — so this is a container and a bit, not a new window.
        ///
        /// <para>The stock is the LIVE container at the location, not the save snapshot: a shop
        /// that has already sold something this session has a different shelf, and the snapshot
        /// would hand back the one it started with.</para>
        /// </remarks>
        /// <summary>
        /// Put the shop's repair-category mask where the hotspot's dialog can branch on it —
        /// <c>g_gameState.lEvtArgAuxValue</c> (TOWNSCN.C:566), which the walker reads as
        /// <c>VarCondition</c> Var 18.
        /// </summary>
        /// <remarks>
        /// Written for EVERY hotspot dialog, not only a shop's, because the original writes it for
        /// every one: the load sits in the dialog-first block ahead of the action dispatch, and a
        /// scene with no shop block simply publishes nothing rather than leaving the last shop's
        /// answer standing.
        /// </remarks>
        private void PublishRepairCategories() =>
            _session?.SetGlobalValue(RepairCategoriesGlobal, ShopBlock()?.RepairCategories ?? 0);

        /// <summary>Var 18 — <c>30000 + 18</c>, the walker's own mapping.</summary>
        private const int RepairCategoriesGlobal = 30018;

        private async UniTask RunShopAsync(GdsHotspot hotspot) {
            // The scene's container, not the party's feet — see LocationContainer/ShopBlock. A
            // location's container is filed under (zone 15, scene number, scene letter), so a
            // lookup by world position finds nothing and the shop reads as having no stock.
            // *** CODE 16 IS THE MENDER, NOT A COUNTER. *** TOWNSCN.C's `di == 0x10` arm calls
            // modalscreen_inventory_request(bInvreq_arg_x, bInvreq_arg_y) — the inventory screen in
            // mode 2, browsing the PARTY's items — while buying and selling is a container arm
            // (codes 5, 6 and 8). The port ran both through SetShop, so Romney's tinker offered a
            // price list.
            if (_inventoryMenu != null && hotspot != null
                && hotspot.ActionCode == GdsActionDispatch.RepairActionCode
                && _inventoryMenu.SetRepair(ShopBlock(), portraitSlot: 0)) {
                await RaiseScreenAsync(_inventoryMenu);

                return;
            }

            GameData.Resources.Inventory.RuntimeContainer stock = LocationContainer();
            if (_inventoryMenu == null || !_inventoryMenu.SetShop(stock, ShopBlock())) {
                _logger.LogInformation(
                    "Location {Scene}: a shop hotspot with no stock to show.", _scene?.Id);

                return;
            }

            await RaiseScreenAsync(_inventoryMenu);
        }

        /// <summary>
        /// The container holding this location's own stats — its prices, its tier, its rate.
        /// </summary>
        /// <remarks>
        /// <b>Filed under the scene, not under the party's feet.</b> <c>gds_loadSceneFile</c>
        /// resolves it with <c>GetContainerAtLocation(zone 15, X = scene number, Y = scene letter)</c>
        /// — see <see cref="GdsSceneContainer"/> — so the "location" fields on these containers are
        /// the scene's identity rather than world coordinates.
        ///
        /// <para>Looking it up by where the party is standing finds nothing, because the party is
        /// never standing at (70, 1). That was this screen's first reading of it, and it failed
        /// SILENTLY: every arm that needs the container simply did nothing. Driving a temple is what
        /// found it; no test could, since the wrong lookup is a valid call that returns null.</para>
        /// </remarks>
        /// <summary>The scene's own container — see <see cref="GdsSceneContainer"/>.</summary>
        private GameData.Resources.Inventory.RuntimeContainer LocationContainer() =>
            _session?.GetLiveContainerAt(
                GdsSceneContainer.Zone,
                GdsSceneContainer.LocationX(_sceneNumber),
                GdsSceneContainer.LocationY(_sceneLetter));

        /// <summary>
        /// The shop block the rest arm prices from, with the one scripted rate applied.
        /// </summary>
        /// <remarks>
        /// <b>Written into the container, not just used.</b> TOWNSCN.C:553-555 stores
        /// <c>bRest_gold_cost</c> in the scene container's own record before the stay, so the rate
        /// persists with the save. Only scene 62E (the Rapid Rooks Inn) does this
        /// (<see cref="GdsActionDispatch.ScriptedInnRate"/>): 10 once story flag 56092 is set, else 72.
        /// </remarks>
        private GameData.Resources.Data.SaveGameContainerShopData InnBlock() {
            GameData.Resources.Data.SaveGameContainerShopData shop = ShopBlock();
            int? rate = GdsActionDispatch.ScriptedInnRate(_sceneNumber, _sceneLetter,
                (_session?.GetGlobalValue(GdsActionDispatch.ScriptedInnFlag) ?? 0) != 0);
            if (shop == null || rate == null) {
                return shop;
            }
            shop = shop.WithInnCostPerNight((byte)rate.Value);
            GameData.Resources.Inventory.RuntimeContainer container = _session?.GetRuntimeContainerAt(
                GdsSceneContainer.Zone, GdsSceneContainer.LocationX(_sceneNumber),
                GdsSceneContainer.LocationY(_sceneLetter));
            if (container != null) {
                container.Shop = shop;
                container.HeaderDirty = true;
            }
            return shop;
        }

        private GameData.Resources.Data.SaveGameContainerShopData ShopBlock() =>
            _session?.GetContainerAt(GdsSceneContainer.Zone,
                GdsSceneContainer.LocationX(_sceneNumber),
                GdsSceneContainer.LocationY(_sceneLetter))?.ShopData;

        /// <summary>
        /// The hotspot's visit count, published where its dialog can branch on it, then advanced.
        /// </summary>
        /// <remarks>
        /// <b>Dialogs branch on how often a hotspot has been used.</b> The original writes the count
        /// into a global before showing the dialog and increments it afterwards, saturating at
        /// <see cref="GdsActionDispatch.VisitCountCap"/>. Skipping it makes every such dialog take
        /// its first-visit arm forever.
        /// </remarks>
        private void PublishVisitCount(int index) {
            _visits.TryGetValue(index, out int count);
            _session?.SetGlobalValue(DialogSubjectGlobal, count);
            _visits[index] = GdsActionDispatch.NextVisitCount(count);
        }

        /// <summary>
        /// The global a location writes whatever the dialog it is about to show should branch on.
        /// </summary>
        /// <remarks>
        /// <b>One slot, two subjects, written at two moments.</b> The visit count goes in before a
        /// hotspot's action dialog and the temple's type before a service dialog — the original
        /// writes both to this same global (0x4e57f and the visit-count publish), because each is
        /// only ever read by the dialog that follows it. Giving them separate slots here would put
        /// one of them somewhere no shipped dialog looks.
        /// </remarks>
        private const int DialogSubjectGlobal = 30000;

        private GdsHotspot HotspotFor(int menuEntryActionId) {
            int index = GdsSceneInteraction.HotspotIndexFor(menuEntryActionId);
            if (_scene?.Hotspots == null || index < 0 || index >= _scene.Hotspots.Length) {
                _logger?.LogWarning("Location {Scene}: action id {Id} has no hotspot.",
                    _scene?.Id, menuEntryActionId);
                return null;
            }
            return _scene.Hotspots[index];
        }
    }
}

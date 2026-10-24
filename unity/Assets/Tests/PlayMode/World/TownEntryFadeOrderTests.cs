namespace BakAgain.Tests.PlayMode.World {
    using BakAgain.CutScenes;
    using BakAgain.UI.InGame;
    using BakAgain.UI.Navigation;
    using BakAgain.World.Scenes;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Animation;
    using GameData.Resources.Animation.FrameCommands;
    using GameData.Resources.Palette;
    using GameData.Resources.Scene;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using UnityEngine.UIElements;
    using VContainer;

    /// <summary>
    /// A location's description arrives with its picture, under the fade, never over the world
    /// (TASK-904); a sub-scene cuts instead of fading, and leaving with a teleport queued charges the
    /// step first and moves the party under the one fade (TASK-906 items 3 and 4).
    /// </summary>
    /// <remarks>
    /// The original paints the description bubble while the palette is still black and fades the
    /// picture and the text in together (TOWNSCN.C:378-404). The port started the entry animation
    /// before fading out, and the animation's hold raised the description at once — so entering
    /// LaMut printed "In many ways the town looked like..." over the travel view, with the HUD still
    /// up, for the whole fade-out. Drives the real <see cref="LocationScenePlayer.ShowAsync"/> and a
    /// real <see cref="CutscenePresenter"/>; only the data, the fade and the frame processor are
    /// stand-ins.
    /// </remarks>
    public class TownEntryFadeOrderTests {
        private const int DescriptionId = 4242;

        private GameObject _hudGo;
        private GameObject _screenGo;
        private GameObject _canvasGo;
        private GameObject _dialogGo;
        private PanelSettings _panelSettings;

        /// <summary>Every cue played ("play 12") and stopped ("stop 12"), in order.</summary>
        private readonly System.Collections.Generic.List<string> _sounds = new();
        private readonly System.Action<int> _played, _stopped;

        public TownEntryFadeOrderTests() {
            _played = id => _sounds.Add("play " + id);
            _stopped = id => _sounds.Add("stop " + id);
        }

        [SetUp]
        public void ListenForCues() {
            _sounds.Clear();
            BakAgain.Audio.MenuSoundService.CueRequested += _played;
            BakAgain.Audio.MenuSoundService.CueStopped += _stopped;
        }

        [TearDown]
        public void TearDown() {
            BakAgain.Audio.MenuSoundService.CueRequested -= _played;
            BakAgain.Audio.MenuSoundService.CueStopped -= _stopped;
            foreach (GameObject go in new[] { _hudGo, _screenGo, _canvasGo, _dialogGo, _teleportGo, _menuGo }) {
                if (go != null) { Object.DestroyImmediate(go); }
            }
            if (_panelSettings != null) { Object.DestroyImmediate(_panelSettings); }
        }

        [UnityTest]
        public IEnumerator ArrivingFadesASubSceneCutsAndLeavingFades() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud), teleport: null);

                try {
                    UniTask run = player.RunAsync(1, 1);

                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    Assert.That(dialogs.Moments, Is.EqualTo(new[] { "covered, hud down, before fade-in" }),
                        "arriving: the description is painted under the fade, after the travel HUD is down "
                        + "and before the fade-in starts, as TOWNSCN.C:378-404 does");
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }));

                    // Into a second sub-scene. This show awaits the first one's held animation, which
                    // is where a stray continuation on it throws (TASK-904).
                    fade.Events.Clear();
                    Choose(player, 2);
                    await Until(() => dialogs.Moments.Count == 2 && fade.Events.Count >= 2,
                        "sub-scene 2 never showed");
                    await UniTask.Yield();
                    Assert.That(fade.Events, Is.EqualTo(new[] { "freeze", "thaw" }),
                        "a sub-scene CUTS: fadeFlag is 1 only for the first scene (TOWNSCN.C:325, :376, "
                        + ":398), and the owner chose the cut for the remake on 2026-10-21");
                    Assert.That(dialogs.Moments[1], Is.EqualTo("frozen, hud down"),
                        "the new description is painted while the old frame is still held, so it "
                        + "appears with its picture at the cut (TOWNSCN.C:392 then :409)");

                    // Leaving still fades: TOWNSCN.C:680 raises g_nSceneReloadPending and the world
                    // loop fades out, repaints and fades in (WORLDLP.C:102-139).
                    fade.Events.Clear();
                    Choose(player, 0);
                    await run;
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }));
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// A hotspot dialog that precedes a sub-scene change stays drawn until the cut (TASK-914).
        /// </summary>
        /// <remarks>
        /// TOWNSCN.C:478 plays the hotspot's dialog with <c>dialog_play_record(key, 0)</c>. Inside a
        /// scene that dialog never clears the screen when it closes: <c>screenDirty</c> is only set
        /// by a modal record (DIALOG.C:874, modal is 0 here) or by the out-of-scene speaker branch
        /// (:1005), so :1492-1500 leaves the last page drawn. A code 3/4 answer then skips the
        /// refresh (needRefresh = 0, :608-617) and the next thing presented is the new sub-scene
        /// (:409). Measured at Krondor GDS2A hotspot 4 (dialog 1500029): the narration page is on
        /// screen until GDS2E replaces it in one change. The port took the panel down when the
        /// play ended, so the still the cut held was of the bare old picture. And the new sub-scene
        /// is drawn over the whole screen, so the page must not outlive the cut either: GDS40K
        /// hotspot 0 leads into GDS40L, which has no description to replace the panel with.
        /// </remarks>
        [UnityTest]
        [BakAgain.Tests.TestSupport.RequiresShippedGameData]
        public IEnumerator AHotspotDialogBeforeASubSceneStaysDrawnUntilTheCut() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                BakAgain.UI.DialogManager dialogs = RealDialogs();
                // Sub-scene B has no description, as GDS40L has none.
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud), teleport: null,
                    undescribed: GdsSceneLoader.ResourceName(1, 2));
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");

                    // GDS2A hotspot 4's shape: code 4 to the hotspot's own letter, behind a dialog.
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        Hotspots = new[] {
                            new GdsHotspot { ActionCode = 4, NextSceneLetter = 2, ActionDialogId = ActionDialogId },
                        },
                    }, 1, 1, letter => Choose(player, letter));
                    VisualElement atFreeze = null;
                    bool drawnAtFreeze = false;
                    fade.OnFreeze = () => {
                        atFreeze = ActivePanel(dialogs);
                        drawnAtFreeze = atFreeze?.panel != null;
                    };
                    fade.Events.Clear();

                    VisualElement described = ActivePanel(dialogs);
                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    // The control: the hotspot's page did go up, so a missing panel at the freeze
                    // is the teardown and not a dialog that never rendered.
                    await Until(() => ActivePanel(dialogs) is { } page && page != described,
                        "the hotspot's page never showed");
                    await Until(() => fade.Events.Contains("thaw"), "sub-scene 2 never showed");

                    Assert.That(drawnAtFreeze, Is.True,
                        "the hotspot's last page is still drawn when the cut takes its still: in a "
                        + "scene dialog_play_record leaves it up (DIALOG.C:1492-1500) until the next "
                        + "sub-scene is presented (TOWNSCN.C:409)");
                    Assert.That(atFreeze.panel, Is.Null,
                        "and the new sub-scene replaces it: it is gone once the cut is over");
                    Assert.That(ActivePanel(dialogs), Is.Null,
                        "a sub-scene with no description shows no dialog at all");
                } finally {
                    player.Hide();
                }
            });

        private const int ActionDialogId = 1500029;

        /// <summary>dlgRecord 0x49, the tavern keeper's "Out, out, out" (TOWNSCN.C:285).</summary>
        private const int ThrownOutDialogId = 73;

        /// <summary>
        /// A performer thrown out of the tavern: dialog 73 stays drawn until the exit scene is
        /// presented, with no description redrawn in between (TASK-916).
        /// </summary>
        /// <remarks>
        /// <c>townscene_resolv_enc_outcome</c> plays the line with <c>dialog_play_record(0x49, 0)</c>
        /// inside the scene (TOWNSCN.C:299), so its page is not cleared when it closes
        /// (DIALOG.C:1492-1500), and outcome 0 (:285-286) turns the action into code 3 (:533-538),
        /// whose arm sets needRefresh = 0 (:608-609): no redraw until the exit scene's present
        /// (:409). Measured at Krondor's Rainbow Parrot (GDS2C) on t916: the line stays until GDS2A
        /// replaces it in one change. The port closed the page and redrew the old description, so the
        /// cut held the bare tavern.
        /// </remarks>
        [UnityTest]
        [BakAgain.Tests.TestSupport.RequiresShippedGameData]
        public IEnumerator AThrownOutPerformanceStaysDrawnUntilTheExitScene() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                BakAgain.UI.DialogManager dialogs = RealDialogs();
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud), teleport: null,
                    session: TavernSession(bardingDifficulty: 80, fund: 50, performerBarding: 10));
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    Assume.That(GameData.Resources.Scene.Barding.ThrownOut(50, 80, 10), Is.True);

                    // GDS2C's shape: the lute is code 9, and the scene's exit is letter 2 here.
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        NextSceneLetter = 2,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 9 } },
                    }, 1, 1, letter => Choose(player, letter));
                    VisualElement atFreeze = null;
                    bool drawnAtFreeze = false;
                    fade.OnFreeze = () => {
                        atFreeze = ActivePanel(dialogs);
                        drawnAtFreeze = atFreeze?.panel != null;
                    };
                    fade.Events.Clear();

                    VisualElement described = ActivePanel(dialogs);
                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    VisualElement thrownOut = null;
                    await Until(() => (thrownOut = ActivePanel(dialogs)) != null && thrownOut != described,
                        "the thrown-out line never showed");
                    await Until(() => fade.Events.Contains("thaw"), "the exit scene never showed");

                    Assert.That(atFreeze, Is.SameAs(thrownOut),
                        "the cut's still holds the thrown-out line, not the bare tavern or its old "
                        + "description: needRefresh is 0 on the code-3 arm (TOWNSCN.C:608-609)");
                    Assert.That(drawnAtFreeze, Is.True, "and the line is still drawn then");
                    Assert.That(thrownOut.panel, Is.Null, "the exit scene replaces it at the cut");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// A temple teleport: the arrival page stays drawn until the destination temple is presented,
        /// and the travel HUD the map's pop reveals is down before the destination is queued
        /// (TASK-917).
        /// </summary>
        /// <remarks>
        /// <c>modalscreen_teleport_spell_run</c> ends the map (MODALSCR.C:346-353) and plays 0x13d660
        /// = 1300064 inside the scene (:361), which is not cleared when it closes; the destination
        /// temple is then presented in one change (TOWNSCN.C:576-581 sets the next scene, :409).
        /// Measured at Malac's Cross (GDS3C) to the Temple of Ishap on t917: the page stays until the
        /// temple, picture and description, replaces it. The port popped the map first, which put
        /// the travel HUD back over the location; the HUD's world-loop pump then drained the queued
        /// move while the destination temple was loading (zone rebuilt, the HUD left over the
        /// temple's description).
        /// </remarks>
        [UnityTest]
        [BakAgain.Tests.TestSupport.RequiresShippedGameData]
        public IEnumerator ATempleTeleportKeepsTheArrivalPageAndNeverRaisesTheHud() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                BakAgain.UI.DialogManager dialogs = RealDialogs();
                var teleport = new GameData.Resources.Location.PendingTeleport();
                var navigator = new HudRevealingNavigator(_hudGo);
                BakAgain.Core.GameSession session = TempleSession(currentTemple: 5, visited: 2);
                var mapGo = new GameObject("TeleportScreen");
                _teleportGo = mapGo;
                var map = mapGo.AddComponent<BakAgain.UI.Teleport.TeleportScreen>();
                map.Construct(new TownCache(), navigator, dialogs, session);
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade), teleport,
                    session: session, teleportScreen: map, navigator: navigator);
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    _hudGo.SetActive(false);   // the location took the HUD down on the way in

                    bool? hudUpAtQueue = null;
                    VisualElement arrival = null;
                    int thawsAtQueue = 0;
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        Hotspots = new[] { new GdsHotspot { ActionCode = 11 } },
                    }, 1, 1, letter => {
                        hudUpAtQueue = _hudGo.activeSelf;
                        arrival = ActivePanel(dialogs);
                        thawsAtQueue = fade.Events.Count(e => e == "thaw");
                        Choose(player, letter);
                    });
                    VisualElement atFreeze = null;
                    bool drawnAtFreeze = false;
                    fade.OnFreeze = () => {
                        if (hudUpAtQueue != null) {   // the destination's cut, not the map's
                            atFreeze = ActivePanel(dialogs);
                            drawnAtFreeze = atFreeze?.panel != null;
                        }
                    };
                    fade.Events.Clear();

                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    await Until(() => navigator.Pushed != null, "the map never opened");
                    map.PrimaryAction(GameData.Resources.Location.TeleportMenu.ActionIdForTemple(2));
                    await Until(() => hudUpAtQueue != null, "the destination was never queued");
                    await Until(() => fade.Events.Count(e => e == "thaw") > thawsAtQueue,
                        "the destination temple never showed");

                    Assert.That(hudUpAtQueue, Is.False,
                        "the HUD the map's pop revealed is down before the destination is queued: "
                        + "while it is up its world-loop pump drains the move mid-location");
                    Assert.That(arrival, Is.Not.Null,
                        "the arrival page is still drawn when the destination is queued "
                        + "(MODALSCR.C:361, in the scene, not cleared on close)");
                    Assert.That(atFreeze, Is.SameAs(arrival),
                        "and it is what the destination's cut holds as its still");
                    Assert.That(drawnAtFreeze, Is.True, "drawn, not a panel already taken down");
                    Assert.That(arrival.panel, Is.Null, "the destination temple replaces it at the cut");
                    Assert.That(teleport.HasAnything, Is.True,
                        "the world half waits for the loop's exit; nothing drained it");
                    Assert.That(_sounds, Is.EqualTo(new[] { "play 12", "stop 12" }),
                        "the spark's flight sounds 12_SPELL2 from take-off and stops it on landing "
                        + "(MODALSCR.C:294, :341-342)");
                } finally {
                    player.Hide();
                }
            });

        private GameObject _teleportGo;

        /// <summary>
        /// A declined temple teleport puts the location back, description included, in the one cut
        /// that takes the map's last page down, and draws the description once (TASK-917).
        /// </summary>
        /// <remarks>
        /// MODALSCR.C:364-366 plays 0x13d661 = 1300065 inside the scene and returns 0, so the
        /// code-11 arm sets needRefresh (TOWNSCN.C:584) and the refresh paints the idle picture and
        /// the description and presents them together, with no fade (:612-633). Driven in the Temple
        /// of Silban: the port put the location back under the cut and then redrew the description
        /// a second time, which wiped its text in again over the bare picture.
        /// </remarks>
        [UnityTest]
        [BakAgain.Tests.TestSupport.RequiresShippedGameData]
        public IEnumerator ADeclinedTempleTeleportPutsTheLocationBackOnceAtTheCut() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                var navigator = new HudRevealingNavigator(_hudGo);
                BakAgain.Core.GameSession session = TempleSession(currentTemple: 5, visited: 2);
                _teleportGo = new GameObject("TeleportScreen");
                var map = _teleportGo.AddComponent<BakAgain.UI.Teleport.TeleportScreen>();
                map.Construct(new TownCache(), navigator, dialogs, session);
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade),
                    new GameData.Resources.Location.PendingTeleport(), session: session,
                    teleportScreen: map, navigator: navigator);
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    _hudGo.SetActive(false);
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        SceneDialogId = DescriptionId,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 11 } },
                    }, 1, 1, letter => Choose(player, letter));
                    fade.Events.Clear();
                    dialogs.Moments.Clear();

                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    await Until(() => navigator.Pushed != null, "the map never opened");
                    map.PrimaryAction(GameData.Resources.Location.TeleportMenu.CancelActionId);
                    await Until(() => fade.Events.Contains("thaw"), "the location never came back");
                    for (var i = 0; i < 30; i++) {
                        await UniTask.Yield();
                    }

                    Assert.That(dialogs.Moments, Is.EqualTo(new[] { "frozen, hud down" }),
                        "the description is drawn once, under the cut that takes the page down and "
                        + "after the HUD the pop revealed is down again (TOWNSCN.C:612-633)");
                    Assert.That(_sounds, Is.Empty, "a declined teleport has no flight and no flight sound");
                } finally {
                    player.Hide();
                }
            });

        private GameObject _menuGo;

        /// <summary>
        /// A screen raised over a location puts the location back with its description drawn once
        /// (TASK-922).
        /// </summary>
        /// <remarks>
        /// The container and shop arms (codes 5, 6, 8) run the inventory screen and set needRefresh
        /// (TOWNSCN.C:519-549); the refresh paints the idle picture and the description once and
        /// presents them (:624-650). Measured at the Rainbow Parrot (GDS2C hotspot 4): the original
        /// fades out of the shop and fades the tavern in with its description; the port wiped the
        /// description in, took it down and wiped it in again, because the raise redrew it and the
        /// action's own tail redrew it a second time.
        /// </remarks>
        [UnityTest]
        public IEnumerator AScreenRaisedOverALocationPutsTheDescriptionBackOnce() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                var navigator = new HudRevealingNavigator(_hudGo) { ClosesAtOnce = true };
                BakAgain.Core.GameSession session = LocationSession(
                    new GameData.Resources.Data.SaveGameContainerShopData(
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (GameData.ShopItemCategories)0));
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade),
                    teleport: null, session: session, navigator: navigator,
                    inventoryMenu: InventoryMenu(session, navigator));
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    _hudGo.SetActive(false);
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        SceneDialogId = DescriptionId,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 5 } },
                    }, 1, 1, letter => Choose(player, letter));
                    dialogs.Moments.Clear();

                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    await Until(() => dialogs.Moments.Count > 0, "the location never came back");
                    for (var i = 0; i < 30; i++) {
                        await UniTask.Yield();
                    }

                    Assert.That(navigator.Pushed, Is.Null, "the container screen was raised and closed");
                    Assert.That(dialogs.Moments, Has.Count.EqualTo(1),
                        "the description is drawn once when the screen closes (TOWNSCN.C:624-650)");
                    StringAssert.Contains("hud down", dialogs.Moments[0],
                        "and over the location, after the HUD the pop revealed is down again");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// A shop or container closing over a location fades back onto the location with its
        /// description, never onto the travel HUD (TASK-923 item 1).
        /// </summary>
        /// <remarks>
        /// Codes 5, 6 and 8 set <c>needRefresh = fadeFlag = 1</c> when the inventory screen returns
        /// (TOWNSCN.C:519-549), and the refresh fades out, repaints the idle picture and the
        /// description bubble, and fades in (:624-650). The port's navigator pop showed the travel
        /// HUD, because a location is not on its stack: measured at the Rainbow Parrot (t906), one
        /// frame of the HUD over the world, then the bare tavern, then the description wiping in.
        /// </remarks>
        [UnityTest]
        public IEnumerator AScreenClosingOverALocationFadesBackOntoItsDescriptionNotOntoTheHud() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                var navigator = new FadingNavigator(hud, fade);
                BakAgain.Core.GameSession session = LocationSession(
                    new GameData.Resources.Data.SaveGameContainerShopData(
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (GameData.ShopItemCategories)0));
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade),
                    teleport: null, session: session, navigator: navigator,
                    inventoryMenu: InventoryMenu(session, navigator));
                SetLocations(hud, player);
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        SceneDialogId = DescriptionId,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 5 } },
                    }, 1, 1, letter => Choose(player, letter));

                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    await Until(() => navigator.Pushed != null, "the container screen was never raised");
                    dialogs.Moments.Clear();
                    fade.Events.Clear();

                    await navigator.Pop();   // the screen's own Exit
                    for (var i = 0; i < 30; i++) {
                        await UniTask.Yield();
                    }

                    Assert.That(navigator.HudShown, Is.False,
                        "the pop lands on the location, not on the travel HUD");
                    Assert.That(dialogs.Moments, Is.EqualTo(new[] { "covered, hud down, before fade-in" }),
                        "the description is drawn once, under the fade, and fades in with the picture "
                        + "(TOWNSCN.C:624-650)");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// The real navigator cuts, so the location fades the shop in and out itself (TASK-923 item 1).
        /// </summary>
        /// <remarks>
        /// The inventory screen fades the town out before drawing (CMBINV.C:232, :278); the town's
        /// refresh fades the inventory out and the town in (TOWNSCN.C:624-650).
        /// </remarks>
        [UnityTest]
        public IEnumerator TheShopOpensAndClosesUnderTheWorldFadeOnTheRealNavigator() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                var navigator = new ScreenNavigator();
                await navigator.ResetTo(new HudStandIn(hud));
                BakAgain.Core.GameSession session = LocationSession(
                    new GameData.Resources.Data.SaveGameContainerShopData(
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (GameData.ShopItemCategories)0));
                var shop = new ClosableScreen();
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade),
                    teleport: null, session: session, navigator: navigator,
                    inventoryMenu: InventoryMenu(session, navigator));
                SetLocations(hud, player);
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();
                _menuGo.SetActive(false);

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        SceneDialogId = DescriptionId,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 5 } },
                    }, 1, 1, letter => Choose(player, letter));
                    fade.Events.Clear();

                    // The container arm pushes the inventory menu; stand a closable screen in for it.
                    typeof(LocationScreen).GetMethod("PushFadedAndWaitAsync", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(screen, new object[] { shop });
                    await Until(() => ReferenceEquals(navigator.Current, shop) && fade.Events.Count == 3,
                        "the shop never opened");
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }), "into the shop under a fade");

                    fade.Events.Clear();
                    await navigator.Pop();   // the shop's own Exit
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }), "and out of it under one");
                    await Until(() => navigator.Fade is NullScreenFade, "the navigator never cut again afterwards");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// Leaving the temple's service menu puts the location back in one change (TASK-923).
        /// </summary>
        /// <remarks>
        /// After the service loop the arm's <c>needRefresh = 1</c> runs the unfaded refresh: the idle
        /// picture and the description bubble are painted and presented once (TOWNSCN.C:624-650), so
        /// the farewell page is on screen until both are. Measured at Malac's Cross (t917 SAVE02):
        /// the port took the page down and wiped the description in over the bare chapel.
        /// </remarks>
        [UnityTest]
        public IEnumerator LeavingTheServiceMenuPutsTheLocationBackInOneChange() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                BakAgain.Core.GameSession session = TempleSession(currentTemple: 1, visited: 2);
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade),
                    teleport: null, session: session, navigator: new FadingNavigator(hud, fade));
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        SceneDialogId = DescriptionId,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 13, ActionDialogId = 1300072 } },
                    }, 1, 1, letter => Choose(player, letter));
                    dialogs.Moments.Clear();

                    // The service menu answers -1 here: the loop ends at once.
                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    await Until(() => dialogs.Moments.Count > 0, "the location never came back");
                    for (var i = 0; i < 10; i++) {
                        await UniTask.Yield();
                    }

                    Assert.That(dialogs.Moments, Is.EqualTo(new[] { "frozen, hud down" }),
                        "the description is drawn under a still of the last page and appears with the "
                        + "picture in one change (TOWNSCN.C:624-650)");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>The travel HUD as the navigator's base screen.</summary>
        private sealed class HudStandIn : IScreen {
            private readonly InGameScreen _hud;
            public HudStandIn(InGameScreen hud) => _hud = hud;
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => _hud.HideAsync();
        }

        private sealed class ClosableScreen : IScreen {
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => UniTask.CompletedTask;
        }

        /// <summary>
        /// The inn's panel comes and goes under fades, with the location's sign off it (TASK-923 item 2).
        /// </summary>
        /// <remarks>
        /// <c>modalscreen_rest_until_time</c> fades the location out before drawing the panel and
        /// fades the panel out when it ends (MODALSCR.C:724-737, :830-835); code 7 then sets
        /// <c>needRefresh = fadeFlag = 1</c> (TOWNSCN.C:550-559) and the refresh fades the tavern in
        /// with its description (:624-650). Measured at the Rainbow Parrot's inn (t906): the port
        /// kept "Rainbow Parrot Inn" over the panel and cut back to the tavern.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheInnOpensAndClosesUnderAFadeAndTheLocationFadesBackIn() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var dialogs = new MomentRecordingDialogs(fade, _hudGo);
                BakAgain.Core.GameSession session = LocationSession(
                    new GameData.Resources.Data.SaveGameContainerShopData(
                        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (GameData.ShopItemCategories)0));
                // The nightmaster's offer answers -1 here: declined.
                var inn = new InnService(session, new BakAgain.Core.Services.GameClock(session), null,
                    dialogs, null);
                LocationScenePlayer player = Player(fade, dialogs, Resolver(hud, fade: fade),
                    teleport: null, session: session, navigator: new FadingNavigator(hud, fade), inn: inn);
                LocationScreen screen = _screenGo.GetComponent<LocationScreen>();

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    screen.Bind(new GdsScene(GdsSceneLoader.ResourceName(1, 1)) {
                        SceneDialogId = DescriptionId,
                        Hotspots = new[] { new GdsHotspot { ActionCode = 7 } },
                    }, 1, 1, letter => Choose(player, letter));
                    dialogs.Moments.Clear();
                    fade.Events.Clear();

                    screen.PrimaryAction(GdsSceneRules.HotspotActionIdBase);
                    await Until(() => dialogs.Moments.Count > 0, "the location never came back");
                    for (var i = 0; i < 30; i++) {
                        await UniTask.Yield();
                    }

                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in", "out", "covered", "in" }),
                        "into the inn under a fade, and back out of it under another");
                    Assert.That(dialogs.Moments, Has.Count.EqualTo(1), "the description comes back once");
                    StringAssert.StartsWith("covered", dialogs.Moments[0],
                        "while the second fade still covers the screen");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>
        /// A full-screen page replacing one left drawn opens over it, not over the location beneath
        /// (TASK-923 item 3).
        /// </summary>
        /// <remarks>
        /// In a scene the priest's reply is <c>dialog_play_record(.., 0)</c> (CHARSCRN.C:449-452),
        /// which leaves its page drawn (DIALOG.C:1492-1500), and the service loop's next
        /// <c>dialog_play_record</c> (TOWNSCN.C:597) boxes the menu page out over it: one page to the
        /// next. The port removed the reply's panel before the menu's full-screen wipe, so the wipe
        /// opened over the bare chapel (Malac's Cross, t917 SAVE02).
        /// </remarks>
        [UnityTest]
        [BakAgain.Tests.TestSupport.RequiresShippedGameData]
        public IEnumerator AFullScreenPageOpensOverThePageLeftDrawnNotOverTheLocation() =>
            UniTask.ToCoroutine(async () => {
                BakAgain.UI.DialogManager dialogs = RealDialogs();
                await dialogs.ShowByIdLeavingLastPage(ReplyDialogId);
                VisualElement reply = ActivePanel(dialogs);
                Assume.That(reply?.panel, Is.Not.Null, "the reply's page is left drawn");

                GameData.Resources.Dialog.DialogPlay menu = await dialogs.ResolveById(MenuDialogId);
                UniTask shown = dialogs.DisplayEntry(menu);
                var frames = 0;
                var bare = 0;
                while (shown.Status == UniTaskStatus.Pending && frames++ < 600) {
                    if (reply.panel == null) {
                        bare++;
                    }
                    await UniTask.Yield();
                }
                await shown;

                Assert.That(bare, Is.Zero,
                    "the reply stays under the menu's page until its wipe has opened it all");
                Assert.That(reply.panel, Is.Null, "and goes once the menu's page covers the screen");
                dialogs.ClearDialog();
            });

        private const int ReplyDialogId = 1300083;
        private const int MenuDialogId = 1300072;

        /// <summary>
        /// A navigator that transitions as the real one does: under a fade, the outgoing screen is
        /// hidden and the revealed one shown — the travel HUD, a location not being on its stack.
        /// </summary>
        private sealed class FadingNavigator : IScreenNavigator {
            private readonly InGameScreen _hud;
            private readonly IScreenFade _fade;
            public IScreen Pushed;
            public bool HudShown;
            private bool _busy;

            public FadingNavigator(InGameScreen hud, IScreenFade fade) {
                _hud = hud;
                _fade = fade;
            }

            public IScreen Current => Pushed;
            public UniTask ResetTo(IScreen root) => UniTask.CompletedTask;
            public async UniTask Push(IScreen screen) {
                _busy = true;
                await _fade.FadeOutAsync();
                await _hud.HideAsync();
                Pushed = screen;
                await _fade.FadeInAsync();
                _busy = false;
            }
            public async UniTask PushAndWaitAsync(IScreen screen) {
                await Push(screen);
                await UniTask.WaitUntil(() => Pushed == null && !_busy);
            }
            public async UniTask Pop() {
                _busy = true;
                await _fade.FadeOutAsync();
                Pushed = null;
                await _hud.ShowAsync();
                HudShown |= _hud.IsVisible;
                await _fade.FadeInAsync();
                _busy = false;
            }
            public UniTask Replace(IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        private static void SetLocations(InGameScreen hud, LocationScenePlayer player) =>
            typeof(InGameScreen).GetField("_locations", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(hud, player);

        private BakAgain.UI.Inventory.InventoryMenu InventoryMenu(BakAgain.Core.GameSession session,
            IScreenNavigator navigator) {
            _menuGo = new GameObject("InventoryMenu");
            var document = _menuGo.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            var menu = _menuGo.AddComponent<BakAgain.UI.Inventory.InventoryMenu>();
            menu.Construct(session, new BakAgain.Tests.PlayMode.UI.Inventory.NoOpResources(), navigator,
                new BakAgain.Core.Services.DialogExecutor(
                    NullLogger<BakAgain.Core.Services.DialogExecutor>.Instance, session,
                    new BakAgain.Core.Services.GameClock(session)),
                new BakAgain.Tests.PlayMode.UI.Inventory.NoOpDialogs());
            return menu;
        }

        /// <summary>
        /// A move queued while a location is open waits for the location to close, whatever brings
        /// the travel HUD back meanwhile (TASK-917).
        /// </summary>
        /// <remarks>
        /// The original's world loop does not run while <c>townscene_main_loop</c> does: the move a
        /// temple teleport queues is made by the world loop's next pass after the scene returns
        /// (WORLDLP.C:94-95). The port's pump runs every frame the travel HUD is visible, and a
        /// screen raised over a location brings the HUD back when it pops. Measured in the Temple of
        /// Ishap after a teleport from Malac's Cross: declining a second teleport popped the map,
        /// the pump took the first teleport's still-queued move, and the zone was rebuilt under the
        /// temple.
        /// </remarks>
        [UnityTest]
        public IEnumerator AQueuedMoveWaitsForTheLocationToClose() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var fade = new RecordingFade();
                var teleport = new GameData.Resources.Location.PendingTeleport();
                LocationScenePlayer player = Player(fade, new MomentRecordingDialogs(fade, _hudGo),
                    Resolver(hud), teleport);
                var flow = new BakAgain.Core.Services.GameFlow(
                    NullLogger<BakAgain.Core.Services.GameFlow>.Instance, null,
                    new BakAgain.Core.GameSession(), null, null, null, null, null, null, null, null, null,
                    null, null, null, teleport: teleport, locations: player);

                try {
                    UniTask run = player.RunAsync(1, 1);
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");
                    // The world half a temple teleport leaves behind once its scene half is taken.
                    teleport.Queue(new GameData.Resources.Location.TeleportDestination {
                        Location = new GameData.Resources.Location.Location { ZoneNumber = 1 }, Id = 1,
                    });

                    typeof(BakAgain.Core.Services.GameFlow)
                        .GetMethod("PumpWorldLoop", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(flow, null);

                    Assert.That(teleport.HasAnything, Is.True,
                        "the world loop does not run under an open location: the move waits for it to close");
                } finally {
                    player.Hide();
                }
            });

        /// <summary>The tavern's container, at the location's own (zone 15, scene, letter).</summary>
        private static BakAgain.Core.GameSession TavernSession(byte bardingDifficulty, byte fund,
            byte performerBarding) {
            var session = LocationSession(new GameData.Resources.Data.SaveGameContainerShopData(
                0, 0, 0, 0, 0, 0, bardingDifficulty, fund, fund, lastRestockChapter: 10, 0, 0, 0, 0,
                (GameData.ShopItemCategories)0));
            var stats = new GameData.Resources.Character.ActorStat[
                GameData.Resources.Character.StatEngine.TableSize];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new GameData.Resources.Character.ActorStat { Base = 0, Max = 100 };
            }
            stats[(int)GameData.ActorAttribute.Barding].Base = performerBarding;
            session.SetActorStatsForTest(0, stats);
            session.SetActiveParty(1, new byte[] { 0 });
            return session;
        }

        /// <summary>A temple's container: its number, a flat fare, and one other temple visited.</summary>
        private static BakAgain.Core.GameSession TempleSession(byte currentTemple, int visited) {
            var session = LocationSession(new GameData.Resources.Data.SaveGameContainerShopData(
                currentTemple, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (GameData.ShopItemCategories)100));
            session.SetGlobalValue(GameData.Resources.Location.TeleportMenu.VisitedFlagFor(visited), 1);
            session.PartyGold = 5000;
            return session;
        }

        private static BakAgain.Core.GameSession LocationSession(
            GameData.Resources.Data.SaveGameContainerShopData shop) {
            var container = new GameData.Resources.Data.SaveGameContainerData(
                new GameData.Resources.Data.SaveGameContainerLocationData(zone: 15, minChapter: 0,
                    maxChapter: 10, worldItemId: 0, x: 1, y: 1, actorNumber: 1),
                GameData.Resources.Data.SaveGameContainerType.FixedWorldItem, numberOfItems: 0,
                capacity: 5, dataTypes: GameData.Resources.Data.SaveGameContainerDataType.Shop,
                items: new GameData.Resources.Data.SaveGameInventoryItemData[5], lockData: null,
                dialogData: null, shopData: shop, encounterData: null, timestamp: 0,
                globalStateIndex: null);
            var session = new BakAgain.Core.GameSession();
            session.SetZoneContainersForTest(new GameData.Resources.Data.SaveGameZoneContainerStateData(new[] {
                new GameData.Resources.Data.SaveGameZoneContainerEntryData(15, 0, new[] { container }),
            }), chapter: 1);
            return session;
        }

        /// <summary>
        /// A navigator whose pop brings the travel HUD back, as <c>EnsureTopShownAsync</c> does: the
        /// location is not on its stack, so the HUD is what the pop reveals.
        /// </summary>
        private sealed class HudRevealingNavigator : IScreenNavigator {
            private readonly GameObject _hud;
            public IScreen Pushed;

            public HudRevealingNavigator(GameObject hud) {
                _hud = hud;
            }

            /// <summary>The pushed screen is closed by the player at once (its own Exit pops it).</summary>
            public bool ClosesAtOnce;

            public IScreen Current => Pushed;
            public UniTask ResetTo(IScreen root) => UniTask.CompletedTask;
            public UniTask Push(IScreen screen) {
                Pushed = screen;
                return UniTask.CompletedTask;
            }
            public async UniTask PushAndWaitAsync(IScreen screen) {
                await Push(screen);
                if (ClosesAtOnce) {
                    await Pop();
                }
            }
            public UniTask Pop() {
                Pushed = null;
                _hud.SetActive(true);
                return UniTask.CompletedTask;
            }
            public UniTask Replace(IScreen screen) => UniTask.CompletedTask;
            public UniTask Clear() => UniTask.CompletedTask;
        }

        /// <summary>The real dialog overlay, with the hotspot's page and the location's description.</summary>
        private BakAgain.UI.DialogManager RealDialogs() {
            _dialogGo = new GameObject("DialogOverlay");
            var document = _dialogGo.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = _panelSettings;
            document.sortingOrder = 10;

            // An AutoAdvanceTimer page closes itself (TASK-912), so the play ends with no input. The
            // shipped 1500029 waits for a click instead; both end in the same narrative teardown.
            var action = new GameData.Resources.Dialog.DialogEntry {
                Offset = 100, Id = ActionDialogId, Text = "\tDown.",
                Flags = GameData.Resources.Dialog.DialogEntryFlags.Legacy10
                    | GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer,
                Actions = new List<GameData.Resources.Dialog.Actions.DialogActionBase>(),
                Branches = new List<GameData.Resources.Dialog.Branches.DialogBranchBase>(),
            };
            var description = new GameData.Resources.Dialog.DialogEntry {
                Offset = 200, Id = DescriptionId, Text = "\tThe square.",
                Actions = new List<GameData.Resources.Dialog.Actions.DialogActionBase>(),
                Branches = new List<GameData.Resources.Dialog.Branches.DialogBranchBase>(),
            };
            // Barding's thrown-out line and the temple's arrival page, both closing themselves.
            GameData.Resources.Dialog.DialogEntry Closing(int offset, int id, string text) => new() {
                Offset = offset, Id = (uint)id, Text = text,
                Flags = GameData.Resources.Dialog.DialogEntryFlags.Legacy10
                    | GameData.Resources.Dialog.DialogEntryFlags.AutoAdvanceTimer,
                Actions = new List<GameData.Resources.Dialog.Actions.DialogActionBase>(),
                Branches = new List<GameData.Resources.Dialog.Branches.DialogBranchBase>(),
            };
            // The temple priest's reply and the service menu: full-screen pages.
            GameData.Resources.Dialog.DialogEntry reply = Closing(600, ReplyDialogId, "\tThe priest shrugged.");
            reply.DialogType = GameData.Resources.Dialog.DialogType.PlainFullScreen;
            GameData.Resources.Dialog.DialogEntry menu = Closing(700, MenuDialogId, "\tLocklear pulled the curtains aside.");
            menu.DialogType = GameData.Resources.Dialog.DialogType.PlainFullScreen;
            var dialog = new GameData.Resources.Dialog.Dialog("dial_z15");
            foreach (GameData.Resources.Dialog.DialogEntry e in new[] {
                         action, description, reply, menu, Closing(300, ThrownOutDialogId, "\tOut, out, out."),
                         Closing(400, GameData.Resources.Location.TeleportMenu.ArrivalDialog, "\tA new temple wall."),
                         Closing(500, GameData.Resources.Location.TeleportMenu.IntroDialog, "\tThe design was odd."),
                     }) {
                dialog.Entries.Add(e);
                e.Key = GameData.Resources.Content.ContentKey.ForBase("ddx:dial_z15", e.Offset);
            }

            var session = new BakAgain.Core.GameSession();
            var executor = new BakAgain.Core.Services.DialogExecutor(
                NullLogger<BakAgain.Core.Services.DialogExecutor>.Instance, session, clock: null, provider: null);
            executor.UseResourceLoader(new OneDialogLoader(dialog));
            var manager = _dialogGo.AddComponent<BakAgain.UI.DialogManager>();
            manager.Construct(cursorManager: null, stack: null, menuSound: null, gameSession: session,
                executor: executor, preferences: new BakAgain.Tests.Editor.World.FakePreferencesService());
            return manager;
        }

        private static VisualElement ActivePanel(BakAgain.UI.DialogManager dialogs) =>
            (VisualElement)typeof(BakAgain.UI.DialogManager)
                .GetField("_activePanel", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(dialogs);

        private sealed class OneDialogLoader : BakAgain.UI.IDialogResourceLoader {
            private readonly GameData.Resources.Dialog.Dialog _dialog;

            public OneDialogLoader(GameData.Resources.Dialog.Dialog dialog) {
                _dialog = dialog;
            }

            public UniTask<GameData.Resources.Dialog.Dialog> LoadDialogAsync(int dialogId) =>
                UniTask.FromResult(_dialog);

            public UniTask<GameData.Resources.Location.TeleportDestinationSet> GetTeleportDestinationsAsync() =>
                UniTask.FromResult(new GameData.Resources.Location.TeleportDestinationSet("TELEPORT.DAT"));
        }

        /// <summary>
        /// Leaving a town with a teleport queued (TASK-906 item 3): the step is charged first, then
        /// the party moves under the one fade that takes the town down.
        /// </summary>
        /// <remarks>
        /// The town returns into the world loop's dispatch pass, which charges the step there and
        /// then (WORLDLP.C:191-198). Only the NEXT pass sees the queued record and moves the party
        /// (:94-95, modalscreen_pending_scene_trans), and its answer raises the one fade-out /
        /// repaint / fade-in (:102-139), so the destination is what fades in. The port faded the
        /// old position in, then moved, and charged the step after the move.
        /// </remarks>
        [UnityTest]
        public IEnumerator LeavingWithAQueuedTeleportChargesTheStepFirstAndMovesUnderTheSameFade() =>
            UniTask.ToCoroutine(async () => {
                InGameScreen hud = TravelHud();
                var flow = new RecordingFlow();
                var fade = new RecordingFade();
                var teleport = new GameData.Resources.Location.PendingTeleport();
                // The location keeps its own session (its loader reads only the chapter); the step,
                // the clock and the gate are the rig's.
                LocationScenePlayer player = Player(fade, new MomentRecordingDialogs(fade, _hudGo),
                    Resolver(hud, flow), teleport);
                TownGateStepTests.Rig rig = TownGateStepTests.Build(player, () => flow);
                flow.Rig = rig;
                flow.Fade = fade;

                try {
                    long before = rig.Clock.Ticks;
                    rig.Dialogs.Answer.TrySetResult(0);   // Yes, as soon as it is asked
                    rig.Movement.MoveForward();
                    await Until(() => fade.Events.Contains("in"), "the town never faded in");

                    // TELEPORT.DAT row 19, the Krondor sewer door's (GDS2E hotspot 0, dialog 2300001).
                    fade.Events.Clear();
                    teleport.Queue(new GameData.Resources.Location.TeleportDestination {
                        Location = new GameData.Resources.Location.Location {
                            ZoneNumber = 11, X = 10, Y = 10, XOffset = 38, YOffset = 22,
                        },
                        Id = 19,
                    });
                    Choose(player, 0);
                    await Until(() => flow.At != null && fade.Events.Contains("in"), "the party never moved");
                    await UniTask.Yield();
                    await UniTask.Yield();

                    Assert.That(flow.At.Value.Ticks, Is.EqualTo(before + TownGateStepTests.TicksPerLargeStep),
                        "the step is charged before the teleport moves the party (WORLDLP.C:191-198, then :94-95)");
                    Assert.That(flow.At.Value.Covered, Is.True,
                        "the party moves while the town's fade-out still covers the screen");
                    Assert.That(fade.Events, Is.EqualTo(new[] { "out", "covered", "in" }),
                        "one fade: out of the town and in on the destination (WORLDLP.C:102-139)");
                    Assert.That(rig.Clock.Ticks, Is.EqualTo(before + TownGateStepTests.TicksPerLargeStep),
                        "and the step is charged once");
                } finally {
                    player.Hide();
                }
            });

        // A travel HUD that is on screen, which HideAsync takes down synchronously, so "down" is
        // observable without waiting.
        private InGameScreen TravelHud() {
            _hudGo = new GameObject("TravelHud");
            _hudGo.SetActive(false);
            InGameScreen hud = _hudGo.AddComponent<InGameScreen>();
            // Twice, as the original test did: the first activation runs the screen's own OnEnable,
            // which takes it down again.
            _hudGo.SetActive(true);
            _hudGo.SetActive(true);
            Assume.That(_hudGo.activeSelf, Is.True, "the HUD starts on screen");
            return hud;
        }

        private static VContainer.IObjectResolver Resolver(InGameScreen hud,
            BakAgain.Core.Services.IGameFlow flow = null, IScreenFade fade = null) {
            var builder = new VContainer.ContainerBuilder();
            builder.RegisterInstance(hud);
            if (fade != null) {
                builder.RegisterInstance(fade).As<IScreenFade>();
            }
            if (flow != null) {
                builder.RegisterInstance(flow).As<BakAgain.Core.Services.IGameFlow>();
            }
            return builder.Build();
        }

        private LocationScenePlayer Player(RecordingFade fade, BakAgain.UI.IDialogManager dialogs,
            VContainer.IObjectResolver resolver, GameData.Resources.Location.PendingTeleport teleport,
            string undescribed = null, BakAgain.Core.GameSession session = null,
            BakAgain.UI.Teleport.TeleportScreen teleportScreen = null,
            BakAgain.UI.Navigation.IScreenNavigator navigator = null,
            BakAgain.UI.Inventory.InventoryMenu inventoryMenu = null, InnService inn = null) {
            _screenGo = new GameObject("Location");
            LocationScreen screen = _screenGo.AddComponent<LocationScreen>();
            session ??= new BakAgain.Core.GameSession();
            var cache = new TownCache { Undescribed = undescribed };
            // The screen's own resolver only where a test raises something over the location: it is
            // what finds the travel HUD and the fade.
            screen.Construct(dialogs, null, session, teleport, teleportScreen, null, inn, cache,
                inventoryMenu, navigator, navigator != null ? resolver : null);

            var presenter = new CutscenePresenter(NullLogger<CutscenePresenter>.Instance, cache,
                new HoldingPlayerFactory(), null, null);
            return new LocationScenePlayer(NullLogger<LocationScenePlayer>.Instance, presenter,
                new CanvasView(_canvasGo = new GameObject("LocationCanvas")),
                new GdsSceneLoader(cache, session), cache, screen, teleport, resolver, fade);
        }

        /// <summary>What a hotspot's transition hands the loop (LocationScreen's bound callback).</summary>
        private static void Choose(LocationScenePlayer player, int letter) =>
            typeof(LocationScenePlayer)
                .GetField("_pendingTransition", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(player, (int?)letter);

        private static async UniTask Until(System.Func<bool> condition, string failure) {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!condition()) {
                if (Time.realtimeSinceStartup > deadline) {
                    Assert.Fail(failure);
                }
                await UniTask.Yield();
            }
        }

        /// <summary>Records the clock and the cover at the moment the party is moved.</summary>
        private sealed class RecordingFlow : BakAgain.Core.Services.IGameFlow {
            public TownGateStepTests.Rig Rig;
            public RecordingFade Fade;
            public (long Ticks, bool Covered)? At;

            public UniTask<GameData.Resources.Location.ZoneTransitionKind> TransitionTo(
                GameData.Resources.Location.Location destination) {
                At = (Rig.Clock.Ticks, Fade.Covered);
                return UniTask.FromResult(GameData.Resources.Location.ZoneTransitionKind.ChangeZone);
            }

            public UniTask<bool> LoadSave(string path) => UniTask.FromResult(false);
            public UniTask Boot() => UniTask.CompletedTask;
            public UniTask ShowMainMenu() => UniTask.CompletedTask;
            public UniTask StartNewGame(bool playChapterScenes = true) => UniTask.CompletedTask;
            public UniTask GoToChapter(int chapter) => UniTask.CompletedTask;
            public UniTask EnterWorld() => UniTask.CompletedTask;
        }

        /// <summary>A fade that takes a few frames to cover, as the real one does.</summary>
        private sealed class RecordingFade : IScreenFade {
            public readonly List<string> Events = new();
            public bool Covered;
            public bool Frozen;
            public System.Action OnFreeze;

            public async UniTask FadeOutAsync() {
                Events.Add("out");
                await UniTask.Yield();
                await UniTask.Yield();
                Covered = true;
                Events.Add("covered");
            }

            public UniTask FadeInAsync() {
                Covered = false;
                Events.Add("in");
                return UniTask.CompletedTask;
            }

            public async UniTask CutAsync(System.Func<UniTask> change) {
                Events.Add("freeze");
                OnFreeze?.Invoke();
                Frozen = true;
                try {
                    await change();
                } finally {
                    Frozen = false;
                    Events.Add("thaw");
                }
            }
        }

        /// <summary>Records the screen's state each time the description is asked for.</summary>
        private sealed class MomentRecordingDialogs : EndChapterHotspotTests.ClearCountingDialogs {
            private readonly RecordingFade _fade;
            private readonly GameObject _hud;

            public MomentRecordingDialogs(RecordingFade fade, GameObject hud) {
                _fade = fade;
                _hud = hud;
            }

            public readonly List<string> Moments = new();

            public override UniTask<GameData.Resources.Dialog.DialogPlay> ResolveById(int id,
                CancellationToken cancellationToken = default) {
                if (id == DescriptionId) {
                    string hud = _hud.activeSelf ? ", HUD UP" : ", hud down";
                    Moments.Add(_fade.Frozen
                        ? "frozen" + hud
                        : (_fade.Covered ? "covered" : "NOT covered") + hud
                            + (_fade.Events.Contains("in") ? ", AFTER fade-in" : ", before fade-in"));
                }
                return UniTask.FromResult<GameData.Resources.Dialog.DialogPlay>(null);
            }
        }

        /// <summary>
        /// One scene with an entry and an idle script, both empty, so the presenter runs straight
        /// to its hold. No REQ layout, so no hotspots are built — the order is what is under test.
        /// </summary>
        private sealed class TownCache : IResourceCache {
            /// <summary>The one scene file that has no description, or null.</summary>
            public string Undescribed;

            public UniTask<T> GetOrLoadAsync<T>(string key) where T : class {
                object result = null;
                if (typeof(T) == typeof(GdsScene)) {
                    result = new GdsScene(key) {
                        AnimationResource = "G_TOWN", EntryAnimationTag = 1, IdleAnimationTag = 2,
                        SceneDialogId = key == Undescribed ? 0 : DescriptionId,
                    };
                } else if (typeof(T) == typeof(AnimatorResource)) {
                    result = new AnimatorResource("G_TOWN.ADS") {
                        ResourceFiles = new Dictionary<int, string> { [0] = "G_TOWN.TTM" },
                        Animations = new List<AnimatorScript> {
                            new() { Id = 1, Tag = "ENTRY", Script = "" },
                            new() { Id = 2, Tag = "IDLE", Script = "" },
                        },
                    };
                } else if (typeof(T) == typeof(AnimationResource)) {
                    result = new AnimationResource("TTM", "V1.0", new Dictionary<int, string>(), new List<Frame>());
                } else if (typeof(T) == typeof(PaletteResource)) {
                    result = new PaletteResource("options.pal");
                } else if (typeof(T) == typeof(GameData.Resources.Location.TeleportDestinationSet)) {
                    result = new GameData.Resources.Location.TeleportDestinationSet(key) {
                        // Indexed by row: row 0 is the Temple of Kilian's, unused here.
                        Destinations = {
                            new GameData.Resources.Location.TeleportDestination {
                                Location = new GameData.Resources.Location.Location { ZoneNumber = 1 },
                            },
                            new GameData.Resources.Location.TeleportDestination {
                                Id = GameData.Resources.Location.TeleportMenu.DestinationIdForTemple(2),
                                GdsNumber = 1, GdsLetter = 2,
                                Location = new GameData.Resources.Location.Location { ZoneNumber = 1 },
                            },
                        },
                    };
                }
                return UniTask.FromResult((T)result);
            }

            public void Clear() { }
        }

        private sealed class HoldingPlayerFactory : ICutscenePlayerFactory {
            public CutscenePlayer Create(CutsceneState cutsceneState) =>
                new(cutsceneState, null, null, new HoldingProcessor(), null);
        }

        /// <summary>Holds until the location's token is cancelled; nothing else is reached.</summary>
        private sealed class HoldingProcessor : ICutsceneFrameProcessor {
            public UniTask ProcessFrameRuntimeAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
                CancellationToken cancellationToken, System.Func<UniTask> showDialogs = null) => UniTask.CompletedTask;
            public UniTask ProcessFrameEditorAsync(IEnumerable<FrameCommand> commands, CutsceneState state,
                CancellationToken cancellationToken) => UniTask.CompletedTask;
            public UniTask PreloadFrameResourcesAsync(IEnumerable<FrameCommand> commands, CutsceneState state) =>
                UniTask.CompletedTask;
            public void PreloadFrameResourcesEditor(IEnumerable<FrameCommand> commands, CutsceneState state) { }
            public UniTask HoldPaletteCyclesAsync(CutsceneState state, CancellationToken cancellationToken) =>
                UniTask.WaitUntilCanceled(cancellationToken);
        }

        /// <summary>A view with a real canvas, so <c>new CutsceneState</c> gets past construction.</summary>
        private sealed class CanvasView : ICutsceneView {
            private readonly BakAgain.Graphics.UiImage _canvas;

            public CanvasView(GameObject host) {
                RawImage raw = host.AddComponent<RawImage>();
                raw.texture = new Texture2D(8, 8);
                _canvas = new BakAgain.Graphics.UiImage(raw);
            }

            public BakAgain.Graphics.UiImage Canvas => _canvas;
            public void Show() { }
            public void Hide() { }
            public UniTask ShowAsync() => UniTask.CompletedTask;
            public UniTask HideAsync() => UniTask.CompletedTask;
        }
    }
}

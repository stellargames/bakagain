namespace BakAgain.UI {
    using BakAgain.Core;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData.Resources.Inventory;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The map a note shows — the picture half of <see cref="NoteMapView"/>.
    /// </summary>
    /// <remarks>
    /// <b>The original does not open a screen for this.</b> It blits RIFTMAP into the frame and
    /// holds a dialog over it, so the map is backdrop for a line of text rather than a place the
    /// player navigates. This reproduces that shape: full-screen art, an optional marker, and a
    /// dialog that dismisses the whole thing when it closes.
    ///
    /// <para>The marker's position is <see cref="NoteMapView"/>'s and is expressed as a FRACTION of
    /// the map, not in canonical units — the art is a 320x200 canvas whose pixels the projection is
    /// written in, so a fraction survives the stage being any size. That is also why nothing here
    /// carries a coordinate of its own.</para>
    /// </remarks>
    public sealed class RiftMapScreen : MonoBehaviour, Navigation.IScreen {
        /// <summary>The space the map projection is written in (<c>NoteMapView</c>'s VGA canvas).</summary>
        private const float MapPixelsAcross = 320f;

        private const float MapPixelsDown = 200f;

        private const string MarkerName = "BakRiftMapMarker";

        private ILogger _logger;
        private GameSession _session;
        private IDialogManager _dialogs;
        private IResourceCache _resources;

        [Inject]
        public void Construct(GameSession session, IDialogManager dialogs, IResourceCache resources) {
            _session = session;
            _dialogs = dialogs;
            _resources = resources;
        }

        private void Awake() =>
            _logger = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                .CreateLogger<RiftMapScreen>(LogManager.LoggerFactory);

        /// <summary>Which map to show, set before the screen is pushed.</summary>
        /// <remarks>The typed pre-push setter pattern — navigation design doc 3.2.</remarks>
        public void SetMap(int mapId) => _mapId = mapId;

        private int _mapId = NoteMapView.RiftMapId;

        public async UniTask ShowAsync() {
            gameObject.SetActive(true);

            // *** THE DIALOG BRANCHES ON THE ZONE. *** Use_Item writes the current zone into global
            // 30000 immediately before showing this line (0x59292), and the entry picks its wording
            // from it — without the write the map always says "if only we were in a place we could
            // use it", whatever zone the party is standing in.
            _session.SetGlobalValue(ZoneSelectorGlobal, _session.CurrentZone);

            // Loaded BEFORE the marker is drawn, not as a side effect of drawing it: PaletteColors
            // answers a palette it has not got with the FALLBACK rather than complaining, so a
            // late load paints the marker silently wrong instead of failing. This project has shipped
            // that bug twice.
            _palette ??= await _resources.GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(
                GameData.PaletteMapping.GetPaletteFor(NoteMapView.MapBackground));
            await DrawMarkerAsync();
        }

        /// <summary>
        /// Shows a map and holds it until its line is dismissed — the whole of what a map note does.
        /// </summary>
        /// <remarks>
        /// <b>The dialog IS the screen's lifetime</b>: the map stands behind it and goes when it
        /// does, which is the original's shape — it never opened a navigable screen for this.
        ///
        /// <para><b>Push, dialog, pop — from OUT here, never from inside <see cref="ShowAsync"/>.</b>
        /// The navigator serializes its operations: an op enqueues and only drains the queue when
        /// nothing else is pumping. <c>ShowAsync</c> runs INSIDE the push's own drain, so a Pop
        /// raised there queues behind a push that cannot finish until <c>ShowAsync</c> returns —
        /// a deadlock that leaves the map on screen for ever with the whole stack wedged behind it
        /// (measured 2026-09-12: <c>pumping=True queued=1</c>, nothing clickable afterwards).
        /// TempleHeal, CharacterSheet and Teleport all run this same shape from their own
        /// <c>RunAsync</c>, which is why none of them wedge.</para>
        /// </remarks>
        public async UniTask RunAsync(int mapId) {
            SetMap(mapId);
            await _navigator.Push(this);
            await _dialogs.ShowById(NoteMapView.MapShownDialogId);
            if (_navigator.Current == (Navigation.IScreen)this) {
                await _navigator.Pop();
            }
        }

        public UniTask HideAsync() {
            gameObject.SetActive(false);

            return UniTask.CompletedTask;
        }

        private Navigation.IScreenNavigator _navigator;

        [Inject]
        public void ConstructNavigator(Navigation.IScreenNavigator navigator) => _navigator = navigator;

        /// <summary>
        /// Paints the "you are here" box, in the one zone that has one.
        /// </summary>
        /// <remarks>
        /// <b>Everywhere but that zone the map carries no marker at all</b>, which is the whole of
        /// its zone-dependence — drawing one always would show the player a confident position the
        /// original never claims.
        /// </remarks>
        private async UniTask DrawMarkerAsync() {
            VisualElement stage = await WaitForStageAsync();
            if (stage == null) {
                return;
            }
            stage.Q<VisualElement>(MarkerName)?.RemoveFromHierarchy();

            if (!NoteMapView.HasImage(_mapId) || !NoteMapView.ShowsMarker(_session.CurrentZone)) {
                return;
            }

            // PositionX/Y, not WorldX/Y: the projection subtracts a 640000 origin and divides by
            // ~2300 per map pixel, so it wants the fine world position the camera carries. WorldX/Y
            // are the tile bytes and would collapse the whole map onto its origin.
            (int x, int y) = NoteMapView.MarkerTopLeft(_session.PositionX, _session.PositionY);
            var marker = new VisualElement {
                name = MarkerName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = Length.Percent(x / MapPixelsAcross * 100f),
                    top = Length.Percent(y / MapPixelsDown * 100f),
                    width = Length.Percent(NoteMapView.MarkerWidth / MapPixelsAcross * 100f),
                    height = Length.Percent(NoteMapView.MarkerHeight / MapPixelsDown * 100f),
                    backgroundColor = MarkerColour(),
                },
            };
            stage.Add(marker);
        }

        /// <summary>
        /// The stage, once the background loader has built one.
        /// </summary>
        /// <remarks>
        /// <b>Activating the object does not give you a stage in the same frame.</b>
        /// <c>BackgroundImageLoader</c> builds on OnEnable and its load is async, so drawing
        /// immediately after SetActive finds an empty document and silently skips the marker —
        /// which is exactly what the first cut of this screen did.
        /// </remarks>
        private async UniTask<VisualElement> WaitForStageAsync() {
            UIDocument document = GetComponent<UIDocument>();
            for (var frame = 0; frame < StageWaitFrames; frame++) {
                VisualElement stage = CanonicalStage.Find(document?.rootVisualElement);
                if (stage != null) {
                    return stage;
                }
                await UniTask.Yield();
            }

            _logger.LogWarning("RiftMapScreen: no canonical stage after {Frames} frames; "
                + "the map is shown without its marker.", StageWaitFrames);

            return null;
        }

        /// <summary>Bounded so a background that never loads cannot hang the screen.</summary>
        private const int StageWaitFrames = 30;

        /// <summary>
        /// Global 30000, which this dialog branches on. It answers to other names elsewhere
        /// (<c>InnStay.RepeatOfferGlobal</c>, <c>TeleportMenu.HelpTopicGlobal</c>) — one scratch
        /// global the original reuses per screen, not three.
        /// </summary>
        private const int ZoneSelectorGlobal = 30000;

        /// <summary>The marker's colour — pen 0x6C of the map's palette.</summary>
        /// <remarks>
        /// The fallback is magenta rather than something plausible: a palette that failed to load
        /// should look broken, not like a slightly-off marker sitting in the wrong place.
        /// </remarks>
        private Color MarkerColour() =>
            PaletteColors.ResolvePen(_palette, NoteMapView.MarkerPen, Color.magenta);

        private GameData.Resources.Palette.PaletteResource _palette;
    }
}

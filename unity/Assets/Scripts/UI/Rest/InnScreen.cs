namespace BakAgain.UI.Rest {
    using BakAgain.Core;
    using BakAgain.Core.Services;
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using Cysharp.Threading.Tasks;
    using GameData.Money;
    using GameData.Resources.Config;
    using GameData.Resources.Text;
    using UnityEngine;
    using UnityEngine.UIElements;
    using VContainer;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// What the player watches while a bought night passes — <c>UI_RestUntilTime</c> @0x4ff5c.
    /// </summary>
    /// <remarks>
    /// <b>A full screen, unlike camping.</b> The camp panel is an overlay that leaves the travel HUD
    /// standing around it; the inn is entered from a location scene, and the original clears the
    /// frame and draws its own bevel. So this is a plain <c>UIDocument</c> with no REQ behind it —
    /// there are no buttons on an inn's rest screen, and nothing to navigate.
    ///
    /// <para>The dial is <see cref="RestDialView"/>, shared with camping; the geometry is
    /// <see cref="InnScreenLayout"/>. This class only assembles them and keeps them current as the
    /// hours pass.</para>
    /// </remarks>
    public sealed class InnScreen : MonoBehaviour {
        private const string PanelName = "BakInnPanel";
        private const string FrameName = "BakInnFrame";
        private const string PurseName = "BakInnPurse";

        /// <summary>ENCAMP.SCX — the dial artwork, the same sub-rect the camp screen cuts.</summary>
        private const string Backdrop = "ENCAMP.SCX";

        /// <summary>The palette the original loads for this screen (0x4ffd8).</summary>
        private const string Palette = "INVENTOR.PAL";

        private IResourceCache _resources;
        private GameSession _session;
        private IGameClock _clock;
        private ILogger _logger;
        private EncampData _encamp;
        private GameData.Resources.Palette.PaletteResource _palette;
        private int _wakeHour = -1;
        private readonly RestPartyTableView _partyTable = new();

        [Inject]
        public void Construct(IResourceCache resources, GameSession session, IGameClock clock) {
            _resources = resources;
            _session = session;
            _clock = clock;
            _logger = Microsoft.Extensions.Logging.LoggerFactoryExtensions
                .CreateLogger<InnScreen>(LogManager.LoggerFactory);
        }

        /// <summary>Brings the screen up for a stay ending at <paramref name="wakeHour"/>.</summary>
        public void Open(int wakeHour) {
            _wakeHour = wakeHour;
            gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>Takes it down again.</summary>
        public void Close() {
            _wakeHour = -1;
            gameObject.SetActive(false);
        }

        /// <summary>True while the screen is up.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>
        /// Redraws the parts that change: the dial's marked hour and the purse.
        /// </summary>
        /// <remarks>
        /// Called once an hour by the stay, which is what the original's per-frame repaint of the
        /// panel amounts to — the only things that move are the hour marker and, once, the gold.
        /// </remarks>
        public void Refresh() => RefreshAsync().Forget();

        private void OnEnable() => Refresh();

        private async UniTaskVoid RefreshAsync() {
            VisualElement stage = CanonicalStage.GetOrCreate(
                GetComponent<UIDocument>()?.rootVisualElement, null);
            if (stage == null || _resources == null) {
                return;
            }

            // The palette FIRST. The frame and the purse both resolve pens through it, and a pen
            // resolved against a null palette silently falls back to plain black — so loading it
            // as a side effect of drawing the purse left the bevel flat on the first pass and
            // correct only after some later redraw.
            _palette ??= await _resources
                .GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(Palette);

            await BuildPanelAsync(stage);
            BuildFrame(stage);
            await BuildDialAsync(stage);
            // The same table camping draws — the original's call takes no arguments and writes at
            // absolute positions, so the inn's panel gets the identical one.
            _partyTable.Build(stage, _session, _palette);
            BuildPurse(stage);
        }

        /// <summary>
        /// Draws ENCAMP.SCX clipped to the dial panel's sub-rect.
        /// </summary>
        /// <remarks>
        /// The same window-and-shift trick the camp screen uses, and for the same reason: ENCAMP.SCX
        /// is a full 320x200 canvas carrying the panel plus a copy of frame chrome, and everything
        /// outside the panel is palette index 0 — which the SCX converter bakes as opaque black.
        /// Painted across the stage it would be a black screen with a dial in it.
        /// </remarks>
        private async UniTask BuildPanelAsync(VisualElement stage) {
            if (stage.Q<VisualElement>(PanelName) != null) {
                return; // the artwork never changes; only the dial on top of it does
            }

            var backdrop = await _resources.GetOrLoadAsync<Sprite>(Backdrop);
            if (backdrop == null) {
                _logger.LogError("InnScreen: {Backdrop} did not load; the panel has no artwork.",
                    Backdrop);

                return;
            }

            var window = new VisualElement {
                name = PanelName,
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = InnScreenLayout.PanelX, top = InnScreenLayout.PanelY,
                    width = InnScreenLayout.PanelWidth, height = InnScreenLayout.PanelHeight,
                    overflow = Overflow.Hidden,
                },
            };
            window.Add(new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = -InnScreenLayout.PanelX, top = -InnScreenLayout.PanelY,
                    width = Canonical.Width, height = Canonical.Height,
                    backgroundImage = new StyleBackground(backdrop),
                },
            });
            stage.Add(window);
        }

        /// <summary>
        /// The bevelled frame: four lines twice over, plus the four corner pixels.
        /// </summary>
        /// <remarks>
        /// <b>Which edge gets which pen is the whole effect.</b> Left and bottom are the shaded
        /// pair, top and right the lit one, and the inner and outer rectangles use different pens
        /// again — four in total. Drawn AFTER the panel because it lands on the artwork's outermost
        /// row, not around it (see <see cref="InnScreenLayout.PanelHeight"/>).
        /// </remarks>
        private void BuildFrame(VisualElement stage) {
            stage.Q<VisualElement>(FrameName)?.RemoveFromHierarchy();
            var frame = new VisualElement {
                name = FrameName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };

            AddEdges(frame, InnScreenLayout.FrameInnerX, InnScreenLayout.FrameInnerY,
                InnScreenLayout.FrameInnerRight, InnScreenLayout.FrameInnerBottom,
                InnScreenLayout.FrameInnerShadowPen, InnScreenLayout.FrameInnerLightPen);
            AddEdges(frame, InnScreenLayout.FrameOuterX, InnScreenLayout.FrameOuterY,
                InnScreenLayout.FrameOuterRight, InnScreenLayout.FrameOuterBottom,
                InnScreenLayout.FrameOuterShadowPen, InnScreenLayout.FrameOuterLightPen);

            stage.Add(frame);
        }

        private void AddEdges(VisualElement frame, int left, int top, int right, int bottom,
            int shadowPen, int lightPen) {
            const int thickness = InnScreenLayout.FrameLineWidth;
            Color shadow = PaletteColors.ResolvePen(_palette, shadowPen, Color.black);
            Color light = PaletteColors.ResolvePen(_palette, lightPen, Color.white);

            AddLine(frame, left, top, thickness, bottom - top, shadow);            // left
            AddLine(frame, left, bottom, right - left + thickness, thickness, shadow); // bottom
            AddLine(frame, left, top, right - left, thickness, light);             // top
            AddLine(frame, right, top, thickness, bottom - top, light);            // right
        }

        private static void AddLine(VisualElement frame, int x, int y, int width, int height,
            Color colour) =>
            frame.Add(new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x, top = y, width = width, height = height,
                    backgroundColor = colour,
                },
            });

        private async UniTask BuildDialAsync(VisualElement stage) {
            _encamp ??= await _resources.GetOrLoadAsync<EncampData>("ENCAMP.DAT");
            if (_encamp?.ClockEntries == null) {
                _logger.LogError("InnScreen: ENCAMP.DAT did not load; the dial has no stones.");

                return;
            }

            // The gold stone is the WAKING hour, not a cursor — an inn's dial tells you when you
            // will be woken and never moves. See EncampDial.IconFor.
            await RestDialView.BuildAsync(stage, _resources, _encamp,
                markedHour: _clock?.HourOfDay ?? 0, highlightedStone: _wakeHour,
                shadowTicksOfDay: TicksOfDay(), palette: _palette);
        }

        /// <summary>Where the clock stands within the current day, for the sundial's shadow.</summary>
        private int TicksOfDay() =>
            _clock == null ? -1 : (int)(_clock.Ticks % GameData.Resources.Character.InnStay.TicksPerDay);

        /// <summary>
        /// The party purse — the one screen in the game that shows it in words.
        /// </summary>
        private void BuildPurse(VisualElement stage) {
            stage.Q<VisualElement>(PurseName)?.RemoveFromHierarchy();
            var purse = new VisualElement {
                name = PurseName,
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, left = 0, top = 0, right = 0, bottom = 0 },
            };

            Color ink = PaletteColors.ResolvePen(_palette, InnScreenLayout.PurseTextPen, Color.black);
            AddText(purse, UiStrings.Get(InnScreenLayout.PurseLabelKey),
                InnScreenLayout.PurseLabelX, ink);
            // GoldAndSilver, not the inventory screen's abbreviation: this readout is the reason
            // that wording exists.
            AddText(purse, MoneyFormatter.Format(_session?.PartyGold ?? 0, CurrencyStyle.GoldAndSilver),
                InnScreenLayout.PurseAmountX, ink);

            stage.Add(purse);
        }

        private static void AddText(VisualElement parent, string text, int x, Color ink) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x, top = InnScreenLayout.PurseY,
                    color = ink,
                },
            };
            GameFontText.Apply(label);
            parent.Add(label);
        }
    }
}

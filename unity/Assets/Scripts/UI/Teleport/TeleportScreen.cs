namespace BakAgain.UI.Teleport {
    using BakAgain.CutScenes;
    using BakAgain.Graphics;
    using BakAgain.ResourceManagement.Loaders;
    using BakAgain.UI;
    using BakAgain.UI.InputCore;
    using Cysharp.Threading.Tasks;
    using GameData.Money;
    using GameData.Resources.Location;
    using Microsoft.Extensions.Logging;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    /// <summary>
    /// The temple rift map — pick a temple, pay the fare, be sent there.
    /// Faithful port of <c>UI_teleportation</c> @0x4ee7e; the rules it consults are
    /// <see cref="TeleportMenu"/> and <see cref="TeleportCost"/>.
    ///
    /// <para>The twelve pins come free from <see cref="UserInterfaceLoader"/>: REQ_TELE ships them
    /// <c>Visible:false</c>, which the loader builds as invisible-but-clickable hit-boxes (the same
    /// treatment REQ_INV's portraits get). So this screen never places a hit target — it only draws
    /// markers on top of pins that already exist, and lets a pin with no marker keep its hit-box
    /// harmlessly, since an unoffered temple is refused on click anyway.</para>
    /// </summary>
    public class TeleportScreen : BakAgain.UI.Navigation.ScreenBase, IActionHandler {
        private const string MarkerClass = "teleport-marker";
        private const string PanelClass = "teleport-panel";

        private ILogger _logger;
        private IResourceCache _resources;
        private BakAgain.UI.Navigation.IScreenNavigator _navigator;
        private IDialogManager _dialogs;
        private Core.GameSession _session;
        private UserInterfaceLoader _ui;
        private IReadOnlyList<NavWidget> _widgets;
        private GameData.Resources.Palette.PaletteResource _palette;

        private int _currentTemple = 1;
        private int _baseCost;
        private int _costPerUnit;
        private int _hoveredTemple;
        private UniTaskCompletionSource<int> _choice;

        [VContainer.Inject]
        public void Construct(IResourceCache resources,
            BakAgain.UI.Navigation.IScreenNavigator navigator, IDialogManager dialogs,
            Core.GameSession session) {
            _resources = resources;
            _navigator = navigator;
            _dialogs = dialogs;
            _session = session;
            _logger = Core.LogManager.LoggerFactory.CreateLogger<TeleportScreen>();
        }

        private void Awake() => _ui = GetComponent<UserInterfaceLoader>();

        /// <summary>
        /// The whole visit: the priest's greeting, the map, the fare, and the arrival — or the
        /// refusal, whichever the temple gives.
        /// </summary>
        /// <param name="currentTemple">The temple the party is standing in, 1-12.</param>
        /// <param name="baseCost">Its flat charge, from the location's shop block.</param>
        /// <param name="costPerUnit">Its charge per unit of map distance.</param>
        /// <returns>The <c>TELEPORT.DAT</c> row the party should be moved to, or -1 for no move.</returns>
        /// <remarks>
        /// <b>The order of the last three steps is the original's and is load-bearing.</b> The spark
        /// flies while the screen is still up, the screen comes down, and only then is the purse
        /// checked — so a party that cannot afford the fare watches nothing, loses nothing, and hears
        /// its apology after the map has gone. Checking the purse up front would be tidier and would
        /// change what the player sees.
        /// </remarks>
        public async UniTask<int> RunAsync(int currentTemple, int baseCost, int costPerUnit) {
            _currentTemple = currentTemple;
            _baseCost = baseCost;
            _costPerUnit = costPerUnit;
            _hoveredTemple = 0;

            // Standing inside the closed chapel: it will do nothing but vital healings.
            if (currentTemple == TeleportMenu.ChapelOfIshap && ChapelIsClosed()) {
                await Say(TeleportMenu.ChapelRefusesServiceDialog);

                return -1;
            }

            await Say(TeleportMenu.IntroDialog);

            if (!TeleportMenu.AnyDestinationOffered(currentTemple, IsVisited)) {
                await Say(TeleportMenu.NoOtherTemplesDialog);

                return -1;
            }

            _choice = new UniTaskCompletionSource<int>();
            await _navigator.Push(this);
            int temple = await _choice.Task;

            // Aiming at the closed chapel from elsewhere: a different refusal, and it cancels.
            if (temple == TeleportMenu.ChapelOfIshap && ChapelIsClosed()) {
                await Say(TeleportMenu.ChapelUnreachableDialog);
                temple = 0;
            }

            long fare = temple > 0 ? FareTo(temple) : 0;
            bool affordable = temple > 0 && _session != null && _session.PartyGold >= fare;
            if (affordable) {
                await FlyAsync(_currentTemple, temple);
            }

            await _navigator.Pop();

            if (temple <= 0) {
                await Say(TeleportMenu.DeclinedDialog);

                return -1;
            }

            if (!affordable) {
                await Say(TeleportMenu.CannotAffordDialog);

                return -1;
            }

            // The full royal fare, not the sovereign figure the quote rounded down to.
            _session.PartyGold -= (int)fare;
            await Say(TeleportMenu.ArrivalDialog);

            return TeleportMenu.DestinationIdForTemple(temple);
        }

        private bool ChapelIsClosed() =>
            TeleportMenu.ChapelIsClosed(_session?.Chapter ?? 0,
                (_session?.GetGlobalValue(TeleportMenu.ChapelReopenedFlag) ?? 0) != 0);

        private bool IsVisited(int temple) =>
            (_session?.GetGlobalValue(TeleportMenu.VisitedFlagFor(temple)) ?? 0) != 0;

        private UniTask Say(int dialogId) =>
            _dialogs == null ? UniTask.CompletedTask : _dialogs.ShowById(dialogId);

        // ---- the fare --------------------------------------------------------------------

        /// <summary>
        /// The fare to a temple, in royals.
        /// </summary>
        /// <remarks>
        /// <b>Measured in the original's pixels, not ours</b> — TeleportCost.PriceCanonical does the
        /// conversion, so the octagonal distance is not stretched north-south against east-west.
        /// </remarks>
        private long FareTo(int temple) {
            Rect from = PinRect(_currentTemple);
            Rect to = PinRect(temple);
            return TeleportCost.PriceCanonical(
                Mathf.RoundToInt(from.x), Mathf.RoundToInt(from.y),
                Mathf.RoundToInt(to.x), Mathf.RoundToInt(to.y),
                _baseCost, _costPerUnit);
        }

        private Rect PinRect(int temple) {
            if (_widgets == null) {
                return Rect.zero;
            }

            int actionId = TeleportMenu.ActionIdForTemple(temple);
            foreach (NavWidget widget in _widgets) {
                if (widget.ActionId == actionId) {
                    return widget.CanonicalRect;
                }
            }

            return Rect.zero;
        }

        // ---- building -------------------------------------------------------------------

        private void OnEnable() {
            if (_ui == null) {
                return;
            }

            _ui.Built += OnBuilt;

            // IsBuilt as well as the event: the loader's build is cached and async, so it can finish
            // before this component subscribes and the event then never arrives.
            if (_ui.IsBuilt) {
                OnBuilt(_ui.CurrentNavWidgets);
            }
        }

        private void OnDisable() {
            if (_ui != null) {
                _ui.Built -= OnBuilt;
            }
        }

        private void OnBuilt(IReadOnlyList<NavWidget> widgets) {
            _widgets = widgets;
            HookHover(widgets);
            RedrawAsync().Forget();
        }

        /// <summary>
        /// Makes each pin report when it is pointed at.
        /// </summary>
        /// <remarks>
        /// Hover, not selection, is what drives this screen's panel and fare — the original recomputes
        /// both from whatever the cursor is over, before any click. Hanging that off the widgets'
        /// own pointer events rather than polling the cursor gets <b>keyboard navigation for free</b>:
        /// <see cref="NavigableLayer"/> already synthesises a PointerEnter when focus moves, so
        /// arrowing between pins quotes fares exactly as sweeping the mouse does.
        /// </remarks>
        private void HookHover(IReadOnlyList<NavWidget> widgets) {
            if (widgets == null) {
                return;
            }

            foreach (NavWidget widget in widgets) {
                int temple = TeleportMenu.TempleForAction(widget.ActionId);
                if (temple == 0 || widget.Element == null) {
                    continue;
                }

                widget.Element.RegisterCallback<PointerEnterEvent>(_ => Hover(temple));
                widget.Element.RegisterCallback<PointerLeaveEvent>(_ => Hover(0));
            }
        }

        private void Hover(int temple) {
            // An unoffered pin is not a destination, so it quotes nothing — its hit-box survives only
            // because the REQ owns it.
            if (temple != 0 && !TeleportMenu.IsOffered(temple, _currentTemple, IsVisited(temple))) {
                temple = 0;
            }

            if (temple == _hoveredTemple) {
                return;
            }

            _hoveredTemple = temple;
            RedrawAsync().Forget();
        }

        private async UniTask RedrawAsync() {
            VisualElement stage = Stage();
            // `_resources` belongs in this guard, not two lines below it: a screen enabled without
            // its collaborators (a reachability probe does exactly that) otherwise reached the
            // palette load and threw into a fire-and-forget task, which surfaces at GC time against
            // whatever test is running. Same shape as CastScreen.ShowSpellNamesAsync, 2026-09-13.
            if (stage == null || _widgets == null || _resources == null) {
                return;
            }

            // The pens the panel's text is drawn with resolve against this screen's own palette, the
            // same one its bitmaps are drawn under.
            _palette ??= await _resources.GetOrLoadAsync<GameData.Resources.Palette.PaletteResource>(
                TeleportMenu.Palette);

            Clear(stage, MarkerClass);
            Clear(stage, PanelClass);
            await DrawPinsAsync(stage);
            await DrawPanelAsync(stage);
        }

        private async UniTask DrawPinsAsync(VisualElement stage) {
            for (int temple = 1; temple <= TeleportMenu.TempleCount; temple++) {
                int icon = TeleportMenu.PinIcon(temple, _currentTemple, _hoveredTemple,
                    TeleportMenu.IsOffered(temple, _currentTemple, IsVisited(temple)));
                if (icon < 0) {
                    continue;
                }

                Sprite sprite = await Icon(icon);
                if (sprite == null) {
                    continue;
                }

                // Centred in the pin's rect, as drawTeleportMenu centres its marker in the uiElement.
                Rect rect = PinRect(temple);
                stage.Add(Marker(sprite, MarkerClass,
                    rect.x + ((rect.width - sprite.rect.width) / 2f),
                    rect.y + ((rect.height - sprite.rect.height) / 2f)));
            }
        }

        private async UniTask DrawPanelAsync(VisualElement stage) {
            Sprite title = await Icon(TeleportMenu.TitleIcon);
            if (title != null) {
                stage.Add(Marker(title, PanelClass,
                    TeleportMenu.PanelCentreX - (title.rect.width / 2f), TeleportMenu.TitleY));
            }

            stage.Add(Caption(GameData.Resources.Text.UiStrings.Get("base:uistring:money.teleport_from_label"), TeleportMenu.FromCaptionY));
            await DrawTempleAsync(stage, _currentTemple,
                TeleportMenu.SourceNameY, TeleportMenu.SourcePortraitY);

            stage.Add(Caption(GameData.Resources.Text.UiStrings.Get("base:uistring:money.teleport_to_label"), TeleportMenu.ToCaptionY));
            await DrawTempleAsync(stage, _hoveredTemple,
                TeleportMenu.DestinationNameY, TeleportMenu.DestinationPortraitY);

            stage.Add(Caption(GameData.Resources.Text.UiStrings.Get("base:uistring:money.teleport_cost_label"), TeleportMenu.CostY, TeleportMenu.CostCaptionCentreX));
            if (_hoveredTemple > 0) {
                // "12 sovereigns" — always plural, royals dropped. The bill is the full fare.
                stage.Add(Ink(LeftAligned(
                    MoneyFormatter.Format((int)FareTo(_hoveredTemple), CurrencyStyle.TeleportQuote),
                    TeleportMenu.CostAmountX, TeleportMenu.CostY), value: true));
            }
        }

        // The temple's name is the REQ entry's own label — REQ_TELE carries all twelve, so there is
        // no name table to look anything up in.
        private async UniTask DrawTempleAsync(VisualElement stage, int temple, int nameY, int portraitY) {
            if (temple <= 0) {
                return;
            }

            stage.Add(Value(TempleName(temple), nameY));

            Sprite portrait = await Icon(TeleportMenu.PortraitIcon(temple));
            if (portrait != null) {
                stage.Add(Marker(portrait, PanelClass,
                    TeleportMenu.PanelCentreX - (portrait.rect.width / 2f), portraitY));
            }
        }

        private string TempleName(int temple) {
            int actionId = TeleportMenu.ActionIdForTemple(temple);
            if (_widgets != null) {
                foreach (NavWidget widget in _widgets) {
                    if (widget.ActionId == actionId) {
                        return widget.Label;
                    }
                }
            }

            return string.Empty;
        }

        private UniTask<Sprite> Icon(int index) =>
            _resources.GetOrLoadAsync<Sprite>($"{TeleportMenu.IconSet}#{index}");

        // ---- the flight ------------------------------------------------------------------

        /// <summary>
        /// Sends the spark from one pin to the other.
        /// </summary>
        /// <remarks>
        /// Stepped along the longer axis one pixel at a time, exactly as the original walks it, with
        /// the shorter axis carried by an accumulator — so the path is the same staircase and takes
        /// the same number of frames. The bow is <see cref="TeleportMenu.FlightArcOffset"/>.
        /// </remarks>
        private async UniTask FlyAsync(int from, int to) {
            VisualElement stage = Stage();
            if (stage == null) {
                return;
            }

            Rect a = PinRect(from);
            Rect b = PinRect(to);
            var start = new Vector2(a.x, a.y);
            Vector2 delta = new Vector2(b.x, b.y) - start;
            bool acrossIsLonger = Mathf.Abs(delta.x) > Mathf.Abs(delta.y);
            int steps = TeleportMenu.FlightSteps(delta.x, delta.y);
            if (steps <= 0) {
                return;
            }

            for (var step = 0; step <= steps; step++) {
                Sprite sprite = await Icon(TeleportMenu.SparkIcon(step));
                if (sprite == null) {
                    break;
                }

                float progress = (float)step / steps;
                (float bowX, float bowY) = TeleportMenu.FlightBow(step, steps, acrossIsLonger);
                Vector2 at = start + (delta * progress) + new Vector2(bowX, bowY);

                Clear(stage, SparkClass);
                VisualElement spark = Marker(sprite, SparkClass,
                    at.x + TeleportMenu.SparkOffsetCanonicalX,
                    at.y + TeleportMenu.SparkOffsetCanonicalY);
                stage.Add(spark);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            Clear(stage, SparkClass);
        }

        private const string SparkClass = "teleport-spark";

        // ---- input -----------------------------------------------------------------------

        /// <inheritdoc/>
        /// <remarks>
        /// Clicking a pin that is not offered does nothing at all — the original has already stripped
        /// those pins of their markers, so there is nothing there to click as far as the player can
        /// see, and a refusal message would be answering a question nobody asked.
        /// </remarks>
        public void PrimaryAction(int menuEntryActionId) {
            if (menuEntryActionId == TeleportMenu.CancelActionId) {
                _choice?.TrySetResult(0);

                return;
            }

            int temple = TeleportMenu.TempleForAction(menuEntryActionId);
            if (temple == 0 || !TeleportMenu.IsOffered(temple, _currentTemple, IsVisited(temple))) {
                return;
            }

            _choice?.TrySetResult(temple);
        }

        /// <inheritdoc/>
        /// <remarks>Right-click is help, with the topic chosen by what was clicked.</remarks>
        public async Awaitable SecondaryAction(int menuEntryActionId) {
            if (_dialogs == null) {
                return;
            }

            _session?.SetGlobalValue(TeleportMenu.HelpTopicGlobal,
                TeleportMenu.HelpTopicFor(menuEntryActionId));
            await _dialogs.ShowById(TeleportMenu.HelpDialog).AsTask();
        }

        // ---- elements --------------------------------------------------------------------

        private VisualElement Stage() {
            var document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            return root == null ? null : CanonicalStage.GetOrCreate(root, _ui.Frame);
        }

        private static VisualElement Marker(Sprite sprite, string className, float left, float top) {
            var element = new VisualElement {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = left,
                    top = top,
                    width = sprite.rect.width,
                    height = sprite.rect.height,
                    backgroundImage = new StyleBackground(sprite),
                },
            };
            element.AddToClassList(className);
            return element;
        }

        // Centred by laying the label across the panel and centring its text, rather than measuring
        // the string — same result, and it sidesteps UI Toolkit's measure-before-layout problem.
        private const int PanelBoxWidth = 560;

        private VisualElement Caption(string text, int y, int centreX = TeleportMenu.PanelCentreX) =>
            Ink(Centred(text, y, centreX), value: false);

        private VisualElement Value(string text, int y, int centreX = TeleportMenu.PanelCentreX) =>
            Ink(Centred(text, y, centreX), value: true);

        /// <summary>
        /// Paints a panel line in its pen, with the value's drop shadow but not the caption's
        /// absence of one.
        /// </summary>
        /// <remarks>
        /// The shadow is offset by exactly one ORIGINAL pixel, which is a different number on each
        /// axis in canonical space - 5 across, 6 down. A single offset for both would lean the
        /// shadow, which is the same anisotropy trap the fare has.
        /// </remarks>
        private VisualElement Ink(VisualElement element, bool value) {
            int pen = value ? TeleportMenu.ValuePen : TeleportMenu.CaptionPen;
            element.style.color = PaletteColors.ResolvePen(_palette, pen, Color.black);
            if (value) {
                element.style.textShadow = new StyleTextShadow(new TextShadow {
                    offset = new Vector2(GameData.Resources.Layout.OriginalPixel.Width, GameData.Resources.Layout.OriginalPixel.Height),
                    color = PaletteColors.ResolvePen(_palette, TeleportMenu.ValueShadowPen, Color.black),
                });
            }

            return element;
        }

        private static VisualElement Centred(string text, int y, int centreX = TeleportMenu.PanelCentreX) {
            // Narrowed when the centre sits near the left edge, so the box cannot hang off it: the
            // "Cost:" caption is centred at 120 and a full-width box would start at -160 and clip.
            int width = Mathf.Min(PanelBoxWidth, centreX * 2);
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = centreX - (width / 2),
                    top = y,
                    width = width,
                    unityTextAlign = TextAnchor.MiddleCenter,
                },
            };
            label.AddToClassList(PanelClass);
            GameFontText.Apply(label);
            return label;
        }

        private static VisualElement LeftAligned(string text, int x, int y) {
            var label = new Label(text) {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    left = x,
                    top = y,
                },
            };
            label.AddToClassList(PanelClass);
            GameFontText.Apply(label);
            return label;
        }

        private static void Clear(VisualElement stage, string className) {
            foreach (VisualElement stale in stage.Query<VisualElement>(className: className).ToList()) {
                stale.RemoveFromHierarchy();
            }
        }
    }
}

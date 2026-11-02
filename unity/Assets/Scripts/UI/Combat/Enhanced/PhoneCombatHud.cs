namespace BakAgain.UI.Combat.Enhanced {
    using System;
    using System.Collections.Generic;
    using BakAgain.UI.InGame;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using GameData.Resources.Text;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The Enhanced combat HUD for phones (spec 2026-10-28, frames phone-01..18, landscape 844x390 pt):
    /// a top row (hero chip, hero coins, "+N allies", the sentence), foe chips on the right, the camera
    /// buttons on the left, and along the bottom Confirm with Cancel above it, the peek and the 2x3
    /// action grid. The hero card opens as a left sheet. Everything sits inside the safe area.
    /// </summary>
    /// <remarks>
    /// Sizes are in points from <see cref="EnhancedCombatLayout"/>, turned into panel units from the
    /// window's height in points, so a 44 pt target is 44 pt on every phone. Tap = the event; the
    /// controller decides what it means: Confirm commits, and so does a second tap on what is forecast;
    /// Cancel is the only way back.
    /// </remarks>
    public sealed class PhoneCombatHud : ICombatHud {
        private readonly EnhancedCombatLayout _layout;
        private readonly Func<Rect> _safeArea;
        private readonly Func<Vector2> _screen;
        private readonly Func<float> _pixelsPerPoint;
        private readonly Func<int, string> _stoneImage;
        private readonly VisualElement _root;
        private readonly VisualElement _safe;
        private readonly List<(VisualElement E, float W, float H)> _sized = new();
        private readonly List<(VisualElement E, float Pt)> _fonts = new();

        private readonly VisualElement _top, _heroChip, _heroCoins, _sheet, _foeChips, _peek, _grid, _cam;

        // Kept across the 4 Hz Show: the switch buttons (a press longer than one refresh lost its button,
        // and with it the click) and the sheet's portrait (re-made each Show, it reloaded and flickered).
        private readonly VisualElement _switchBox, _peekInfo, _sheetBody;
        private readonly Button _switchThrust, _switchSwing;
        private readonly ArchiveImage _sheetHead;
        private readonly Label _heroName, _heroLine, _allies, _sentence, _sentenceEnds;
        private readonly ArchiveImage _heroHead;
        private readonly Button _confirm, _cancel;
        private readonly Label _confirmTitle, _confirmDetail;
        private readonly Dictionary<CombatCommands.Command, VisualElement> _tiles = new();
        private float _scale = 1f;
        private CombatHudModel _model;

        public event Action ConfirmPressed;
        public event Action CancelPressed;
        public event Action<CombatCommands.Command> StoneActivated;
        public event Action<CombatCommands.Command> StoneDescribed;
        public event Action<int> FoeRowHovered { add { } remove { } }   // a phone has no hover
        public event Action<int> FoeRowChosen;
        public event Action HeroCardRequested;
        public event Action ResetViewPressed;

        public event Action<ForecastAction> SwitchPressed;

        /// <param name="safeArea">The OS safe area in screen pixels (default <see cref="Screen.safeArea"/>).</param>
        /// <param name="screen">The window in pixels (default the <see cref="Screen"/> size).</param>
        /// <param name="pixelsPerPoint">Screen pixels per point (default from <see cref="Screen.dpi"/> on a phone, 1 elsewhere).</param>
        public PhoneCombatHud(VisualElement parent, EnhancedCombatLayout layout, Func<int, string> stoneImage = null,
            Func<Rect> safeArea = null, Func<Vector2> screen = null, Func<float> pixelsPerPoint = null) {
            _layout = layout;
            _stoneImage = stoneImage;
            _screen = screen ?? (() => new Vector2(Screen.width, Screen.height));
            _safeArea = safeArea ?? (() => Screen.safeArea);
            _pixelsPerPoint = pixelsPerPoint ?? (() => Application.isMobilePlatform && Screen.dpi > 0f
                ? Screen.dpi / layout.PointsPerInch : 1f);
            _root = Box("ec-phone", "ec-phone");
            DesktopCombatHud.Fill(_root);
            _root.style.display = DisplayStyle.None;
            parent.Add(_root);
            _safe = Box("ec-phone-safe", "ec-phone-safe");
            _safe.style.position = Position.Absolute;
            _root.Add(_safe);

            float gap = layout.PhoneGapPt;
            float row = layout.PhoneTopRowPt;

            // Top row.
            // The docks are opaque panels: a tap on them (or between their tiles) must never reach the
            // board under them. Only the free board between the docks passes through.
            _top = Box("ec-phone-top", "ec-phone-top");
            _top.pickingMode = PickingMode.Position;
            _safe.Add(_top);
            Size(_top, -1, row);
            _heroChip = Box("ec-phone-hero-chip", "ec-phone-chip");
            _heroChip.pickingMode = PickingMode.Position;
            _heroChip.RegisterCallback<ClickEvent>(_ => { ToggleSheet(); HeroCardRequested?.Invoke(); });
            Size(_heroChip, layout.PhoneHeroChipWidthPt, row);
            _top.Add(_heroChip);
            _heroHead = new ArchiveImage { sizeToImage = false, pickingMode = PickingMode.Ignore };
            _heroHead.AddToClassList("ec-phone-head");
            PortraitRingView.Coin(_heroHead);
            Size(_heroHead, row - 6, row - 6);
            _heroChip.Add(_heroHead);
            VisualElement heroText = Box(null, "ec-phone-col");
            _heroChip.Add(heroText);
            _heroName = Txt(heroText, "ec-phone-hero-name", layout.PhoneHeadTextPt, "ec-phone-strong");
            _heroLine = Txt(heroText, "ec-phone-hero-line", layout.MinTextPt, null);
            _heroCoins = Box("ec-phone-coins", "ec-phone-row");
            _top.Add(_heroCoins);
            _allies = Txt(_top, "ec-phone-allies", layout.PhoneBodyTextPt, "ec-phone-chip");
            _allies.pickingMode = PickingMode.Position;
            Size(_allies, row, row);
            VisualElement sentence = Box("ec-phone-sentence", "ec-phone-sentence");
            _top.Add(sentence);
            _sentence = Txt(sentence, "ec-phone-sentence-text", layout.PhoneBodyTextPt, "ec-phone-strong");
            _sentenceEnds = Txt(sentence, "ec-phone-sentence-ends", layout.PhoneBodyTextPt, "ec-phone-ends");

            // Camera column, left, under the top row.
            _cam = Box("ec-phone-cam", "ec-phone-cam");
            _safe.Add(_cam);
            Place(_cam, top: row + gap, left: 0);
            CamButton(_cam, "ec-cam-reset", UiTemplates.Format(UiTemplates.HudReset), () => ResetViewPressed?.Invoke());
            // The grid (the G key's toggle), here rather than in the touch side bar, which sat under the
            // sentence box in a fight (owner, 2026-10-29).
            CamButton(_cam, "ec-cam-grid", "#", () => BakAgain.UI.InputCore.TouchInputState.Instance?.RequestGridToggle());

            // Foe chips, right, under the top row.
            _foeChips = Box("ec-phone-foes", "ec-phone-foes");
            _foeChips.pickingMode = PickingMode.Position;
            _safe.Add(_foeChips);
            Place(_foeChips, top: row + gap, right: 0);
            Size(_foeChips, 2 * layout.FoeChipPt + gap, -1);

            // Bottom: Confirm with Cancel above, the peek, the grid.
            _confirm = new Button(() => ConfirmPressed?.Invoke()) { name = "ec-phone-confirm" };
            _confirm.AddToClassList("ec-phone-confirm");
            _safe.Add(_confirm);
            Place(_confirm, bottom: 0, left: 0);
            Size(_confirm, layout.PhoneConfirmWidthPt, layout.PhoneConfirmHeightPt);
            _confirmTitle = Txt(_confirm, "ec-phone-confirm-title", layout.PhoneHeadTextPt, "ec-phone-strong");
            _confirmDetail = Txt(_confirm, "ec-phone-confirm-detail", layout.MinTextPt, null);
            _cancel = new Button(() => { ShowSheet(false); CancelPressed?.Invoke(); }) { name = "ec-phone-cancel", text = "✕" };
            _cancel.AddToClassList("ec-phone-button");
            _safe.Add(_cancel);
            Place(_cancel, bottom: layout.PhoneConfirmHeightPt + layout.ConfirmCancelGapPt, left: 0);
            Size(_cancel, layout.FoeChipPt, layout.FoeChipPt);
            SetFont(_cancel, layout.PhoneHeadTextPt);

            _peek = Box("ec-phone-peek", "ec-phone-peek");
            _peek.pickingMode = PickingMode.Position;
            _safe.Add(_peek);
            Place(_peek, bottom: 0, left: layout.PhoneConfirmWidthPt + gap);
            Size(_peek, layout.PhonePeekWidthPt, layout.PhonePeekHeightPt);
            VisualElement peekBody = Box(null, "ec-phone-row");
            _peek.Add(peekBody);
            _switchBox = Box("ec-phone-switch", "ec-phone-col");
            peekBody.Add(_switchBox);
            _switchThrust = SwitchButton(_switchBox, ForecastAction.Thrust);
            _switchSwing = SwitchButton(_switchBox, ForecastAction.Swing);
            _peekInfo = Box("ec-phone-peek-info", "ec-phone-col");
            _peekInfo.style.flexGrow = 1;
            peekBody.Add(_peekInfo);

            _grid = Box("ec-phone-grid", "ec-phone-grid");
            _grid.pickingMode = PickingMode.Position;
            _safe.Add(_grid);
            Place(_grid, bottom: 0, right: 0);
            Size(_grid, 3 * layout.TileWidthPt + 2 * gap, 2 * layout.TileHeightPt + gap);
            // Two rows of three, the gap between them in points (UI Toolkit has no row gap).
            VisualElement row0 = Box(null, "ec-phone-grid__row"), row1 = Box(null, "ec-phone-grid__row");
            _grid.Add(row0);
            _grid.Add(row1);
            for (int i = 0; i < CombatHudText.PhoneGrid.Length; i++) {
                CombatCommands.Command c = CombatHudText.PhoneGrid[i];
                (i < 3 ? row0 : row1).Add(_tiles[c] = Tile(c));
            }

            // The hero card: a left sheet under the top row; Cancel closes it.
            _sheet = Box("ec-phone-sheet", "ec-phone-sheet");
            _sheet.pickingMode = PickingMode.Position;
            _sheet.style.display = DisplayStyle.None;
            _safe.Add(_sheet);
            Place(_sheet, top: row + gap / 2, left: 0);
            // Content-sized, up to a cap (owner: the 500 pt sheet spread its few numbers too far apart).
            VisualElement sheetRow = Box(null, "ec-phone-row");
            _sheet.Add(sheetRow);
            _sheetHead = new ArchiveImage { name = "ec-phone-sheet-head", sizeToImage = false, pickingMode = PickingMode.Ignore };
            _sheetHead.AddToClassList("ec-phone-sheet__head");
            Size(_sheetHead, layout.PhoneSheetHeadPt, layout.PhoneSheetHeadPt);
            sheetRow.Add(_sheetHead);
            _sheetBody = Box("ec-phone-sheet-body", "ec-phone-col");
            sheetRow.Add(_sheetBody);

            _root.RegisterCallback<GeometryChangedEvent>(_ => Resize());
        }

        public bool Shown => _root.style.display == DisplayStyle.Flex;

        public VisualElement Root => _root;

        /// <summary>Panel units per point at the current window size.</summary>
        public float Scale => _scale;

        public bool SheetShown => _sheet.style.display == DisplayStyle.Flex;

        public void Hide() => _root.style.display = DisplayStyle.None;

        public void Show(CombatHudModel m) {
            _model = m;
            _root.style.display = DisplayStyle.Flex;
            if (m == null) return;
            ShowTop(m);
            ShowFoes(m);
            ShowConfirm(m);
            ShowPeek(m);
            ShowTiles(m);
            if (SheetShown) BuildSheet(m);
            Resize();
        }

        public void ShowSheet(bool on) {
            _sheet.style.display = on && _model?.Hero != null && !_model.Hero.IsFoe ? DisplayStyle.Flex : DisplayStyle.None;
            if (SheetShown) {
                BuildSheet(_model);
                Resize();
            }
        }

        private void ToggleSheet() => ShowSheet(!SheetShown);

        // ---- top row ----

        private void ShowTop(CombatHudModel m) {
            HeroCardModel h = m.Hero;
            _heroName.text = h?.Name ?? "";
            _heroLine.text = h == null || h.IsFoe ? ""
                : UiTemplates.Format(UiTemplates.HudChip, ("health", h.Health), ("max", h.HealthMax),
                    ("stamina", h.Stamina), ("speed", h.Speed));
            string head = h != null && h.HeadId >= 0 ? ActorStatsPanel.PortraitImage + "#" + h.HeadId : null;
            if (_heroHead.address != head) _heroHead.address = head;
            PortraitRingView.SetCoin(_heroHead, Fractions(h), m.TheirTurn ? RingVariant.Ready : RingVariant.Active);
            _heroChip.EnableInClassList("ec-phone-chip--theirs", m.TheirTurn);

            // The other heroes as coins (the chip is one of MaxHeroCoins), the rest behind "+N allies".
            var others = new List<PartyCoinModel>();
            foreach (PartyCoinModel p in m.Party ?? Array.Empty<PartyCoinModel>()) {
                if (p.State != RingVariant.Active) others.Add(p);
            }
            int coins = Math.Min(others.Count, Math.Max(0, _layout.MaxHeroCoins - 1));
            DesktopCombatHud.Fit(_heroCoins, coins, () => {
                var c = new ArchiveImage { sizeToImage = false, pickingMode = PickingMode.Ignore };
                c.AddToClassList("ec-phone-coin");
                PortraitRingView.Coin(c);
                Size(c, _layout.PhoneTopRowPt, _layout.PhoneTopRowPt);
                return c;
            });
            for (int i = 0; i < coins; i++) {
                var c = (ArchiveImage)_heroCoins[i];
                string a = others[i].HeadId >= 0 ? ActorStatsPanel.PortraitImage + "#" + others[i].HeadId : null;
                if (c.address != a) c.address = a;
                PortraitRingView.SetCoin(c, Fractions(others[i].Stamina, others[i].StaminaMax, others[i].Health, others[i].HealthMax),
                    others[i].State);
            }
            int rest = others.Count - coins;
            _allies.style.display = rest > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _allies.text = UiTemplates.Format(UiTemplates.HudAllies, ("n", rest));

            CombatForecast f = m.Forecast;
            _sentence.text = f != null ? Title(m, f) : m.Sentence ?? "";
            _sentence.EnableInClassList("ec-phone-theirs", m.TheirTurn);
            _sentenceEnds.text = f != null && f.Allowed && f.EndsTurn
                ? UiTemplates.Format(UiTemplates.HudEndsTurn, ("name", h?.Name ?? ""))
                : f == null ? CombatHudText.Join(CombatHudText.Round(m.Round),
                    m.TheirTurn ? "" : UiTemplates.Format(UiTemplates.HudTurnHint)) : "";
            _sentenceEnds.EnableInClassList("ec-phone-dim", f == null);
            _sentenceEnds.EnableInClassList("ec-phone-ends", f != null);
        }

        private static (double S, double H) Fractions(HeroCardModel h) =>
            h == null || h.IsFoe ? (0d, 0d) : Fractions(h.Stamina, h.StaminaMax, h.Health, h.HealthMax);

        private static (double S, double H) Fractions(int s, int sMax, int hp, int hMax) {
            int max = sMax + hMax;
            return max <= 0 ? (0d, 0d) : ((double)Math.Max(0, s) / max, (double)Math.Max(0, hp) / max);
        }

        private static string Title(CombatHudModel m, CombatForecast f) {
            FoeRowModel target = Foe(m, f.TargetSlot);
            string action = CombatHudText.Verb(f);
            return target == null ? action : UiTemplates.Format(UiTemplates.HudJoin, ("a", action),
                ("b", UiTemplates.Format(UiTemplates.EnhancedLogFoe, ("creature", target.Name), ("letter", target.Letter))));
        }

        private static FoeRowModel Foe(CombatHudModel m, int slot) {
            if (m?.Foes == null || slot < 0) return null;
            foreach (FoeRowModel f in m.Foes) if (f.Slot == slot) return f;
            return null;
        }

        // ---- foe chips ----

        private void ShowFoes(CombatHudModel m) {
            IReadOnlyList<FoeRowModel> foes = m.Foes ?? Array.Empty<FoeRowModel>();
            DesktopCombatHud.Fit(_foeChips, foes.Count, () => {
                VisualElement chip = Box(null, "ec-phone-foe");
                chip.pickingMode = PickingMode.Position;
                Size(chip, _layout.FoeChipPt, _layout.FoeChipPt);
                Txt(chip, null, _layout.PhoneBodyTextPt, "ec-phone-foe__letter");
                Txt(chip, null, _layout.MinTextPt, "ec-phone-foe__note");
                chip.RegisterCallback<ClickEvent>(_ => FoeRowChosen?.Invoke((int)chip.userData));
                return chip;
            });
            int target = m.Forecast?.TargetSlot ?? -1;
            for (int i = 0; i < foes.Count; i++) {
                FoeRowModel f = foes[i];
                VisualElement chip = _foeChips[i];
                chip.userData = f.Slot;
                chip.Q<Label>(className: "ec-phone-foe__letter").text = f.Letter.ToString();
                chip.Q<Label>(className: "ec-phone-foe__note").text = f.Seen > 0 ? "−" + f.Seen : "";
                chip.EnableInClassList("ec-phone-foe--chosen", f.Slot == target);
                chip.EnableInClassList("ec-phone-foe--acting", f.Acting);
                chip.EnableInClassList("ec-phone-foe--down", f.Down);
            }
        }

        // ---- Confirm ----

        private void ShowConfirm(CombatHudModel m) {
            CombatForecast f = m.Forecast;
            bool live = f != null && f.Allowed && !m.TheirTurn;
            _confirm.SetEnabled(live);
            _confirm.EnableInClassList("ec-phone-confirm--empty", f == null);
            _confirm.EnableInClassList("ec-phone-confirm--warned", live && f.Warned);
            if (f == null) {
                _confirmTitle.text = "—";
                _confirmDetail.text = UiTemplates.Format(UiTemplates.HudConfirmEmpty);
                return;
            }
            _confirmTitle.text = UiTemplates.Format(UiTemplates.HudConfirm, ("action", CombatHudText.Action(f.Action)));
            _confirmDetail.text = !f.Allowed ? CombatHudText.Reason(f)
                : CombatHudText.Join(CombatHudText.Cost(f, true),
                    f.EndsTurn ? UiTemplates.Format(UiTemplates.HudEndsTurnShort) : "");
        }

        // ---- the peek ----

        private void ShowPeek(CombatHudModel m) {
            _peekInfo.Clear();
            Prune();
            CombatForecast f = m.Forecast;
            bool melee = f != null && (f.Action == ForecastAction.Thrust || f.Action == ForecastAction.Swing);
            _switchBox.style.display = melee ? DisplayStyle.Flex : DisplayStyle.None;
            if (f == null) {
                foreach (string logLine in m.LogLines ?? CombatHudText.Empty) {
                    Txt(_peekInfo, null, _layout.PhoneBodyTextPt, "ec-phone-log").text = CombatHudText.LogLine(logLine, m, _layout);
                }
                return;
            }
            if (melee) {
                ShowSwitch(_switchThrust, ForecastAction.Thrust, f, m.Alternative);
                ShowSwitch(_switchSwing, ForecastAction.Swing, f, m.Alternative);
            }
            VisualElement info = _peekInfo;
            FoeRowModel target = Foe(m, f.TargetSlot);
            VisualElement head = Box(null, "ec-phone-row");
            info.Add(head);
            Txt(head, null, _layout.PhoneBodyTextPt, "ec-phone-strong").text = target == null
                ? CombatHudText.Action(f.Action)
                : UiTemplates.Format(UiTemplates.EnhancedLogFoe, ("creature", target.Name), ("letter", target.Letter));
            if (target != null) {
                Txt(head, null, _layout.MinTextPt, "ec-phone-dim").text = CombatHudText.Join(
                    target.Seen > 0 ? UiTemplates.Format(UiTemplates.EnhancedCombatSeen, ("count", target.Seen)) : "",
                    target.Inspected ? "" : UiTemplates.Format(UiTemplates.HudHoldToInspect));
            }
            VisualElement line = Box(null, "ec-phone-row");
            info.Add(line);
            if (!f.Allowed || f.Warned) {
                Label r = Txt(line, "ec-phone-reason", _layout.PhoneBodyTextPt, f.Allowed ? "ec-phone-warn" : "ec-phone-reason");
                r.text = CombatHudText.Reason(f);
            }
            if (f.Allowed && melee) {
                Txt(line, null, _layout.PhoneBodyTextPt, null).text = UiTemplates.Format(UiTemplates.HudHit);
                string about = CombatHudText.HitAbout(f);
                if (about != null) Txt(line, null, _layout.PhoneBodyTextPt, null).text = about;
                else line.Add(Pips(f.HitPips));
                Txt(line, null, _layout.PhoneBodyTextPt, null).text = CombatHudText.Join("",
                    UiTemplates.Format(UiTemplates.HudDamage) + " " + CombatHudText.Damage(f));
            }
            if (f.Allowed && f.EndsTurn) {
                Txt(line, null, _layout.PhoneBodyTextPt, "ec-phone-ends").text = UiTemplates.Format(UiTemplates.HudEndsTurnShort);
            }
            if (target != null) {
                VisualElement glyphs = Box("ec-phone-glyphs", "ec-phone-row");
                info.Add(glyphs);
                foreach (FoeStatRow r in target.Rows) {
                    VisualElement g = Box(null, "ec-phone-glyph");
                    glyphs.Add(g);
                    Txt(g, null, _layout.MinTextPt, "ec-phone-dim").text = r.ShortLabel;
                    Txt(g, null, _layout.MinTextPt, "ec-phone-strong").text = r.Value;
                }
            }
            ApplySizes();
        }

        private VisualElement Pips(int n) {
            VisualElement pips = DesktopCombatHud.Pips(n);
            foreach (VisualElement p in pips.Children()) Size(p, _layout.MinTextPt, _layout.MinTextPt);
            return pips;
        }

        // A Button clicks on its release however long it was held, as long as it is still there: built once.
        private Button SwitchButton(VisualElement parent, ForecastAction action) {
            var b = new Button(() => SwitchPressed?.Invoke(action)) { name = "ec-phone-switch-" + action };
            b.AddToClassList("ec-phone-switch");
            Txt(b, null, _layout.PhoneBodyTextPt, "ec-phone-strong").text = CombatHudText.Action(action);
            Txt(b, null, _layout.MinTextPt, null);
            Size(b, _layout.PhoneConfirmWidthPt * 0.66f, _layout.FoeChipPt);
            parent.Add(b);
            return b;
        }

        private static void ShowSwitch(Button b, ForecastAction action, CombatForecast f, CombatForecast alt) {
            CombatForecast mine = f.Action == action ? f : alt?.Action == action ? alt : null;
            b.EnableInClassList("ec-phone-switch--on", f.Action == action);
            b.SetEnabled(mine != null && mine.Allowed);
            ((Label)b.ElementAt(1)).text = mine == null || !mine.Allowed ? ""
                : CombatHudText.Join(CombatHudText.Damage(mine), CombatHudText.Cost(mine, true) is { Length: > 0 } c ? c
                    : UiTemplates.Format(UiTemplates.HudFree));
        }

        // ---- the grid ----

        private VisualElement Tile(CombatCommands.Command command) {
            VisualElement tile = Box("ec-tile-" + command, "ec-phone-tile");
            tile.pickingMode = PickingMode.Position;
            tile.userData = command;
            Size(tile, _layout.TileWidthPt, _layout.TileHeightPt);
            var icon = new ArchiveImage { name = "ec-tile-icon", sizeToImage = false, pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ec-phone-tile__icon");
            Size(icon, _layout.TileHeightPt * 0.5f, _layout.TileHeightPt * 0.5f);
            tile.Add(icon);
            if (command == CombatCommands.Command.CharacterScreen) {
                icon.Add(DesktopCombatHud.PackBag(null));
            }
            Txt(tile, "ec-tile-name", _layout.MinTextPt, "ec-phone-tile__name");
            // Two different badges, never colour alone: a grey round "!" = unavailable, an amber triangle = warned.
            Label off = Txt(tile, "ec-tile-off", _layout.MinTextPt, "ec-phone-badge--off");
            off.text = "!";
            Label warn = Txt(tile, "ec-tile-warn", _layout.MinTextPt, "ec-phone-badge--warn");
            warn.text = "!";
            float badge = _layout.MinTextPt * 1.6f;
            Size(off, badge, badge);
            Size(warn, badge, badge);
            LongPress(tile, () => StoneActivated?.Invoke((CombatCommands.Command)tile.userData),
                () => StoneDescribed?.Invoke((CombatCommands.Command)tile.userData));
            return tile;
        }

        private void ShowTiles(CombatHudModel m) {
            foreach (CombatCommands.Command command in CombatHudText.PhoneGrid) {
                VisualElement tile = _tiles[command];
                bool shoot = command == CombatCommands.Command.Cast && !m.TheirTurn && CombatHudText.SharedSlotShoots(m.Stones);
                CombatCommands.Command shown = shoot ? CombatCommands.Command.Shoot : command;
                tile.userData = shown;
                ActionAvailability a = CombatHudText.Availability(m, shown);
                bool stop = m.AutoRunning && command == CombatCommands.Command.AutoResolve;   // Auto's Stop
                bool off = !stop && (!a.Enabled || m.TheirTurn);
                bool warned = !off && a.Warned;
                tile.EnableInClassList("ec-phone-tile--off", off);
                tile.EnableInClassList("ec-phone-tile--warned", warned);
                tile.Q("ec-tile-off").style.display = off && !m.TheirTurn ? DisplayStyle.Flex : DisplayStyle.None;
                tile.Q("ec-tile-warn").style.display = warned ? DisplayStyle.Flex : DisplayStyle.None;
                tile.Q<Label>("ec-tile-name").text = stop ? UiTemplates.Format(UiTemplates.HudStoneStop) : CombatHudText.Stone(command, shoot);
                var icon = (ArchiveImage)tile.Q("ec-tile-icon");
                string address = _stoneImage?.Invoke(command == CombatCommands.Command.CharacterScreen ? -1
                    : CombatHudText.ActionId(command, shoot));
                if (icon.address != address) icon.address = address;
            }
        }

        private void LongPress(VisualElement e, Action tap, Action hold) {
            IVisualElementScheduledItem timer = null;
            bool held = true;   // a release with no press on this tile (a drag ending here) is not a tap
            e.RegisterCallback<PointerDownEvent>(_ => {
                held = false;
                timer?.Pause();
                timer = e.schedule.Execute(() => { held = true; hold(); })
                    .StartingIn((long)(_layout.LongPressSeconds * 1000));
            });
            e.RegisterCallback<PointerUpEvent>(_ => {
                timer?.Pause();
                if (!held) tap();
                held = true;   // one press, one event
            });
            e.RegisterCallback<PointerLeaveEvent>(_ => { timer?.Pause(); held = true; });
        }

        // ---- the hero sheet ----

        private void BuildSheet(CombatHudModel m) {
            _sheetBody.Clear();
            Prune();
            HeroCardModel h = m?.Hero;
            string address = h != null && !h.IsFoe && h.HeadId >= 0 ? ActorStatsPanel.PortraitImage + "#" + h.HeadId : null;
            if (_sheetHead.address != address) _sheetHead.address = address;   // only when the hero changes
            if (h == null || h.IsFoe) return;   // a foe's turn: no hero, no face
            VisualElement stats = Box(null, "ec-phone-sheet__stats");
            _sheetBody.Add(stats);
            Pair(stats, UiStrings.Get("base:uistring:combat.assess_health_label").TrimEnd(':', ' '),
                UiTemplates.Format(UiTemplates.HudCurrentOfMax, ("current", h.Health), ("max", h.HealthMax)));
            Pair(stats, UiStrings.Get("base:uistring:combat.assess_stamina_label").TrimEnd(':', ' '),
                UiTemplates.Format(UiTemplates.HudCurrentOfMax, ("current", h.Stamina), ("max", h.StaminaMax)));
            Pair(stats, UiStrings.Get("base:uistring:combat.assess_speed_label").TrimEnd(':', ' '),
                UiTemplates.Format(UiTemplates.HudCurrentBase, ("current", h.Speed), ("base", h.SpeedBase)));
            Pair(stats, UiStrings.Get("base:uistring:combat.assess_strength_label").TrimEnd(':', ' '),
                UiTemplates.Format(UiTemplates.HudCurrentBase, ("current", h.Strength), ("base", h.StrengthBase)));
            float pt = _layout.PhoneSheetTextPt;
            Txt(_sheetBody, "ec-phone-sheet-conditions", pt, "ec-phone-sheet__line").text = h.Conditions.Count == 0
                ? UiTemplates.Format(UiTemplates.HudNoConditions) : string.Join(", ", h.Conditions);
            foreach (KitItemModel k in new[] { h.Weapon, h.Armour }) {
                if (k == null) continue;
                // The name and condition, then its numbers on their own line: one short line each.
                Txt(_sheetBody, null, pt, "ec-phone-strong").text = CombatHudText.Join(k.Name,
                    k.ConditionPercent >= 0 ? UiTemplates.Percent(k.ConditionPercent) : "");
                if (!string.IsNullOrEmpty(k.Stats)) Txt(_sheetBody, null, pt, "ec-phone-sheet__line").text = k.Stats;
            }
            ApplySizes();
        }

        // Label and value side by side, the label a fixed width so the values line up in one column.
        private void Pair(VisualElement parent, string label, string value) {
            VisualElement p = Box(null, "ec-phone-pair");
            parent.Add(p);
            Label l = Txt(p, null, _layout.PhoneSheetTextPt, "ec-phone-dim");
            l.text = label;
            Size(l, _layout.PhoneSheetLabelPt, -1);
            Txt(p, null, _layout.PhoneSheetTextPt, "ec-phone-strong").text = value;
        }

        // ---- sizes in points ----

        private void Resize() {
            float height = _root.resolvedStyle.height;
            Vector2 screen = _screen();
            if (float.IsNaN(height) || height <= 0f || screen.y <= 0f) return;
            float ppp = Mathf.Max(0.01f, _pixelsPerPoint());
            _scale = height / (screen.y / ppp);
            // The safe area, as fractions of the window (the panel and the screen share their aspect).
            Rect safe = _safeArea();
            _safe.style.left = Length.Percent(100f * safe.xMin / screen.x);
            _safe.style.right = Length.Percent(100f * (screen.x - safe.xMax) / screen.x);
            _safe.style.top = Length.Percent(100f * (screen.y - safe.yMax) / screen.y);   // screen y is bottom-up
            _safe.style.bottom = Length.Percent(100f * safe.yMin / screen.y);
            ApplySizes();
        }

        private void ApplySizes() {
            Prune();
            foreach ((VisualElement e, float w, float h) in _sized) {
                if (w >= 0) e.style.width = w * _scale;
                if (h >= 0) e.style.height = h * _scale;
            }
            foreach ((VisualElement e, float pt) in _fonts) e.style.fontSize = pt * _scale;
            _sheet.style.maxWidth = _layout.PhoneSheetWidthPt * _scale;
            foreach ((VisualElement e, Placement p) in _placed) {
                if (p.Top.HasValue) e.style.top = p.Top.Value * _scale;
                if (p.Left.HasValue) e.style.left = p.Left.Value * _scale;
                if (p.Right.HasValue) e.style.right = p.Right.Value * _scale;
                if (p.Bottom.HasValue) e.style.bottom = p.Bottom.Value * _scale;
            }
        }

        private readonly struct Placement {
            public Placement(float? top, float? left, float? right, float? bottom) { Top = top; Left = left; Right = right; Bottom = bottom; }
            public float? Top { get; }
            public float? Left { get; }
            public float? Right { get; }
            public float? Bottom { get; }
        }

        private readonly List<(VisualElement, Placement)> _placed = new();

        private void Place(VisualElement e, float? top = null, float? left = null, float? right = null, float? bottom = null) {
            e.style.position = Position.Absolute;
            _placed.Add((e, new Placement(top, left, right, bottom)));
        }

        private void Size(VisualElement e, float wPt, float hPt) => _sized.Add((e, wPt, hPt));

        /// <summary>
        /// Drops the size and font registrations of elements no longer in this HUD's tree: the peek and
        /// the sheet are rebuilt on every Show, and their old labels keep their own parents.
        /// </summary>
        private void Prune() {
            _sized.RemoveAll(x => !Owned(x.E));
            _fonts.RemoveAll(x => !Owned(x.E));
        }

        private bool Owned(VisualElement e) {
            for (VisualElement v = e; v != null; v = v.parent) {
                if (v == _root) return true;
            }
            return false;
        }

        /// <summary>Test seam: how many elements are registered for sizing in points.</summary>
        internal int RegisteredCount => _sized.Count + _fonts.Count;

        private void SetFont(VisualElement e, float pt) => _fonts.Add((e, pt));

        private Label Txt(VisualElement parent, string name, float pt, string cls) {
            Label l = DesktopCombatHud.Text(parent, name, "ec-phone-text");
            if (cls != null) l.AddToClassList(cls);
            SetFont(l, pt);
            return l;
        }

        private Button CamButton(VisualElement parent, string name, string text, Action press) {
            var b = new Button(press) { name = name, text = text };
            b.AddToClassList("ec-phone-button");
            Size(b, _layout.FoeChipPt, _layout.FoeChipPt);
            SetFont(b, _layout.MinTextPt);
            parent.Add(b);
            return b;
        }

        private static VisualElement Box(string name, string cls) => DesktopCombatHud.Box(name, cls);
    }
}

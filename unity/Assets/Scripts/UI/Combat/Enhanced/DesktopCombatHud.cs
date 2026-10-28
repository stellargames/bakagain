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
    /// The Enhanced combat HUD for desktop (spec 2026-10-28, frames desktop-01..18): a band along the
    /// bottom with five slots that never change shape (medallion + party, hero card, ledger, foe slot,
    /// stones), the turn sentence and the camera buttons over the board. Sizes come from
    /// <see cref="EnhancedCombatLayout"/>, styling from the theme's <c>.ec-desk-*</c> classes.
    /// </summary>
    /// <remarks>
    /// It is a HUD base, not a screen: only the band and the buttons take clicks, the board above the
    /// band is clicked through it. A stone's reason is shown only in a hover tooltip (owner,
    /// 2026-10-28): an unavailable stone is greyed, a warned one carries a small amber mark.
    /// </remarks>
    public sealed class DesktopCombatHud : ICombatHud {
        private readonly EnhancedCombatLayout _layout;
        private readonly Func<int, string> _stoneImage;
        private readonly VisualElement _root;
        private readonly VisualElement _band;
        private readonly VisualElement[] _slots = new VisualElement[5];
        private readonly VisualElement _banner;
        private readonly Label _bannerText, _bannerRound, _ledgerRound;
        private readonly Label _tip;

        // Slot 0
        private readonly VisualElement _medallion;
        private readonly ArchiveImage _medallionHead;
        private readonly Label _medallionLetter;
        private readonly Label _medallionName;
        private readonly Label _medallionCaption;
        private readonly VisualElement _coins;

        // Slot 1
        private readonly VisualElement _heroCard;
        private readonly Label _heroName;
        private readonly VisualElement _heroChips;
        private readonly Label _health, _stamina, _speed, _strength;
        private readonly Label _healthLabel, _staminaLabel, _speedLabel, _strengthLabel;
        private readonly VisualElement _poolStamina, _poolHealth, _poolCost;
        private readonly Label _weapon, _weaponCondition, _weaponStats, _armour, _armourCondition, _armourStats;
        private readonly VisualElement _partyCard;
        private readonly Label _partyTitle;
        private readonly VisualElement _partyRows;

        // Slot 2
        private readonly Label _ledgerTitle;
        private readonly Label _endsTurn;
        private readonly VisualElement _ledgerBody;
        private readonly VisualElement _ledgerFooter;

        // Slot 3
        private readonly VisualElement _foeList;
        private readonly Label _foeHeader;
        private readonly Label _foeCount;
        private readonly VisualElement _foeRows;
        private readonly VisualElement _foeCard;
        private readonly VisualElement _foeDiscs;
        private readonly Label _foeCardName;
        private readonly Label _foeCardNote;
        private readonly VisualElement _foeCardRows;

        // Slot 4
        private readonly Dictionary<CombatCommands.Command, VisualElement> _stones = new();
        private CombatHudModel _model;

        public event Action ConfirmPressed { add { } remove { } }   // desktop acts on the board click (Task 9)
        public event Action CancelPressed { add { } remove { } }
        public event Action<CombatCommands.Command> StoneActivated;
        public event Action<CombatCommands.Command> StoneDescribed;
        public event Action<int> FoeRowHovered;
        public event Action<int> FoeRowChosen;
        public event Action HeroCardRequested;
        public event Action<int> TurnQuarterPressed;
        public event Action ResetViewPressed;
        public event Action<ForecastAction> SwitchPressed { add { } remove { } }

        /// <param name="parent">The document root the HUD is laid over (full window).</param>
        /// <param name="stoneImage">COMBAT.DAT action id -> its stone's image address (-1: the bare stone);
        /// null draws stones without faces.</param>
        public DesktopCombatHud(VisualElement parent, EnhancedCombatLayout layout, Func<int, string> stoneImage = null) {
            _layout = layout;
            _stoneImage = stoneImage;
            _root = Box("ec-desk", "ec-desk");
            Fill(_root);
            _root.style.display = DisplayStyle.None;
            parent.Add(_root);

            _banner = Box("ec-desk-banner", "ec-desk-banner");
            _root.Add(_banner);
            _bannerText = Text(_banner, "ec-desk-banner-text", "ec-desk-banner__text");
            _bannerRound = Text(_banner, "ec-desk-banner-round", "ec-desk-banner__round");
            VisualElement cam = Box("ec-desk-cam", "ec-desk-cam");
            _root.Add(cam);
            CamButton(cam, "ec-cam-left", "⟲", () => TurnQuarterPressed?.Invoke(-1));
            CamButton(cam, "ec-cam-right", "⟳", () => TurnQuarterPressed?.Invoke(1));
            CamButton(cam, "ec-cam-reset", UiTemplates.Format(UiTemplates.HudReset), () => ResetViewPressed?.Invoke());

            _band = Box("ec-desk-band", "ec-desk-band");
            _band.pickingMode = PickingMode.Position;   // the board stops where the band starts
            _band.style.height = Length.Percent(layout.BandHeight * 100f);
            _root.Add(_band);
            // Each slot is its share of the band (border-box percentages, so padding never skews the
            // shares); the parchment sits inside it with the gap as its margin.
            float total = 0f;
            foreach (float w in layout.BandSlotWidths) total += w;
            var inner = new VisualElement[_slots.Length];
            for (int i = 0; i < _slots.Length; i++) {
                _slots[i] = Box("ec-desk-slot-" + i, "ec-desk-slot");
                _slots[i].style.width = Length.Percent(100f * layout.BandSlotWidths[i] / total);
                _slots[i].style.flexShrink = 0;
                _band.Add(_slots[i]);
                inner[i] = Box("ec-desk-slot-inner-" + i, "ec-desk-slot__inner");
                _slots[i].Add(inner[i]);
                if (i >= 1 && i <= 3) {
                    inner[i].AddToClassList("ec-desk-parchment");
                    inner[i].style.backgroundImage = EnhancedArt.Load("parchment");
                }
            }
            inner[0].AddToClassList("ec-desk-slot-0");

            // Slot 0: medallion and coins.
            VisualElement left = Box("ec-desk-medallion-col", "ec-desk-medallion-col");
            inner[0].Add(left);
            _medallion = Box("ec-desk-medallion", "ec-desk-medallion");
            _medallion.pickingMode = PickingMode.Position;
            _medallion.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) HeroCardRequested?.Invoke(); });
            left.Add(_medallion);
            _medallionHead = new ArchiveImage { name = "ec-desk-medallion-head", sizeToImage = false, pickingMode = PickingMode.Ignore };
            _medallionHead.AddToClassList("ec-desk-medallion__head");
            _medallion.Add(_medallionHead);
            _medallionLetter = Text(_medallion, "ec-desk-medallion-letter", "ec-desk-medallion__letter");
            _medallionName = Text(left, "ec-desk-medallion-name", "ec-desk-caption");
            _medallionCaption = Text(left, "ec-desk-medallion-caption", "ec-desk-caption--small");
            _coins = Box("ec-desk-coins", "ec-desk-coins");
            inner[0].Add(_coins);

            // Slot 1: hero card + kit, or the party on their turn.
            _heroCard = Box("ec-desk-hero", "ec-desk-card");
            inner[1].Add(_heroCard);
            VisualElement title = Box(null, "ec-desk-row");
            _heroCard.Add(title);
            _heroName = Text(title, "ec-desk-hero-name", "ec-desk-title");
            _heroChips = Box("ec-desk-hero-chips", "ec-desk-row");
            title.Add(_heroChips);
            VisualElement columns = Box(null, "ec-desk-row");
            _heroCard.Add(columns);
            VisualElement stats = Box(null, "ec-desk-col");
            VisualElement kit = Box(null, "ec-desk-col");
            columns.Add(stats);
            columns.Add(kit);
            (_healthLabel, _health) = Pair(stats);
            (_staminaLabel, _stamina) = Pair(stats);
            VisualElement pool = Box("ec-desk-pool", "ec-desk-pool");
            stats.Add(pool);
            _poolStamina = Box(null, "ec-desk-pool__stamina");
            _poolHealth = Box(null, "ec-desk-pool__health");
            _poolCost = Box("ec-desk-pool-cost", "ec-desk-pool__cost");
            pool.Add(_poolStamina); pool.Add(_poolHealth); pool.Add(_poolCost);
            (_speedLabel, _speed) = Pair(stats);
            (_strengthLabel, _strength) = Pair(stats);
            (_weapon, _weaponCondition) = Pair(kit);
            _weaponStats = Text(kit, "ec-desk-weapon-stats", "ec-desk-small");
            (_armour, _armourCondition) = Pair(kit);
            _armourStats = Text(kit, "ec-desk-armour-stats", "ec-desk-small");
            _partyCard = Box("ec-desk-party", "ec-desk-card");
            inner[1].Add(_partyCard);
            VisualElement partyHead = Box(null, "ec-desk-row");
            _partyCard.Add(partyHead);
            _partyTitle = Text(partyHead, "ec-desk-party-title", "ec-desk-title");
            Text(partyHead, null, "ec-desk-small").text = UiTemplates.Format(UiTemplates.HudWatching);
            _partyRows = Box("ec-desk-party-rows", "ec-desk-col");
            _partyCard.Add(_partyRows);

            // Slot 2: the ledger.
            VisualElement ledgerHead = Box(null, "ec-desk-row");
            inner[2].Add(ledgerHead);
            _ledgerTitle = Text(ledgerHead, "ec-desk-ledger-title", "ec-desk-title");
            _endsTurn = Text(ledgerHead, "ec-desk-ends-turn", "ec-desk-ends-turn");
            _ledgerRound = Text(ledgerHead, "ec-desk-ledger-round", "ec-desk-small");
            _ledgerBody = Box("ec-desk-ledger-body", "ec-desk-ledger-body");
            inner[2].Add(_ledgerBody);
            _ledgerFooter = Box("ec-desk-ledger-footer", "ec-desk-ledger-footer");
            inner[2].Add(_ledgerFooter);

            // Slot 3: the foe list, or one foe's card.
            _foeList = Box("ec-desk-foelist", "ec-desk-card");
            inner[3].Add(_foeList);
            VisualElement foeHead = Box(null, "ec-desk-row");
            _foeList.Add(foeHead);
            _foeHeader = Text(foeHead, null, "ec-desk-title");
            _foeHeader.text = UiTemplates.Format(UiTemplates.HudFoes);
            _foeCount = Text(foeHead, "ec-desk-foe-count", "ec-desk-small");
            _foeRows = Box("ec-desk-foe-rows", "ec-desk-col");
            _foeList.Add(_foeRows);
            _foeCard = Box("ec-desk-foecard", "ec-desk-card");
            inner[3].Add(_foeCard);
            _foeDiscs = Box("ec-desk-foe-discs", "ec-desk-row");
            _foeCard.Add(_foeDiscs);
            _foeCardName = Text(_foeCard, "ec-desk-foecard-name", "ec-desk-title");
            _foeCardNote = Text(_foeCard, "ec-desk-foecard-note", "ec-desk-small");
            _foeCardRows = Box("ec-desk-foecard-rows", "ec-desk-foecard-rows");
            _foeCard.Add(_foeCardRows);

            // Slot 4: six stones, two rows of three.
            VisualElement grid = Box("ec-desk-stones", "ec-desk-stones");
            inner[4].Add(grid);
            foreach ((CombatCommands.Command command, char key) in CombatHudText.Stones) {
                grid.Add(_stones[command] = Stone(command, key));
            }

            _tip = Text(_root, "ec-desk-tip", "ec-desk-tip");
            _tip.style.display = DisplayStyle.None;

            _root.RegisterCallback<GeometryChangedEvent>(_ => Resize());
        }

        public bool Shown => _root.style.display == DisplayStyle.Flex;

        /// <summary>The HUD's root, for tests and the host.</summary>
        public VisualElement Root => _root;

        public void Hide() {
            _root.style.display = DisplayStyle.None;
            HideTip();
        }

        public void Show(CombatHudModel m) {
            _model = m;
            _root.style.display = DisplayStyle.Flex;
            if (m == null) {
                return;
            }
            _bannerText.text = m.Sentence ?? "";
            _bannerRound.text = CombatHudText.Round(m.Round);
            _ledgerRound.text = CombatHudText.Round(m.Round);
            _ledgerRound.style.display = m.Forecast == null && m.Round > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _banner.EnableInClassList("ec-desk-banner--theirs", m.TheirTurn);
            ShowMedallion(m);
            ShowCoins(m);
            ShowHero(m);
            ShowLedger(m);
            ShowFoes(m);
            ShowStones(m);
        }

        // ---- slot 0 ----

        private void ShowMedallion(CombatHudModel m) {
            HeroCardModel h = m.Hero;
            bool foe = h?.IsFoe ?? false;
            _medallion.EnableInClassList("ec-desk-medallion--theirs", foe);
            _medallionHead.style.display = foe || h == null || h.HeadId < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            string address = h != null && h.HeadId >= 0 ? ActorStatsPanel.PortraitImage + "#" + h.HeadId : null;
            if (_medallionHead.address != address) _medallionHead.address = address;
            _medallionLetter.text = foe ? h.Letter.ToString() : "";
            _medallionName.text = h?.Name ?? "";
            _medallionCaption.text = UiTemplates.Format(UiTemplates.HudActing);
        }

        private void ShowCoins(CombatHudModel m) {
            int count = Math.Min(m.Party?.Count ?? 0, _layout.MaxPartyCoins);
            Fit(_coins, count, () => {
                VisualElement coin = Box(null, "ec-desk-coin");
                var head = new ArchiveImage { sizeToImage = false, pickingMode = PickingMode.Ignore };
                head.AddToClassList("ec-desk-coin__head");
                coin.Add(head);
                Text(coin, null, "ec-desk-coin__letter");
                PortraitRingView.Coin(coin);
                return coin;
            });
            for (int i = 0; i < count; i++) {
                PartyCoinModel p = m.Party[i];
                VisualElement coin = _coins[i];
                var head = (ArchiveImage)coin.Q(className: "ec-desk-coin__head");
                string address = p.HeadId >= 0 ? ActorStatsPanel.PortraitImage + "#" + p.HeadId : null;
                if (head.address != address) head.address = address;
                coin.Q<Label>(className: "ec-desk-coin__letter").text =
                    p.HeadId >= 0 || string.IsNullOrEmpty(p.Name) ? "" : p.Name.Substring(0, 1);
                PortraitRingView.SetCoin(coin, Fractions(p.Stamina, p.StaminaMax, p.Health, p.HealthMax), p.State);
            }
            ResizeCoins();
        }

        private static (double S, double H) Fractions(int stamina, int staminaMax, int health, int healthMax) {
            int max = staminaMax + healthMax;
            return max <= 0 ? (0d, 0d) : ((double)Math.Max(0, stamina) / max, (double)Math.Max(0, health) / max);
        }

        // ---- slot 1 ----

        private void ShowHero(CombatHudModel m) {
            bool party = m.TheirTurn || m.Hero == null;
            _heroCard.style.display = party ? DisplayStyle.None : DisplayStyle.Flex;
            _partyCard.style.display = party ? DisplayStyle.Flex : DisplayStyle.None;
            if (party) {
                _partyTitle.text = UiTemplates.Format(UiTemplates.HudPartyTitle);
                int n = m.Party?.Count ?? 0;
                Fit(_partyRows, n, () => {
                    VisualElement row = Box(null, "ec-desk-row");
                    Text(row, null, "ec-desk-party__name");
                    VisualElement bar = Box(null, "ec-desk-pool");
                    bar.Add(Box(null, "ec-desk-pool__stamina"));
                    bar.Add(Box(null, "ec-desk-pool__health"));
                    row.Add(bar);
                    Text(row, null, "ec-desk-party__value");
                    return row;
                });
                for (int i = 0; i < n; i++) {
                    PartyCoinModel p = m.Party[i];
                    VisualElement row = _partyRows[i];
                    row.Q<Label>(className: "ec-desk-party__name").text = p.Name;
                    SetPool(row.Q(className: "ec-desk-pool"), p.Stamina, p.StaminaMax, p.Health, p.HealthMax);
                    row.Q<Label>(className: "ec-desk-party__value").text = p.State == RingVariant.Down
                        ? UiTemplates.Format(UiTemplates.HudDown)
                        : UiTemplates.Format(UiTemplates.HudCurrentOfMax, ("current", p.Health), ("max", p.HealthMax));
                }
                return;
            }
            HeroCardModel h = m.Hero;
            _heroName.text = h.Name;
            Fit(_heroChips, h.Conditions.Count, () => Text(null, null, "ec-desk-chip"));
            for (int i = 0; i < h.Conditions.Count; i++) ((Label)_heroChips[i]).text = h.Conditions[i];
            _healthLabel.text = UiStrings.Get("base:uistring:combat.assess_health_label").TrimEnd(':', ' ');
            _staminaLabel.text = UiStrings.Get("base:uistring:combat.assess_stamina_label").TrimEnd(':', ' ');
            _speedLabel.text = UiStrings.Get("base:uistring:combat.assess_speed_label").TrimEnd(':', ' ');
            _strengthLabel.text = UiStrings.Get("base:uistring:combat.assess_strength_label").TrimEnd(':', ' ');
            _health.text = UiTemplates.Format(UiTemplates.HudCurrentOfMax, ("current", h.Health), ("max", h.HealthMax));
            _stamina.text = UiTemplates.Format(UiTemplates.HudCurrentOfMax, ("current", h.Stamina), ("max", h.StaminaMax));
            _speed.text = UiTemplates.Format(UiTemplates.HudCurrentBase, ("current", h.Speed), ("base", h.SpeedBase));
            _strength.text = UiTemplates.Format(UiTemplates.HudCurrentBase, ("current", h.Strength), ("base", h.StrengthBase));
            SetPool(_poolStamina.parent, h.Stamina, h.StaminaMax, h.Health, h.HealthMax);
            int max = h.StaminaMax + h.HealthMax;
            int cost = m.Forecast == null ? 0 : m.Forecast.StaminaCost + m.Forecast.HealthCost;
            _poolCost.style.display = cost > 0 && max > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (cost > 0 && max > 0) {
                float end = 100f * (h.Stamina + h.Health) / max;
                float width = 100f * cost / max;
                _poolCost.style.left = Length.Percent(Math.Max(0f, end - width));
                _poolCost.style.width = Length.Percent(width);
            }
            Kit(h.Weapon, _weapon, _weaponCondition, _weaponStats);
            Kit(h.Armour, _armour, _armourCondition, _armourStats);
        }

        private static void Kit(KitItemModel item, Label name, Label condition, Label stats) {
            name.text = item?.Name ?? "";
            condition.text = item == null || item.ConditionPercent < 0 ? "" : UiTemplates.Percent(item.ConditionPercent);
            stats.text = item?.Stats ?? "";
        }

        private static void SetPool(VisualElement pool, int stamina, int staminaMax, int health, int healthMax) {
            (double s, double hp) = Fractions(stamina, staminaMax, health, healthMax);
            pool.Q(className: "ec-desk-pool__stamina").style.width = Length.Percent((float)(s * 100));
            pool.Q(className: "ec-desk-pool__health").style.width = Length.Percent((float)(hp * 100));
        }

        // ---- slot 2 ----

        private void ShowLedger(CombatHudModel m) {
            CombatForecast f = m.Forecast;
            IReadOnlyList<string> log = m.LogLines ?? CombatHudText.Empty;
            _ledgerBody.Clear();
            _ledgerFooter.Clear();
            string actor = m.Hero?.Name ?? "";
            _endsTurn.text = f != null && f.EndsTurn && f.Allowed ? UiTemplates.Format(UiTemplates.HudEndsTurn, ("name", actor)) : "";
            _endsTurn.style.display = _endsTurn.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _ledgerFooter.style.display = f == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (f == null) {
                _ledgerTitle.text = UiTemplates.Format(UiTemplates.HudFightLog);
                foreach (string line in log) Text(_ledgerBody, null, "ec-desk-log");
                for (int i = 0; i < log.Count; i++) ((Label)_ledgerBody[i]).text = log[i];
                return;
            }
            FoeRowModel target = f.TargetSlot >= 0 ? Find(m.Foes, f.TargetSlot) : null;
            _ledgerTitle.text = target != null
                ? UiTemplates.Format(UiTemplates.EnhancedLogFoe, ("creature", target.Name), ("letter", target.Letter))
                : CombatHudText.Action(f.Action);
            bool melee = f.Action == ForecastAction.Thrust || f.Action == ForecastAction.Swing;
            if (melee) {
                CombatForecast thrust = f.Action == ForecastAction.Thrust ? f : m.Alternative;
                CombatForecast swing = f.Action == ForecastAction.Swing ? f : m.Alternative;
                VisualElement table = Box("ec-desk-forecast", "ec-desk-table");
                _ledgerBody.Add(table);
                Row(table, "", Head(thrust, ForecastAction.Thrust, f), Head(swing, ForecastAction.Swing, f));
                Row(table, UiTemplates.Format(UiTemplates.HudHit), Hit(thrust), Hit(swing));
                Row(table, UiTemplates.Format(UiTemplates.HudDamage), Cell(thrust, CombatHudText.Damage), Cell(swing, CombatHudText.Damage));
                Row(table, UiTemplates.Format(UiTemplates.HudCost), Cell(thrust, x => CombatHudText.Cost(x, false)), Cell(swing, x => CombatHudText.Cost(x, false)));
                Row(table, UiTemplates.Format(UiTemplates.HudWalk), Cell(thrust, CombatHudText.Walk), Cell(swing, CombatHudText.Walk));
            } else if (f.Action == ForecastAction.Move) {
                VisualElement table = Box("ec-desk-forecast", "ec-desk-table");
                _ledgerBody.Add(table);
                Row(table, UiTemplates.Format(UiTemplates.HudWalk), Plain(CombatHudText.Walk(f)));
            } else {
                string cost = CombatHudText.Cost(f, false);
                if (f.Action != ForecastAction.Inspect && cost.Length > 0) {
                    VisualElement table = Box("ec-desk-forecast", "ec-desk-table");
                    _ledgerBody.Add(table);
                    Row(table, UiTemplates.Format(UiTemplates.HudCost), Plain(cost));
                }
            }
            string reason = CombatHudText.Reason(f);
            if (reason.Length > 0) {
                Label r = Text(_ledgerBody, "ec-desk-reason", "ec-desk-reason");
                r.text = reason;
                r.EnableInClassList("ec-desk-reason--warn", f.Allowed && f.Warned);
            }
            int from = Math.Max(0, log.Count - _layout.LedgerLogLines);
            for (int i = from; i < log.Count; i++) Text(_ledgerFooter, null, "ec-desk-log").text = log[i];
        }

        private static FoeRowModel Find(IReadOnlyList<FoeRowModel> foes, int slot) {
            if (foes == null) return null;
            foreach (FoeRowModel f in foes) if (f.Slot == slot) return f;
            return null;
        }

        private static VisualElement Head(CombatForecast f, ForecastAction action, CombatForecast chosen) {
            Label l = Plain(CombatHudText.Action(action));
            l.AddToClassList("ec-desk-table__head");
            l.EnableInClassList("ec-desk-table__head--chosen", chosen.Action == action);
            l.EnableInClassList("ec-desk-off", f == null || !f.Allowed);
            return l;
        }

        private static VisualElement Hit(CombatForecast f) {
            if (f == null || !f.Allowed) return Plain("–");
            string about = CombatHudText.HitAbout(f);
            return about != null ? Plain(about) : Pips(f.HitPips);
        }

        /// <summary>Three pips, the first <paramref name="n"/> filled: drawn, never a glyph.</summary>
        internal static VisualElement Pips(int n) {
            VisualElement pips = Box(null, "ec-pips");
            for (int i = 0; i < 3; i++) {
                VisualElement pip = Box(null, "ec-pip");
                pip.EnableInClassList("ec-pip--on", i < n);
                pips.Add(pip);
            }
            return pips;
        }

        private static VisualElement Cell(CombatForecast f, Func<CombatForecast, string> text) =>
            Plain(f == null || !f.Allowed ? "–" : text(f));

        private static Label Plain(string text) {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("ec-desk-table__cell");
            return l;
        }

        private static void Row(VisualElement table, string label, params VisualElement[] cells) {
            VisualElement row = Box(null, "ec-desk-table__row");
            Label head = Plain(label);
            head.AddToClassList("ec-desk-table__label");
            row.Add(head);
            foreach (VisualElement c in cells) {
                c.AddToClassList("ec-desk-table__col");
                row.Add(c);
            }
            table.Add(row);
        }

        // ---- slot 3 ----

        private void ShowFoes(CombatHudModel m) {
            IReadOnlyList<FoeRowModel> foes = m.Foes ?? Array.Empty<FoeRowModel>();
            FoeRowModel card = m.CardFoe is int slot ? Find(foes, slot) : null;
            _foeList.style.display = card == null ? DisplayStyle.Flex : DisplayStyle.None;
            _foeCard.style.display = card == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (card == null) {
                int alive = 0;
                foreach (FoeRowModel f in foes) if (!f.Down) alive++;
                _foeCount.text = UiTemplates.Format(UiTemplates.HudFoeCount, ("count", alive));
                Fit(_foeRows, foes.Count, () => FoeRow());
                for (int i = 0; i < foes.Count; i++) {
                    FoeRowModel f = foes[i];
                    VisualElement row = _foeRows[i];
                    row.userData = f.Slot;
                    row.Q<Label>(className: "ec-letter").text = f.Letter.ToString();
                    row.Q<Label>(className: "ec-desk-foe__name").text = f.Name;
                    SetMarks(row.Q(className: "ec-marks"), f.Swing ? 2 : f.Thrust ? 1 : 0);
                    row.Q<Label>(className: "ec-desk-foe__note").text = f.Down ? UiTemplates.Format(UiTemplates.HudDown)
                        : f.Seen > 0 ? "−" + f.Seen
                        : f.Far ? UiTemplates.Format(UiTemplates.HudFar) : "";
                    row.EnableInClassList("ec-desk-foe--down", f.Down);
                    row.EnableInClassList("ec-desk-foe--acting", f.Acting);
                }
                return;
            }
            Fit(_foeDiscs, foes.Count, () => {
                Label disc = Text(null, null, "ec-letter");
                disc.pickingMode = PickingMode.Position;
                disc.RegisterCallback<ClickEvent>(_ => FoeRowChosen?.Invoke((int)disc.userData));
                return disc;
            });
            for (int i = 0; i < foes.Count; i++) {
                var disc = (Label)_foeDiscs[i];
                disc.userData = foes[i].Slot;
                disc.text = foes[i].Letter.ToString();
                disc.EnableInClassList("ec-letter--chosen", foes[i].Slot == card.Slot);
            }
            _foeCardName.text = UiTemplates.Format(UiTemplates.HudJoin, ("a", card.Letter), ("b", card.Name));
            _foeCardNote.text = card.Inspected ? UiTemplates.Format(UiTemplates.HudInspected)
                : card.Seen > 0 ? UiTemplates.Format(UiTemplates.EnhancedCombatSeen, ("count", card.Seen)) : "";
            Fit(_foeCardRows, card.Rows.Count, () => {
                VisualElement row = Box(null, "ec-desk-foecard__row");
                Text(row, null, "ec-desk-foecard__label");
                Text(row, null, "ec-desk-foecard__value");
                return row;
            });
            for (int i = 0; i < card.Rows.Count; i++) {
                _foeCardRows[i].Q<Label>(className: "ec-desk-foecard__label").text = card.Rows[i].Label;
                Label value = _foeCardRows[i].Q<Label>(className: "ec-desk-foecard__value");
                value.text = card.Rows[i].Value;
                value.EnableInClassList("ec-desk-foecard__value--known", card.Rows[i].Known);
            }
        }

        private VisualElement FoeRow() {
            VisualElement row = Box(null, "ec-desk-foe");
            row.pickingMode = PickingMode.Position;
            Text(row, null, "ec-letter");
            Text(row, null, "ec-desk-foe__name");
            row.Add(Marks());
            Text(row, null, "ec-desk-foe__note");
            row.RegisterCallback<PointerEnterEvent>(_ => FoeRowHovered?.Invoke((int)row.userData));
            row.RegisterCallback<PointerLeaveEvent>(_ => FoeRowHovered?.Invoke(-1));
            row.RegisterCallback<ClickEvent>(_ => FoeRowChosen?.Invoke((int)row.userData));
            return row;
        }

        /// <summary>The board's ⚔ / ⚔⚔ reach marks, drawn as crossed blades.</summary>
        internal static VisualElement Marks() {
            VisualElement marks = Box(null, "ec-marks");
            for (int i = 0; i < 2; i++) {
                VisualElement blade = Box(null, "ec-mark");
                blade.generateVisualContent += ctx => {
                    Rect r = blade.contentRect;
                    Painter2D p = ctx.painter2D;
                    p.lineWidth = Mathf.Max(1f, r.width * 0.12f);
                    p.strokeColor = blade.resolvedStyle.color;
                    p.BeginPath(); p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.Stroke();
                    p.BeginPath(); p.MoveTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMin, r.yMax)); p.Stroke();
                };
                marks.Add(blade);
            }
            return marks;
        }

        internal static void SetMarks(VisualElement marks, int n) {
            for (int i = 0; i < marks.childCount; i++) {
                marks[i].style.display = i < n ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // ---- slot 4 ----

        private VisualElement Stone(CombatCommands.Command command, char key) {
            VisualElement stone = Box("ec-stone-" + command, "ec-stone");
            stone.pickingMode = PickingMode.Position;
            stone.userData = command;
            var disc = new ArchiveImage { name = "ec-stone-disc", sizeToImage = false, pickingMode = PickingMode.Ignore };
            disc.AddToClassList("ec-stone__disc");
            stone.Add(disc);
            if (command == CombatCommands.Command.CharacterScreen) {
                // Pack: the original's bare stone with the port's satchel over it (asset-origins.md).
                VisualElement satchel = Box("ec-stone-satchel", "ec-stone__satchel");
                satchel.style.backgroundImage = EnhancedArt.Load("satchel");
                disc.Add(satchel);
            }
            Text(stone, "ec-stone-key", "ec-stone__key").text = key.ToString();
            Text(stone, "ec-stone-name", "ec-stone__name");
            stone.Add(Box("ec-stone-warn", "ec-stone__warn"));
            stone.RegisterCallback<ClickEvent>(_ => StoneActivated?.Invoke(StoneCommand(stone)));
            stone.RegisterCallback<PointerDownEvent>(e => { if (e.button == 1) StoneDescribed?.Invoke(StoneCommand(stone)); });
            stone.RegisterCallback<PointerEnterEvent>(_ => Hover(stone, true));
            stone.RegisterCallback<PointerLeaveEvent>(_ => Hover(stone, false));
            return stone;
        }

        // Cast and Shoot share one stone; the shown command is kept on the element.
        private static CombatCommands.Command StoneCommand(VisualElement stone) => (CombatCommands.Command)stone.userData;

        private void ShowStones(CombatHudModel m) {
            foreach ((CombatCommands.Command command, char _) in CombatHudText.Stones) {
                VisualElement stone = _stones[command];
                CombatCommands.Command shown = command;
                bool shoot = false;
                if (command == CombatCommands.Command.Cast) {
                    // The shared slot's winner is the original's own rule (shooting is tested first).
                    shoot = !m.TheirTurn && CombatMenuSlots.CapabilitySlot(
                        CombatHudText.Availability(m, CombatCommands.Command.Shoot).Enabled,
                        CombatHudText.Availability(m, CombatCommands.Command.Cast).Enabled) == CombatMenuSlots.ShootActionId;
                    shown = shoot ? CombatCommands.Command.Shoot : CombatCommands.Command.Cast;
                }
                stone.userData = shown;
                ActionAvailability a = CombatHudText.Availability(m, shown);
                bool asleep = m.TheirTurn;
                stone.EnableInClassList("ec-stone--off", !a.Enabled || asleep);
                stone.EnableInClassList("ec-stone--warned", a.Enabled && a.Warned && !asleep);
                stone.Q<Label>("ec-stone-name").text = CombatHudText.Stone(command, shoot);
                int id = CombatHudText.ActionId(command, shoot);
                string address = _stoneImage?.Invoke(command == CombatCommands.Command.CharacterScreen ? -1 : id);
                var disc = (ArchiveImage)stone.Q("ec-stone-disc");
                if (disc.address != address) disc.address = address;
            }
            if (_tipStone != null) Hover(_tipStone, true);
        }

        private VisualElement _tipStone;

        /// <summary>Hover: the stone glows and, when it has a reason, the tooltip shows it.</summary>
        internal void Hover(VisualElement stone, bool on) {
            stone.EnableInClassList("ec-stone--hover", on);
            if (!on) {
                HideTip();
                return;
            }
            _tipStone = stone;
            string reason = CombatHudText.Availability(_model, StoneCommand(stone)).Reason;
            if (string.IsNullOrEmpty(reason) || _model == null || _model.TheirTurn) {
                _tip.style.display = DisplayStyle.None;
                return;
            }
            _tip.text = reason;
            _tip.style.display = DisplayStyle.Flex;
            Rect r = stone.worldBound;
            Vector2 local = VisualElementExtensions.WorldToLocal(_root, new Vector2(r.center.x, r.yMin));
            _tip.style.left = local.x;
            _tip.style.top = local.y;
        }

        /// <summary>Test seam: what a pointer entering or leaving the stone does.</summary>
        internal void HoverStone(CombatCommands.Command command, bool on) => Hover(_stones[command], on);

        private void HideTip() {
            _tipStone = null;
            _tip.style.display = DisplayStyle.None;
        }

        // ---- sizes ----

        private void Resize() {
            float band = _band.resolvedStyle.height;
            if (float.IsNaN(band) || band <= 0f) return;
            float medallion = band * _layout.MedallionBandFraction;
            _medallion.style.width = medallion;
            _medallion.style.height = medallion;
            float stone = band * _layout.StoneBandFraction;
            foreach (VisualElement s in _stones.Values) {
                VisualElement disc = s.Q("ec-stone-disc");
                disc.style.width = stone;
                disc.style.height = stone;
            }
            ResizeCoins();
        }

        private void ResizeCoins() {
            float height = _root.resolvedStyle.height;
            if (float.IsNaN(height) || height <= 0f) return;
            float coin = _layout.PartyCoinPx / _layout.DesktopReferenceHeightPx * height;
            foreach (VisualElement c in _coins.Children()) {
                c.style.width = coin;
                c.style.height = coin;
            }
        }

        // ---- helpers ----

        private void CamButton(VisualElement parent, string name, string text, Action press) {
            var b = new Button(press) { name = name, text = text };
            b.AddToClassList("ec-desk-cam__button");
            parent.Add(b);
        }

        /// <summary>Over the whole of its parent, whatever the theme says.</summary>
        internal static void Fill(VisualElement e) {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        }

        internal static VisualElement Box(string name, string cls) {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            if (cls != null) e.AddToClassList(cls);
            return e;
        }

        internal static Label Text(VisualElement parent, string name, string cls) {
            var l = new Label { name = name, pickingMode = PickingMode.Ignore };
            if (cls != null) l.AddToClassList(cls);
            parent?.Add(l);
            return l;
        }

        private static (Label Label, Label Value) Pair(VisualElement parent) {
            VisualElement row = Box(null, "ec-desk-pair");
            parent.Add(row);
            return (Text(row, null, "ec-desk-pair__label"), Text(row, null, "ec-desk-pair__value"));
        }

        /// <summary>Makes <paramref name="list"/> hold exactly <paramref name="count"/> children.</summary>
        internal static void Fit(VisualElement list, int count, Func<VisualElement> make) {
            while (list.childCount > count) list.RemoveAt(list.childCount - 1);
            while (list.childCount < count) list.Add(make());
        }
    }
}

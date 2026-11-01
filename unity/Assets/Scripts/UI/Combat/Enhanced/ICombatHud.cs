namespace BakAgain.UI.Combat.Enhanced {
    using System;
    using System.Collections.Generic;
    using GameData.Resources.Combat;

    /// <summary>
    /// The Enhanced combat HUD (TASK-972): the desktop band or the phone docks. A view renders a
    /// <see cref="CombatHudModel"/> and reports what was pressed; it never computes game state.
    /// </summary>
    public interface ICombatHud {
        void Show(CombatHudModel model);

        void Hide();

        bool Shown { get; }

        event Action ConfirmPressed;

        event Action CancelPressed;

        /// <summary>A stone (desktop click) or an action tile (phone tap).</summary>
        event Action<CombatCommands.Command> StoneActivated;

        /// <summary>Describe a stone: desktop right click, phone long press.</summary>
        event Action<CombatCommands.Command> StoneDescribed;

        /// <summary>A foe's row (desktop list) or chip (phone) is pointed at; -1 when it is left.</summary>
        event Action<int> FoeRowHovered;

        event Action<int> FoeRowChosen;

        /// <summary>Phone: the hero chip. Desktop: a right click on the medallion (the character sheet).</summary>
        event Action HeroCardRequested;

        event Action ResetViewPressed;

        /// <summary>Phone: the peek's Thrust / Swing switch, with the action asked for. Desktop: never (Shift+click swings).</summary>
        event Action<ForecastAction> SwitchPressed;
    }

    /// <summary>
    /// Everything one HUD frame shows. Built by <see cref="EnhancedCombatController.Model"/> (through
    /// <see cref="CombatHudModelBuilder"/>), never by the views.
    /// </summary>
    /// <param name="ActorSlot">The acting combatant's party slot (1-based), or 0 on their turn.</param>
    /// <param name="Hero">The acting hero's card; on their turn the acting foe's (<see cref="HeroCardModel.IsFoe"/>).</param>
    /// <param name="Forecast">The previewed action, null when nothing is chosen.</param>
    /// <param name="Stones">Availability per stone; Cast stands for the shared Cast/Shoot slot.</param>
    /// <param name="Sentence">The one-line turn sentence ("Owyn's turn").</param>
    /// <param name="Alternative">The other melee column next to <paramref name="Forecast"/> (Swing beside
    /// Thrust); null when there is none.</param>
    /// <param name="Round">The fight's round, 1-based; 0 when unknown (not shown).</param>
    public sealed record CombatHudModel(
        int ActorSlot,
        HeroCardModel Hero,
        IReadOnlyList<PartyCoinModel> Party,
        CombatForecast Forecast,
        IReadOnlyList<string> LogLines,
        IReadOnlyList<FoeRowModel> Foes,
        int? CardFoe,
        IReadOnlyDictionary<CombatCommands.Command, ActionAvailability> Stones,
        bool TheirTurn,
        string Sentence,
        CombatForecast Alternative = null,
        int Round = 0,
        HeroCardModel CardHero = null);

    /// <summary>A weapon or armour line: its name, condition in percent (-1 = not tracked) and stat line.</summary>
    public sealed record KitItemModel(string Name, int ConditionPercent, string Stats);

    /// <summary>
    /// The hero card. Speed and Strength are "current (base)", read without writing anything
    /// (<c>StatEngine.Peek</c>). On their turn it is the acting foe: <see cref="IsFoe"/>, a
    /// <see cref="Letter"/>, no head and no numbers.
    /// </summary>
    public sealed record HeroCardModel(
        string Name, int HeadId, IReadOnlyList<string> Conditions,
        int Health, int HealthMax, int Stamina, int StaminaMax,
        int Speed, int SpeedBase, int Strength, int StrengthBase,
        KitItemModel Weapon, KitItemModel Armour, bool IsFoe = false, char Letter = ' ');

    /// <summary>One party coin: a hero or a summon, with its Health/Stamina ring.</summary>
    public sealed record PartyCoinModel(
        int Slot, string Name, int HeadId, BakAgain.UI.InGame.RingVariant State,
        int Health, int HealthMax, int Stamina, int StaminaMax);

    /// <summary>One foe line of the 8-row card: the label and what is known ("?" until it is).</summary>
    public sealed record FoeStatRow(string Label, string ShortLabel, string Value, bool Known);

    /// <summary>
    /// One foe, lettered by roster order. <see cref="Thrust"/>/<see cref="Swing"/> are the board's
    /// reach marks; <see cref="Seen"/> the damage the player saw; <see cref="Rows"/> the card.
    /// </summary>
    public sealed record FoeRowModel(
        int Slot, char Letter, string Name, bool Thrust, bool Swing, int Seen, bool Far,
        bool Inspected, bool Down, bool Acting, IReadOnlyList<FoeStatRow> Rows);
}

namespace BakAgain.UI.Combat.Enhanced {
    using System.Collections.Generic;
    using GameData.Resources.Combat;
    using GameData.Resources.Text;

    /// <summary>How both HUDs word a forecast and a stone. Wording only; every number is the model's.</summary>
    internal static class CombatHudText {
        public static string Action(ForecastAction a) => UiTemplates.Format(a switch {
            ForecastAction.Thrust => UiTemplates.HudThrust,
            ForecastAction.Swing => UiTemplates.HudSwing,
            ForecastAction.Move => UiTemplates.HudMove,
            ForecastAction.Inspect => UiTemplates.HudInspect,
            ForecastAction.Defend => UiTemplates.HudStoneDefend,
            ForecastAction.Rest => UiTemplates.HudStoneRest,
            ForecastAction.Retreat => UiTemplates.HudStoneRetreat,
            ForecastAction.Auto => UiTemplates.HudStoneAuto,
            _ => UiTemplates.HudStonePack,
        });

        /// <summary>The forecast's verb: "Walks 3, then thrusts" for a Thrust that walks first, else its action.</summary>
        public static string Verb(CombatForecast f) =>
            f.Action == ForecastAction.Thrust && f.Path != null && f.Path.Count > 1
                ? UiTemplates.Format(UiTemplates.HudWalksThenThrusts, ("n", f.Path.Count - 1))
                : Action(f.Action);

        public static string Stone(CombatCommands.Command c, bool shoot = false) => UiTemplates.Format(c switch {
            CombatCommands.Command.Cast => shoot ? UiTemplates.HudStoneShoot : UiTemplates.HudStoneCast,
            CombatCommands.Command.Shoot => UiTemplates.HudStoneShoot,
            CombatCommands.Command.BackOrRetreat => UiTemplates.HudStoneRetreat,
            CombatCommands.Command.Defend => UiTemplates.HudStoneDefend,
            CombatCommands.Command.CharacterScreen => UiTemplates.HudStonePack,
            CombatCommands.Command.Rest => UiTemplates.HudStoneRest,
            _ => UiTemplates.HudStoneAuto,
        });

        /// <summary>"about 52%" once Defense is revealed; null while only pips can be shown.</summary>
        public static string HitAbout(CombatForecast f) =>
            f.HitPercentAbout is int p ? UiTemplates.Format(UiTemplates.HudAboutPercent, ("n", p)) : null;

        public static string Damage(CombatForecast f) =>
            f.DamageAbout is int d ? UiTemplates.Format(UiTemplates.HudAboutDamage, ("n", d))
            : f.DamageHigh == int.MaxValue ? UiTemplates.Format(UiTemplates.HudDamageOpen, ("low", f.DamageLow))
            : f.DamageLow == f.DamageHigh ? f.DamageLow.ToString()
            : UiTemplates.Format(UiTemplates.HudDamageRange, ("low", f.DamageLow), ("high", f.DamageHigh));

        public static string Cost(CombatForecast f, bool shortForm) =>
            f.StaminaCost > 0 ? UiTemplates.Format(shortForm ? UiTemplates.HudStaminaCostShort : UiTemplates.HudStaminaCost, ("n", f.StaminaCost))
            : f.HealthCost > 0 ? UiTemplates.Format(UiTemplates.HudHealthCostShort, ("n", f.HealthCost))
            : f.RestGain > 0 ? UiTemplates.Format(UiTemplates.HudRestGain, ("n", f.RestGain))
            : shortForm ? "" : UiTemplates.Format(UiTemplates.HudFree);

        /// <summary>The path includes the start cell.</summary>
        public static string Walk(CombatForecast f) {
            int squares = f.Path == null ? 0 : System.Math.Max(0, f.Path.Count - 1);
            return squares == 0 ? UiTemplates.Format(UiTemplates.HudNone)
                : UiTemplates.Format(UiTemplates.HudSquares, ("n", squares));
        }

        /// <summary>Joins the non-empty parts with the template's separator.</summary>
        public static string Join(params string[] parts) {
            string result = "";
            foreach (string p in parts) {
                if (string.IsNullOrEmpty(p)) continue;
                result = result.Length == 0 ? p : UiTemplates.Format(UiTemplates.HudJoin, ("a", result), ("b", p));
            }
            return result;
        }

        /// <summary>"round N", or empty when the round is unknown.</summary>
        public static string Round(int n) => n > 0 ? UiTemplates.Format(UiTemplates.HudRound, ("n", n)) : "";

        public static string Reason(CombatForecast f) =>
            !string.IsNullOrEmpty(f?.Reason) ? f.Reason
            : string.IsNullOrEmpty(f?.ReasonKey) ? "" : UiTemplates.Format(f.ReasonKey);

        public static ActionAvailability Availability(CombatHudModel m, CombatCommands.Command c) =>
            m?.Stones != null && m.Stones.TryGetValue(c, out ActionAvailability a) ? a : ActionAvailability.On;

        /// <summary>The six stones, desktop order: Cast, Retreat, Defend / Pack, Rest, Auto, with their keys.</summary>
        public static readonly (CombatCommands.Command Command, char Key)[] Stones = {
            (CombatCommands.Command.Cast, 'C'), (CombatCommands.Command.BackOrRetreat, 'F'),
            (CombatCommands.Command.Defend, 'D'), (CombatCommands.Command.CharacterScreen, 'P'),
            (CombatCommands.Command.Rest, 'R'), (CombatCommands.Command.AutoResolve, 'A'),
        };

        /// <summary>The phone grid, 2x3, left to right: Auto, Flee, Rest / Defend, Pack, Cast.</summary>
        public static readonly CombatCommands.Command[] PhoneGrid = {
            CombatCommands.Command.AutoResolve, CombatCommands.Command.BackOrRetreat, CombatCommands.Command.Rest,
            CombatCommands.Command.Defend, CombatCommands.Command.CharacterScreen, CombatCommands.Command.Cast,
        };

        /// <summary>The COMBAT.DAT entry each stone is drawn from (its BICONS face); Pack has none of its own.</summary>
        public static int ActionId(CombatCommands.Command c, bool shoot = false) => c switch {
            CombatCommands.Command.Cast => shoot ? CombatCommands.ShootId : CombatCommands.CastId,
            CombatCommands.Command.BackOrRetreat => CombatCommands.BackOrRetreatId,
            CombatCommands.Command.Defend => CombatCommands.DefendId,
            CombatCommands.Command.Rest => CombatCommands.RestId,
            CombatCommands.Command.AutoResolve => CombatCommands.AutoResolveId,
            _ => -1,
        };

        public static readonly IReadOnlyList<string> Empty = System.Array.Empty<string>();
    }
}

namespace BakAgain.UI.Combat.Enhanced {
    using System.Collections.Generic;
    using BakAgain.Combat;
    using BakAgain.Core;
    using BakAgain.UI.InGame;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Text;

    /// <summary>
    /// Builds a <see cref="CombatHudModel"/> from the live fight: who acts, the party, the foes with
    /// what the player knows, the log and the stones' availability. Every value is read from the
    /// rules' own owners (<see cref="CombatRuntime"/>, <see cref="GameSession"/>) without writing;
    /// nothing is decided here. The forecast is the caller's (<see cref="EnhancedCombatController"/>).
    /// </summary>
    public static class CombatHudModelBuilder {
        /// <summary>The model for the fight as it stands, or null outside one.</summary>
        /// <param name="marks">The board's reach marks (<c>HotspotService.EnhancedBoardMarks</c>); null for none.</param>
        public static CombatHudModel Build(CombatRuntime combat, GameSession session, BoardMarks marks,
            int logLines, CombatForecast forecast = null, CombatForecast alternative = null, int? cardFoe = null) {
            CombatEncounter fight = combat?.Encounter;
            if (fight == null) {
                return null;
            }
            Combatant acting = fight.Current;
            bool theirs = acting != null && !fight.Party.Contains(acting);
            marks ??= BoardMarks.Empty;

            var party = new List<PartyCoinModel>();
            foreach (Combatant c in fight.Party) {
                (int hMax, int sMax) = Maxima(combat, c);
                RingVariant state = c.IsDead ? RingVariant.Down
                    : c == acting ? RingVariant.Active
                    : combat.IsSummoned(c) ? RingVariant.Summon
                    : (c.Flags & CombatantFlags.Ready) == 0 ? RingVariant.Acted
                    : RingVariant.Ready;
                party.Add(new PartyCoinModel(c.PartySlot, NameOf(session, combat, c), c.IsPartyMember ? c.ClassId : -1,
                    state, c.Health, hMax, c.Stamina, sMax));
            }

            var foes = new List<FoeRowModel>();
            for (int i = 0; i < fight.Enemies.Count; i++) {
                Combatant foe = fight.Enemies[i];
                if (foe == null) {
                    continue;
                }
                bool thrust = System.Linq.Enumerable.Contains(marks.ThrustTargets, i);
                foes.Add(new FoeRowModel(i, BoardMarks.LetterFor(i), session?.CreatureNameOf(foe.ClassId) ?? "",
                    thrust, System.Linq.Enumerable.Contains(marks.SwingTargets, i), combat.Knowledge.Seen(i),
                    Far: !theirs && !thrust && !foe.IsDead,
                    combat.Knowledge.Inspected(i), foe.IsDead, foe == acting, CardRows(combat, foe, i)));
            }

            HeroCardModel hero = acting == null ? null
                : theirs ? FoeCard(session, fight, acting)
                : HeroCard(combat, session, acting);
            string sentence = hero == null ? ""
                : UiTemplates.Format(UiTemplates.HudTurnOf, ("name", hero.Name));
            return new CombatHudModel(theirs || acting == null ? 0 : acting.PartySlot, hero, party, forecast,
                combat.Log.Last(logLines), foes, cardFoe,
                theirs ? new Dictionary<CombatCommands.Command, ActionAvailability>() : combat.AvailabilityFor(acting),
                theirs, sentence, alternative, combat.Log.Round);
        }

        private static (int HealthMax, int StaminaMax) Maxima(CombatRuntime combat, Combatant c) {
            ActorStat[] stats = combat.StatsFor(c);
            int Max(ActorAttribute a) => stats != null && (int)a < stats.Length && stats[(int)a] != null ? stats[(int)a].Max : 0;
            return (Max(ActorAttribute.Health), Max(ActorAttribute.Stamina));
        }

        private static string NameOf(GameSession session, CombatRuntime combat, Combatant c) {
            if (!c.IsPartyMember || combat.IsSummoned(c)) {
                return session?.CreatureNameOf(c.ClassId) ?? "";
            }
            IReadOnlyList<string> names = session?.PartyActorNames ?? System.Array.Empty<string>();
            return c.ClassId >= 0 && c.ClassId < names.Count ? names[c.ClassId] : "";
        }

        private static HeroCardModel HeroCard(CombatRuntime combat, GameSession session, Combatant hero) {
            (int hMax, int sMax) = Maxima(combat, hero);
            var conditions = new List<string>();
            ActorConditions ranks = hero.IsPartyMember ? session?.ConditionsOf(hero.ClassId) : null;
            for (int c = 0; c < ActorConditions.Count && ranks != null; c++) {
                if (ranks.Has((ActorCondition)c)) {
                    conditions.Add(ActorLabels.ConditionName(c));
                }
            }
            (GameData.Resources.Object.ObjectInfo weapon, int weaponCondition) =
                combat.EquippedItem(hero, CombatRuntime.MeleeWeaponCategory);
            if (weapon == null) {
                (weapon, weaponCondition) = combat.EquippedItem(hero, CombatRuntime.StaffCategory);
            }
            (GameData.Resources.Object.ObjectInfo armour, int armourCondition) =
                combat.EquippedItem(hero, CombatRuntime.ArmorCategory);
            return new HeroCardModel(NameOf(session, combat, hero), hero.IsPartyMember ? hero.ClassId : -1,
                conditions, hero.Health, hMax, hero.Stamina, sMax,
                combat.PeekStat(hero, ActorAttribute.Speed), combat.PeekStat(hero, ActorAttribute.Speed, unscaled: true),
                combat.PeekStat(hero, ActorAttribute.Strength), combat.PeekStat(hero, ActorAttribute.Strength, unscaled: true),
                weapon == null ? null : new KitItemModel(weapon.Name ?? "", weaponCondition,
                    UiTemplates.Format(UiTemplates.HudWeaponLine, ("thrust", weapon.ThrustBaseDamage),
                        ("swing", weapon.SwingBaseDamage))),
                armour == null ? null : new KitItemModel(armour.Name ?? "", armourCondition,
                    UiTemplates.Format(UiTemplates.HudArmourLine, ("n", armour.SwingAccuracy_ArmorMod_BowAccuracy))));
        }

        /// <summary>Their turn: the acting foe, by letter and kind; no numbers (hidden information).</summary>
        private static HeroCardModel FoeCard(GameSession session, CombatEncounter fight, Combatant foe) {
            int i = fight.Enemies.IndexOf(foe);
            char letter = i >= 0 ? BoardMarks.LetterFor(i) : ' ';
            string kind = session?.CreatureNameOf(foe.ClassId) ?? "";
            string name = UiTemplates.Format(UiTemplates.EnhancedLogFoe, ("creature", kind), ("letter", letter));
            return new HeroCardModel(name, -1, System.Array.Empty<string>(), 0, 0, 0, 0, 0, 0, 0, 0, null, null,
                IsFoe: true, Letter: letter);
        }

        // The Inspect panel's rows, in the card's two columns: Health, Stamina, Missile, Cast | Speed,
        // Strength, Melee, Defense. "?" until revealed, and an absent ability is "?" too (spec).
        private static readonly (ActorAttribute Attribute, string Key, string Short, bool Percent)[] CardRowDefs = {
            (ActorAttribute.Health, "base:uistring:combat.assess_health_label", UiTemplates.HudShortHealth, false),
            (ActorAttribute.Stamina, "base:uistring:combat.assess_stamina_label", UiTemplates.HudShortStamina, false),
            (ActorAttribute.AccuracyCrossbow, "base:uistring:combat.assess_missile_label", UiTemplates.HudShortMissile, true),
            (ActorAttribute.AccuracyCasting, "base:uistring:combat.assess_cast_label", UiTemplates.HudShortCast, true),
            (ActorAttribute.Speed, "base:uistring:combat.assess_speed_label", UiTemplates.HudShortSpeed, false),
            (ActorAttribute.Strength, "base:uistring:combat.assess_strength_label", UiTemplates.HudShortStrength, false),
            (ActorAttribute.AccuracyMelee, "base:uistring:combat.assess_melee_label", UiTemplates.HudShortMelee, true),
            (ActorAttribute.Defense, "base:uistring:combat.assess_defense_label", UiTemplates.HudShortDefense, true),
        };

        private static IReadOnlyList<FoeStatRow> CardRows(CombatRuntime combat, Combatant foe, int slot) {
            var rows = new List<FoeStatRow>(CardRowDefs.Length);
            foreach ((ActorAttribute attribute, string key, string shortKey, bool percent) in CardRowDefs) {
                string label = UiStrings.Get(key).TrimEnd(':', ' ');
                bool known = combat.Knowledge.TryGet(slot, attribute, out int value);
                rows.Add(new FoeStatRow(label, UiTemplates.Format(shortKey),
                    !known ? UiTemplates.Format(UiTemplates.HudUnknown)
                    : percent ? UiTemplates.Percent(value) : value.ToString(), known));
            }
            return rows;
        }
    }
}

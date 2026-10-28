namespace BakAgain.Tests.Editor.Combat {
    using System.Collections.Generic;
    using BakAgain.Combat;
    using BakAgain.Core;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using GameData.Resources.Layout;
    using GameData.Resources.Text;
    using NUnit.Framework;
    using MeleeAttack = GameData.Resources.Combat.CombatActionDispatch.MeleeAttack;

    /// <summary>The forecast model (TASK-972 phase 1, task 6).</summary>
    public class CombatForecastTests {
        // Hero: Melee 60, Strength 10, no weapon. Foe: Defense 40 -> rating 10 -> true chance 50%,
        // armour 10% -> a landed blow does 9.
        private static (CombatRuntime Runtime, CombatEncounter Fight) Duel(
            (byte X, byte Y) foe, byte foeDefense = 40) {
            ActorStat[] hero = CombatRuntimeTests.StatBlock(health: 30);
            hero[(int)ActorAttribute.AccuracyMelee] = new ActorStat { Base = 60, Max = 99 };
            hero[(int)ActorAttribute.Strength] = new ActorStat { Base = 10, Max = 99 };
            hero[(int)ActorAttribute.Health] = new ActorStat { Base = 30, Max = 30 };   // full health: no wound scaling on the stats
            hero[(int)ActorAttribute.Stamina] = new ActorStat { Base = 20, Max = 99 };
            var session = new GameSession();
            session.SetActorStatsForTest(0, hero);
            session.SetActiveParty(1, new byte[] { 0 });
            ActorStat[] enemy = CombatRuntimeTests.StatBlock(health: 20);
            enemy[(int)ActorAttribute.Health] = new ActorStat { Base = 20, Max = 20 };
            enemy[(int)ActorAttribute.Defense] = new ActorStat { Base = foeDefense, Max = 99 };
            session.SetRosterActorForTest(400, enemy, gridX: foe.X, gridY: foe.Y);
            var runtime = new CombatRuntime(session, null, CombatRuntimeTests.Entries(((byte)3, (byte)4)));
            return (runtime, runtime.EnterRoster(new short[] { 400 }));
        }

        private static readonly EnhancedCombatLayout Layout = new EnhancedCombatLayout();

        [Test]
        public void TheForecastUsesTheTrueRoll_NotThePanelPreview() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            r.Knowledge.Record(0, ActorAttribute.Defense, 40);
            CombatForecast thrust = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);

            Assert.AreEqual(50, thrust.HitPercentAbout);
            Assert.AreEqual(2, thrust.HitPips);
            Assert.GreaterOrEqual(50, Layout.HitPipEvenAtLeast);
            Assert.Less(50, Layout.HitPipLikelyAtLeast);
            // The same chance the fight rolls against: 49 lands, 50 misses.
            Assert.IsTrue(r.ResolveMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust, _ => 49).Hit);
            (CombatRuntime r2, CombatEncounter f2) = Duel((4, 4));
            Assert.IsFalse(r2.ResolveMelee(f2.Party[0], f2.Enemies[0], MeleeAttack.Thrust, _ => 50).Hit);
        }

        [Test]
        public void BeforeInspectNoPercent_AfterInspectRevealedDefenseAPercent() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            CombatForecast before = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.IsNull(before.HitPercentAbout);
            Assert.IsNull(before.DamageAbout);
            Assert.AreEqual(2, before.HitPips, "pips are shown before Inspect");
            Assert.AreEqual((6, 9), (before.DamageLow, before.DamageHigh), "a fixed bucket before");

            r.Knowledge.Record(0, ActorAttribute.Defense, 40);
            CombatForecast after = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.AreEqual(50, after.HitPercentAbout);
            Assert.AreEqual(9, after.DamageAbout);
        }

        [Test]
        public void SwingCostsOneStamina_OrHealthWhenStaminaIsZero() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            CombatForecast swing = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Swing);
            Assert.AreEqual((1, 0), (swing.StaminaCost, swing.HealthCost));
            CombatForecast thrust = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.AreEqual((0, 0), (thrust.StaminaCost, thrust.HealthCost));

            f.Party[0].Stamina = 0;
            swing = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Swing);
            Assert.AreEqual((0, 1), (swing.StaminaCost, swing.HealthCost));
        }

        [Test]
        public void AnUnreachableEnemyIsNotAllowed_WithItsReason() {
            (CombatRuntime r, CombatEncounter f) = Duel((7, 12));
            f.Party[0].Speed = 2;
            CombatForecast thrust = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.IsFalse(thrust.Allowed);
            Assert.AreEqual(UiTemplates.EnhancedForecastOutOfReach, thrust.ReasonKey);
            Assert.IsEmpty(thrust.Path);

            CombatForecast move = r.ForecastMove(f.Party[0], 7, 12);
            Assert.IsFalse(move.Allowed);
            Assert.AreEqual(UiTemplates.EnhancedForecastOutOfReach, move.ReasonKey);
        }

        [Test]
        public void ARouteIsAllowedAndCarriesItsPath() {
            (CombatRuntime r, CombatEncounter f) = Duel((6, 4));
            f.Party[0].Speed = 5;
            CombatForecast thrust = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.IsTrue(thrust.Allowed);
            Assert.AreEqual((3, 4), thrust.Path[0]);
            CombatForecast move = r.ForecastMove(f.Party[0], 5, 4);
            Assert.IsTrue(move.Allowed);
            Assert.AreEqual((5, 4), move.Path[move.Path.Count - 1]);
            Assert.AreEqual((3, 4), (f.Party[0].X, f.Party[0].Y), "a forecast moves nobody");
        }

        private static (CombatRuntime Runtime, CombatEncounter Fight, GameSession Session) Resting(
            byte maxEach, byte health, byte stamina) {
            ActorStat[] stats = CombatRuntimeTests.StatBlock(health: health);
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = health, Max = maxEach };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = stamina, Max = maxEach };
            var session = new GameSession();
            session.SetActorStatsForTest(0, stats);
            session.SetActorConditionsForTest(0, new GameData.Resources.Character.ActorConditions());
            session.SetActiveParty(1, new byte[] { 0 });
            var runtime = new CombatRuntime(session, null, CombatRuntimeTests.Entries(((byte)3, (byte)4)));
            return (runtime, runtime.EnterRoster(null), session);
        }

        [Test]
        public void RestWhilePoisonedIsAllowedButWarned_GainZero() {
            (CombatRuntime r, CombatEncounter f, GameSession session) = Resting(40, 10, 10);
            session.ConditionsOf(0)[ActorCondition.Poisoned] = 2;
            CombatForecast rest = r.ForecastNoTarget(f.Party[0], ForecastAction.Rest);
            Assert.IsTrue(rest.Allowed);
            Assert.IsTrue(rest.Warned);
            Assert.AreEqual(0, rest.RestGain);
            Assert.AreEqual(UiTemplates.EnhancedAvailNoHealCondition, rest.ReasonKey);
            Assert.IsTrue(rest.EndsTurn);
        }

        [Test]
        public void RestGainIsAThirtiethOfThePool_CappedAt80() {
            (CombatRuntime r, CombatEncounter f, _) = Resting(40, 10, 10);
            Assert.AreEqual(2, r.ForecastNoTarget(f.Party[0], ForecastAction.Rest).RestGain, "(40+40)/30");

            f.Party[0].Health = 32;
            f.Party[0].Stamina = 31;   // pool 63 of a cap of 64
            Assert.AreEqual(1, r.ForecastNoTarget(f.Party[0], ForecastAction.Rest).RestGain);
            f.Party[0].Stamina = 32;   // at the cap already
            Assert.AreEqual(0, r.ForecastNoTarget(f.Party[0], ForecastAction.Rest).RestGain);
        }

        [Test]
        public void RetreatWithAHeroDownIsRefused() {
            (CombatRuntime r, CombatEncounter f) = CombatRuntimeTests.ArcherAgainst(((byte)7, (byte)0));
            Assert.IsTrue(r.ForecastNoTarget(f.Party[0], ForecastAction.Retreat).Allowed);
            f.Party.Add(new Combatant { PartySlot = 2, ClassId = 1, Flags = CombatantFlags.Dead });
            CombatForecast retreat = r.ForecastNoTarget(f.Party[0], ForecastAction.Retreat);
            Assert.IsFalse(retreat.Allowed);
            Assert.AreEqual(UiTemplates.EnhancedAvailHeroDown, retreat.ReasonKey);
        }

        [Test]
        public void EveryForecastEndsTheTurn() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            Combatant hero = f.Party[0];
            var all = new List<CombatForecast> {
                r.ForecastMelee(hero, f.Enemies[0], MeleeAttack.Thrust),
                r.ForecastMelee(hero, f.Enemies[0], MeleeAttack.Swing),
                r.ForecastMove(hero, 3, 5),
                r.ForecastInspect(hero, f.Enemies[0]),
            };
            foreach (ForecastAction a in new[] { ForecastAction.Defend, ForecastAction.Rest,
                         ForecastAction.Retreat, ForecastAction.Auto, ForecastAction.Pack }) {
                all.Add(r.ForecastNoTarget(hero, a));
            }
            foreach (CombatForecast c in all) {
                Assert.IsTrue(c.EndsTurn, c.Action.ToString());
            }
            Assert.AreEqual(UiTemplates.EnhancedForecastPackNote,
                r.ForecastNoTarget(hero, ForecastAction.Pack).ReasonKey);
        }

        [Test]
        public void TheBandIsAFixedBucket_NotCentredOnTheTrueDamage() {
            // Defense 40 and 20 both leave 9 after armour; Defense 0 leaves 10 (the next bucket).
            var seen = new List<(int, int)>();
            foreach (byte def in new byte[] { 40, 20 }) {
                (CombatRuntime r, CombatEncounter f) = Duel((4, 4), def);
                CombatForecast c = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
                seen.Add((c.DamageLow, c.DamageHigh));
            }
            Assert.AreEqual(seen[0], seen[1], "same bucket, same band");
            (CombatRuntime r0, CombatEncounter f0) = Duel((4, 4), 0);
            CombatForecast ten = r0.ForecastMelee(f0.Party[0], f0.Enemies[0], MeleeAttack.Thrust);
            Assert.AreEqual((10, 14), (ten.DamageLow, ten.DamageHigh));
        }

        [Test]
        public void TheBucketBoundsComeFromTheLayout_AndTheLastIsOpenEnded() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            r.ForecastLayout = new EnhancedCombatLayout { DamageBandEdges = new[] { 1, 5, 9 } };
            CombatForecast c = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.AreEqual((9, int.MaxValue), (c.DamageLow, c.DamageHigh));
            r.ForecastLayout = new EnhancedCombatLayout { DamageBandEdges = new[] { 1, 5, 12 } };
            c = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            Assert.AreEqual((5, 11), (c.DamageLow, c.DamageHigh));
        }

        [Test]
        public void TheSwingColumnUsesTheSwingDamage() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            r.Knowledge.Record(0, ActorAttribute.Defense, 40);
            CombatForecast swing = r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Swing);
            int dealt = r.ResolveMelee(f.Party[0], f.Enemies[0], MeleeAttack.Swing, _ => 0).Damage;
            Assert.AreEqual(dealt, swing.DamageAbout);
        }

        [Test]
        public void AForecastWritesNoCachedStatByte() {
            (CombatRuntime r, CombatEncounter f) = Duel((4, 4));
            var blocks = new[] { r.StatsFor(f.Party[0]), r.StatsFor(f.Enemies[0]) };
            foreach (ActorStat[] block in blocks) {
                foreach (ActorStat stat in block) {
                    stat.Effective = 77;
                }
            }
            r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Thrust);
            r.ForecastMelee(f.Party[0], f.Enemies[0], MeleeAttack.Swing);
            foreach (ActorStat[] block in blocks) {
                foreach (ActorStat stat in block) {
                    Assert.AreEqual(77, stat.Effective);
                }
            }
        }
    }
}

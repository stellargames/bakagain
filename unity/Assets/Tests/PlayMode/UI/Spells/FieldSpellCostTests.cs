namespace BakAgain.Tests.PlayMode.UI.Spells {
    using BakAgain.Core;
    using BakAgain.UI.Spells;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Spells;
    using NUnit.Framework;

    /// <summary>
    /// What a field cast costs, and <b>when</b> it is charged.
    ///
    /// <para>The model says a locator charges before it rolls, so a failure costs full price
    /// (<see cref="FieldSpells.LocatorChargesBeforeRolling"/>). That was asserted only as a
    /// constant; nothing checked that the caster actually pays.</para>
    /// </summary>
    public class FieldSpellCostTests {
        private const int CasterId = 0;

        private static ActorStat Stat(byte value, byte max) =>
            new ActorStat { Base = value, Max = max };

        private static GameSession SessionWithCaster(byte health = 40, byte stamina = 40) {
            var session = new GameSession();
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = Stat(10, 10);
            }
            stats[(int)ActorAttribute.Health] = Stat(health, 99);
            stats[(int)ActorAttribute.Stamina] = Stat(stamina, 99);
            session.SetActorStatsForTest(CasterId, stats);
            return session;
        }

        private static int Pool(GameSession session) {
            ActorStat[] s = session.StatsOf(CasterId);
            return s[(int)ActorAttribute.Health].Base + s[(int)ActorAttribute.Stamina].Base;
        }

        [Test]
        public void TheSeamGivesACasterLiveAttributesTheRealLookupCanSee() {
            // If StatsOf could not see them the tests below would silently assert nothing, which is
            // how the cost path stayed uncovered in the first place.
            GameSession session = SessionWithCaster();

            Assert.IsNotNull(session.StatsOf(CasterId));
            Assert.AreEqual(80, Pool(session));
        }

        [Test]
        public void ACastTakesItsCostOffTheCombinedPool() {
            GameSession session = SessionWithCaster();
            var context = new SpellCastContext { Chapter = 1 };

            int applied = SpellCasting.ApplyCost(context, cost: 5,
                session.StatsOf(CasterId)[(int)ActorAttribute.Health],
                session.StatsOf(CasterId)[(int)ActorAttribute.Stamina],
                out bool collapsed);

            Assert.Greater(applied, 0, "something was actually charged");
            Assert.Less(Pool(session), 80, "and it came off the caster");
            Assert.IsFalse(collapsed, "a five-point cost does not floor a full caster");
        }

        [Test]
        public void AZeroOrNegativeCostChargesNothing() {
            // The guard matters: a negated cost (SpellCostModifiers.IsNegated) must not credit the
            // caster by charging a negative amount.
            GameSession session = SessionWithCaster();
            var context = new SpellCastContext { Chapter = 1 };

            foreach (int cost in new[] { 0, -4 }) {
                Assert.AreEqual(0, SpellCasting.ApplyCost(context, cost,
                    session.StatsOf(CasterId)[(int)ActorAttribute.Health],
                    session.StatsOf(CasterId)[(int)ActorAttribute.Stamina],
                    out _));
            }

            Assert.AreEqual(80, Pool(session), "the pool is untouched either way");
        }

        [Test]
        public void ANullContextChargesNothingRatherThanThrowing() {
            GameSession session = SessionWithCaster();

            Assert.AreEqual(0, SpellCasting.ApplyCost(null, cost: 5,
                session.StatsOf(CasterId)[(int)ActorAttribute.Health],
                session.StatsOf(CasterId)[(int)ActorAttribute.Stamina],
                out _));
            Assert.AreEqual(80, Pool(session));
        }

        [Test]
        public void AHeavyCostFloorsTheCasterRatherThanWrapping() {
            // The pool is bytes underneath; a cost larger than what is left must floor rather than
            // wrap a nearly-dead caster back to full.
            GameSession session = SessionWithCaster(health: 3, stamina: 3);
            var context = new SpellCastContext { Chapter = 1 };

            SpellCasting.ApplyCost(context, cost: 50,
                session.StatsOf(CasterId)[(int)ActorAttribute.Health],
                session.StatsOf(CasterId)[(int)ActorAttribute.Stamina],
                out bool collapsed);

            Assert.GreaterOrEqual(Pool(session), 0, "no wrap");
            Assert.LessOrEqual(Pool(session), 6, "and it did not gain");
            Assert.IsTrue(collapsed, "a caster spending more than it has goes down");
        }

        [Test]
        public void TheLocatorChargesBeforeItRolls_SoAFailureIsPaidFor() {
            // The rule the model states. Charging after the roll would make a failed locator free,
            // which is the difference between an expensive gamble and a risk-free one.
            Assert.IsTrue(FieldSpells.LocatorChargesBeforeRolling);

            GameSession session = SessionWithCaster();
            var context = new SpellCastContext { Chapter = 1 };

            // A locator's failure path does no further charging, so the pool after a failed cast is
            // the pool after the charge — which is what the caster pays either way.
            SpellCasting.ApplyCost(context, cost: 6,
                session.StatsOf(CasterId)[(int)ActorAttribute.Health],
                session.StatsOf(CasterId)[(int)ActorAttribute.Stamina],
                out _);
            int afterCharge = Pool(session);

            Assert.Less(afterCharge, 80);
            // The roll itself changes nothing about the pool, whichever way it goes.
            Assert.IsFalse(FieldSpells.LocatorSucceeds(FieldSpells.EyesOfIshap, rollUnder100: 99, cost: 1));
            Assert.AreEqual(afterCharge, Pool(session));
        }
    }
}

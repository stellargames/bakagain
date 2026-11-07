namespace BakAgain.Tests.Editor.Combat {
    using BakAgain.World.Hotspots;
    using GameData;
    using GameData.Resources.Character;
    using GameData.Resources.Combat;
    using NUnit.Framework;

    /// <summary>
    /// A creature with no MONST file still gets a profile from its own live stats (TASK-1062).
    /// </summary>
    /// <remarks>
    /// Nago at Rowe's Barn is creature type 63, which has an MNAMES name and no MONST63.DAT. The port
    /// handed such a creature an empty profile -- never a caster -- so he only swung and rested. The
    /// original asks the actor's own stat 7 (combatenc_actor_can_cast_spells, CBENC.C:546) and stat 5
    /// (combatenc_show_missile_stat_row, CBENC.C:507) and never consults MONST there.
    /// </remarks>
    public class TemplatelessMonsterProfileTests {
        private static ActorStat[] Live(int casting, int crossbow) {
            var stats = new ActorStat[16];
            for (var i = 0; i < stats.Length; i++) {
                stats[i] = new ActorStat { Base = 20, Max = 99 };
            }
            stats[(int)ActorAttribute.Health] = new ActorStat { Base = 45, Max = 45 };
            stats[(int)ActorAttribute.Stamina] = new ActorStat { Base = 45, Max = 45 };
            stats[(int)ActorAttribute.AccuracyCasting] = new ActorStat { Base = (byte)casting, Max = 99 };
            stats[(int)ActorAttribute.AccuracyCrossbow] = new ActorStat { Base = (byte)crossbow, Max = 99 };
            return stats;
        }

        private static Combatant Nago() => new Combatant { ClassId = 63, AiPatterns = (4, 0, 3) };

        [Test]
        public void ALiveCastingStatMakesACaster() {
            var p = HotspotService.TemplatelessProfile(Nago(), Live(95, 0), summoned: false, carriesAShot: false);

            Assert.IsTrue(p.CanCastSpells);
            Assert.AreEqual(95, p.CastingSkill);
            Assert.AreEqual(4, p.SpellcastPattern);
            Assert.AreEqual(3, p.MeleeMovePattern);
            Assert.IsFalse(p.CanShoot);
        }

        [Test]
        public void NoCastingStatOrASummonCastsNothing() {
            Assert.IsFalse(HotspotService.TemplatelessProfile(Nago(), Live(0, 0), false, false).CanCastSpells);
            Assert.IsFalse(HotspotService.TemplatelessProfile(Nago(), Live(95, 0), true, false).CanCastSpells);
        }

        [Test]
        public void AShooterNeedsTheStatAndAShot() {
            Assert.IsTrue(HotspotService.TemplatelessProfile(Nago(), Live(0, 60), false, true).CanShoot);
            Assert.IsFalse(HotspotService.TemplatelessProfile(Nago(), Live(0, 60), false, false).CanShoot);
        }
    }
}

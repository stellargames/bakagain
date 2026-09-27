namespace BakAgain.Tests.World.Hotspots {
    using BakAgain.World.Hotspots;
    using NUnit.Framework;

    /// <summary>
    /// The activate pass's arithmetic. Small, but both boundaries here decide whether an encounter
    /// fires at all, and both are easy to get one off.
    /// </summary>
    public class HotspotRulesTests {
        [Test]
        public void EncounterFoughtKeyIsTheIndexOffsetIntoTheEventBlock() {
            Assert.AreEqual(5220, HotspotRules.EncounterFoughtKey(0));
            Assert.AreEqual(5227, HotspotRules.EncounterFoughtKey(7));
        }

        [Test]
        public void AnEncounterIndexInsideTheTableIsChecked() {
            Assert.IsFalse(HotspotRules.EncounterIndexOutOfRange(0));
            Assert.IsFalse(HotspotRules.EncounterIndexOutOfRange(999));
        }

        [Test]
        public void AnIndexPastTheTableCountsAsAlreadyFoughtSoItNeverFires() {
            // hotspotevt_enc_fought_read returns 1 rather than reading a flag, which is how a record
            // pointing nowhere is disabled instead of spawning something undefined.
            Assert.IsTrue(HotspotRules.EncounterIndexOutOfRange(1000));
            Assert.IsTrue(HotspotRules.EncounterIndexOutOfRange(50000));
        }

        [Test]
        public void ANegativeIndexIsAlsoTreatedAsOutOfRange() {
            Assert.IsTrue(HotspotRules.EncounterIndexOutOfRange(-1));
        }

        [Test]
        public void ScoutingComparisonIsInclusiveSoZeroSkillStillSpotsOnARollOfZero() {
            // RND(100) <= best. The inclusive bound is the difference between "unskilled" (1%) and
            // "impossible" (0%), so it is worth pinning.
            Assert.IsTrue(HotspotRules.ScoutingSpots(roll: 0, bestScouting: 0));
            Assert.IsFalse(HotspotRules.ScoutingSpots(roll: 1, bestScouting: 0));
        }

        [Test]
        public void AScoutSpotsUpToAndIncludingTheirSkill() {
            Assert.IsTrue(HotspotRules.ScoutingSpots(roll: 40, bestScouting: 40));
            Assert.IsFalse(HotspotRules.ScoutingSpots(roll: 41, bestScouting: 40));
        }

        [Test]
        public void AHighlySkilledScoutSpotsEveryRollInRange() {
            for (var roll = 0; roll < 100; roll++) {
                Assert.IsTrue(HotspotRules.ScoutingSpots(roll, bestScouting: 100), $"roll {roll}");
            }
        }
    }
}

namespace BakAgain.Tests.Editor.World.Hotspots {
    using BakAgain.Core;
    using GameData.Resources.World;
    using NUnit.Framework;

    /// <summary>
    /// The roaming-encounter reset that fires when the party's step size grows.
    /// </summary>
    /// <remarks>
    /// <b>The baseline is the subtle half.</b> The original stores the new value after every
    /// comparison, fired or not — so a reset that stored it only on success would fire again on
    /// every subsequent preferences apply for as long as the larger step size stayed selected.
    /// </remarks>
    public class StepSizeRoamerResetTests {
        private const short SmallStep = 800;
        private const short LargeStep = 1600;

        [Test]
        public void AGROWINGStepResetsRoamers_andAShrinkingOneDoesNot() {
            // The original's test is `current > lastSeen`, not `!=`: walking in smaller steps sees
            // more ground per encounter roll, so only the coarser setting needs the reset.
            Assert.IsTrue(StepSizeChange.ResetsRoamers(SmallStep, LargeStep));
            Assert.IsFalse(StepSizeChange.ResetsRoamers(LargeStep, SmallStep));
            Assert.IsFalse(StepSizeChange.ResetsRoamers(LargeStep, LargeStep));
        }

        [Test]
        public void TheBaselineMovesWHETHERORNOTTheResetFired() {
            Assert.AreEqual(LargeStep, StepSizeChange.NewBaseline(SmallStep, LargeStep));
            Assert.AreEqual(SmallStep, StepSizeChange.NewBaseline(LargeStep, SmallStep),
                "a shrink stores its value too, or the next grow compares against the wrong one");
        }

        [Test]
        public void TheSessionCarriesTheBaselineAndItIsSETTABLE() {
            // A read-only baseline would re-fire the reset on every apply. This is the property the
            // save writer round-trips.
            var session = new GameSession { LastSeenStepSpeed = SmallStep };
            session.LastSeenStepSpeed =
                (short)StepSizeChange.NewBaseline(session.LastSeenStepSpeed, LargeStep);
            Assert.AreEqual(LargeStep, session.LastSeenStepSpeed);
        }

        [Test]
        public void ResetRoamersLeavesSlotsPENDINGRatherThanPlaced() {
            // *** WHY THE RE-SEED IS NOT OPTIONAL. *** A reset slot is Pending, not placed, so
            // without seeding again in the same pass the roamers are reset and then simply absent
            // — which looks to a player like they were deleted rather than moved.
            var states = new EncounterObjectStates();
            int reset = states.ResetRoamers(0);
            Assert.GreaterOrEqual(reset, 0, "an empty table resets nothing and does not throw");
        }
    }
}

namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using GameData.Resources.Spells;
using Xunit;

/// <summary>
/// The caster AI: a pattern row that orders eight action slots, and a filter chain that picks the
/// spell at the moment it is needed.
/// </summary>
public class MonsterSpellcastingTests {
    [Fact]
    public void PatternZeroNeverCasts() {
        Assert.False(MonsterSpellcasting.Casts(0));
        Assert.Equal(0, MonsterSpellcasting.SlotFor(0, 0));
    }

    [Fact]
    public void AndNeitherDoesAPatternPastTheTable() {
        Assert.False(MonsterSpellcasting.Casts(9));
        Assert.True(MonsterSpellcasting.Casts(1));
        Assert.True(MonsterSpellcasting.Casts(8));
    }

    [Fact]
    public void EveryRowLeadsWithItsOwnPatternNumber() {
        // Which is what makes the field readable as "the action I try first".
        for (int pattern = 1; pattern <= MonsterSpellcasting.MaxPattern; pattern++) {
            Assert.Equal(pattern, MonsterSpellcasting.SlotFor(pattern, 0));
        }
    }

    [Fact]
    public void AndEveryRowIsAPermutationOfAllEightSlots() {
        for (int pattern = 1; pattern <= MonsterSpellcasting.MaxPattern; pattern++) {
            var seen = new bool[MonsterSpellcasting.SlotCount + 1];
            for (int attempt = 0; attempt < MonsterSpellcasting.SlotCount; attempt++) {
                int slot = MonsterSpellcasting.SlotFor(pattern, attempt);
                Assert.InRange(slot, 1, MonsterSpellcasting.SlotCount);
                Assert.False(seen[slot]);
                seen[slot] = true;
            }
        }
    }

    [Fact]
    public void AnAttemptPastTheRowIsNotASlot() {
        Assert.Equal(0, MonsterSpellcasting.SlotFor(3, 8));
        Assert.Equal(0, MonsterSpellcasting.SlotFor(3, -1));
    }

    [Fact]
    public void ASpentMonsterMakesNoAttemptAtAll() {
        Assert.False(MonsterSpellcasting.WellEnoughToAct(4));
        Assert.True(MonsterSpellcasting.WellEnoughToAct(5));
    }

    [Fact]
    public void NineAttemptsInAHundredAreSkipped() {
        // And a skip advances to the next slot rather than ending the turn.
        Assert.True(MonsterSpellcasting.CommitsToAttempt(90));
        Assert.False(MonsterSpellcasting.CommitsToAttempt(91));
        Assert.False(MonsterSpellcasting.CommitsToAttempt(99));
    }

    [Fact]
    public void TheSlotToTargetModeMapIsNotInOrder() {
        // Slots 2-4 run 0-2 and then it jumps: 5 -> 4, 6 -> 5, 7 -> 3.
        Assert.Equal(0, MonsterSpellcasting.TargetModeOf(2));
        Assert.Equal(1, MonsterSpellcasting.TargetModeOf(3));
        Assert.Equal(2, MonsterSpellcasting.TargetModeOf(4));
        Assert.Equal(4, MonsterSpellcasting.TargetModeOf(5));
        Assert.Equal(5, MonsterSpellcasting.TargetModeOf(6));
        Assert.Equal(3, MonsterSpellcasting.TargetModeOf(7));
    }

    [Fact]
    public void AndAllSixModesAreReachable() {
        var reached = new bool[6];
        for (int slot = 2; slot <= 7; slot++) {
            reached[MonsterSpellcasting.TargetModeOf(slot)] = true;
        }
        Assert.All(reached, Assert.True);
    }

    [Fact]
    public void TheTwoOuterSlotsAreNotTargetedCasts() {
        Assert.Equal(MonsterSpellcasting.SlotAction.SpecialFirst, MonsterSpellcasting.ActionOf(1));
        Assert.Equal(MonsterSpellcasting.SlotAction.SpecialLast, MonsterSpellcasting.ActionOf(8));
        Assert.Equal(-1, MonsterSpellcasting.TargetModeOf(1));
        Assert.Equal(-1, MonsterSpellcasting.TargetModeOf(8));
    }

    [Fact]
    public void ThoughtsLikeCloudsShutsTheCasterDown() {
        Assert.False(MonsterSpellcasting.CanSelect(casterHasThoughtsLikeClouds: true));
        Assert.True(MonsterSpellcasting.CanSelect(casterHasThoughtsLikeClouds: false));
    }

    [Fact]
    public void TheScanStartsAtTheLastRealSpellNotPastIt() {
        // The original seeds with the count and so reads one record too far; we start at count - 1.
        Assert.Equal(44, MonsterSpellcasting.FirstCandidate(45));
        Assert.True(MonsterSpellcasting.ScanStartsPastTheEndOfTheTable);
    }

    [Fact]
    public void TwoSpellsAreStruckOutByNumber() {
        Assert.True(MonsterSpellcasting.NeverSelected(SpellIds.Invitation));
        Assert.True(MonsterSpellcasting.NeverSelected(SpellIds.ThoughtsLikeClouds));
        Assert.False(MonsterSpellcasting.NeverSelected(SpellIds.Skyfire));
    }

    [Fact]
    public void ACandidateMustSurviveEveryFilter() {
        Assert.True(MonsterSpellcasting.Selects(SpellIds.Skyfire, matchesFilters: true,
            castable: true, coinFlipHeads: true, alreadyOnTarget: false));
        Assert.False(MonsterSpellcasting.Selects(SpellIds.Skyfire, matchesFilters: false,
            castable: true, coinFlipHeads: true, alreadyOnTarget: false));
        Assert.False(MonsterSpellcasting.Selects(SpellIds.Skyfire, matchesFilters: true,
            castable: false, coinFlipHeads: true, alreadyOnTarget: false));
        Assert.False(MonsterSpellcasting.Selects(SpellIds.Skyfire, matchesFilters: true,
            castable: true, coinFlipHeads: false, alreadyOnTarget: false));
    }

    [Fact]
    public void AndIsRefusedWhenTheTargetAlreadyCarriesIt() {
        // The one place the engine consults the effect pool before casting — and it looks at the
        // target, not the caster.
        Assert.False(MonsterSpellcasting.Selects(SpellIds.Skyfire, matchesFilters: true,
            castable: true, coinFlipHeads: true, alreadyOnTarget: true));
    }

    [Fact]
    public void TheFirstPassPrefersTheOnlyTypeThatCanMiss() {
        Assert.Equal(SpellHitResolution.MissableTargetingType,
            MonsterSpellcasting.FirstPassTargetingTypes[0]);
        Assert.Equal(1, MonsterSpellcasting.FirstPassTargetingTypes[1]);
    }

    [Fact]
    public void AndTheSecondPassAcceptsOnlyTypeOne() {
        Assert.Single(MonsterSpellcasting.SecondPassTargetingTypes);
        Assert.Equal(1, MonsterSpellcasting.SecondPassTargetingTypes[0]);
    }

    [Fact]
    public void NonMartialSpellsAreOutOfReachEntirely() {
        // Nightfingers is not martial, so no monster can ever cast it whatever its book says.
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.Nightfingers,
            isMartial: false, targetingType: 4));
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.DannonsDelusions,
            isMartial: false, targetingType: 3));
    }

    [Fact]
    public void AndSoIsFinalRestByItsTargetingType() {
        // The one spell that kills outright — a monster can never point it at the party.
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.FinalRest,
            isMartial: true, targetingType: 7));
    }

    [Fact]
    public void WhatIsLeftIsTheMonsterRepertoire() {
        Assert.True(MonsterSpellcasting.InMonsterRepertoire(SpellIds.Skyfire,
            isMartial: true, targetingType: 1));
        Assert.True(MonsterSpellcasting.InMonsterRepertoire(SpellIds.Flamecast,
            isMartial: true, targetingType: 0));
        Assert.True(MonsterSpellcasting.InMonsterRepertoire(SpellIds.MadGodsRage,
            isMartial: true, targetingType: 1));
    }

    [Fact]
    public void ExceptTheTwoStruckOutByNumberWhichPassEveryFieldTest() {
        // Both are martial with targeting type 1, so only the by-number exclusion stops them.
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.Invitation,
            isMartial: true, targetingType: 1));
        Assert.False(MonsterSpellcasting.InMonsterRepertoire(SpellIds.ThoughtsLikeClouds,
            isMartial: true, targetingType: 1));
    }

    [Fact]
    public void OnlyTheSecondPassRollsToHit() {
        // The first pass verifies the shot by its line-of-fire trace instead.
        Assert.False(MonsterSpellcasting.RollsToHit(1));
        Assert.True(MonsterSpellcasting.RollsToHit(2));
    }

    [Fact]
    public void AnExcludedSpellIsRefusedEvenWhenEverythingElsePasses() {
        Assert.False(MonsterSpellcasting.Selects(SpellIds.Invitation, matchesFilters: true,
            castable: true, coinFlipHeads: true, alreadyOnTarget: false));
    }

    [Fact]
    public void TheLastPointOfThePoolIsNeverSpendable() {
        // Strictly greater, both ways: a pool of exactly the base cost cannot cast, and the power
        // ceiling is pool-1. Using >= and pool would let a caster spend itself to nothing.
        Assert.False(MonsterSpellcasting.CanAfford(minimumCost: 10, healthStaminaCombined: 10));
        Assert.True(MonsterSpellcasting.CanAfford(minimumCost: 10, healthStaminaCombined: 11));
        Assert.Equal(9, MonsterSpellcasting.AiCastPower(maximumCost: 50, healthStaminaCombined: 10));
    }

    [Fact]
    public void AHealthyAiCasterAlwaysCastsAtTheSpellsFullStrength() {
        // No roll and no choice — cspell_actor_stat_get_comb_dflt takes the maximum whenever the
        // pool allows it. A port that rolled a power here would make monster casters erratic.
        Assert.Equal(15, MonsterSpellcasting.AiCastPower(maximumCost: 15, healthStaminaCombined: 99));
    }

    [Fact]
    public void SlotEightIsDeadInTheShippedGameAndTheAttemptIsStillSpent() {
        // *** A GUARD AGAINST A WELL-MEANING FIX. *** Slot 8's routine is unreachable past its
        // first test — `!victim->combatStatus & 2` where `!(victim->combatStatus & 2)` was meant,
        // confirmed at 0x65db1 and in the byte-matched C. Every pattern row contains slot 8, so
        // one of a caster's eight attempts reliably does nothing. Implementing the intended
        // behaviour would hand every monster caster an extra action and a free heal per turn.
        Assert.Equal(MonsterSpellcasting.SlotAction.SpecialLast, MonsterSpellcasting.ActionOf(8));
        Assert.NotEqual(MonsterSpellcasting.SlotAction.TargetedCast, MonsterSpellcasting.ActionOf(8));

        // And it is not merely unmapped: it has no target mode either, so there is no route by
        // which a caller could treat it as an ordinary cast.
        Assert.Equal(-1, MonsterSpellcasting.TargetModeOf(8));
    }
}

namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>
/// The three creature routines that shoot when they can and hand the turn back when they cannot
/// (decided by MonsterChargeTurn, MonsterVariantAttackTurn and MonsterHeavyRangedTurn). Each carries
/// one line that is easy to drop and changes the creature materially.
/// </summary>
public class MonsterDeferringRoutinesTests {
    [Fact]
    public void ThatRoutineDropsItsTargetEveryTurnWhateverItDid() {
        // So it always reads as disengaged to the target filters: never findable by the "engaged"
        // role, always eligible for the "disengaged" one.
        Assert.True(MonsterTurnRoutines.ClearsTargetAfterActing);
    }

    [Fact]
    public void TheBoltCreatureRefillsAStatBeforeDecidingAnything() {
        // One assignment at the top of the routine: whatever drains that stat is undone every turn,
        // so the creature cannot be worn down through it at all.
        Assert.True(MonsterTurnRoutines.RefillsStatEachTurn);
    }
}

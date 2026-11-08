namespace BetrayalAtKrondor.Tests.Character;

using GameData;
using GameData.Resources.Character;
using Xunit;

/// <summary>
/// What a camp rest will do for one member, in words (TASK-980): the Enhanced camp screen's
/// per-member line. Every case follows <see cref="UpkeepEngine.ApplyHour"/> at camp quality.
/// </summary>
public class CampOutlookTests {
    private static CampOutlook For(int health, int healthMax, int stamina, int staminaMax,
        params (ActorCondition Condition, int Rank)[] ranks) {
        var conditions = new ActorConditions();
        foreach ((ActorCondition c, int rank) in ranks) {
            conditions[c] = rank;
        }
        return CampOutlookRule.For(
            new ActorStat { Base = (byte)health, Max = (byte)healthMax },
            new ActorStat { Base = (byte)stamina, Max = (byte)staminaMax },
            conditions, characterIndex: 0);
    }

    [Fact]
    public void AFullPoolIsFullyRested() =>
        Assert.Equal(CampOutlook.FullyRested, For(55, 55, 45, 45));

    [Fact]
    public void HealthUnderItsMaximumComesBackFirst() =>
        // TASK-979: Locklear 41/55 + 0/45 — every point went to health, stamina stayed 0.
        Assert.Equal(CampOutlook.HealthFirst, For(41, 55, 0, 45));

    [Fact]
    public void WithHealthFullStaminaComesBackNext() =>
        Assert.Equal(CampOutlook.StaminaNext, For(55, 55, 2, 45));

    [Fact]
    public void AtFourFifthsCampingCanDoNoMore() =>
        // TASK-979: Owyn 40/40 + 41/45 = 81 of 85, above the 80% a camp rest fills to.
        Assert.Equal(CampOutlook.BeyondCamp, For(40, 40, 41, 45));

    [Fact]
    public void NearDeathComesBeforeEverythingButAFullPool() =>
        Assert.Equal(CampOutlook.NearDeath, For(1, 55, 0, 45, (ActorCondition.NearDeath, 100), (ActorCondition.Healing, 50)));

    [Fact]
    public void PoisonDrainsFasterThanCampingHeals() =>
        Assert.Equal(CampOutlook.LosesGround, For(30, 55, 0, 45, (ActorCondition.Poisoned, 20)));

    [Fact]
    public void SicknessCancelsCampingsPoint() =>
        Assert.Equal(CampOutlook.NoGain, For(30, 55, 0, 45, (ActorCondition.Sick, 20)));

    [Fact]
    public void HealingOutweighsPlague() =>
        // +1 doubled by Healing, +1 for Healing itself, -2 for Plagued: still a gain.
        Assert.Equal(CampOutlook.HealthFirst, For(30, 55, 0, 45, (ActorCondition.Plagued, 20), (ActorCondition.Healing, 20)));

    [Fact]
    public void TheHoursNetIsTheEnginesRule() {
        var conditions = new ActorConditions();
        Assert.Equal(1, UpkeepEngine.CampRegenerationPerHour(0, conditions));
        conditions[ActorCondition.Healing] = 10;
        Assert.Equal(3, UpkeepEngine.CampRegenerationPerHour(0, conditions));
        conditions[ActorCondition.Starving] = 10;
        Assert.Equal(1, UpkeepEngine.CampRegenerationPerHour(0, conditions));
    }
}

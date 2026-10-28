using GameData;
using GameData.Resources.Combat;
using Xunit;

namespace BetrayalAtKrondor.Tests.Combat;

public class CapabilityRefusalTests {
    private static readonly int[] Ladder = CombatCapability.ShippedHealthThresholds;

    [Fact]
    public void ShootNamesTheRealCause() {
        int t = CombatCapability.DenyingTerrain;
        Assert.Equal(CombatCapability.ShootRefusal.Terrain, CombatCapability.WhyNotShoot(t, 5, 5, true, 0, 5));
        Assert.Equal(CombatCapability.ShootRefusal.NoCrossbow, CombatCapability.WhyNotShoot(0, 5, 5, false, 0, 5));
        Assert.Equal(CombatCapability.ShootRefusal.NoQuarrels, CombatCapability.WhyNotShoot(0, 5, 0, true, 0, 5));
        Assert.Equal(CombatCapability.ShootRefusal.FoeAdjacent, CombatCapability.WhyNotShoot(0, 1, 5, true, 0, 5));
        Assert.Equal(CombatCapability.ShootRefusal.NoSkill, CombatCapability.WhyNotShoot(0, 5, 5, true, 0, 0));
        Assert.Equal(CombatCapability.ShootRefusal.None, CombatCapability.WhyNotShoot(0, 5, 5, true, 0, 5));
    }

    [Fact]
    public void CastNamesTheRealCause() {
        int t = CombatCapability.DenyingTerrain;
        Assert.Equal(CombatCapability.CastRefusal.Terrain, CombatCapability.WhyNotCast(t, 5, 5, 9, Ladder, false));
        Assert.Equal(CombatCapability.CastRefusal.ChapterGate, CombatCapability.WhyNotCast(0, 5, 5, 9, Ladder, true));
        Assert.Equal(CombatCapability.CastRefusal.NotACaster, CombatCapability.WhyNotCast(0, 5, 0, 9, Ladder, false));
        Assert.Equal(CombatCapability.CastRefusal.NoHealth, CombatCapability.WhyNotCast(0, 5, 5, 0, Ladder, false));
        Assert.Equal(CombatCapability.CastRefusal.FoeAdjacent, CombatCapability.WhyNotCast(0, 1, 5, 9, Ladder, false));
        Assert.Equal(CombatCapability.CastRefusal.None, CombatCapability.WhyNotCast(0, 5, 5, 9, Ladder, false));
    }

    [Fact]
    public void FirstBlockingConditionSkipsHealing() {
        var ranks = new int[7];
        ranks[(int)ActorCondition.Healing] = 3;
        Assert.Null(RestAction.FirstBlockingCondition(ranks));
        ranks[(int)ActorCondition.Drunk] = 1;
        Assert.Equal(ActorCondition.Drunk, RestAction.FirstBlockingCondition(ranks));
    }
}

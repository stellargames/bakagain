namespace GameData.Resources.Character;

using GameData.Resources.Config;

/// <summary>What a camp rest will do for one member next — the Enhanced camp screen's per-member line (TASK-980).</summary>
public enum CampOutlook {
    /// <summary>Health and stamina are both full.</summary>
    FullyRested,

    /// <summary>Near-death caps any heal to a pool of 1-2 until the rank decays, a point a day (STAT.C:206-211).</summary>
    NearDeath,

    /// <summary>The hour's afflictions outweigh the rest: the pool drains while camping.</summary>
    LosesGround,

    /// <summary>The hour's afflictions cancel the rest exactly.</summary>
    NoGain,

    /// <summary>Already at the 80% a camp rest fills to; only an inn fills further.</summary>
    BeyondCamp,

    /// <summary>The pool refills health to its maximum before any stamina (STAT.C:196-232).</summary>
    HealthFirst,

    /// <summary>Health is full, so the rest goes into stamina.</summary>
    StaminaNext,
}

/// <summary>Picks a member's <see cref="CampOutlook"/> from the same rules a camp hour runs.</summary>
public static class CampOutlookRule {
    public static CampOutlook For(ActorStat health, ActorStat stamina, ActorConditions conditions, int characterIndex) {
        int pool = health.Base + stamina.Base;
        int max = health.Max + stamina.Max;
        if (pool >= max) {
            return CampOutlook.FullyRested;
        }
        if (conditions.Has(ActorCondition.NearDeath)) {
            return CampOutlook.NearDeath;
        }
        int net = UpkeepEngine.CampRegenerationPerHour(characterIndex, conditions);
        if (net < 0) {
            return CampOutlook.LosesGround;
        }
        if (net == 0) {
            return CampOutlook.NoGain;
        }
        // "Camp until Healed" stops on the base pool against 80% of max (STAT.C:451-470).
        if (!CampPartyStats.IsWounded(pool, max)) {
            return CampOutlook.BeyondCamp;
        }
        return health.Base < health.Max ? CampOutlook.HealthFirst : CampOutlook.StaminaNext;
    }
}

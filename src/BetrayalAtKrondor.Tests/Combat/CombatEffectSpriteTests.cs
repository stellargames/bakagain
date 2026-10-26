namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

/// <summary>
/// The effect sprite a projectile flies, per caller.
/// </summary>
/// <remarks>
/// These are four magic numbers out of instruction encodings, so the test pins them to the shipped
/// COMBAT.TBL names they were confirmed against rather than restating the switch — an id that
/// silently drifts to a different entry would still look like a plausible integer.
/// </remarks>
public class CombatEffectSpriteTests {
    private const int FlamecastSpellId = 4;
    private const int BaneOfBlackSlayersSpellId = 9;

    [Fact]
    public void Flamecast_flies_its_own_sprite() {
        Assert.Equal(CombatEffectSprite.Flamecast, CombatEffectSprite.ForSpell(FlamecastSpellId));
    }

    [Fact]
    public void BaneOfBlackSlayers_flies_its_own_sprite() {
        Assert.Equal(CombatEffectSprite.BaneOfBlackSlayers,
            CombatEffectSprite.ForSpell(BaneOfBlackSlayersSpellId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(44)]
    public void Every_other_spell_flies_the_generic_sprite(int spellId) {
        Assert.Equal(CombatEffectSprite.GenericSpell, CombatEffectSprite.ForSpell(spellId));
    }

    /// <summary>The four ids are distinct COMBAT.TBL entries, not aliases of one.</summary>
    [Fact]
    public void The_four_effect_sprites_are_four_entries() {
        int[] ids = {
            CombatEffectSprite.Rock, CombatEffectSprite.Flamecast,
            CombatEffectSprite.BaneOfBlackSlayers, CombatEffectSprite.GenericSpell,
        };
        Assert.Equal(ids.Length, new System.Collections.Generic.HashSet<int>(ids).Count);
    }

    /// <summary>combataiturn_ranged_attack (CBTAITRN.C:121-139): the rock for kind 8, creature 0x1a's sling,
    /// else the arrow. The port flew the rock for every quarrel (TASK-1008).</summary>
    [Theory]
    [InlineData(0, 0, CombatEffectSprite.Arrow)]
    [InlineData(3, 12, CombatEffectSprite.Arrow)]
    [InlineData(3, 0x1a, CombatEffectSprite.Sling)]
    [InlineData(8, 12, CombatEffectSprite.Rock)]
    [InlineData(8, 0x1a, CombatEffectSprite.Rock)]
    public void A_shot_flies_the_rock_only_when_it_is_one(int quarrelKind, int creatureType, int expected) {
        Assert.Equal(expected, CombatEffectSprite.ForShot(quarrelKind, creatureType));
    }

    /// <summary>world_render_combat_grid_sprite (WORLDHIT.C:743-760): `ashadow` under the arrow, `flat`
    /// under any other flying shape, nothing under the growing shape 4.</summary>
    [Theory]
    [InlineData(CombatEffectSprite.Arrow, CombatEffectSprite.ArrowShadow)]
    [InlineData(CombatEffectSprite.Rock, CombatEffectSprite.FlatShadow)]
    [InlineData(CombatEffectSprite.Flamecast, CombatEffectSprite.FlatShadow)]
    [InlineData(4, CombatEffectSprite.NoShadow)]
    public void A_flying_shape_casts_its_shadow(int shape, int shadow) {
        Assert.Equal(shadow, CombatEffectSprite.ShadowFor(shape));
    }
}

namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using Xunit;

public class CheatCentralTests {
    [Fact]
    public void TheKnockKnockMenuMapsEveryCase() {
        Assert.Equal(CheatCentral.Effect.AddGold, CheatCentral.KnockKnockEffect(0x81));
        Assert.Equal(CheatCentral.Effect.OpenItemChest, CheatCentral.KnockKnockEffect(0x82));
        Assert.Equal(CheatCentral.Effect.PlayLine, CheatCentral.KnockKnockEffect(0x83));
        Assert.Equal(CheatCentral.Effect.SkipChapter, CheatCentral.KnockKnockEffect(0x84));
        Assert.Equal(CheatCentral.Effect.HealParty, CheatCentral.KnockKnockEffect(0x85));
        Assert.Equal(CheatCentral.Effect.LearnAllSpells, CheatCentral.KnockKnockEffect(0x86));
        Assert.Equal(CheatCentral.Effect.UnlockAllTeleports, CheatCentral.KnockKnockEffect(0x87));
        Assert.Equal(CheatCentral.Effect.Leave, CheatCentral.KnockKnockEffect(1));
    }

    [Fact]
    public void TheChestsChapterSkipNeedsRightShiftAndAltOnly() {
        Assert.Equal(CheatCentral.Effect.SkipChapter, CheatCentral.ChestEffect(0x31, true, true, false, false));
        Assert.Equal(CheatCentral.Effect.None, CheatCentral.ChestEffect(0x31, true, true, true, false));
        Assert.Equal(CheatCentral.Effect.None, CheatCentral.ChestEffect(0x31, true, true, false, true));
        Assert.Equal(CheatCentral.Effect.None, CheatCentral.ChestEffect(0x31, false, true, false, false));
        Assert.Equal(CheatCentral.Effect.HealParty, CheatCentral.ChestEffect(0x81, false, false, false, false));
    }
}

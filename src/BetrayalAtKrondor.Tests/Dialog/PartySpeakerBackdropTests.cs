namespace BetrayalAtKrondor.Tests.Dialog;

using GameData.Resources.Dialog;
using Xunit;

/// <summary>DIALOG.C:978-987 — the in-location backdrop under a party speaker (TASK-810).</summary>
public class PartySpeakerBackdropTests {
    [Theory]
    [InlineData(1, true)]   // Locklear
    [InlineData(6, true)]   // last pool character
    [InlineData(0, false)]  // nobody speaks
    [InlineData(7, false)]  // first NPC
    [InlineData(30, false)]
    public void OnlyThePoolCharactersStandOnIt(int actor, bool expected) {
        Assert.Equal(expected, PartySpeakerBackdrop.Applies(actor));
    }
}

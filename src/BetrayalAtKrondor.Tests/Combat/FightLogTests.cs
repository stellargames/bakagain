namespace BetrayalAtKrondor.Tests.Combat;

using GameData.Resources.Combat;
using Xunit;

public class FightLogTests {
    [Fact]
    public void TheLogKeepsTheNewestLast() {
        var log = new FightLog();
        log.Add("a");
        log.Add("b");
        log.Add("c");
        Assert.Equal(new[] { "b", "c" }, log.Last(2));
        Assert.Equal(new[] { "a", "b", "c" }, log.Last(10));
        log.Round = 3;
        log.Clear();
        Assert.Empty(log.Last(5));
        Assert.Equal(0, log.Round);
    }
}

using GameData.Resources.Config;
using Xunit;

namespace BetrayalAtKrondor.Tests.Data;

// ENCAMP.C:173-195: each frame over a stone, a HELD button latches that stone; the first frame not
// held over the latched stone commits it; a frame off every stone clears the latch.
public class EncampStoneLatchTests {
    private const int None = EncampData.NoEntry;

    [Fact]
    public void ADragFromOneStoneToAnotherSleepsToWhereItIsLetGo() {
        var latch = new EncampStoneLatch();
        Assert.Equal(None, latch.Frame(6, held: true));
        Assert.Equal(None, latch.Frame(8, held: true));
        Assert.Equal(8, latch.Frame(8, held: false));
    }

    [Fact]
    public void APressStartedOffTheDialAndDraggedOnCommits() {
        var latch = new EncampStoneLatch();
        Assert.Equal(None, latch.Frame(None, held: true));
        Assert.Equal(None, latch.Frame(3, held: true));
        Assert.Equal(3, latch.Frame(3, held: false));
    }

    [Fact]
    public void SlidingOffTheDialAbandonsIt() {
        var latch = new EncampStoneLatch();
        latch.Frame(6, held: true);
        Assert.Equal(None, latch.Frame(None, held: true));
        Assert.Equal(None, latch.Frame(None, held: false));
    }

    [Fact]
    public void HoveringWithoutAPressCommitsNothing() {
        var latch = new EncampStoneLatch();
        Assert.Equal(None, latch.Frame(4, held: false));
    }

    [Fact]
    public void ItCommitsOnce() {
        var latch = new EncampStoneLatch();
        latch.Frame(5, held: true);
        Assert.Equal(5, latch.Frame(5, held: false));
        Assert.Equal(None, latch.Frame(5, held: false));
    }
}

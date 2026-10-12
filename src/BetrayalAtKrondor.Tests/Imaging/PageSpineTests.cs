namespace BetrayalAtKrondor.Tests.Imaging;

using GameData.Resources.Layout;
using Xunit;

/// <summary>
/// Which edge of a full-screen parchment is bound into the book: the straight one. The shipped
/// parchments measure ~97% opaque on the straight edge's column and ~15% on the frayed one.
/// </summary>
public class PageSpineTests {
    private static bool[] Column(int rows, int opaque) {
        var column = new bool[rows];
        for (int i = 0; i < opaque; i++) {
            column[i * rows / opaque] = true;
        }
        return column;
    }

    [Fact]
    public void StraightLeftFrayedRight_IsBoundOnTheLeft() =>
        Assert.Equal(SpineSide.Left, PageSpine.Of(Column(1200, 1164), Column(1200, 180)));

    [Fact]
    public void FrayedLeftStraightRight_IsBoundOnTheRight() =>
        Assert.Equal(SpineSide.Right, PageSpine.Of(Column(1200, 180), Column(1200, 1164)));

    [Fact]
    public void BothFrayed_IsNotAPage() =>
        Assert.Equal(SpineSide.NotAPage, PageSpine.Of(Column(1200, 180), Column(1200, 300)));

    [Fact]
    public void BothStraight_IsNotAPage() =>
        Assert.Equal(SpineSide.NotAPage, PageSpine.Of(Column(1200, 1200), Column(1200, 1200)));

    [Fact]
    public void JustUnderTheShare_IsFrayed() =>
        Assert.Equal(SpineSide.Right, PageSpine.Of(Column(100, 89), Column(100, 100)));

    [Fact]
    public void AnEmptyImage_IsNotAPage() =>
        Assert.Equal(SpineSide.NotAPage, PageSpine.Of(new bool[0], new bool[0]));

    // Rows top to bottom; '#' dark, '.' paper.
    private static bool[] Grid(params string[] rows) {
        var dark = new bool[rows.Length * rows[0].Length];
        for (int y = 0; y < rows.Length; y++) {
            for (int x = 0; x < rows[y].Length; x++) {
                dark[y * rows[0].Length + x] = rows[y][x] == '#';
            }
        }
        return dark;
    }

    [Fact]
    public void TornAway_IsTheDarkThatReachesTheEdge_NotTheInkInside() {
        bool[] torn = PageSpine.TornAway(Grid(
            "##.#",
            "#...",
            "..#.",
            "...#"), 4, 4);

        Assert.Equal(Grid(
            "##.#",
            "#...",
            "....",
            "...#"), torn);
    }

    [Fact]
    public void TornAway_FollowsANotchInward() {
        bool[] torn = PageSpine.TornAway(Grid(
            ".#..",
            ".#..",
            ".##.",
            "...."), 4, 4);

        Assert.Equal(Grid(
            ".#..",
            ".#..",
            ".##.",
            "...."), torn);
    }
}

namespace GameData.Resources.Layout;

using System.Collections.Generic;
using System.Linq;

/// <summary>How a full-screen image sits in the open book.</summary>
public enum SpineSide
{
    /// <summary>
    /// Not a bound page: both outer edges straight (a picture) or neither (a panel such as CAST.SCX,
    /// whose lower third is torn-black down both sides). Nothing is drawn around it.
    /// </summary>
    NotAPage,

    /// <summary>Bound on its left edge: the facing page and the binding are to its left.</summary>
    Left,

    /// <summary>Bound on its right edge.</summary>
    Right,
}

/// <summary>
/// Reads which edge of a parchment is bound into the book. The binding edge is cut straight and
/// the others are torn, so the straight one is the edge whose outermost column is (nearly) opaque
/// all the way down.
/// </summary>
public static class PageSpine
{
    /// <summary>The share of an edge column's rows that must be opaque for the edge to count as straight.</summary>
    public const float StraightShare = 0.9f;

    /// <summary>The spine, from the opacity of the image's leftmost and rightmost columns (one entry per row).</summary>
    public static SpineSide Of(bool[] leftColumnOpaque, bool[] rightColumnOpaque) =>
        (IsStraight(leftColumnOpaque), IsStraight(rightColumnOpaque)) switch
        {
            (true, false) => SpineSide.Left,
            (false, true) => SpineSide.Right,
            _ => SpineSide.NotAPage,
        };

    /// <summary>
    /// The torn-away part of a page: the dark pixels joined (4-way) to the image's border. The tears
    /// are drawn in the palette's black, which an opaque image cannot tell from ink, but ink inside
    /// the page never touches the border. Row-major, <paramref name="width"/> x <paramref name="height"/>.
    /// </summary>
    public static bool[] TornAway(bool[] dark, int width, int height)
    {
        var torn = new bool[dark.Length];
        var open = new Stack<int>();
        void Reach(int i)
        {
            if (dark[i] && !torn[i])
            {
                torn[i] = true;
                open.Push(i);
            }
        }
        for (int x = 0; x < width; x++)
        {
            Reach(x);
            Reach((height - 1) * width + x);
        }
        for (int y = 0; y < height; y++)
        {
            Reach(y * width);
            Reach(y * width + width - 1);
        }
        while (open.Count > 0)
        {
            int i = open.Pop();
            int x = i % width;
            if (x > 0) Reach(i - 1);
            if (x < width - 1) Reach(i + 1);
            if (i >= width) Reach(i - width);
            if (i + width < dark.Length) Reach(i + width);
        }
        return torn;
    }

    private static bool IsStraight(bool[] column) =>
        column.Length > 0 && column.Count(opaque => opaque) >= StraightShare * column.Length;
}

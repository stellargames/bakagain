namespace GameData.Resources.Layout;

using System.Linq;

/// <summary>How a full-screen image sits in the open book.</summary>
public enum SpineSide
{
    /// <summary>Both outer edges straight: a picture, not a page. Nothing is drawn around it.</summary>
    NotAPage,

    /// <summary>Bound on its left edge: the facing page and the binding are to its left.</summary>
    Left,

    /// <summary>Bound on its right edge.</summary>
    Right,

    /// <summary>Frayed on both sides: a loose page, with only the table around it.</summary>
    Loose,
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
            (true, true) => SpineSide.NotAPage,
            (true, false) => SpineSide.Left,
            (false, true) => SpineSide.Right,
            _ => SpineSide.Loose,
        };

    private static bool IsStraight(bool[] column) =>
        column.Length > 0 && column.Count(opaque => opaque) >= StraightShare * column.Length;
}

namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

/// <summary>
/// <see cref="ConvexClip"/>: convex subject minus convex hole, used to cut later-painted coplanar
/// overlays out of the faces beneath them so no two surfaces share depth.
/// </summary>
public class ConvexClipTests {
    private static (double X, double Y)[] Rect(double x0, double y0, double x1, double y1) =>
        new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };

    private static double TotalArea(IEnumerable<(double X, double Y)[]> pieces) =>
        pieces.Sum(p => Math.Abs(ConvexClip.SignedArea(p)));

    private static void AssertAllConvexCcw(IEnumerable<(double X, double Y)[]> pieces) {
        foreach (var p in pieces) {
            Assert.True(p.Length >= 3);
            Assert.True(ConvexClip.SignedArea(p) > 0, "pieces come out CCW");
            Assert.True(ConvexClip.IsConvex(p), "every piece is convex");
        }
    }

    [Fact]
    public void AHoleInTheMiddleLeavesTheRingAndExactlyTheRemainingArea() {
        var pieces = ConvexClip.Subtract(Rect(0, 0, 10, 10), Rect(4, 4, 6, 6));
        Assert.Equal(100 - 4, TotalArea(pieces), 6);
        Assert.Equal(4, pieces.Count);
        AssertAllConvexCcw(pieces);
    }

    [Fact]
    public void AHoleAtACorner() {
        var pieces = ConvexClip.Subtract(Rect(0, 0, 10, 10), Rect(-2, -2, 3, 3));
        Assert.Equal(100 - 9, TotalArea(pieces), 6);
        AssertAllConvexCcw(pieces);
    }

    [Fact]
    public void AHoleCrossingAnEdge() {
        var pieces = ConvexClip.Subtract(Rect(0, 0, 10, 10), Rect(4, -5, 6, 5));
        Assert.Equal(100 - 10, TotalArea(pieces), 6);
        AssertAllConvexCcw(pieces);
    }

    [Fact]
    public void AHoleCoveringEverythingLeavesNothing() {
        Assert.Empty(ConvexClip.Subtract(Rect(0, 0, 10, 10), Rect(-1, -1, 11, 11)));
        Assert.Empty(ConvexClip.Subtract(Rect(0, 0, 10, 10), Rect(0, 0, 10, 10)));
    }

    [Fact]
    public void ADisjointHoleLeavesTheSubjectUnchanged() {
        var subject = Rect(0, 0, 10, 10);
        var pieces = ConvexClip.Subtract(subject, Rect(20, 20, 30, 30));
        Assert.Single(pieces);
        Assert.Same(subject, pieces[0]);
    }

    [Fact]
    public void TouchingAlongAnEdgeIsNotOverlap() {
        var subject = Rect(0, 0, 10, 10);
        var hole = Rect(10, 0, 20, 10);
        Assert.False(ConvexClip.Overlaps(subject, hole));
        Assert.False(ConvexClip.Overlaps(subject, Rect(10, 10, 12, 12)), "corner touch");
        var pieces = ConvexClip.Subtract(subject, hole);
        Assert.Single(pieces);
        Assert.Same(subject, pieces[0]);
    }

    [Fact]
    public void OverlapIsDetectedWhicheverWayEitherPolygonWinds() {
        var cw = Rect(4, 4, 6, 6).Reverse().ToArray();
        Assert.True(ConvexClip.Overlaps(Rect(0, 0, 10, 10), cw));
        var pieces = ConvexClip.Subtract(Rect(0, 0, 10, 10).Reverse().ToArray(), cw);
        Assert.Equal(96, TotalArea(pieces), 6);
        AssertAllConvexCcw(pieces);
    }

    [Fact]
    public void ATriangleHoleInAPentagon() {
        var pentagon = Enumerable.Range(0, 5)
            .Select(k => (Math.Cos(k * 2 * Math.PI / 5) * 10, Math.Sin(k * 2 * Math.PI / 5) * 10))
            .ToArray();
        var tri = new[] { (-1.0, -1.0), (1.0, -1.0), (0.0, 1.0) };
        var pieces = ConvexClip.Subtract(pentagon, tri);
        Assert.Equal(Math.Abs(ConvexClip.SignedArea(pentagon)) - 2, TotalArea(pieces), 6);
        AssertAllConvexCcw(pieces);
    }
}

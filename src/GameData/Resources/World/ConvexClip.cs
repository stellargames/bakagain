namespace GameData.Resources.World;

using System;
using System.Collections.Generic;

/// <summary>
/// 2D convex-polygon subtraction, for cutting later-painted coplanar overlays (timbers, windows,
/// doors) out of the faces beneath them when a depth-sorted model is built.
/// </summary>
/// <remarks>
/// The original had no z-buffer and resolved exactly-coplanar faces by painting them in order. A
/// depth buffer cannot: two surfaces at one depth z-fight. Removing the covered part of the earlier
/// face leaves nothing to fight over. Faces here are convex (3-5 vertices), so the subtraction is a
/// sequence of half-plane splits and every piece it returns is convex too.
/// </remarks>
public static class ConvexClip {
    /// <summary>
    /// Distance tolerance, in the caller's units (Unity world units in practice). TBL vertices are
    /// integer BaK units divided by 100, so the coordinate grid is 0.01; a tenth of that separates
    /// "touching" from "overlapping" without ever swallowing a real one-unit overlap, and sits far
    /// above double rounding on values of ~0.1-100.
    /// </summary>
    public const double Epsilon = 1e-3;

    /// <summary>Pieces smaller than this (a 0.1 x 0.01 sliver) are dropped.</summary>
    public const double AreaEpsilon = 1e-5;

    /// <summary>Shoelace signed area: positive when counter-clockwise.</summary>
    public static double SignedArea(IReadOnlyList<(double X, double Y)> p) {
        double a = 0;
        for (int i = 0; i < p.Count; i++) {
            var q = p[i];
            var r = p[(i + 1) % p.Count];
            a += (q.X * r.Y) - (r.X * q.Y);
        }
        return a / 2;
    }

    /// <summary>True when every turn has the same sign (collinear runs allowed).</summary>
    public static bool IsConvex(IReadOnlyList<(double X, double Y)> p) {
        bool pos = false, neg = false;
        for (int i = 0; i < p.Count; i++) {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            var c = p[(i + 2) % p.Count];
            double cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
            if (cross > 1e-12) pos = true;
            else if (cross < -1e-12) neg = true;
        }
        return !(pos && neg);
    }

    /// <summary>
    /// Separating-axis test for two convex polygons. Sharing only an edge or a point is NOT overlap:
    /// the interiors must intersect by more than <see cref="Epsilon"/> along every axis.
    /// </summary>
    public static bool Overlaps(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b) =>
        !HasSeparatingAxis(a, a, b) && !HasSeparatingAxis(b, a, b);

    private static bool HasSeparatingAxis(IReadOnlyList<(double X, double Y)> edges,
        IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b) {
        for (int i = 0; i < edges.Count; i++) {
            var p = edges[i];
            var q = edges[(i + 1) % edges.Count];
            double nx = -(q.Y - p.Y), ny = q.X - p.X;
            double len = Math.Sqrt((nx * nx) + (ny * ny));
            if (len < 1e-12) continue;
            nx /= len; ny /= len;
            var (minA, maxA) = Project(a, nx, ny);
            var (minB, maxB) = Project(b, nx, ny);
            if (maxA - minB <= Epsilon || maxB - minA <= Epsilon) return true;
        }
        return false;
    }

    private static (double Min, double Max) Project(IReadOnlyList<(double X, double Y)> p, double nx, double ny) {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in p) {
            double d = (v.X * nx) + (v.Y * ny);
            if (d < min) min = d;
            if (d > max) max = d;
        }
        return (min, max);
    }

    /// <summary>
    /// <paramref name="subject"/> minus <paramref name="hole"/>, both convex, as convex CCW pieces.
    /// Empty when the hole covers the subject; the subject itself (same instance) when they do not
    /// overlap.
    /// </summary>
    /// <remarks>
    /// For each hole edge, the part of what is left of the subject that lies outside that edge is a
    /// finished piece, and the inside part carries on to the next edge. Whatever survives every edge
    /// is inside the hole and is dropped.
    /// </remarks>
    public static IReadOnlyList<(double X, double Y)[]> Subtract((double X, double Y)[] subject,
        (double X, double Y)[] hole) {
        if (!Overlaps(subject, hole)) return new[] { subject };

        var pieces = new List<(double X, double Y)[]>();
        var rest = Ccw(subject);
        var h = Ccw(hole);
        for (int i = 0; i < h.Length && rest.Length >= 3; i++) {
            var a = h[i];
            var b = h[(i + 1) % h.Length];
            var outside = HalfPlane(rest, a, b, -1);
            if (outside.Length >= 3 && SignedArea(outside) > AreaEpsilon) pieces.Add(outside);
            rest = HalfPlane(rest, a, b, +1);
        }
        return pieces;
    }

    private static (double X, double Y)[] Ccw((double X, double Y)[] p) {
        if (SignedArea(p) >= 0) return p;
        var r = ((double X, double Y)[])p.Clone();
        Array.Reverse(r);
        return r;
    }

    /// <summary>Sutherland-Hodgman against one line: keep the side <paramref name="side"/> (+1 left
    /// of a→b, -1 right), points within <see cref="Epsilon"/> of the line counting as on it.</summary>
    private static (double X, double Y)[] HalfPlane((double X, double Y)[] poly, (double X, double Y) a,
        (double X, double Y) b, int side) {
        double ex = b.X - a.X, ey = b.Y - a.Y;
        double len = Math.Sqrt((ex * ex) + (ey * ey));
        double Dist((double X, double Y) p) {
            double d = side * ((ex * (p.Y - a.Y)) - (ey * (p.X - a.X))) / len;
            return Math.Abs(d) < Epsilon ? 0 : d;
        }

        var result = new List<(double X, double Y)>(poly.Length + 2);
        for (int i = 0; i < poly.Length; i++) {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Length];
            double dp = Dist(p), dq = Dist(q);
            if (dp >= 0) Add(result, p);
            if ((dp > 0 && dq < 0) || (dp < 0 && dq > 0)) {
                double t = dp / (dp - dq);
                Add(result, (p.X + (t * (q.X - p.X)), p.Y + (t * (q.Y - p.Y))));
            }
        }
        if (result.Count > 1 && Near(result[0], result[^1])) result.RemoveAt(result.Count - 1);
        return result.Count >= 3 ? result.ToArray() : Array.Empty<(double X, double Y)>();
    }

    private static void Add(List<(double X, double Y)> list, (double X, double Y) p) {
        if (list.Count == 0 || !Near(list[^1], p)) list.Add(p);
    }

    private static bool Near((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
}

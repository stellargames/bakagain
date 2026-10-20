namespace GameData.Mods;

/// <summary>
/// A version constraint in one of three forms: <c>&gt;=X</c>, <c>^X</c> (same major, at least X) or
/// exact <c>X</c> (mod system spec 4.1). Compares the core version only.
/// </summary>
public sealed class VersionRange {
    private enum Kind { AtLeast, Caret, Exact }

    private readonly Kind _kind;
    private readonly SemVersion _base;

    private VersionRange(Kind kind, SemVersion baseVersion) {
        _kind = kind;
        _base = baseVersion;
    }

    public static bool TryParse(string s, out VersionRange r) {
        r = null!;
        Kind kind = Kind.Exact;
        if (s.StartsWith(">=")) { kind = Kind.AtLeast; s = s.Substring(2); }
        else if (s.StartsWith("^")) { kind = Kind.Caret; s = s.Substring(1); }
        if (!SemVersion.TryParse(s, out SemVersion v)) return false;
        r = new VersionRange(kind, v);
        return true;
    }

    public bool Satisfies(SemVersion v) => _kind switch {
        Kind.AtLeast => v.CompareCore(_base) >= 0,
        Kind.Caret => v.Major == _base.Major && v.CompareCore(_base) >= 0,
        _ => v.CompareCore(_base) == 0,
    };

    public override string ToString() => (_kind switch { Kind.AtLeast => ">=", Kind.Caret => "^", _ => "" }) + _base;
}

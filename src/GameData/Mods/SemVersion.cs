namespace GameData.Mods;

using System;

/// <summary>
/// A <c>major.minor.patch[-prerelease]</c> version (mod system spec 4.1). Build metadata
/// (<c>+...</c>) is rejected; ranges compare the core only, so the prerelease tag never orders.
/// </summary>
public readonly record struct SemVersion(int Major, int Minor, int Patch, string? Prerelease) {
    public static bool TryParse(string s, out SemVersion v) {
        v = default;
        if (string.IsNullOrEmpty(s) || s.Contains('+')) return false;
        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0) {
            pre = s.Substring(dash + 1);
            if (!ValidPrerelease(pre)) return false;
            s = s.Substring(0, dash);
        }
        string[] parts = s.Split('.');
        if (parts.Length != 3 || !TryPart(parts[0], out int ma) || !TryPart(parts[1], out int mi) || !TryPart(parts[2], out int pa)) return false;
        v = new SemVersion(ma, mi, pa, pre);
        return true;
    }

    // SemVer 2.0 section 9: dot-separated, non-empty identifiers of [0-9A-Za-z-]; numeric ones have no leading zero.
    private static bool ValidPrerelease(string pre) {
        foreach (string id in pre.Split('.')) {
            if (id.Length == 0) return false;
            bool numeric = true;
            foreach (char c in id) {
                bool digit = c >= '0' && c <= '9';
                if (!digit && !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && c != '-') return false;
                numeric &= digit;
            }
            if (numeric && id.Length > 1 && id[0] == '0') return false;
        }
        return true;
    }

    // Digits only, no leading zero: int.TryParse would also accept signs and whitespace.
    private static bool TryPart(string p, out int n) {
        n = 0;
        if (p.Length == 0 || (p.Length > 1 && p[0] == '0')) return false;
        foreach (char c in p) if (c < '0' || c > '9') return false;
        return int.TryParse(p, out n);
    }

    /// <summary>Orders by major, minor, patch; ignores the prerelease tag.</summary>
    public int CompareCore(SemVersion other) {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        return c != 0 ? c : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Prerelease == null ? "" : "-" + Prerelease);
}

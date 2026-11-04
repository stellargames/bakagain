namespace GameData.Resources.Combat;

using System.Collections.Generic;
using System.Linq;

/// <summary>The result lines the player saw this fight, oldest first.</summary>
public sealed class FightLog {
    private readonly List<string> _lines = new();

    public int Round { get; set; }

    public void Add(string line) {
        if (!string.IsNullOrWhiteSpace(line)) {
            _lines.Add(line);
        }
    }

    /// <summary>The newest <paramref name="n"/> lines, the newest last.</summary>
    public IReadOnlyList<string> Last(int n) => Last(n, _lines.Count);

    /// <summary>The newest <paramref name="n"/> of the first <paramref name="count"/> lines: the log as it stood
    /// before the lines a screen has not shown yet.</summary>
    public IReadOnlyList<string> Last(int n, int count) {
        count = System.Math.Clamp(count, 0, _lines.Count);
        return _lines.Take(count).Skip(System.Math.Max(0, count - n)).ToList();
    }

    public int Count => _lines.Count;

    public void Clear() {
        _lines.Clear();
        Round = 0;
    }
}

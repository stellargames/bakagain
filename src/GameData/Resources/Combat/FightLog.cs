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
    public IReadOnlyList<string> Last(int n) => _lines.Skip(System.Math.Max(0, _lines.Count - n)).ToList();

    public void Clear() {
        _lines.Clear();
        Round = 0;
    }
}

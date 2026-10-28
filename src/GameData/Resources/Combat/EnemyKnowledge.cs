namespace GameData.Resources.Combat;

using System.Collections.Generic;

/// <summary>
/// What the player has learned about each enemy this fight: the stats an Inspect roll revealed and the
/// damage the floating numbers showed. Per fight, per enemy slot (the enemy's index in the encounter).
/// </summary>
public sealed class EnemyKnowledge {
    private readonly Dictionary<int, Dictionary<ActorAttribute, int>> _stats = new();
    private readonly Dictionary<int, int> _seen = new();

    /// <summary>Remember a revealed stat. A row whose roll failed is simply never recorded.</summary>
    public void Record(int enemySlot, ActorAttribute row, int value) {
        if (!_stats.TryGetValue(enemySlot, out Dictionary<ActorAttribute, int> rows)) {
            _stats[enemySlot] = rows = new Dictionary<ActorAttribute, int>();
        }
        rows[row] = value;
    }

    public bool TryGet(int enemySlot, ActorAttribute row, out int value) {
        value = 0;
        return _stats.TryGetValue(enemySlot, out Dictionary<ActorAttribute, int> rows) && rows.TryGetValue(row, out value);
    }

    /// <summary>Whether any stat of this enemy has been revealed.</summary>
    public bool Inspected(int enemySlot) => _stats.ContainsKey(enemySlot);

    /// <summary>Add damage the player saw. A miss (0) and a heal (negative) add nothing.</summary>
    public void AddSeen(int enemySlot, int dealt) {
        if (dealt > 0) {
            _seen[enemySlot] = Seen(enemySlot) + dealt;
        }
    }

    public int Seen(int enemySlot) => _seen.TryGetValue(enemySlot, out int n) ? n : 0;

    public void Clear() {
        _stats.Clear();
        _seen.Clear();
    }
}

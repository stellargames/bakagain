namespace GameData.Resources.Combat;

using System.Collections.Generic;

/// <summary>
/// What the enhanced combat board marks for the acting party member: the cells they can walk to,
/// the foes a Thrust or a Swing would reach, and each foe's letter (TASK-972).
/// </summary>
/// <remarks>
/// Every set is read from the rules the click itself uses (<c>CellMovable</c>,
/// <see cref="CombatActionDispatch.WithinReach"/>, <see cref="CombatWalk.MeleeApproach"/>,
/// <see cref="MeleeStatsPanel.ShowsSwingColumn"/>) -- this type only carries the answer. Foes are
/// keyed by their index in the encounter's enemy list, which is the roster order, so a letter
/// ('A' + index) never moves to another foe when one dies.
/// </remarks>
public sealed record BoardMarks(
    IReadOnlyCollection<(int Col, int Row)> Reach,
    IReadOnlyCollection<int> ThrustTargets,
    IReadOnlyCollection<int> SwingTargets,
    IReadOnlyDictionary<int, char> FoeLetters) {
    /// <summary>Nothing marked.</summary>
    public static readonly BoardMarks Empty = new(
        new HashSet<(int, int)>(), new HashSet<int>(), new HashSet<int>(), new Dictionary<int, char>());

    /// <summary>The letter of the foe at this roster index: A, B, C...</summary>
    public static char LetterFor(int rosterIndex) => (char)('A' + rosterIndex);
}

namespace GameData.Resources.Combat;

/// <summary>
/// Heights the combat spell visuals fly and draw at, in square-world units: the original's values
/// x1.2 (<see cref="GameData.Resources.World.WorldUp"/>, TASK-764). The original's number is in
/// each comment.
/// </summary>
public static class SpellHeights {
    /// <summary>Chest height a ranged spell flies at: z=200 (WORLDHIT.C:641-645).</summary>
    public const int Flight = 240;

    /// <summary>The crystal chain's beam: z=235.</summary>
    public const int CrystalBeam = 282;

    /// <summary>Where a lightning bolt starts, at the actor's chest: z=250.</summary>
    public const int BoltBase = 300;

    /// <summary>
    /// World units per original screen pixel for the bolt's rises and side steps. ponytail: one
    /// original px taken as 12 units across at arena depth; a pixel is 1.2x taller than wide, so up
    /// is 14.4.
    /// </summary>
    public const float BoltUnitsPerPixelAcross = 12f;
    public const float BoltUnitsPerPixelUp = 14.4f;
}

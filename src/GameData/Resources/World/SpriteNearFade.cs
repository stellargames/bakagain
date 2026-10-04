namespace GameData.Resources.World;

/// <summary>
/// How world sprites near the camera are faded out — the port's softened form of the original's
/// sprite near cull.
/// </summary>
/// <remarks>
/// <b>The original culls; the port fades (owner's decision, 2026-10-04, TASK-746).</b>
/// <c>worldrender_sprite_billboard</c> returns without drawing when the sprite is closer than
/// octagonal 0x5dc = 1500 world units (WORLDRND.C:196-199). Without any cull, a sprite at the camera
/// filled the whole viewport with giant texels (c668, c846n, c13). The port fades a sprite out between
/// <see cref="ShownFrom"/> (the original's threshold, so anything the original draws is drawn whole)
/// and <see cref="HiddenWithin"/>, to avoid the hard pop.
/// </remarks>
public static class SpriteNearFade {
    /// <summary>The original's cull distance: sprites at or beyond it are drawn whole.</summary>
    public const int ShownFrom = 0x5dc;

    /// <summary>Closer than this the sprite is not drawn at all.</summary>
    public const int HiddenWithin = 1000;

    /// <summary>0 (hidden) .. 1 (whole) for a horizontal camera-to-sprite distance in world units.</summary>
    public static double Visibility(double distance) {
        if (distance <= HiddenWithin) {
            return 0;
        }
        if (distance >= ShownFrom) {
            return 1;
        }
        return (distance - HiddenWithin) / (ShownFrom - HiddenWithin);
    }
}

namespace GameData.Resources.World;

using System;

/// <summary>
/// World-up (Z) in the square-pixel world: the original's heights x1.2 (TASK-764).
/// </summary>
/// <remarks>
/// <b>Every world height in GameData is already in these units.</b> The original projects with one
/// focal length in VGA pixels, and VGA pixels are 1.2x taller than wide on the 4:3 screen it was
/// made for — so its models, terrain and camera heights were authored to be seen 1.2x taller than
/// their numbers. The port renders square pixels with an isotropic camera, so the stretch moves into
/// the data: extractors scale heights on the way in, and constants ported from the original's code
/// are written pre-scaled (the original value stays in the comment).
///
/// <para><b>Unity never calls this.</b> It is for extractors, the save reader/writer, and the rare
/// ported formula that mixes axes and must compute in the original's units before converting its
/// result. Mod data is authored square and never passes through it.</para>
/// </remarks>
public static class WorldUp {
    /// <summary>A VGA pixel's height over its width on the original's 4:3 display.</summary>
    public const double Aspect = 6.0 / 5.0;

    /// <summary>The original's height in the square world; clamped rather than wrapped at int's range.</summary>
    public static int FromOriginal(int z) =>
        (int)Math.Clamp(Math.Round(z * Aspect, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    /// <summary>The inverse, for writing a save the original can read.</summary>
    public static int ToOriginal(int z) => (int)Math.Round(z / Aspect, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A camera pitch in the original's signed 16-bit angle units (65536 per turn), turned to look
    /// at the same world point once heights are x1.2: <c>tan</c> scales by <see cref="Aspect"/>.
    /// </summary>
    /// <remarks>
    /// <b>Not exact, and nothing could be.</b> The original stretches its IMAGE 1.2x after
    /// projecting; for a pitched camera that stretch does not commute with the rotation, so no
    /// square camera reproduces it. This is the principled mapping and, simulated over the arena
    /// grid, within 0.03 VGA px rms of the best fit any single angle gives. A level camera (the
    /// walking view, 280) barely moves.
    /// </remarks>
    public static int PitchFromOriginal(int pitch16) {
        double a = (short)pitch16 * (2 * Math.PI / 65536.0);
        double b = Math.Atan2(Aspect * Math.Sin(a), Math.Cos(a));
        return (int)Math.Round(b * (65536.0 / (2 * Math.PI)), MidpointRounding.AwayFromZero);
    }
}

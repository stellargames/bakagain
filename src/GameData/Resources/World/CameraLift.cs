namespace GameData.Resources.World;

using System;
using System.Collections.Generic;

/// <summary>
/// The camera's rise and fall when the Wooden Chest is opened —
/// <c>itemuse_cam_vert_raise_anim(amplitude_z, apex_hold_frames)</c> (ITEMUSE.C), called with
/// (0x1194, 0x23) when the inventory closes on result 0x66 (CMBINV.C:463-466).
/// </summary>
/// <remarks>
/// One height per drawn frame, in world units above where the camera started:
/// <c>amp/2 - cos(angle) * amp/2</c> with <c>angle</c> stepping 0x400 from 0x400 until it wraps to 0,
/// so it rises to the full amplitude at 0x8000 and back down. At the apex the step is withheld while
/// <c>apex_hold_frames--</c> is not yet negative. The original's cosine is a Q14 table
/// (<c>r3d_tbl_cos</c>); a double is within a unit of it. The camera is restored afterwards,
/// including any turn or tilt made while up.
/// </remarks>
public static class CameraLift {
    /// <summary>The Wooden Chest's lift: the original's 0x1194, in square-world units (WorldUp, TASK-764).</summary>
    public const int WoodenChestAmplitude = 5400;

    /// <summary>0x23: extra frames held at the top.</summary>
    public const int WoodenChestApexHold = 0x23;

    /// <summary>Yaw added per frame while an arrow key turns the lifted view (0x140).</summary>
    public const int TurnStep = 0x140;

    /// <summary>Pitch change per frame for up/down (0x40), kept within -0x17C..0x1C2.</summary>
    public const int PitchStep = 0x40;

    /// <summary>
    /// Seconds per step. The original draws one world frame per step, unpaced, so this is MEASURED:
    /// in Spice86 (zone 4, 2026-10-02) the lift took about 11 s for its 99 steps — about 2.7 s up,
    /// 3.9 s held, 4.7 s down (frames near the ground are slower to draw).
    /// </summary>
    public const double MeasuredSecondsPerStep = 0.11;

    public const int PitchMin = -0x17c;
    public const int PitchMax = 0x1c2;

    private const int AngleStep = 0x400;
    private const int Apex = 0x8000;

    public static IEnumerable<int> Heights(int amplitude, int apexHoldFrames) {
        int angle = AngleStep;
        int hold = apexHoldFrames;
        while (angle != 0) {
            double cos = Math.Cos(angle / 65536.0 * 2 * Math.PI);
            yield return (int)Math.Round(amplitude / 2.0 - cos * amplitude / 2.0);
            if (angle != Apex || hold-- < 0) {
                angle = (angle + AngleStep) & 0xFFFF;
            }
        }
    }
}

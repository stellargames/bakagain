namespace BetrayalAtKrondor.Tests.World;

using GameData.Resources.World;
using System.Linq;
using Xunit;

/// <summary>
/// The Wooden Chest's lift — <c>itemuse_cam_vert_raise_anim(0x1194, 0x23)</c> (ITEMUSE.C).
/// </summary>
/// <remarks>
/// <c>angle</c> starts at 0x400 and steps 0x400 a frame until it wraps to 0; the height above the
/// start is <c>amp/2 - cos(angle) * amp/2</c>, so it rises to the full amplitude at 0x8000 and comes
/// back down. At the apex the step is withheld while <c>apex_hold_frames--</c> is not yet negative.
/// </remarks>
public class CameraLiftTests {
    [Fact]
    public void ItRisesToTheFullAmplitudeAndComesBackDown() {
        int[] heights = CameraLift.Heights(CameraLift.WoodenChestAmplitude, CameraLift.WoodenChestApexHold).ToArray();
        Assert.Equal(CameraLift.WoodenChestAmplitude, heights.Max());
        Assert.True(heights[0] > 0 && heights[0] < 100, $"first frame {heights[0]}");
        Assert.True(heights[^1] > 0 && heights[^1] < 100, $"last frame {heights[^1]}");
        Assert.Equal(heights[0], heights[^1]);
    }

    [Fact]
    public void TheApexIsHeldThirtySevenFrames() {
        // hold = 35: the check `hold-- < 0` fails for 35..0 and passes at -1, so the apex frame is
        // drawn 37 times.
        int[] heights = CameraLift.Heights(CameraLift.WoodenChestAmplitude, CameraLift.WoodenChestApexHold).ToArray();
        Assert.Equal(37, heights.Count(h => h == CameraLift.WoodenChestAmplitude));
        Assert.Equal(63 + 36, heights.Length);   // 63 angles, the apex once more per extra hold frame
    }
}

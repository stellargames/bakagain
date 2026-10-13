namespace BetrayalAtKrondor.Tests.Layout;

using GameData.Resources.Layout;
using Xunit;

public class FieldFitTests {
    // InventoryLayout.FullItemsBox, the faithful item area.
    private const float Bx = 65f, By = 66f, Bw = 1470f, Bh = 726f;

    [Theory]
    [InlineData(1600f, 1200f)] // 4:3
    [InlineData(2133f, 1200f)] // 16:9
    [InlineData(2667f, 1200f)] // 20:9
    public void TheBoxFillsTheFieldOnOneAxisAndFitsOnTheOther(float windowWidth, float windowHeight) {
        float fx = 40f, fy = 40f, fw = windowWidth * 0.75f, fh = windowHeight * 0.72f;

        (float s, float x, float y) = FieldFit.Fit(Bx, By, Bw, Bh, fx, fy, fw, fh);

        float left = x + Bx * s, top = y + By * s, right = x + (Bx + Bw) * s, bottom = y + (By + Bh) * s;
        Assert.True(left >= fx - 0.01f && right <= fx + fw + 0.01f, "inside horizontally");
        Assert.True(top >= fy - 0.01f && bottom <= fy + fh + 0.01f, "inside vertically");
        Assert.True(System.Math.Abs(right - left - fw) < 0.01f || System.Math.Abs(bottom - top - fh) < 0.01f,
            "touches the field on one axis");
        Assert.Equal(fx + fw / 2f, (left + right) / 2f, 2);
        Assert.Equal(fy + fh / 2f, (top + bottom) / 2f, 2);
    }

    [Fact]
    public void AnEmptyFieldLeavesTheScaleAlone() {
        (float s, _, _) = FieldFit.Fit(Bx, By, Bw, Bh, 0f, 0f, 0f, 0f);
        Assert.Equal(1f, s);
    }
}

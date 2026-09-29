namespace BetrayalAtKrondor.Tests.Menu;

using GameData.Resources.Layout;
using Xunit;

public class TouchControlsLayoutTests
{
    [Fact]
    public void DefaultsKeepEveryControlInsideTheBar()
    {
        var l = new TouchControlsLayout();
        Assert.InRange(l.PadSize, 0.1f, 1f);
        Assert.InRange(l.ButtonWidth, 0.1f, 1f);
        foreach (float y in new[] { l.ExamineY, l.ThrustY, l.SwingY, l.PadCentreY })
        {
            Assert.InRange(y, 0f, 1f);
        }
        Assert.True(l.SwingY - l.ThrustY >= l.ButtonHeight, "Thrust and Swing must not overlap");
    }
}

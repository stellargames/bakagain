namespace BetrayalAtKrondor.Tests.Dialog;

using GameData.Resources.Dialog;
using GameData.Resources.Layout;
using Xunit;

public class DialogStyleFrameTests {
    [Fact]
    public void TheInventoryRowAtItsOwnColumnDrawsNoFrame() {
        // dialog_frame_draw skips the border and the bevel when the rect's x is 0x0D
        // (DIALOG.C:346-357). Rows 2 and 5 sit there, so a shop's price quote is a flat panel.
        DialogStyle row5 = new DialogStyleTable().Get(5)!;
        Assert.False(row5.DrawsFrame(row5.DefaultArea));
    }

    [Fact]
    public void AResizeAwayFromThatColumnBringsTheFrameBack() {
        DialogStyle row2 = new DialogStyleTable().Get(2)!;
        Assert.True(row2.DrawsFrame(LayoutHint.PxRect(200, 300, 1200, 400)));
    }

    [Fact]
    public void AStyleWithoutTheExceptionAlwaysFrames() {
        var style = new DialogStyle { DefaultArea = LayoutHint.PxRect(65, 66, 100, 100) };
        Assert.True(style.DrawsFrame(style.DefaultArea));
    }
}

using GameData.Resources.Dialog;
using GameData.Resources.Layout;
using Xunit;

namespace BetrayalAtKrondor.Tests.Dialog;

/// <summary>
/// A speakerless message raised by a world click is centred on the clicked object and kept inside
/// x 14..306, y 12..111 (VGA; canonical x5/y6): dialog_animate_open, DIALOG.C:731-760 (TASK-1113 row 2).
/// </summary>
public class DialogWorldAnchorTests {
    private static LayoutHint Box(float w, float h) => new() {
        Left = LayoutLength.Px(100), Top = LayoutLength.Px(100),
        Width = LayoutLength.Px(w), Height = LayoutLength.Px(h),
    };

    [Fact]
    public void TheBoxIsCentredOnTheAnchor() {
        LayoutHint placed = DialogWorldAnchor.Place(Box(400, 180), 800, 400)!;
        Assert.Equal(600f, placed.Left.Value);
        Assert.Equal(310f, placed.Top.Value);
        Assert.Equal(400f, placed.Width.Value);
    }

    [Fact]
    public void ItStaysInsideTheOriginalsMargins() {
        LayoutHint topLeft = DialogWorldAnchor.Place(Box(400, 180), 0, 0)!;
        Assert.Equal(14 * 5f, topLeft.Left.Value);
        Assert.Equal(12 * 6f, topLeft.Top.Value);
        LayoutHint bottomRight = DialogWorldAnchor.Place(Box(400, 180), 1600, 1200)!;
        Assert.Equal(306 * 5f - 400, bottomRight.Left.Value);
        Assert.Equal(111 * 6f - 180, bottomRight.Top.Value);
    }

    [Fact]
    public void APercentageAreaIsLeftWhereItsAuthorPutIt() {
        var authored = new LayoutHint { Left = LayoutLength.Percent(10), Top = LayoutLength.Px(10),
            Width = LayoutLength.Px(100), Height = LayoutLength.Px(100) };
        Assert.Null(DialogWorldAnchor.Place(authored, 800, 400));
    }
}

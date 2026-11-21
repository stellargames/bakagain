using GameData.Resources.Layout;

namespace GameData.Resources.Dialog;

/// <summary>
/// Where a speakerless message raised by a world click sits: centred on the clicked object and kept
/// inside the original's margins (dialog_animate_open, DIALOG.C:731-760; TASK-1113 row 2).
/// </summary>
/// <remarks>
/// The original moves every op-6 rect of the record to <c>anchor - size / 2</c>, then clamps:
/// x &gt;= 14 and y &gt;= 12 first, then x + w &lt;= 306 and y + h &lt;= 111 (VGA), so the right and
/// bottom limits win on a box too big for both. Canonical units are the VGA ones x5 / x6.
/// </remarks>
public static class DialogWorldAnchor {
    public const float MinLeft = 14 * 5f, MaxRight = 306 * 5f, MinTop = 12 * 6f, MaxBottom = 111 * 6f;

    /// <summary>The area moved onto the anchor (canonical px), or null when it is not all px.</summary>
    public static LayoutHint? Place(LayoutHint area, float anchorX, float anchorY) {
        if (area == null || area.Width.Unit != LayoutLengthUnit.Px || area.Height.Unit != LayoutLengthUnit.Px
            || area.Left.Unit != LayoutLengthUnit.Px || area.Top.Unit != LayoutLengthUnit.Px) {
            return null;
        }
        float w = area.Width.Value, h = area.Height.Value;
        float x = anchorX - w / 2f, y = anchorY - h / 2f;
        if (x < MinLeft) x = MinLeft;
        if (y < MinTop) y = MinTop;
        if (x + w > MaxRight) x = MaxRight - w;
        if (y + h > MaxBottom) y = MaxBottom - h;
        LayoutHint placed = area.Clone();
        placed.Left = LayoutLength.Px(x);
        placed.Top = LayoutLength.Px(y);
        return placed;
    }
}

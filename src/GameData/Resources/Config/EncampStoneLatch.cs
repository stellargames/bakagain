namespace GameData.Resources.Config;

/// <summary>
/// The camp dial's stone choice, frame by frame (ENCAMP.C:173-195; UI_Encamp@0x703d0).
/// </summary>
public sealed class EncampStoneLatch {
    private int _latched = EncampData.NoEntry;

    /// <summary>One frame: the stone under the cursor and whether the button is held.</summary>
    /// <returns>The stone to sleep to, on the frame it is let go over the latched stone; else NoEntry.</returns>
    public int Frame(int hoverStone, bool held) {
        if (hoverStone == EncampData.NoEntry) {
            _latched = EncampData.NoEntry;
            return EncampData.NoEntry;
        }
        if (held) {
            _latched = hoverStone;
            return EncampData.NoEntry;
        }
        if (_latched != hoverStone) {
            return EncampData.NoEntry;
        }
        _latched = EncampData.NoEntry;
        return hoverStone;
    }

    /// <summary>The cursor left the dial: ENCAMP.C:192 clears the latch.</summary>
    public void Clear() => _latched = EncampData.NoEntry;
}

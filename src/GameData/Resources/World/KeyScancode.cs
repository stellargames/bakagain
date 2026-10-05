namespace GameData.Resources.World;

/// <summary>
/// The DOS (set 1) scancode of a letter or digit key. A REQ page's action ids ARE scancodes, and
/// <c>menupage_run</c> presses the entry whose id equals the key's scancode (MENUPAGE.C:346-366) —
/// see <see cref="TravelHotkeys"/> for how that reads on the travel HUD.
/// </summary>
public static class KeyScancode {
    private const string TopRow = "1234567890";      // 0x02..0x0b
    private const string QRow = "qwertyuiop";        // 0x10..0x19
    private const string ARow = "asdfghjkl";         // 0x1e..0x26
    private const string ZRow = "zxcvbnm";           // 0x2c..0x32

    /// <summary>The key's scancode, or -1 for a key not on these rows (Space, ',' and '.' too).</summary>
    public static int Of(char key) {
        char c = char.ToLowerInvariant(key);
        int i;
        if ((i = TopRow.IndexOf(c)) >= 0) return 0x02 + i;
        if ((i = QRow.IndexOf(c)) >= 0) return 0x10 + i;
        if ((i = ARow.IndexOf(c)) >= 0) return 0x1e + i;
        if ((i = ZRow.IndexOf(c)) >= 0) return 0x2c + i;
        return c switch { ' ' => 0x39, ',' => 0x33, '.' => 0x34, _ => -1 };
    }
}

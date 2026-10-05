namespace GameData.Resources.Object;

using System.Collections.Generic;

/// <summary>
/// How an object's name is broken across two display lines.
/// </summary>
/// <remarks>
/// <b>The break is authored, not measured.</b> <see cref="ObjectInfo.WordWrap"/> is a byte offset
/// into the name; the original NULs that byte, draws <c>name[0..wrap)</c>, then draws
/// <c>name[wrap+1..]</c> and puts the character back. So the byte at the offset is <i>dropped</i>
/// (it is the space), and a zero offset means the name is one line.
///
/// <para>Shared because two screens split the same names and must agree: the inventory inspect
/// panel (<c>UI_showItem</c> @0x5A778) and the shoot menu's target panel
/// (<see cref="Combat.ShootTargetPanel"/>, <c>combat_arena_draw_tgt_info_panel</c>). <b>Where they
/// differ is which line a ONE-line name is drawn on</b> — inspect puts it on the second line, the
/// shoot panel on the first — and that is the view's business, not this method's.</para>
/// </remarks>
public static class ObjectNameLines {
    /// <summary>The name as one or two lines.</summary>
    /// <remarks>
    /// <b>A translated name (TASK-779).</b> The offset is the English name's, so it only holds
    /// while the byte there is still the authored space; every shipped two-line name has one. A
    /// translator's own <c>\n</c> is the break. Failing both, a name authored as two lines breaks at
    /// the space nearest its middle, and one with no space stays a single line.
    /// </remarks>
    public static IReadOnlyList<string> Split(string? name, int wordWrap) {
        name ??= string.Empty;
        int newline = name.IndexOf('\n');
        if (newline >= 0) {
            return new[] { name.Substring(0, newline), name.Substring(newline + 1) };
        }
        if (wordWrap <= 0 || wordWrap >= name.Length) {
            return new[] { name };
        }
        if (name[wordWrap] != ' ') {
            int middle = name.Length / 2, best = -1;
            for (int i = 0; i < name.Length; i++) {
                if (name[i] == ' ' && (best < 0 || System.Math.Abs(i - middle) < System.Math.Abs(best - middle))) {
                    best = i;
                }
            }
            if (best < 0) {
                return new[] { name };
            }
            wordWrap = best;
        }
        return new[] { name.Substring(0, wordWrap), name.Substring(wordWrap + 1) };
    }

    /// <inheritdoc cref="Split(string,int)"/>
    public static IReadOnlyList<string> Split(ObjectInfo? obj) =>
        Split(obj?.Name, obj?.WordWrap ?? 0);
}

namespace GameData.Resources.Text;

using GameData.Resources.Menu;
using GameData.Resources.World;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A language pack's letter keys (TASK-784): <c>port:hotkey:M = K</c> makes the map open on K, so
/// a key can match its translated label ("Kaart"). A port feature: the German release kept the
/// English keys, because its menus press the entry whose action id is the key's DOS scancode.
/// </summary>
/// <remarks>
/// <b>The map stays a permutation.</b> A letter the pack moves away goes dead (M no longer opens the
/// map once K does), so no key ever presses two things. Two English letters moved onto the same key:
/// the first in the alphabet wins. A msgstr that is not one character is ignored.
/// </remarks>
public static class LetterHotkeys {
    public const string Prefix = "port:hotkey:";

    /// <summary>The pack key for an English letter, e.g. <c>port:hotkey:M</c>.</summary>
    public static string Key(char english) => Prefix + char.ToUpperInvariant(english);

    /// <summary>
    /// The English letter (lower case) the key that typed <paramref name="typed"/> presses, or
    /// <c>'\0'</c> for a letter the pack moved away.
    /// </summary>
    public static char Resolve(LanguagePack pack, char typed) {
        char t = char.ToLowerInvariant(typed);
        for (char e = 'a'; e <= 'z'; e++) {
            if (Moved(pack, e, out char target) && target == t) {
                return e;
            }
        }
        return Moved(pack, t, out char away) && away != t ? '\0' : t;
    }

    /// <summary>
    /// The letters a pack can move: the travel HUD's and every REQ entry whose action id is a
    /// letter's scancode — what the translator's template lists.
    /// </summary>
    public static IEnumerable<char> Used(IEnumerable<UserInterface> pages) {
        var ids = new HashSet<int>(pages.SelectMany(p => p.MenuEntries).Select(e => e.ActionId));
        for (char c = 'A'; c <= 'Z'; c++) {
            if (TravelHotkeys.ActionFor(c) != TravelHotkeys.NoAction || ids.Contains(KeyScancode.Of(c))) {
                yield return c;
            }
        }
    }

    private static bool Moved(LanguagePack pack, char english, out char target) {
        target = english;
        if (english is < 'a' or > 'z' || !pack.TryGet(Key(english), out string text) || text.Length != 1) {
            return false;
        }
        target = char.ToLowerInvariant(text[0]);
        return true;
    }
}

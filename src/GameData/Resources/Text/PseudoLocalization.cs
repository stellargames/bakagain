namespace GameData.Resources.Text;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// The built-in debug language, <c>qps</c> (TASK-780): every string accented, about a third longer
/// and bracketed — the standard pseudo-localization of Windows and Android.
/// </summary>
/// <remarks>
/// <para>What it finds at a glance: text with no brackets was never translatable (a literal in
/// code); a cut-off bracket is a box too small for a longer language; a missing letter is a font
/// that cannot draw it. The accents are ones <see cref="Font.GlyphSynthesis"/> composes, so the
/// pseudo language exercises that too.</para>
///
/// <para><b>What the engine parses is left alone:</b> markup tags (<c>&lt;hi/&gt;</c>,
/// <c>&lt;i&gt;</c>), ICU MessageFormat blocks (<c>{name}</c>, a whole <c>{n, plural, …}</c>),
/// printf conversions (<c>%d</c>, <c>%2$d</c>, <c>%%</c>), the <c>@</c> variables with their
/// digit, and a <c>#Speaker#</c> name. The bracket opens after leading whitespace and a speaker
/// prefix and closes before trailing whitespace, so the layout rules that read those still do.</para>
/// </remarks>
public static class PseudoLocalization {
    public const string Locale = "qps";

    private static readonly Dictionary<char, char> Accent = new() {
        ['a'] = 'à', ['e'] = 'é', ['i'] = 'ï', ['o'] = 'ö', ['u'] = 'ü', ['y'] = 'ý', ['c'] = 'ç',
        ['n'] = 'ñ', ['s'] = 'š', ['z'] = 'ž', ['A'] = 'Å', ['E'] = 'É', ['I'] = 'Î', ['O'] = 'Ø',
        ['U'] = 'Ü', ['C'] = 'Ç', ['N'] = 'Ñ',
    };

    /// <summary>The letters the pseudo language adds — what the fonts are asked to compose.</summary>
    public static IEnumerable<char> Letters => Accent.Values;

    public static string Of(string english) {
        if (string.IsNullOrEmpty(english)) {
            return english;
        }
        // Leading whitespace and a speaker prefix stay in front of the bracket.
        int start = 0;
        while (start < english.Length && char.IsWhiteSpace(english[start])) {
            start++;
        }
        if (start < english.Length && english[start] == '#') {
            int close = english.IndexOf('#', start + 1);
            if (close > start) {
                start = close + 1;
                while (start < english.Length && char.IsWhiteSpace(english[start])) {
                    start++;
                }
            }
        }
        int end = english.Length;
        while (end > start && char.IsWhiteSpace(english[end - 1])) {
            end--;
        }
        if (end <= start) {
            return english;
        }

        var text = new StringBuilder(english.Length * 2);
        text.Append(english, 0, start).Append('[');
        int letters = 0;
        for (int i = start; i < end; i++) {
            int token = TokenLength(english, i, end);
            if (token > 0) {
                text.Append(english, i, token);
                i += token - 1;
                continue;
            }
            char c = english[i];
            if (char.IsLetter(c)) {
                letters++;
            }
            text.Append(Accent.TryGetValue(c, out char accented) ? accented : c);
        }
        // About a third longer: German and Dutch run 30-35% past English.
        text.Append(' ').Append('~', System.Math.Max(1, (letters + 2) / 3)).Append(']');
        text.Append(english, end, english.Length - end);
        return text.ToString();
    }

    /// <summary>The length of an engine token starting at <paramref name="i"/>, or 0.</summary>
    private static int TokenLength(string s, int i, int end) {
        char c = s[i];
        if (c == '<') {
            int close = s.IndexOf('>', i);
            return close > i && close < end ? close - i + 1 : 0;
        }
        if (c == '{') {
            int depth = 0;
            for (int j = i; j < end; j++) {
                depth += s[j] == '{' ? 1 : s[j] == '}' ? -1 : 0;
                if (depth == 0) {
                    return j - i + 1;
                }
            }
            return 0;
        }
        if (c == '%') {
            int j = i + 1;
            if (j < end && s[j] == '%') {
                return 2;
            }
            while (j < end && char.IsDigit(s[j])) {
                j++;
            }
            if (j < end && s[j] == '$') {
                j++;
            }
            while (j < end && (s[j] == 'l' || s[j] == 'F' || s[j] == 'h')) {
                j++;
            }
            return j < end && "dscu".IndexOf(s[j]) >= 0 ? j - i + 1 : 0;
        }
        if (c == '@') {
            return i + 1 < end && char.IsDigit(s[i + 1]) ? 2 : 1;
        }
        return 0;
    }
}

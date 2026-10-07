namespace GameData.Resources.Text;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One language's translations, by <see cref="TextKey"/> — read from a gettext PO file (TASK-773).
/// </summary>
/// <remarks>
/// <b>Applied as each resource loads</b> (<see cref="Apply"/>), through the same
/// <see cref="TextSlots"/> walk the inventory reads with, so every screen sees the translated text and
/// no consumer knows a pack exists. A key with no translation keeps the original English — PO's own
/// rule, where an empty msgstr is "not translated yet" — so a partial pack is a working pack.
/// </remarks>
public sealed class LanguagePack {
    /// <summary>The original language: no translations, nothing applied.</summary>
    public static LanguagePack English { get; } = new LanguagePack("en", new Dictionary<string, string>());

    /// <summary>The built-in debug language (TASK-780): every string pseudo-localized from its own English.</summary>
    public static LanguagePack Pseudo { get; } = new LanguagePack(PseudoLocalization.Locale,
        new Dictionary<string, string>(), PseudoLocalization.Of);

    private readonly IReadOnlyDictionary<string, string> _text;
    private readonly System.Func<string, string>? _transform;

    public LanguagePack(string locale, IReadOnlyDictionary<string, string> text)
        : this(locale, text, (System.Func<string, string>?)null) {
    }

    /// <summary>A pack with capitals of its own for book paragraphs (TASK-827).</summary>
    public LanguagePack(string locale, IReadOnlyDictionary<string, string> text, IReadOnlyDictionary<char, int> capitals)
        : this(locale, text, (System.Func<string, string>?)null) {
        Capitals = capitals;
    }

    /// <summary>
    /// The pack's own illuminated capitals: a letter BOOK.BMX lacks, and the picture number the pack
    /// ships for it (its PO header's <c>X-Drop-Caps</c>, e.g. <c>E=19</c>).
    /// </summary>
    public IReadOnlyDictionary<char, int> Capitals { get; } = new Dictionary<char, int>();

    private LanguagePack(string locale, IReadOnlyDictionary<string, string> text, System.Func<string, string>? transform) {
        Locale = locale;
        _text = text;
        _transform = transform;
    }

    /// <summary>The pack's locale, e.g. <c>nl</c> — the PO header's <c>Language</c>.</summary>
    public string Locale { get; }

    /// <summary>How many keys carry a translation.</summary>
    public int TranslatedCount => _text.Count(e => !string.IsNullOrEmpty(e.Value));

    /// <summary>Every character the translations use, as code points — what the fonts must draw.</summary>
    public IEnumerable<int> Characters() {
        var seen = new HashSet<int>();
        if (_transform != null) {
            foreach (char c in PseudoLocalization.Letters) {
                yield return c;
            }
        }
        foreach (string text in _text.Values) {
            for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) {
                int c = char.ConvertToUtf32(text, i);
                if (seen.Add(c)) {
                    yield return c;
                }
            }
        }
    }

    /// <summary>The translation of <paramref name="key"/>, if the pack has one.</summary>
    public bool TryGet(string key, out string text) {
        if (_text.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value)) {
            text = value;
            return true;
        }
        text = string.Empty;
        return false;
    }

    /// <summary>The pack's translated entries whose key starts with <paramref name="prefix"/>.</summary>
    public IEnumerable<KeyValuePair<string, string>> WithPrefix(string prefix) =>
        _text.Where(e => e.Key.StartsWith(prefix, System.StringComparison.Ordinal) && !string.IsNullOrEmpty(e.Value));

    /// <summary>The translation of a string whose key is <paramref name="key"/> and whose English is
    /// <paramref name="english"/> — by key, or for the pseudo language from the English itself.</summary>
    public bool TryTranslate(string key, string english, out string text) {
        if (TryGet(key, out text)) {
            return true;
        }
        if (_transform != null && !string.IsNullOrEmpty(english)) {
            text = _transform(english);
            return true;
        }
        return false;
    }

    /// <summary>Replaces every string of <paramref name="resource"/> the pack translates.</summary>
    /// <returns>How many it replaced.</returns>
    public int Apply(IResource resource, string resourceId) {
        if (_text.Count == 0 && _transform == null) {
            return 0;
        }
        var replaced = 0;
        foreach (TextSlot slot in TextSlots.Of(resource, resourceId, c => Capitals.TryGetValue(c, out int picture) ? picture : null)) {
            if (TryTranslate(slot.Key, slot.Text, out string text)) {
                slot.Text = text;
                replaced++;
            }
        }
        return replaced;
    }
}

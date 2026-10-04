namespace GameData.Resources.Text;

using GameData.Resources.Content;
using System.IO;

/// <summary>
/// The stable key of every player-visible string — what a translation is filed under (the PO
/// <c>msgctxt</c>), TASK-772.
/// </summary>
/// <remarks>
/// <b>Derived, not stored.</b> Strings that already carry a key keep it (DDX entries'
/// <c>base:ddx:…</c>, <c>base:uistring:…</c>). The rest are keyed by their position in the original
/// resource — a list index, an element index, a page and paragraph — which is fixed by the shipped
/// file, so the same key comes out on every machine and every re-extraction. One function per
/// domain, used by the text lookup and the POT generator alike, so the two cannot disagree.
///
/// <para><b>One text per owner uses the owner's key; several texts get a field suffix</b>
/// (<c>base:spell:3:name</c>, <c>base:spell:3:cost</c>), so no two strings share a key.</para>
///
/// <para>A book's unit is the PARAGRAPH: its segments are style runs (an italic word), and
/// translating run by run would cut sentences apart.</para>
/// </remarks>
public static class TextKey {
    public enum SpellDocField { Name, Cost, Damage, Duration, LineOfSight, Effect, EffectLine2 }

    public static string BookParagraph(string book, int page, int paragraph) =>
        $"{ContentKey.BaseNamespace}:bok:{Stem(book)}:{page}.{paragraph}";

    public static string UiLabel(string req, int element) => ContentKey.ForBase($"req:{Stem(req)}", element);

    public static string UiLabelAlt(string req, int element) => UiLabel(req, element) + ":alt";

    public static string InputFieldLabel(string inFile, int field) => ContentKey.ForBase($"in:{Stem(inFile)}", field);

    public static string MenuLabel(string lbl, int label) => ContentKey.ForBase($"lbl:{Stem(lbl)}", label);

    public static string Keyword(int index) => ContentKey.ForBase("keyword", index);

    public static string TownName(int index) => ContentKey.ForBase("fmap", index);

    public static string CreditsTitle => $"{ContentKey.BaseNamespace}:cred:title";

    public static string CreditRole(int line) => ContentKey.ForBase("cred", line) + ":role";

    public static string CreditName(int line) => ContentKey.ForBase("cred", line) + ":name";

    public static string ItemName(int objectId) => ContentKey.ForBase("objinfo", objectId) + ":name";

    public static string SpellName(int spell) => ContentKey.ForBase("spell", spell) + ":name";

    public static string SpellDoc(string spellKey, SpellDocField field) =>
        $"{spellKey}:{FieldName(field)}";

    public static string MonsterName(int creature) => ContentKey.ForBase("mnames", creature);

    private static string FieldName(SpellDocField field) => field switch {
        SpellDocField.Name => "doc_name",
        SpellDocField.Cost => "cost",
        SpellDocField.Damage => "damage",
        SpellDocField.Duration => "duration",
        SpellDocField.LineOfSight => "line_of_sight",
        SpellDocField.Effect => "effect",
        _ => "effect2",
    };

    /// <summary>The resource's name without extension, upper case: the key does not depend on how
    /// the caller spelled the file.</summary>
    private static string Stem(string resource) =>
        Path.GetFileNameWithoutExtension(resource ?? string.Empty).ToUpperInvariant();
}

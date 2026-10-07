namespace GameData.Resources.Dialog;

using GameData.Resources.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// The German release's dialog codes for a creature slot (TASK-826): <c>@d</c>/<c>@D</c> the definite
/// article, <c>@i</c>/<c>@I</c> the indefinite one (upper case starts a sentence), and a case and
/// number on the slot itself — <c>@1ns</c> nominative singular, <c>@1as</c> accusative, <c>@1ds</c>
/// dative, <c>@1np</c> plural. "Wir sehen @d @1as" reads "Wir sehen den Kobold".
/// </summary>
/// <remarks>
/// <para>The German EXE built these in code we have not read; this is our own table, so the rules are
/// the standard German articles and the forms are pack data a translator can correct. Per noun the
/// pack carries, as ordinary PO entries (<see cref="Key"/>):</para>
/// <list type="bullet">
/// <item><c>gender</c> — <c>m</c>, <c>f</c> or <c>n</c>; <c>pl</c> for a noun that is plural itself
/// ("Stiefel": die/die/den); <c>name</c> for a person or a possessive name ("Gorath",
/// "Annas Buch"), which never takes an article. A slot holding a party member is a name too.</item>
/// <item><c>def</c> — the nominative after der/die/das, where an adjective changes: the name itself
/// ("Schwarzer Würger") is the form after "ein" and without an article; "der Schwarze Würger".</item>
/// <item><c>acc</c>, <c>dat</c> — accusative and dative ("den Schurken", "dem Schurken"). Only a
/// masculine's accusative differs from its nominative, so any other follows the nominative's rule.</item>
/// <item><c>pl</c> — the plural, as after "die".</item>
/// </list>
/// <para><b>Fallbacks.</b> A form the pack leaves empty is the name, which is right for most nouns
/// (case changes few German nouns' own spelling). A noun with no gender takes the masculine: most of
/// the game's creatures are masculine (Kobold, Troll, Oger, Riese, Schurke), and an article that
/// disagrees reads better than none in sentences built around it.</para>
/// </remarks>
public static class GermanCaseCodes {
    public const string Prefix = "port:grammar:";

    /// <summary>The fields a noun carries, in the order the template lists them.</summary>
    public static readonly string[] Fields = { "gender", "def", "acc", "dat", "pl" };

    /// <summary>The noun a creature slot holds, e.g. <c>mnames:53</c>.</summary>
    public static string CreatureNoun(int creatureId) => "mnames:" + creatureId;

    /// <summary>The noun an item slot holds, e.g. <c>objinfo:24</c>.</summary>
    public static string ObjectNoun(int objectId) => "objinfo:" + objectId;

    /// <summary>The PO key of one field, e.g. <c>port:grammar:mnames:53:gender</c>.</summary>
    public static string Key(string noun, string field) => Prefix + noun + ":" + field;

    // [article] slot case number — "@d @1as", "@1np".
    private static readonly Regex Code = new Regex(@"(?:@([dDiI]) )?@(\d)([nad])([sp])");

    // Rows nominative, accusative, dative; columns m, f, n, plural.
    private static readonly string[,] Definite = {
        { "der", "die", "das", "die" }, { "den", "die", "das", "die" }, { "dem", "der", "dem", "den" },
    };
    private static readonly string[,] Indefinite = {
        { "ein", "eine", "ein", "" }, { "einen", "eine", "ein", "" }, { "einem", "einer", "einem", "" },
    };

    /// <summary>Replace every coded slot in <paramref name="text"/> by its article and form; a plain
    /// <c>@N</c> is left for the resolver.</summary>
    public static string Apply(string text, IReadOnlyList<string>? slots, IReadOnlyList<string>? nouns,
        IReadOnlyList<int>? kinds = null) =>
        Code.Replace(text, m => {
            int n = m.Groups[2].Value[0] - '0';
            string name = slots != null && n < slots.Count ? slots[n] ?? "" : "";
            string noun = nouns != null && n < nouns.Count ? nouns[n] ?? "" : "";
            int @case = "nad".IndexOf(m.Groups[3].Value[0]);
            bool plural = m.Groups[4].Value == "p";
            char article = m.Groups[1].Success ? m.Groups[1].Value[0] : '\0';
            bool definite = article is 'd' or 'D';

            int kind = kinds != null && n < kinds.Count ? kinds[n] : DialogSlotTable.NoActor;
            bool partyMember = kind != DialogSlotTable.NoActor && kind != DialogSlotTable.CreatureActor;
            string g = partyMember ? "name" : Get(noun, "gender");
            int gender = plural ? 3 : g switch { "f" => 1, "n" => 2, "pl" => 3, _ => 0 };
            string form = plural ? Or(Get(noun, "pl"), name)
                : (@case == 0 || (@case == 1 && gender != 0)) ? (definite ? Or(Get(noun, "def"), name) : name)
                : Or(Get(noun, @case == 1 ? "acc" : "dat"), name);

            string word = article == '\0' || g == "name" ? "" : (definite ? Definite : Indefinite)[@case, gender];
            if (word.Length == 0) {
                return form;
            }
            if (char.IsUpper(article)) {
                word = char.ToUpperInvariant(word[0]) + word.Substring(1);
            }
            return word + " " + form;
        });

    private static string Get(string noun, string field) =>
        noun.Length > 0 && UiStrings.Catalog.TryGet(Key(noun, field), out string v) ? v : "";

    private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;
}

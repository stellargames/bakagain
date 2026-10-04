namespace ResourceExtraction.Text;

using GameData.Resources;
using GameData.Resources.Book;
using GameData.Resources.Creature;
using GameData.Resources.Credits;
using GameData.Resources.Data;
using GameData.Resources.Dialog;
using GameData.Resources.Label;
using GameData.Resources.Location;
using GameData.Resources.Menu;
using GameData.Resources.Object;
using GameData.Resources.Spells;
using GameData.Resources.Text;
using System.Collections.Generic;
using System.Linq;

/// <summary>One player-visible string from the original data, with its stable key.</summary>
/// <param name="Key">The <see cref="TextKey"/> — a translation's PO msgctxt.</param>
/// <param name="Text">The original English.</param>
/// <param name="Source">The resource it came from, for a translator's context.</param>
public sealed record TextEntry(string Key, string Text, string Source);

/// <summary>
/// Every player-visible string in the player's game data, keyed (TASK-772) — what a POT is
/// generated from and what the key tests check for uniqueness.
/// </summary>
/// <remarks>
/// Reads through <see cref="IResourceProvider"/> the way the game does, so it works on the shipped
/// archive. Strings baked into KRONDOR.EXE are already keyed in the UI string catalog and are not
/// repeated here; book text is joined per paragraph (its segments are style runs).
/// </remarks>
public static class TextInventory {
    /// <summary>The resources that hold player-visible text, by id.</summary>
    public static IEnumerable<(string Id, IResource Resource)> TextResources(IResourceProvider provider) {
        foreach (string ddx in WithExtension(provider, ".DDX")) {
            if (Load<Dialog>(provider, ddx) is { } dialog) yield return (ddx, dialog);
        }
        foreach (string bok in WithExtension(provider, ".BOK")) {
            if (Load<BookResource>(provider, bok) is { } book) yield return (bok, book);
        }
        foreach (string req in Matching(provider, "REQ_")) {
            if (Load<UserInterface>(provider, req) is { } ui) yield return (req, ui);
        }
        foreach (string inFile in Matching(provider, "IN_")) {
            if (Load<InputForm>(provider, inFile) is { } form) yield return (inFile, form);
        }
        foreach (string lbl in Matching(provider, "LBL_")) {
            if (Load<LabelSet>(provider, lbl) is { } labels) yield return (lbl, labels);
        }
        if (Load<KeywordList>(provider, "KEYWORD.DAT") is { } keywords) yield return ("KEYWORD.DAT", keywords);
        if (Load<FullMapTowns>(provider, "FMAP_TWN.DAT") is { } towns) yield return ("FMAP_TWN.DAT", towns);
        if (Load<CreditsData>(provider, "CRED.DAT") is { } credits) yield return ("CRED.DAT", credits);
        if (Load<ObjectInfoSet>(provider, "OBJINFO.DAT") is { } items) yield return ("OBJINFO.DAT", items);
        if (Load<SpellList>(provider, "SPELLS.DAT") is { } spells) yield return ("SPELLS.DAT", spells);
        if (Load<SpellDescriptions>(provider, "SPELLDOC.DAT") is { } docs) yield return ("SPELLDOC.DAT", docs);
        if (Load<CreatureNames>(provider, "MNAMES.DAT") is { } creatures) yield return ("MNAMES.DAT", creatures);
    }

    /// <summary>Every string, through the same walk a language pack writes with (<see cref="TextSlots"/>).</summary>
    public static IEnumerable<TextEntry> Enumerate(IResourceProvider provider) =>
        TextResources(provider).SelectMany(r =>
            TextSlots.Of(r.Resource, r.Id).Select(slot => new TextEntry(slot.Key, slot.Text, r.Id)));

    private static IEnumerable<string> WithExtension(IResourceProvider provider, string extension) =>
        provider.GetDictionary().Keys
            .Where(n => n.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> Matching(IResourceProvider provider, string prefix) =>
        provider.GetDictionary().Keys
            .Where(n => n.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                        && n.EndsWith(".DAT", System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase);

    private static T? Load<T>(IResourceProvider provider, string name) where T : class, IResource =>
        provider.CanProvideResource(name) ? provider.GetResource<T>(name) : null;
}

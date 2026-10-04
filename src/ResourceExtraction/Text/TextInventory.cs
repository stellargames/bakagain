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
using System.Text;

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
    public static IEnumerable<TextEntry> Enumerate(IResourceProvider provider) {
        foreach (string ddx in WithExtension(provider, ".DDX")) {
            Dialog? dialog = Load<Dialog>(provider, ddx);
            foreach (DialogEntry entry in dialog?.Entries ?? Enumerable.Empty<DialogEntry>()) {
                if (!string.IsNullOrEmpty(entry.Text)) {
                    yield return new TextEntry(entry.Key, entry.Text!, ddx);
                }
            }
        }

        foreach (string bok in WithExtension(provider, ".BOK")) {
            BookResource? book = Load<BookResource>(provider, bok);
            for (int p = 0; p < (book?.Pages.Count ?? 0); p++) {
                for (int q = 0; q < book!.Pages[p].Paragraphs.Count; q++) {
                    var text = new StringBuilder();
                    foreach (TextSegment segment in book.Pages[p].Paragraphs[q].TextSegments) {
                        text.Append(segment.Text);
                    }
                    if (text.Length > 0) {
                        yield return new TextEntry(TextKey.BookParagraph(bok, p, q), text.ToString(), bok);
                    }
                }
            }
        }

        foreach (string req in Matching(provider, "REQ_")) {
            UiElement[] elements = Load<UserInterface>(provider, req)?.MenuEntries ?? [];
            for (int i = 0; i < elements.Length; i++) {
                if (!string.IsNullOrEmpty(elements[i].Label)) {
                    yield return new TextEntry(TextKey.UiLabel(req, i), elements[i].Label!, req);
                }
                if (!string.IsNullOrEmpty(elements[i].LabelAlt)) {
                    yield return new TextEntry(TextKey.UiLabelAlt(req, i), elements[i].LabelAlt!, req);
                }
            }
        }

        foreach (string inFile in Matching(provider, "IN_")) {
            List<InputField> fields = Load<InputForm>(provider, inFile)?.Fields ?? [];
            for (int i = 0; i < fields.Count; i++) {
                if (!string.IsNullOrEmpty(fields[i].Label)) {
                    yield return new TextEntry(TextKey.InputFieldLabel(inFile, i), fields[i].Label, inFile);
                }
            }
        }

        foreach (string lbl in Matching(provider, "LBL_")) {
            List<Label> labels = Load<LabelSet>(provider, lbl)?.Labels ?? [];
            for (int i = 0; i < labels.Count; i++) {
                if (!string.IsNullOrEmpty(labels[i].Text)) {
                    yield return new TextEntry(TextKey.MenuLabel(lbl, i), labels[i].Text!, lbl);
                }
            }
        }

        foreach (KeyValuePair<int, string> keyword in Load<KeywordList>(provider, "KEYWORD.DAT")?.Keywords
                     ?? new Dictionary<int, string>()) {
            if (!string.IsNullOrEmpty(keyword.Value)) {
                yield return new TextEntry(TextKey.Keyword(keyword.Key), keyword.Value, "KEYWORD.DAT");
            }
        }

        List<FullMapTown> towns = Load<FullMapTowns>(provider, "FMAP_TWN.DAT")?.Towns ?? [];
        for (int i = 0; i < towns.Count; i++) {
            if (!string.IsNullOrEmpty(towns[i].Name)) {
                yield return new TextEntry(TextKey.TownName(i), towns[i].Name, "FMAP_TWN.DAT");
            }
        }

        CreditsData? credits = Load<CreditsData>(provider, "CRED.DAT");
        if (credits != null) {
            if (!string.IsNullOrEmpty(credits.Title)) {
                yield return new TextEntry(TextKey.CreditsTitle, credits.Title, "CRED.DAT");
            }
            for (int i = 0; i < credits.Lines.Count; i++) {
                if (!string.IsNullOrEmpty(credits.Lines[i].Role)) {
                    yield return new TextEntry(TextKey.CreditRole(i), credits.Lines[i].Role, "CRED.DAT");
                }
                if (!string.IsNullOrEmpty(credits.Lines[i].Name)) {
                    yield return new TextEntry(TextKey.CreditName(i), credits.Lines[i].Name, "CRED.DAT");
                }
            }
        }

        IReadOnlyList<ObjectInfo> items = Load<ObjectInfoSet>(provider, "OBJINFO.DAT")?.Items ?? [];
        for (int i = 0; i < items.Count; i++) {
            if (!string.IsNullOrEmpty(items[i].Name)) {
                yield return new TextEntry(TextKey.ItemName(i), items[i].Name!, "OBJINFO.DAT");
            }
        }

        foreach (KeyValuePair<int, Spell> spell in Load<SpellList>(provider, "SPELLS.DAT")?.Spells
                     ?? new Dictionary<int, Spell>()) {
            if (!string.IsNullOrEmpty(spell.Value.Name)) {
                yield return new TextEntry(TextKey.SpellName(spell.Key), spell.Value.Name!, "SPELLS.DAT");
            }
        }

        foreach (SpellDescription doc in Load<SpellDescriptions>(provider, "SPELLDOC.DAT")?.Spells ?? []) {
            foreach ((TextKey.SpellDocField field, string text) in new[] {
                         (TextKey.SpellDocField.Name, doc.Name), (TextKey.SpellDocField.Cost, doc.Cost),
                         (TextKey.SpellDocField.Damage, doc.Damage), (TextKey.SpellDocField.Duration, doc.Duration),
                         (TextKey.SpellDocField.LineOfSight, doc.LineOfSight), (TextKey.SpellDocField.Effect, doc.Effect),
                         (TextKey.SpellDocField.EffectLine2, doc.EffectLine2) }) {
                if (!string.IsNullOrEmpty(text)) {
                    yield return new TextEntry(TextKey.SpellDoc(doc.SpellKey, field), text, "SPELLDOC.DAT");
                }
            }
        }

        foreach (CreatureName creature in Load<CreatureNames>(provider, "MNAMES.DAT")?.Creatures ?? []) {
            if (!string.IsNullOrEmpty(creature.Name)) {
                yield return new TextEntry(TextKey.MonsterName(creature.Number), creature.Name, "MNAMES.DAT");
            }
        }
    }

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

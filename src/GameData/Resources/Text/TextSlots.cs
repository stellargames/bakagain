namespace GameData.Resources.Text;

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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>One player-visible string inside a loaded resource: its key, and how to read and replace it.</summary>
public sealed class TextSlot {
    private readonly Func<string> _get;
    private readonly Action<string> _set;

    public TextSlot(string key, Func<string> get, Action<string> set) {
        Key = key;
        _get = get;
        _set = set;
    }

    /// <summary>The <see cref="TextKey"/>.</summary>
    public string Key { get; }

    public string Text {
        get => _get();
        set => _set(value);
    }
}

/// <summary>
/// The strings a loaded resource holds, keyed (TASK-772/773) — the ONE walk both the inventory (read)
/// and a language pack (write) go through, so the two cannot disagree about a key.
/// </summary>
/// <remarks>
/// A translation is applied to a resource as it loads, so every screen that reads it already sees
/// the translated text and no consumer has to know a language pack exists. Empty strings have no
/// slot: there is nothing to translate.
/// </remarks>
public static class TextSlots {
    /// <summary>The slots of <paramref name="resource"/>, loaded from <paramref name="resourceId"/>.</summary>
    public static IEnumerable<TextSlot> Of(IResource resource, string resourceId) => resource switch {
        GameData.Resources.Dialog.Dialog dialog => OfDialog(dialog),
        BookResource book => OfBook(book, resourceId),
        UserInterface ui => OfUserInterface(ui, resourceId),
        InputForm form => OfInputForm(form, resourceId),
        LabelSet labels => OfLabels(labels, resourceId),
        KeywordList keywords => OfKeywords(keywords),
        FullMapTowns towns => OfTowns(towns),
        CreditsData credits => OfCredits(credits),
        ObjectInfoSet items => OfItems(items),
        SpellList spells => OfSpells(spells),
        SpellDescriptions docs => OfSpellDocs(docs),
        CreatureNames creatures => OfCreatures(creatures),
        _ => Enumerable.Empty<TextSlot>(),
    };

    private static IEnumerable<TextSlot> OfDialog(GameData.Resources.Dialog.Dialog dialog) {
        foreach (DialogEntry entry in dialog.Entries) {
            if (!string.IsNullOrEmpty(entry.Text)) {
                yield return new TextSlot(entry.Key, () => entry.Text!, t => entry.Text = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfBook(BookResource book, string resourceId) {
        for (int p = 0; p < book.Pages.Count; p++) {
            for (int q = 0; q < book.Pages[p].Paragraphs.Count; q++) {
                Paragraph paragraph = book.Pages[p].Paragraphs[q];
                if (paragraph.TextSegments.Any(s => !string.IsNullOrEmpty(s.Text))) {
                    // ponytail: a translation replaces the paragraph as ONE run in the first segment's
                    // style; the italic runs come back with explicit markup (TASK-774).
                    yield return new TextSlot(TextKey.BookParagraph(resourceId, p, q),
                        () => Join(paragraph.TextSegments),
                        t => {
                            paragraph.TextSegments[0].Text = t;
                            for (int i = 1; i < paragraph.TextSegments.Count; i++) {
                                paragraph.TextSegments[i].Text = string.Empty;
                            }
                        });
                }
            }
        }
    }

    private static IEnumerable<TextSlot> OfUserInterface(UserInterface ui, string resourceId) {
        for (int i = 0; i < ui.MenuEntries.Length; i++) {
            UiElement element = ui.MenuEntries[i];
            if (!string.IsNullOrEmpty(element.Label)) {
                yield return new TextSlot(TextKey.UiLabel(resourceId, i), () => element.Label!, t => element.Label = t);
            }
            if (!string.IsNullOrEmpty(element.LabelAlt)) {
                yield return new TextSlot(TextKey.UiLabelAlt(resourceId, i), () => element.LabelAlt!, t => element.LabelAlt = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfInputForm(InputForm form, string resourceId) {
        for (int i = 0; i < form.Fields.Count; i++) {
            InputField field = form.Fields[i];
            if (!string.IsNullOrEmpty(field.Label)) {
                yield return new TextSlot(TextKey.InputFieldLabel(resourceId, i), () => field.Label, t => field.Label = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfLabels(LabelSet labels, string resourceId) {
        for (int i = 0; i < labels.Labels.Count; i++) {
            GameData.Resources.Label.Label label = labels.Labels[i];
            if (!string.IsNullOrEmpty(label.Text)) {
                yield return new TextSlot(TextKey.MenuLabel(resourceId, i), () => label.Text!, t => label.Text = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfKeywords(KeywordList keywords) {
        foreach (int index in keywords.Keywords.Keys.OrderBy(k => k).ToList()) {
            if (!string.IsNullOrEmpty(keywords.Keywords[index])) {
                yield return new TextSlot(TextKey.Keyword(index), () => keywords.Keywords[index], t => keywords.Keywords[index] = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfTowns(FullMapTowns towns) {
        for (int i = 0; i < towns.Towns.Count; i++) {
            FullMapTown town = towns.Towns[i];
            if (!string.IsNullOrEmpty(town.Name)) {
                yield return new TextSlot(TextKey.TownName(i), () => town.Name, t => town.Name = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfCredits(CreditsData credits) {
        if (!string.IsNullOrEmpty(credits.Title)) {
            yield return new TextSlot(TextKey.CreditsTitle, () => credits.Title, t => credits.Title = t);
        }
        for (int i = 0; i < credits.Lines.Count; i++) {
            CreditLine line = credits.Lines[i];
            if (!string.IsNullOrEmpty(line.Role)) {
                yield return new TextSlot(TextKey.CreditRole(i), () => line.Role, t => line.Role = t);
            }
            if (!string.IsNullOrEmpty(line.Name)) {
                yield return new TextSlot(TextKey.CreditName(i), () => line.Name, t => line.Name = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfItems(ObjectInfoSet items) {
        for (int i = 0; i < items.Items.Count; i++) {
            ObjectInfo item = items.Items[i];
            if (!string.IsNullOrEmpty(item.Name)) {
                yield return new TextSlot(TextKey.ItemName(i), () => item.Name!, t => item.Name = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfSpells(SpellList spells) {
        foreach (int id in spells.Spells.Keys.OrderBy(k => k).ToList()) {
            Spell spell = spells.Spells[id];
            if (!string.IsNullOrEmpty(spell.Name)) {
                yield return new TextSlot(TextKey.SpellName(id), () => spell.Name!, t => spell.Name = t);
            }
        }
    }

    private static IEnumerable<TextSlot> OfSpellDocs(SpellDescriptions docs) {
        foreach (SpellDescription doc in docs.Spells) {
            foreach (TextSlot slot in DocFields(doc)) {
                yield return slot;
            }
        }
    }

    private static IEnumerable<TextSlot> DocFields(SpellDescription doc) {
        (TextKey.SpellDocField Field, Func<string> Get, Action<string> Set)[] fields = {
            (TextKey.SpellDocField.Name, () => doc.Name, t => doc.Name = t),
            (TextKey.SpellDocField.Cost, () => doc.Cost, t => doc.Cost = t),
            (TextKey.SpellDocField.Damage, () => doc.Damage, t => doc.Damage = t),
            (TextKey.SpellDocField.Duration, () => doc.Duration, t => doc.Duration = t),
            (TextKey.SpellDocField.LineOfSight, () => doc.LineOfSight, t => doc.LineOfSight = t),
            (TextKey.SpellDocField.Effect, () => doc.Effect, t => doc.Effect = t),
            (TextKey.SpellDocField.EffectLine2, () => doc.EffectLine2, t => doc.EffectLine2 = t),
        };
        foreach ((TextKey.SpellDocField field, Func<string> get, Action<string> set) in fields) {
            if (!string.IsNullOrEmpty(get())) {
                yield return new TextSlot(TextKey.SpellDoc(doc.SpellKey, field), get, set);
            }
        }
    }

    private static IEnumerable<TextSlot> OfCreatures(CreatureNames creatures) {
        foreach (CreatureName creature in creatures.Creatures) {
            if (!string.IsNullOrEmpty(creature.Name)) {
                yield return new TextSlot(TextKey.MonsterName(creature.Number), () => creature.Name, t => creature.Name = t);
            }
        }
    }

    private static string Join(IEnumerable<TextSegment> segments) {
        var text = new StringBuilder();
        foreach (TextSegment segment in segments) {
            text.Append(segment.Text);
        }
        return text.ToString();
    }
}

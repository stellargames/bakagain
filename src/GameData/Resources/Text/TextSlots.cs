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
            Page page = book.Pages[p];
            for (int q = 0; q < page.Paragraphs.Count; q++) {
                Paragraph paragraph = page.Paragraphs[q];
                BookImage? capital = q == 0 ? page.Images.FirstOrDefault(i => BookDropCaps.LetterOf(i.ImageNumber) != null) : null;
                if (capital != null && paragraph.TextSegments.Any(s => !string.IsNullOrEmpty(s.Text))) {
                    yield return DropCapSlot(TextKey.BookParagraph(resourceId, p, q), resourceId, page, paragraph, capital);
                    continue;
                }
                if (paragraph.TextSegments.Any(s => !string.IsNullOrEmpty(s.Text))) {
                    yield return new TextSlot(TextKey.BookParagraph(resourceId, p, q),
                        () => BookMarkup(paragraph.TextSegments),
                        t => paragraph.TextSegments = FromBookMarkup(t, paragraph.TextSegments[0]));
                }
            }
        }
    }

    /// <summary>
    /// The paragraph an illuminated capital begins (TASK-781): it reads with the capital's letter, and
    /// a translation's own first letter picks the capital, or, when the game has no picture for that
    /// letter, is written out with the picture removed.
    /// </summary>
    private static TextSlot DropCapSlot(string key, string resourceId, Page page, Paragraph paragraph, BookImage capital) {
        char letter = BookDropCaps.LetterOf(capital.ImageNumber)!.Value;
        return new TextSlot(key,
            () => BookDropCaps.Join(resourceId, letter, BookMarkup(paragraph.TextSegments)),
            t => {
                int? picture = t.Length > 0 ? BookDropCaps.ImageFor(t[0]) : null;
                if (picture != null) {
                    capital.ImageNumber = picture.Value;
                    t = t.Substring(1).TrimStart(' ');
                } else {
                    page.Images.Remove(capital);
                    // The box the text wrapped around goes with it.
                    page.ReservedAreas.RemoveAll(r => r.X <= capital.X && capital.X <= r.X2 && r.Y <= capital.Y && capital.Y <= r.Y2);
                }
                paragraph.TextSegments = FromBookMarkup(t, paragraph.TextSegments[0]);
            });
    }

    private const string ItalicOpen = "<i>";
    private const string ItalicClose = "</i>";

    /// <summary>
    /// A paragraph's segments as one string, the italic ones inside <c>&lt;i&gt;</c> pairs (TASK-774).
    /// </summary>
    /// <remarks>
    /// Italic is the only thing the shipped books vary between a paragraph's segments — font,
    /// colour and offset are the same in every one of the 35 multi-segment paragraphs — so it is
    /// the only thing the markup carries. Empty segments draw nothing and are not written.
    /// </remarks>
    private static string BookMarkup(IEnumerable<TextSegment> segments) {
        var text = new StringBuilder();
        foreach (TextSegment segment in segments) {
            if (string.IsNullOrEmpty(segment.Text)) {
                continue;
            }
            bool italic = segment.FontStyle.HasFlag(FontStyle.Italic);
            text.Append(italic ? ItalicOpen : string.Empty).Append(segment.Text).Append(italic ? ItalicClose : string.Empty);
        }
        return text.ToString();
    }

    /// <summary>A translation's markup back into segments, styled like <paramref name="template"/> apart from italic.</summary>
    private static List<TextSegment> FromBookMarkup(string markup, TextSegment template) {
        var segments = new List<TextSegment>();
        var italic = false;
        int at = 0;
        while (at < markup.Length) {
            string tag = italic ? ItalicClose : ItalicOpen;
            int next = markup.IndexOf(tag, at, StringComparison.Ordinal);
            int end = next < 0 ? markup.Length : next;
            if (end > at) {
                segments.Add(new TextSegment {
                    Font = template.Font,
                    YOffset = template.YOffset,
                    Color = template.Color,
                    FontStyle = italic ? template.FontStyle | FontStyle.Italic : template.FontStyle & ~FontStyle.Italic,
                    Text = markup.Substring(at, end - at),
                });
            }
            at = next < 0 ? markup.Length : next + tag.Length;
            italic = !italic;
        }
        return segments;
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
}

# Laying out screens and dialogs

Every position and size in the game is **data**, not code. You change a layout by
overriding the JSON the game ships, never by editing a script — which is also why
the UI can be scaled to a window of any size or aspect ratio.

## px or percent

A length is written `"120px"` or `"7.5%"`.

- **px** is canonical-space pixels. The canonical space is 1600×1200, and the game
  scales that whole space to the window. A px layout keeps its proportions but does
  not *reflow*.
- **percent** resolves against the parent at the size it actually has. This is what
  you want if a panel should follow the window rather than the design frame — a
  fluid layout.

Everything shipped is authored in px, because that is what the original binary
holds. Percentages exist for you.

## The one precedence rule worth knowing

**A dialog entry's resize replaces its style row's area outright. It does not merge.**

Dialog geometry comes from two places:

1. `DIALSTYL.json` — a `DefaultArea` per style row, the area every dialog of that
   style uses.
2. A `ResizeDialog` action on an individual DDX entry, which states that one
   entry's area itself.

When an entry carries a resize, the style row's `DefaultArea` is **discarded
wholesale** for that entry — anchor, insets and all. That is the original's
behaviour and the port keeps it.

It matters because of how much shipped data carries one:

| | count |
|---|---|
| dialog entries | 8203 |
| entries carrying a `ResizeDialog` | 550 |
| …of those, text-bearing | 548 |
| resize lengths that are px | 2200 (all of them) |

So **restating a DIALSTYL row in percentages does not make those 550 entries
fluid**. They keep their px rect and place themselves from the top-left, and the
row you carefully rewrote is never consulted for them.

### What to do instead

Override the entry's resize in percentages as well. `ResizeDialog` speaks the same
length vocabulary as everything else, so this works and is exactly as supported:

```json
{ "Entries": [ { "Id": 111, "Actions": [
    { "$type": "ResizeDialog", "Left": "4%", "Top": "5.5%", "Width": "92%", "Height": "50%" }
] } ] }
```

Rule of thumb: **style rows for the dialogs that have no resize, entry overrides
for the ones that do.** Restating the row alone silently covers only part of the
game.

## When the game refuses something

Some fields are in the vocabulary but have no implementation. Rather than ignore
you, the layout translator logs an error naming the field and what you will see
instead:

- `LayoutFlow.Gap` — this Unity's UI Toolkit has no flex-gap, so children lay out
  touching.
- `LayoutHint.Slice` — nine-slice borders are not implemented, so a background is
  drawn stretched rather than sliced. The element is still sized and placed.
- A percentage where only px can be resolved (for example an offset measured in the
  same space as a cell) — the element degrades visibly rather than moving somewhere
  arbitrary.

None of these fire for shipped data: every `Gap` and every `Slice` in the game is
its default. If you see one, it is about something you authored.

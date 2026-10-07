# Language packs

A language pack puts BaK-Again in another language: the dialogs, books, item and spell names, menus
and the game's own messages, and optionally its fonts and the pictures with lettering in them. A pack
is a folder of ordinary files: a gettext `.po` file, plus images and fonts if it needs them. The game
does not need rebuilding, and a pack never changes your game files.

## Installing a pack

1. **Find your overrides folder.** It is the "Override directory" on the screen the game shows at its
   first start (where you pick the folder holding `KRONDOR.001`). If you never changed it, it is:

   | Platform | Overrides folder |
   |---|---|
   | Windows | `%USERPROFILE%\AppData\LocalLow\StellarGameStudio\BaK-Again\overrides` |
   | Linux | `~/.config/unity3d/StellarGameStudio/BaK-Again/overrides` |
   | Android | `Android/data/com.StellarGameStudio.BaKAgain/files/overrides` |

2. **Copy the pack** into `Lang/` inside it. The folder must be named after the language code, and so
   must the `.po` file inside it:

   ```
   overrides/
     Lang/
       nl/
         nl.po
         fonts/  SCX/  BMX/  TTM/     (only if the pack has them)
   ```

3. **Choose the language** in **Preferences**: the Language button steps through English and every
   installed pack. Press OK and restart the game; the button shows "(restart)" until you do.

A language pack works whether or not **Enable overrides** is ticked; that switch is for mods. To go
back to English, choose English in Preferences, or delete the pack folder.

## What a pack contains

| Path | What it is | Needed |
|---|---|---|
| `<code>.po` | the translations, one entry per piece of game text | yes |
| `fonts/GAME.bdf`, `fonts/BOOK.bdf` | extra letters for the game font and the book font | no |
| `SCX/<NAME>.png` | a whole screen (backdrop) with lettering, replaced | no |
| `BMX/<NAME>/<n>.png` | one picture from a picture set, replaced | no |
| `TTM/<NAME>.json` | an animation script, to move a replaced picture | no |

Anything the pack does not translate stays in English, so a half-finished pack is a working pack.

The pack folder is searched before your mod folder, which is searched before the game's own files.
So `SCX/`, `BMX/` and `TTM/` follow exactly the layout described for mods in
[laying out screens](laying-out-screens.md), and any other override a mod can make, a pack can make
too.

## Making a pack

### 1. Get the translation template

The template (`BAK.pot`) lists every piece of text with its English. It is made from **your** copy of
the game, so it is never published; nor should you publish it. Build it with the .NET 10 SDK from this
repository:

```
dotnet run --project src/ResourceExtractor -- --pot "<folder with KRONDOR.001>" BAK.pot
```

### 2. Translate it

Open `BAK.pot` in any PO editor — Poedit, Weblate, Crowdin, Lokalize — and create a translation for your
language; save it as `<code>.po` (e.g. `fr.po`). Set the header's **Language** to the same code.

The rules are gettext's own:

- Each entry is identified by its `msgctxt` (`base:ddx:dial_z01:1234`, `base:objinfo:12:name`,
  `port:template:ok`, ...). The `msgid` is the English, shown for reference.
- An empty translation means "not translated yet": the English is used.
- An entry marked **fuzzy** is treated as an unreviewed guess and is not used.

### 3. Keep the codes in the text

Game text carries a few codes. Keep each one, in the place the sense needs it:

| Code | Meaning |
|---|---|
| `<hi/>` `<i/>` `<reset/>` `<shift/>` `<shift2/>` | the original's highlight and italic switches; `<hi/>` marks the next word (chapter titles mark every word) |
| `#Name#` at the start of a dialog | the speaker's heading line: translate the name only if the game's name for that character is translated |
| `@0` ... `@5` | a name the game fills in (a party member, a creature); keep it, move it where your grammar needs it. `@4s` adds the English possessive *'s*; write your own grammar instead |
| `@` before anything but a digit | the name of the character who is acting ("@ gaped in astonishment") |
| `{kind4, select, creature {...} person {...} other {...}}` | in dialog text: choose words by what slot 4 holds, for articles and cases the English does not need |
| `%d` `%ld` `%s` | numbers and words the game fills in, in order; to change the order write `%2$d` |
| `{name}`, `{n, plural, ...}`, `{case, select, ...}` | in `port:template:` entries only: ICU MessageFormat, with plural and select |
| `\t` at the start of a paragraph | the paragraph indent |

The same tags are what a dialog override (`DDX/<NAME>.json`, see [laying out screens](laying-out-screens.md))
uses. Dialog JSON written before the tags existed carries the original's raw code characters instead
(`≡`, `±`, `≤` ...); those are now drawn as the characters they are, so replace them with the tags.

### 4. Make it fit

Boxes, buttons and pages keep the original's sizes. A translation that does not fit is shortened by
the translator. The only text the game shrinks is a button caption, and only down to 60% of the
font's size. Three things help:

- **The room in the template.** A button caption's entry says how wide its button is and about how
  many characters fit, measured against the English:
  `#. One line, 78 px wide. The English takes 72 px: about 15 characters fit.`

- **The overflow report.** Every string that is cut off, runs past its page, or collides with its
  neighbour is logged once as a `[TextOverflow]` warning in the game's log (its path is shown at the
  bottom of the Preferences screen). Play through with your pack and read the log.
- **The pseudo language** (development builds only): a built-in "Pseudo" language that makes every
  string about a third longer, accented and bracketed, to find the places that will break before
  translating. Text without brackets is not translatable yet.

### 5. Letters the fonts do not have

The game's fonts only have the English alphabet. Accented letters (é, ü, ø, ł, č, ...) are made
automatically from the font's own letters with a small mark added, so most European languages need no
font work at all.

To draw letters yourself, or letters that are not an accented Latin letter, add `fonts/GAME.bdf` and/or
`fonts/BOOK.bdf`: standard BDF bitmap fonts (FontForge and Bits'n'Picas write them) holding only the
letters you add. `ENCODING` is the Unicode code point. The glyphs are placed on the game font's
baseline in its own pixels, and the font's cell does not grow: ink above or below it is cut off, and
a warning names the letters it happened to.

### 6. Pictures with lettering

Some pictures have English drawn into them: the chapter screen and chapter titles, the options and
contents screens, the first credits page, the labels that appear over buildings, a shop sign, and the
intro. To replace one, draw it and save it at the path the game would look up:

- `SCX/<NAME>.png` for a screen, at 1600×1200.
- `BMX/<NAME>/<n>.png` for picture `n` of a set, at the size you want it drawn (1600×1200 space).
  For pictures shown in animations (the chapter titles), put a `<n>.json` beside it with
  `"ScaleX": <width>/1600` and `"ScaleY": <height>/1200`.

The originals, in exactly this layout and size, come from
`dotnet run --project src/ResourceExtractor -- --images "<folder with KRONDOR.001>"`. They are game
art: start from them, but do not publish them unchanged.

**Book capitals.** A book's first paragraph starts with an illuminated capital, which the picture draws,
so its text is stored without that letter. In the template the paragraph shows with its letter; your
translation's own first letter picks the capital. The game has pictures for A B D G I J L O P S T. For
any other letter, either the letter is simply written out, or you draw a capital: put it at
`BMX/BOOK/<n>.png` (19 or higher) and name its letter in the PO header,
`X-Drop-Caps: E=19` (several: `E=19, W=20`).

A replaced picture that is wider or taller than the original may need moving. The chapter title
cards are animation scripts: get them with `-- --ttm "<folder>"`, change the `X`/`Y` of the
`DrawImage` that draws your picture, and put the file in the pack as `TTM/<NAME>.json`.

## Sharing a pack

A pack you share should hold only your own work: your translations, your fonts, your pictures.
Never include the template, the game's files, or unchanged extracted art.

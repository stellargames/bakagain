# Making mods

A mod changes what BaK-Again shows, without touching the game's own files: new pictures for the
trees, other portraits, a translation. It is a zip of ordinary files (PNG pictures, JSON data, a
`.po` translation) with a `mod.json` that names it. Mods hold data and pictures only, never code.

A mod is never allowed to carry the original game's files. Pictures you make from the game's art are
your own work, and sharing them is your responsibility.

## The layout

A mod is a folder, zipped. The paths inside it are the paths the game looks up:

```
hd-trees/
  mod.json                     the manifest (below)
  aliases.json                 optional: one picture serving several keys
  SCX/<NAME>.png               a whole screen (backdrop), at 1600x1200
  BMX/<NAME>/<n>.png           picture n of a picture set
  BMX/<NAME>/<n>.json          optional sidecar for that picture
  BMX/<NAME>/<n>.front.png     optional sun-lit versions of a world billboard (Enhanced only):
  BMX/<NAME>/<n>.left.png        front, left, right and back, all four or none
  BMX/<NAME>/<n>.right.png
  BMX/<NAME>/<n>.back.png
  <EXT>/<NAME>.json            game data, e.g. DDX/<NAME>.json
  ObjectInfo/<any>.json        item records, by their Number
  Resources/<path>.png         a built-in Enhanced picture, e.g. Resources/Enhanced/Sky/moon_large.png
  Resources/<path>.sf2         the music sound bank
  Lang/<code>/<code>.po        a translation (a language mod)
```

To see what a file is called and how big it is, extract the game's own with
`dotnet run --project src/ResourceExtractor -- --images "<folder with KRONDOR.001>"`. It writes
exactly this layout.

- **Pictures** are replaced whole. A replacement may be larger than the original: a world
  billboard keeps the original's size on screen, so a picture with more pixels is simply sharper.
- **The picture sidecar** `<n>.json` can hold `ScaleX`/`ScaleY` for a picture an animation draws
  (its size as a fraction of 1600x1200, see [language packs](language-packs.md)), and for world
  billboards `VarySize`/`VaryColor`: how much each placed copy varies in
  size and colour, e.g. `{"VarySize":0.08,"VaryColor":0.07}`. Each copy keeps its variation for good.
- **Sun-lit billboards.** With all four of `<n>.front.png`, `.left`, `.right` and `.back` beside
  `<n>.png`, the Enhanced lighting blends them by where the sun stands against the view. `<n>.png`
  is the soft, overcast picture shown at night and with a low sun. All five must have the same
  outline: the transparency of `<n>.png` cuts out all of them.
- **A creature in a colour set** (`<NAME>_CS<n>`): for its HD world-billboard frames only, the game
  looks first in `BMX/<NAME>_CS<n>/`, then in `BMX/<NAME>/`, so one HD picture can serve every
  colour, or each its own. Other pictures have no such fallback.
- **Data documents** (`<EXT>/<NAME>.json`) are merged onto the game's own data where the type
  supports it: objects key by key, `null` leaves a value alone, item records (`ObjectInfo`) by their
  `Number`. Any other type is replaced whole.
- **3D models** (`Models/<zone>/<name>.glb`) are not supported yet: the current build loads them
  from nowhere (not from a mod, not from the dev folder), and a mod zip cannot carry them. See
  [replacing models](replacing-models.md) for the intended layout.
- **Translations**: see [language packs](language-packs.md).

### aliases.json

The same picture is often used under several names: a tree is `Z01SLOT0` picture 1 in one zone and
`Z02SLOT0` picture 1 in the next. Rather than copying the files, map the other names to one:

```json
{
  "BMX/Z02SLOT0/1": "BMX/Z01SLOT0/1",
  "BMX/Z04SLOT0/1": "BMX/Z01SLOT0/1"
}
```

Paths have no extension. An alias brings the target's sun-lit versions and sidecar with it, and the
game loads the shared picture once. Only alias names whose original art is truly the same picture:
the same sprite in another zone's palette (a winter zone, say) is a different picture.

## mod.json

```json
{
  "schema": 1,
  "id": "hd-trees",
  "name": "HD Trees",
  "version": "1.0.0",
  "author": "Your name",
  "description": "Photographic trees that follow the sun.",
  "license": "CC-BY-4.0",
  "game": ">=0.3.0",
  "dependencies": { "some-mod": "^1.0.0" },
  "conflicts": [ "other-tree-mod" ],
  "kinds": [ "sprites" ],
  "modes": [ "enhanced" ]
}
```

| Field | Meaning | Needed |
|---|---|---|
| `schema` | `1` | yes |
| `id` | lower-case letters, digits and `-`, 2 to 48 characters, starting with a letter or digit. Also the folder it installs to. | yes |
| `name` | what the Mods screen shows | yes |
| `version` | [SemVer](https://semver.org): `1.2.0`, `0.3.0-beta` | yes |
| `author`, `description`, `license` | shown to players | no |
| `game` | the game versions it works with | no |
| `dependencies` | other mods it needs, with a version range each | no |
| `conflicts` | mods it cannot be used with | no |
| `kinds` | what it changes, for display: `sprites`, `textures`, `portraits`, `sounds`, `music`, `rules`, `items`, `loot`, `text`, `language` | no |
| `modes` | `enhanced`, `classic` or both; `["enhanced"]` when absent | no |

A version range is `>=1.2.0` (this or later), `^1.2.0` (this or later, same major version) or an
exact `1.2.0`. A pre-release tag is ignored when comparing: `>=0.3.0` accepts `0.3.0-beta`.
Unknown fields are ignored.

### Modes

A mod applies only in the modes it lists. Classic is the faithful game, so HD art belongs in
Enhanced; a gameplay, sound or text mod can list `classic` too. Sun-lit billboards are drawn only
in Enhanced. Switching Enhanced on or off takes effect at the next start, because it changes which
mods apply.

### Priority

When two mods replace the same file, the one higher in the Mods screen wins. A new mod goes to the
top. Data documents merge from the bottom up, so two mods changing different fields of the same
file both take effect. The full lookup order, first hit wins:

1. the chosen language (a language mod, or a pack in the dev folder);
2. the dev folder, when it is switched on;
3. the enabled mods for the current mode, top of the list first;
4. the game's own files.

## Packing and checking: bakmod

`bakmod` is the command-line tool in `src/BakMod` (needs the .NET 10 SDK):

```
dotnet run --project src/BakMod -- validate <mod.zip>
dotnet run --project src/BakMod -- pack <folder> [-o <outDir>]
```

- `validate` checks a zip the way the game does before installing it: a readable `mod.json` with
  valid fields, safe paths, no duplicate entries, only the folders above and only `.png`, `.jpg`,
  `.json`, `.po`, `.ttf`, `.otf` and `.sf2` files, sun-lit versions with their `<n>.png`, and
  aliases that point at files in the mod. It prints `OK (<id> <version>)` or the errors.
- `pack` validates the folder and writes `<id>-<version>.zip` into `<outDir>` (default: the current
  folder; never inside the mod folder itself, so a second pack does not swallow the first).

## Installing

The game keeps its mods in a `mods` folder in its data folder:

| Platform | Mods folder |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\StellarGameStudio\BaK-Again\mods` |
| Linux | `~/.config/unity3d/StellarGameStudio/BaK-Again/mods` |
| Android | `Android/data/com.StellarGameStudio.BaKAgain/files/mods` |

Two ways in:

- **Drop the zip into the mods folder.** It is installed at the next start: unpacked into
  `mods/<id>/`, put at the top of the list and switched on, and the zip is deleted. A zip that
  cannot be installed is renamed `<name>.zip.failed` and the log says why.
- **Install from file…** on the Mods screen (desktop): pick the zip. It is unpacked into
  `mods/<id>/` at once and shown at the top, switched on; press **Save** to keep the list as shown.

The Mods screen is under **Preferences → Enhanced → Installed mods**. There you switch mods on and
off, move them up and down, and uninstall them (pressing Uninstall twice only marks the mod).
Every change on the screen needs **Save**: Save writes the order and the on/off state and deletes
the mods marked for uninstall; Cancel drops the changes (a mod installed from a file stays
installed). Saved changes take effect at the next start; until then the buttons say "(restart)". A mod that needs a missing mod, the wrong
version of one, or a newer game is listed with the reason and stays off.

The game's log names the active mods at every start: `Mods (enhanced): hd-trees 0.1.0`.

**Saves remember the mods.** A save made with mods active has a `SAVE##.MODS.json` beside it. When
you load it and one of those mods is missing or switched off, or installed at another major
version, the game names it and asks whether to load anyway.

## The dev folder

While you make a mod, you do not need to pack it at every change. Switch on **Dev overrides** in
**Preferences → Enhanced** and point **Dev overrides folder** at your mod folder (it has the same
layout as a mod; `mod.json` is not needed there). After a restart the dev folder is used above every
installed mod, in Classic and Enhanced alike, so it is also how you test a Classic mod. Pack it with
`bakmod pack` once it is done.

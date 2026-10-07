# Asset origins

Where every committed binary asset comes from, so the public release carries nothing from
*Betrayal at Krondor* and nothing under a non-free license. Audited 2026-09-26 (TASK-663).
**Adding a binary asset? Add its row here.**

## Shipped in the public repo

| Asset | Origin | License |
|---|---|---|
| `Assets/Resources/Audio/GeneralUser-GS.bytes` (+ `-LICENSE.txt`) | GeneralUser GS 2.0.3 soundfont, S. Christian Collins | GeneralUser GS License v2.0 |
| `Assets/Plugins/MeltySynth/MeltySynth.dll` | NuGet MeltySynth 2.4.1 | MIT |
| `Assets/Plugins/SimpleFileBrowser/**` (sprites, prefab) | yasirkula/UnitySimpleFileBrowser 1.7.7 | MIT |
| `Assets/Resources/SimpleFileBrowserCanvas.prefab` | copy of the Simple File Browser prefab | MIT |
| `Assets/Plugins/I18N/*.dll` (Android + standalone players; not the Editor, which has its own) | Mono class libraries | MIT |
| `Assets/Plugins/netstandard2.1/*` | built from `src/` (GameData, ResourceExtraction) + DryWetMidi native libs | MIT |
| `Assets/Custom/Fonts/Victor_Mono/**`, `VictorMono-Regular.ttf` | Victor Mono | SIL OFL 1.1 (`OFL.txt` alongside) |
| `Assets/TextMesh Pro/**` (LiberationSans, EmojiOne, shaders) | TextMesh Pro essential resources (Unity) | Unity Companion License; Liberation Sans OFL 1.1 |
| `Assets/Custom/Icons/Icon.png` | app icon, owner-supplied illustration, not game art | **owner to confirm the source** |
| `Assets/Localization/*.asset` | the port's own UI strings (loading screen, game-folder prompt), English and Dutch | project (MIT) |
| `Assets/Tests/Editor/World/Fixtures/cube.glb` | hand-made 684-byte test cube (8c2bb6b8) | project (MIT) |
| `website/img/hero.jpg`, `website/img/og.jpg` (monorepo, not the Unity project) | original artwork generated for the project with Google Gemini (2026-09-27, prompt: dusk road to a walled port city, no text/logos/game art); title text set in Liberation Serif | project (CC-BY-4.0, as docs/) |
| `website/favicon.ico`, `website/img/apple-touch-icon.png` (monorepo) | scaled down from `Assets/Custom/Icons/Icon.png` (2026-10-07) | as `Icon.png` |
| `Packages/Microsoft.Win32.Registry.5.0.0`, `System.Security.*` | Microsoft NuGet packages | MIT |

## Removed: derived from the original game

| Asset | Was | Now |
|---|---|---|
| `Assets/Resources/Terrain/*.png` (9) | baked from `Z01L.SCX` + `Z01.PAL` by an Editor tool | baked at runtime from the player's copy, `TerrainPenTextures.LoadAll` → `ScxTileBaker`; pixel-identical to the old PNGs (one pixel off by 1) |
| `Assets/Resources/UI/DialogPanel.png` | baked from `OPTIONS0.SCX` + `OPTIONS.PAL` by an Editor tool | baked at runtime, `DialogPanelBuilder.GetPanelTile` → `ScxTileBaker`; pixel-identical |
| `Assets/Custom/Fonts/Game.ttf`, `Book.ttf`, `Puzzle.ttf`, `Book SDF`, `Book Bitmap`, `Puzzle Bitmap`, `UI Toolkit/.../Game SDF` | rebuilt from the Dynamix `.FNT` fonts ("Copyright Dynamix" in the metadata) | built at runtime from the player's GAME.FNT / BOOK.FNT: `FntTrueType` → `GameFonts` (TASK-662). PUZZLE and the two Bitmap assets had no consumer |
| Maestro MIDI Player Toolkit (`Assets/Plugins/MidiPlayer`) | Asset Store EULA | MeltySynth (TASK-661) |

## Kept in the private workspace only (excluded from the export, TASK-669)

These are either development tooling or screenshots that show original game art.

- `com.coplaydev.unity-mcp` (Packages/manifest.json) and `Assets/Resources/Unity-MCP-ConnectionConfig.json`: the Editor MCP bridge.
- `Assets/Plugins/Roslyn/`: used only by the Editor's code-eval tooling.
- `UIElementsSchema/`, `.idea/`, `.windsurfrules`, `App.config`, `packages.config`: generated or editor-local files.
- `Assets/_Recovery/`, `Assets/InitTestScene*.unity`: Editor crash and test-runner leftovers.
- `.probe-shot.png`, `book-c11-page*-target.png`, `ours_puzzle13*.png`: screenshots of game art from verification sessions.
- `docs/modding/images/inn-replaced-with-cube.png`: a screenshot of the port rendering game art. Screenshots are a website question, not a repo one.
- **The website's screenshots** (`https://bakagain.org/shots/*.jpg`, 2026-10-02): six captures of the port running on a copy of the original, which show its art. They live on the VPS in `/srv/bakagain-static/shots/`, served by a Caddy `handle /shots/*` block, and are never committed; `website/index.html` only links to them.
- Odin Inspector (`Assets/Plugins/Sirenix`, gitignored): commercial, unused by any script. Moved out of the project on 2026-09-26 to `~/unity-asset-backups/`.

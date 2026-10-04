# Third-party notices

BaK-Again's own code is MIT-licensed (`LICENSE`), and its documentation and the Kaitai `.ksy`
format specs are CC-BY-4.0 (`LICENSE-docs`). BaK-Again ships **no** content from *Betrayal at
Krondor*: the game reads the player's own copy at runtime.

Bundled or build-time components and their licenses:

| Component | Used for | License |
|---|---|---|
| [MeltySynth](https://github.com/sinshu/meltysynth) 2.4.1 | MIDI synthesis | MIT |
| [GeneralUser GS](https://schristiancollins.com/generaluser.php) 2.0.3 by S. Christian Collins | General MIDI soundfont | GeneralUser GS License v2.0 (free use in software projects; text in `unity/Assets/Resources/Audio/GeneralUser-GS-LICENSE.txt`) |
| [UniTask](https://github.com/Cysharp/UniTask) | async/await | MIT |
| [VContainer](https://github.com/hadashiA/VContainer) 1.19.0 | dependency injection | MIT |
| [Scriban](https://github.com/scriban/scriban) 6.2.1 | text templating | BSD-2-Clause |
| [Serilog](https://serilog.net/) 4.4.0, Serilog.Extensions.Logging 8.0.0 | logging | Apache-2.0 |
| [Serilog.Sinks.Unity3D](https://github.com/KuraiAndras/Serilog.Sinks.Unity3D) 3.0.0 | logging to the Unity console | MIT |
| Newtonsoft.Json (Unity package 3.2.2) | JSON | MIT |
| System.Text.Json 8.0.5, System.Text.Encodings.Web 8.0.0, System.Text.Encoding.CodePages 8/9, and the transitive `Microsoft.Extensions.*` / `System.*` packages they pull in | JSON, DOS code pages, logging abstractions | MIT |
| [Unity glTFast](https://github.com/Unity-Technologies/com.unity.cloud.gltfast) 6.19.0 | glTF models | Apache-2.0 |
| [Simple File Browser](https://github.com/yasirkula/UnitySimpleFileBrowser) 1.7.7 | picking the game folder | MIT |
| [Melanchall DryWetMidi](https://github.com/melanchall/drywetmidi) 8.0.0 | building MIDI files from game audio | MIT |
| [Karambolo.PO](https://github.com/adams85/po) 1.13.0 + Karambolo.Common 3.4.1 | reading gettext PO language packs | MIT |
| [MessageFormat](https://github.com/jeffijoe/messageformat.net) 8.0.0 | ICU MessageFormat templates (plurals, reordering) in translations | MIT |
| [CommunityToolkit.HighPerformance](https://github.com/CommunityToolkit/dotnet) 8.3.2 | data extraction | MIT |
| Mono I18N (`I18N.dll`, `I18N.West.dll`) | the DOS code page (437) in players | MIT |
| [Victor Mono](https://rubjo.github.io/victor-mono/) | debug/monospace text | SIL OFL 1.1 |
| Liberation Sans (TextMesh Pro default) | fallback text | SIL OFL 1.1 |

Unity engine packages (`com.unity.*`) are used under the Unity Companion License / Unity Terms of
Service and are fetched by the Unity Package Manager, not redistributed in this repository.

*Betrayal at Krondor* is © its respective rights holders. BaK-Again is an unofficial fan project
and is not affiliated with or endorsed by them.

# BaK-Again

A modern, moddable remake of the 1993 role-playing game *Betrayal at Krondor*, built in Unity. It
runs the original game's story, world, rules and art from **your own copy** of the game, and plays
on Windows, Linux and Android.

> **Alpha.** Every chapter can be played to the end, and plenty is still rough. Bug reports are
> welcome on the [forum](https://forum.bakagain.org).

## You need the original game

BaK-Again contains **nothing** from *Betrayal at Krondor*. It reads the game's data files
(`KRONDOR.001` and friends) from an installed copy, which you can buy from
[GOG](https://www.gog.com/). On first start it asks where that copy is.

## Download

Builds for Windows, Linux and Android are on the
[Releases page](https://github.com/stellargames/bakagain/releases) and at
[bakagain.org](https://bakagain.org).

## Building from source

- **Unity:** the version in `unity/ProjectSettings/ProjectVersion.txt`.
- **.NET 10 SDK:** for the data libraries in `src/`.

Build the two libraries the Unity project uses (this writes them into
`unity/Assets/Plugins/netstandard2.1/`), then open `unity/` in the Unity Editor:

```bash
dotnet build src/ResourceExtraction/ResourceExtraction.csproj -c Unity
```

The .NET tests run with `dotnet test src/BakAgain.slnx`. Tests that need the original game's
files skip when it is not present.

## Modding

Put replacement assets in an `Overrides/` folder next to the game files. See `unity/docs/modding/`, and
`formats/` for the original file formats (Kaitai Struct specs).

## License

- **Code:** MIT (`LICENSE`).
- **Documentation and format specs:** CC-BY-4.0 (`LICENSE-docs`).
- **Third-party components:** see `THIRD-PARTY-NOTICES.md`.

*Betrayal at Krondor* is © its respective rights holders. BaK-Again is an unofficial fan project
and is not affiliated with or endorsed by them.

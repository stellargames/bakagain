namespace ResourceExtractor;

using GameData.Resources.Animation;
using GameData.Resources.Audio;
using GameData.Resources.Book;
using GameData.Resources.Combat;
using GameData.Resources.Config;
using GameData.Resources.Credits;
using GameData.Resources.Data;
using GameData.Resources.Dialog;
using GameData.Resources.Image;
using GameData.Resources.Label;
using GameData.Resources.Location;
using GameData.Resources.Menu;
using GameData.Resources.Object;
using GameData.Resources.Palette;
using GameData.Resources.Spells;
using GameData.Resources.Monster;
using GameData.Resources.World;
using GameData.Resources.Creature;
using ResourceExtraction;
using ResourceExtraction.Extractors;
using ResourceExtraction.Imaging;
using ResourceExtraction.Extractors.Animation;
using ResourceExtraction.Extractors.Def;
using ResourceExtraction.Providers;
using ResourceExtractor.Extensions;
using ResourceExtraction.Extractors.Dialog;
using ResourceExtraction.Extractors.Exe;
using ResourceExtractor.Imaging;
using System.Linq;
using System.Text;
using System.Text.Json;
using Color = GameData.Resources.Palette.Color;
using PaletteExtractor = ResourceExtraction.Extractors.PaletteExtractor;
using ResourceType = GameData.Resources.ResourceType;

internal static class Program {

    public static void Main(string[] args) {

        // Before the switch into generated/: the template holds the game's English, so it goes
        // where the player asked, never into the repository.
        if (args.Length >= 1 && args[0] == "--pot") {
            if (args.Length < 3) {
                Console.Error.WriteLine("usage: --pot <game dir> <out.pot>");
                return;
            }
            WriteTranslationTemplate(args[1], args[2]);
            return;
        }

        string generatedDir = ResolveGeneratedDir();
        Directory.CreateDirectory(generatedDir);
        Directory.SetCurrentDirectory(generatedDir);

        if (args.Length >= 1 && args[0] == "--cutscene-data") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            string outputDir = args.Length >= 3 ? args[2] : "generated";
            ExportCutsceneData(gamePath, outputDir);
            return;
        }

        if (args.Length >= 1 && args[0] == "--ddx") {
            ExportDdxDialogs(args);
            return;
        }

        if (args.Length >= 1 && args[0] == "--objinfo") {
            ExportObjectInfo(args);
            return;
        }

        if (args.Length >= 1 && args[0] == "--chapters") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            IResourceProvider provider = ResourceProviderFactory.CreateResourceProvider(gamePath);
            ChapterCatalog catalog = provider.GetResource<ChapterCatalog>(ChapterCatalog.ResourceId);
            WriteToJsonFile("CHAPTERS.DAT", ResourceType.DAT, catalog.ToJson());
            return;
        }

        // *** OBJFIXED HAS ITS OWN MODE BECAUSE ITS OUTPUT WENT MISSING. ***
        // `generated/DAT/OBJFIXED.json` had never been committed — and it is the ONLY place the fixed objects' interact-message dialog ids
        // appear, so every `grep generated` sweep was blind to them. TASK-411 spent six correct
        // eliminations reaching "referenced by nothing" because of it. A single-file mode makes the
        // gap cheap to close without a full run over the whole corpus.
        if (args.Length >= 1 && args[0] == "--objfixed") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            const string objFixed = "OBJFIXED.DAT";
            using FileStream stream = File.OpenRead(Path.Combine(gamePath, objFixed));
            FixedObjectSet fixedObjects =
                new ResourceExtraction.Extractors.ObjFixedExtractor().Extract(objFixed, stream);
            WriteToJsonFile(objFixed, ResourceType.DAT, fixedObjects.Containers.ToJson());
            return;
        }

        if (args.Length >= 1 && args[0] == "--dialog-styles") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            IResourceProvider provider = ResourceProviderFactory.CreateResourceProvider(gamePath);
            DialogStyleTable styles = provider.GetResource<DialogStyleTable>(DialogStyleTable.ResourceId);
            WriteToJsonFile("DIALSTYL.DAT", ResourceType.DAT, styles.ToJson());
            return;
        }

        if (args.Length >= 1 && args[0] == "--world") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractZoneData(gamePath);
            ExtractWorldTiles(gamePath);
            ExtractTileEvents(gamePath);
            ExtractMonsterStats(gamePath);
            ExtractZoneTables(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--spellbook") {
            string spellbookPath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider spellbookProvider = new(spellbookPath);
            ExtractSpellBookPage(spellbookProvider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--spells") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider provider = new(gamePath);
            ExtractSpells(provider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--tile-events") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractTileEvents(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--scx") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider provider = new(gamePath);
            ExtractAllScx(gamePath, provider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--bmx") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider provider = new(gamePath);
            ExtractAllBmx(gamePath, provider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--creatures") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            string outDir = args.Length >= 3 ? args[2] : Path.Combine(ResolveGeneratedDir(), "Creatures");
            GeneralResourceProvider provider = new(gamePath);
            ExtractCreatureSprites(gamePath, provider, outDir);
            return;
        }

        if (args.Length >= 1 && args[0] == "--in") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            string outDir = args.Length >= 3 ? args[2] : Path.Combine(ResolveGeneratedDir(), "IN");
            ExtractInputForms(gamePath, outDir);
            return;
        }

        if (args.Length >= 1 && args[0] == "--cursor") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider provider = new(gamePath);
            ExtractCursors(gamePath, provider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--images") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider provider = new(gamePath);
            ExtractAllScx(gamePath, provider);
            ExtractAllBmx(gamePath, provider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--tbl") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractZoneTables(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--sfx") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractAllSounds(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--ads") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractAnimations(gamePath, new GeneralResourceProvider(gamePath));
            return;
        }

        if (args.Length >= 1 && args[0] == "--ttm") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractAnimatorScripts(gamePath, new GeneralResourceProvider(gamePath));
            return;
        }

        if (args.Length >= 1 && args[0] == "--def") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractDefDat(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--chapsong") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractChapterSongMap(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--start") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractStartData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--partycombat") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractPartyCombatEntries(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--movement") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractMovementData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--party") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractPartyData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--onames") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractObjectNames(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--keyword") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractKeywords(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--rmp") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractAllRemappings(gamePath, new GeneralResourceProvider(gamePath));
            return;
        }

        if (args.Length >= 1 && args[0] == "--books") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractBooks(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--teleport") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractTeleportDestinations(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--mnames") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractCreatureNames(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--uistrings") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractUiStrings(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--combataffinity") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractCombatAffinity(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--fmap") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractFullMap(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--req") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractUserInterfaces(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--lbl") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            GeneralResourceProvider provider = new(gamePath);
            ExtractLabels(gamePath, provider);
            return;
        }

        if (args.Length >= 1 && args[0] == "--gds") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractGdsScenes(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--cred") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractCredits(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--symbols") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractSpellSymbols(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--ring") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractCastRing(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--filter") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractFilterData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--detect") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractDetectData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--traps") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractTrapData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--encamp") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractEncampData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--grid") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractGridData(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--spellaffinity") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractSpellAffinities(gamePath);
            return;
        }

        if (args.Length >= 1 && args[0] == "--spelldoc") {
            string gamePath = args.Length >= 2 ? args[1] : @"D:\BaK\OriginalGame";
            ExtractSpellDescriptions(gamePath);
            return;
        }

        if (args.Length >= 2 && args[0] == "--savegame") {
            string gamPath = args[1];
            string outJson = args.Length >= 3
                ? args[2]
                : Path.Combine(ResolveGeneratedDir(),
                    Path.GetFileNameWithoutExtension(gamPath) + ".savegame.json");
            ExtractSaveGame(gamPath, outJson);
            return;
        }

        if (args.Length == 1 && args[0].EndsWith(".GAM", StringComparison.OrdinalIgnoreCase)) {
            ExtractGamFile(args[0]);
            return;
        }

        if (args.Length == 2
            && args[0].EndsWith(".GAM", StringComparison.OrdinalIgnoreCase)
            && args[1].EndsWith(".GAM", StringComparison.OrdinalIgnoreCase)) {
            RunSaveGameFlagDiff(args[0], args[1]);
            return;
        }

        Console.Error.WriteLine("usage: ResourceExtractor --<mode> [game dir] [...]");
        Console.Error.WriteLine("       ResourceExtractor --savegame <SAVE.GAM> [out.json]");
        Console.Error.WriteLine("       ResourceExtractor <SAVE.GAM> | <before.GAM> <after.GAM>");
        Console.Error.WriteLine("       ResourceExtractor --pot <game dir> <out.pot>");
        Console.Error.WriteLine("modes: see the args[0] checks in Program.Main; scripts/verify-generated.sh lists the data ones.");
        Environment.ExitCode = 1;
    }

    // Dumps every sound from FRP.SX to generated/SND/{id}_{name}/{id}_{format}.{mid|wav}.
    // soundFormat byte → driver: 00=SndBlast/AdLib, 07=GenMIDI, 0C=MT-32, 12=STD (see docs/FileFormats/FRP.SX.md).
    private static void ExtractAllSounds(string gamePath) {
        var provider = new AudioResourceProvider(gamePath);
        IDictionary<string, (long, uint)> dictionary = provider.GetDictionary();
        string root = nameof(ResourceType.SND);

        foreach (KeyValuePair<string, (long, uint)> entry in dictionary) {
            AudioResource audioResource;
            try {
                audioResource = provider.GetResource<AudioResource>(entry.Key);
            } catch (Exception ex) {
                Console.Error.WriteLine($"[SFX] {entry.Key}: skip ({ex.Message})");
                continue;
            }

            string safeName = SanitizeFilename(audioResource.Name);
            string resourceDirectory = Path.Combine(root, $"{audioResource.Id}_{safeName}");
            Directory.CreateDirectory(resourceDirectory);

            foreach (KeyValuePair<byte, AudioDataResource> soundVariant in audioResource.Variants) {
                string variantName = soundVariant.Key.ToString("X2");
                if (soundVariant.Value.MidiData != null) {
                    File.WriteAllBytes(Path.Combine(resourceDirectory, $"{audioResource.Id}_{variantName}.mid"),
                        soundVariant.Value.MidiData);
                }
                if (soundVariant.Value.WavData != null) {
                    File.WriteAllBytes(Path.Combine(resourceDirectory, $"{audioResource.Id}_{variantName}.wav"),
                        soundVariant.Value.WavData);
                }
            }
            Console.WriteLine($"[SFX] {audioResource.Id} {audioResource.Name} ({audioResource.Variants.Count} variants)");
        }
    }

    private static string SanitizeFilename(string name) {
        if (string.IsNullOrEmpty(name)) return "_";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) {
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c);
        }
        return sb.ToString();
    }

    private static void ExtractAllRemappings(string filePath, GeneralResourceProvider generalResourceProvider) {
        var remapExtractor = new RemapExtractor();
        foreach (string paletteFile in GetFiles(filePath, "*.RMP")) {
            using var resourceStream = generalResourceProvider.GetResourceStream(paletteFile);
            var remapResource = remapExtractor.Extract(Path.GetFileName(paletteFile), resourceStream);
            WriteToJsonFile(paletteFile, ResourceType.RMP, remapResource.ToJson());
        }
    }

    private static void ExtractAnimatorScripts(string filePath, GeneralResourceProvider generalResourceProvider) {
        var animatorScriptExtractor = new TtmExtractor();
        foreach (string ttmFile in GetFiles(filePath, "*.ttm")
                 // .Where(f => !f.EndsWith("C51.TTM"))
                ) {
            using Stream resourceStream = generalResourceProvider.GetResourceStream(ttmFile);
            AnimationResource ttm = animatorScriptExtractor.Extract(Path.GetFileName(ttmFile), resourceStream);
            WriteToJsonFile(ttmFile, ttm.Type, ttm.ToJson());
        }
    }

    private static void ExtractAnimations(string filePath, GeneralResourceProvider generalResourceProvider) {
        var animationExtractor = new AdsExtractor();
        foreach (string adsFile in GetFiles(filePath, "*.ads")
                 // .Where(f => f.EndsWith("C12.ADS"))
                ) {
            using Stream resourceStream = generalResourceProvider.GetResourceStream(adsFile);
            AnimatorResource anim = animationExtractor.Extract(Path.GetFileName(adsFile), resourceStream);
            WriteToJsonFile(adsFile, anim.Type, anim.ToJson());
        }
        // foreach (ushort command in AdsScriptBuilder.SeenCommands) {
        //     Console.WriteLine($"{command:X4}");
        // }
    }

    private static void ExtractSpellBookPage(GeneralResourceProvider generalResourceProvider) {
        var extractor = new SpellBookPageExtractor();
        // The engine opens it as "InvSpell.dat"; the archive entry is uppercase.
        const string filename = "invspell.dat";
        using Stream resourceStream = generalResourceProvider.GetResourceStream(filename);
        SpellBookPage page = extractor.Extract(filename, resourceStream);
        WriteToJsonFile(filename, ResourceType.DAT, page.ToJson());
    }

    private static void ExtractSpells(GeneralResourceProvider generalResourceProvider) {
        var spellExtractor = new SpellExtractor();
        const string filename = "spells.dat";
        using Stream resourceStream = generalResourceProvider.GetResourceStream(filename);
        SpellList spellList = spellExtractor.Extract(filename, resourceStream);
        WriteToJsonFile(filename, ResourceType.DAT, spellList.ToJson());
        WriteToCsvFile(filename, ResourceType.DAT, spellList.ToCsv());
    }

    private static void ExtractLabels(string filePath, GeneralResourceProvider generalResourceProvider) {
        var labelExtractor = new LabelExtractor();
        foreach (string labelFile in GetFiles(filePath, "lbl_*.dat")) {
            using Stream resourceStream = generalResourceProvider.GetResourceStream(labelFile);
            LabelSet labelSet = labelExtractor.Extract(Path.GetFileName(labelFile), resourceStream);
            WriteToJsonFile(labelFile, labelSet.Type, labelSet.ToJson());
        }
    }

    private static void ExtractAllBmx(string filePath, GeneralResourceProvider generalResourceProvider) {
        string[] bmxFiles = Directory.GetFileSystemEntries(filePath, "*.bmx", new EnumerationOptions {
            MatchCasing = MatchCasing.CaseInsensitive
        });

        var bitmapExtractor = new BitmapExtractor();
        var paletteExtractor = new PaletteExtractor();
        foreach (string bmxFile in bmxFiles) {
            using Stream resourceStream = generalResourceProvider.GetResourceStream(bmxFile);
            ImageSet imageSet = bitmapExtractor.Extract(Path.GetFileName(bmxFile), resourceStream);
            var imageName = $"{Path.GetFileNameWithoutExtension(bmxFile)}";
            Color[] colors = GetColorsFromPalette(filePath, generalResourceProvider, imageName, paletteExtractor);
            // For multiple images we create a directory and extract each image
            var path = ResourceType.BMX.ToString();
            string resourceDirectory = Path.Combine(path, imageName);
            Directory.CreateDirectory(resourceDirectory);
            for (var i = 0; i < imageSet.Images.Count; i++) {
                BmImage bmImage = imageSet.Images[i];
                bmImage.Filename = $"{i}.png";
                File.WriteAllText(Path.Combine(resourceDirectory, $"{i}.json"), bmImage.ToJson());
                WriteToPngFile(i.ToString(), resourceDirectory, bmImage.ToRawImage(colors));
            }
            Console.WriteLine($"[BMX] {imageName} ({imageSet.Images.Count} images)");
        }
    }

    private static void ExtractCreatureSprites(string filePath, GeneralResourceProvider provider, string outDir) {
        Directory.CreateDirectory(outDir);

        using Stream bnStream = provider.GetResourceStream(Path.Combine(filePath, "BNAMES.DAT"));
        CreatureBitmaps creatures = new BNamesExtractor().Extract("BNAMES.DAT", bnStream);
        File.WriteAllText(Path.Combine(outDir, "creature-bitmaps.json"), creatures.ToJson());

        var bitmapExtractor = new BitmapExtractor();
        var lutCache = new Dictionary<int, byte[]>();
        byte[]? LutFor(int colorSet) {
            if (colorSet < 0) {
                return null;
            }
            if (!lutCache.TryGetValue(colorSet, out byte[]? lut)) {
                using Stream s = provider.GetResourceStream(Path.Combine(filePath, $"CS{colorSet}.DAT"));
                lut = CreatureColorSet.ReadLut(s);
                lutCache[colorSet] = lut;
            }
            return lut;
        }

        // ToRawImage mutates palette[0].A (index-0 transparency); clone so the shared static isn't touched.
        Color[] palette = (Color[])CreaturePalette.Colors.Clone();

        foreach (string key in creatures.Creatures.SelectMany(c => c.SpriteKeys).Distinct().OrderBy(k => k)) {
            (string stem, int colorSet) = CreatureColorSet.ParseVariantKey(key);
            using Stream s = provider.GetResourceStream(Path.Combine(filePath, stem + ".BMX"));
            ImageSet set = bitmapExtractor.Extract(stem + ".BMX", s);

            byte[]? lut = LutFor(colorSet);
            if (lut != null) {
                CreatureColorSet.Apply(set, lut);          // recolor the indices (the only new step)
            }

            string dir = Path.Combine(outDir, key);
            Directory.CreateDirectory(dir);
            for (int i = 0; i < set.Images.Count; i++) {
                // reuse the normal BMX→PNG path; only the palette differs
                WriteToPngFile(i.ToString(), dir, set.Images[i].ToRawImage(palette));
            }
            Console.WriteLine($"[CREATURE] {key} ({set.Images.Count} frames)");
        }
    }

    private static void ExtractCursors(string filePath, GeneralResourceProvider generalResourceProvider) {
        var extractor = new CursorExtractor();
        const string outDir = "POINTER";
        Directory.CreateDirectory(outDir);
        foreach (string name in new[] { "POINTER.BMX", "POINTERG.BMX" }) {
            string full = Path.Combine(filePath, name);
            if (!File.Exists(full)) {
                Console.WriteLine($"[CURSOR] missing {name}");
                continue;
            }
            using Stream resourceStream = generalResourceProvider.GetResourceStream(full);
            GameData.Resources.Cursor.CursorSet set = extractor.Extract(name, resourceStream);
            File.WriteAllText(Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(name)}.json"), set.ToJson());
            Console.WriteLine($"[CURSOR] {name} ({set.Images.Count} images)");
        }
    }

    private static Color[] GetColorsFromPalette(string filePath, GeneralResourceProvider generalResourceProvider, string imageName, PaletteExtractor paletteExtractor) {
        var paletteFile = PaletteExtractor.FindPalette(filePath, imageName);
        using Stream paletteStream = generalResourceProvider.GetResourceStream(paletteFile);
        var colors = paletteExtractor.Extract(paletteFile, paletteStream).Colors;

        return colors;
    }

    private static void ExtractAllScx(string filePath, GeneralResourceProvider generalResourceProvider) {
        string[] scxFiles = Directory.GetFileSystemEntries(filePath, "*.scx", new EnumerationOptions {
            MatchCasing = MatchCasing.CaseInsensitive
        });

        var screenExtractor = new ScreenExtractor();
        var paletteExtractor = new PaletteExtractor();
        foreach (string scxFile in scxFiles) {
            using Stream resourceStream = generalResourceProvider.GetResourceStream(scxFile);
            string imageName = Path.GetFileNameWithoutExtension(scxFile);
            BackgroundImage backgroundImage = screenExtractor.Extract(Path.GetFileName(scxFile), resourceStream);
            if (backgroundImage.BitMapData == null) {
                continue;
            }

            Color[] colors = GetColorsFromPalette(filePath, generalResourceProvider, imageName, paletteExtractor);

            var image = new BmImage(scxFile) {
                BitMapData = backgroundImage.BitMapData,
                Width = backgroundImage.Width,
                Height = backgroundImage.Height
            };

            var bitmap = image.ToRawImage(colors);

            WriteToPngFile(imageName, backgroundImage.Type.ToString(), bitmap);
            Console.WriteLine($"[SCX] {imageName} -> {backgroundImage.Width}x{backgroundImage.Height}");
        }
    }

    private static void WriteToPngFile(string filename, string resourceDirectory, RawImage image) {
        if (!Directory.Exists(resourceDirectory)) {
            Directory.CreateDirectory(resourceDirectory);
        }

        string filePath = Path.Combine(resourceDirectory, Path.GetFileNameWithoutExtension(filename) + ".png");
        PngWriter.Write(filePath, image);
    }

    private static void ExtractSpellSymbols(string filePath) {
        var extractor = new SpellSymbolExtractor();
        const string outDir = "SYMBOL";
        Directory.CreateDirectory(outDir);
        foreach (string symbolFile in GetFiles(filePath, "SYMBOL*.DAT").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
            string name = Path.GetFileName(symbolFile);
            using FileStream resourceFile = File.OpenRead(symbolFile);
            GameData.Resources.Spells.SpellSymbolLayout layout = extractor.Extract(name, resourceFile);
            string outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(name) + ".json");
            File.WriteAllText(outPath, layout.ToJson());
            Console.WriteLine($"[SYMBOL] {name} -> {outPath} (category {layout.Category}, {layout.Nodes.Count} nodes)");
        }
    }

    private static void ExtractCastRing(string filePath) {
        var extractor = new CastRingExtractor();
        const string outDir = "SYMBOL"; // co-locate with the casting-UI layouts
        Directory.CreateDirectory(outDir);
        string[] files = GetFiles(filePath, "RING.DAT");
        if (files.Length == 0) {
            Console.WriteLine("[RING] RING.DAT not found");
            return;
        }
        string name = Path.GetFileName(files[0]);
        using FileStream resourceFile = File.OpenRead(files[0]);
        GameData.Resources.Spells.CastRing ring = extractor.Extract(name, resourceFile);
        string outPath = Path.Combine(outDir, "RING.json");
        File.WriteAllText(outPath, ring.ToJson());
        Console.WriteLine($"[RING] {name} -> {outPath} ({ring.Positions.Count} positions)");
    }

    private static void ExtractGdsScenes(string filePath) {
        var gdsExtractor = new GdsSceneExtractor();
        const string gdsDir = "GDS";
        Directory.CreateDirectory(gdsDir);
        foreach (string gdsFile in GetFiles(filePath, "GDS*.DAT").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
            string name = Path.GetFileName(gdsFile);
            using FileStream resourceFile = File.OpenRead(gdsFile);
            GameData.Resources.Scene.GdsScene scene = gdsExtractor.Extract(name, resourceFile);
            string outPath = Path.Combine(gdsDir, Path.GetFileNameWithoutExtension(name) + ".json");
            File.WriteAllText(outPath, scene.ToJson());
            Console.WriteLine($"[GDS] {name} -> {outPath} ({scene.Hotspots.Length} hotspots)");
        }
    }

    private static void ExtractUserInterfaces(string filePath) {
        var userInterfaceExtractor = new UserInterfaceExtractor();
        var reqFiles = new List<string>();
        reqFiles.AddRange(GetFiles(filePath, "REQ_*.DAT").Select(Path.GetFileName)!);
        reqFiles.Add("AROREQ.DAT");
        reqFiles.Add("COMBAT.DAT");
        reqFiles.Add("CONTENTS.DAT");
        reqFiles.Add("EDITREQ.DAT");
        reqFiles.Add("INFOREQ.DAT");
        reqFiles.Add("POWEREQ.DAT");
        reqFiles.Add("SHOOT.DAT");
        reqFiles.Add("SPELL.DAT");
        reqFiles.Add("SPELLREQ.DAT");

        // Export only the REQ screens the shipped game actually loads — each is referenced by name
        // (lowercase) in KRONDOR.EXE. Everything else in the archive is editor / content-authoring
        // tooling (the tile-event & location editors, the book/spell/keyword content editors, the
        // zone editor, the build-time debug menu, the colourset-7 config popups) and is excluded.
        // The "referenced by name" test is authoritative: every known player screen is present —
        // including REQ_OPT1 (in-game menu), REQ_PUZL (riddle), REQ_TELE (temple teleport) and the
        // shipped cheat menus REQ_CHET/REQ_KNOC — while none of the authoring tools are.
        var gameLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "COMBAT", "CONTENTS", "SHOOT", "SPELL",
            "REQ_CAMP", "REQ_CAST", "REQ_CHET", "REQ_CMAP", "REQ_FMAP", "REQ_GDS", "REQ_HEAL",
            "REQ_INFO", "REQ_INV", "REQ_INV2", "REQ_KNOC", "REQ_LOAD", "REQ_MAIN", "REQ_MAP",
            "REQ_OPT0", "REQ_OPT1", "REQ_PREF", "REQ_PUZL", "REQ_SAVE", "REQ_TELE",
        };
        reqFiles.RemoveAll(f => !gameLoaded.Contains(Path.GetFileNameWithoutExtension(f)!));

        const string reqDir = "REQ";
        Directory.CreateDirectory(reqDir);
        foreach (string reqFile in reqFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
            string fullPath = Path.Combine(filePath, reqFile);
            if (!File.Exists(fullPath)) {
                Console.WriteLine($"[REQ] skip {reqFile}: not found");
                continue;
            }
            using FileStream resourceFile = File.OpenRead(fullPath);
            UserInterface userInterface = userInterfaceExtractor.Extract(reqFile, resourceFile);
            string outPath = Path.Combine(reqDir, Path.GetFileNameWithoutExtension(reqFile) + ".json");
            File.WriteAllText(outPath, userInterface.ToJson());
            Console.WriteLine($"[REQ] {reqFile} -> {outPath} ({userInterface.MenuEntries.Length} entries)");
        }
    }

    private static void ExtractBooks(string filePath) {
        var bokExtractor = new BokExtractor();
        foreach (string bokFile in GetFiles(filePath, "*.BOK")) {
            using FileStream resourceFile = File.OpenRead(bokFile);
            BookResource book = bokExtractor.Extract(Path.GetFileName(bokFile), resourceFile);
            WriteToJsonFile(bokFile, book.Type, book.ToJson());
        }
    }

    private static string ResolveGeneratedDir() {
        string? env = Environment.GetEnvironmentVariable("BAK_GENERATED_DIR");
        if (!string.IsNullOrEmpty(env)) return env;

        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir)) {
            if (Directory.Exists(Path.Combine(dir, "OriginalGame")) &&
                Directory.Exists(Path.Combine(dir, "DotNetProjects"))) {
                return Path.Combine(dir, "generated");
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate workspace root from " + AppContext.BaseDirectory +
            " (no ancestor contains both OriginalGame/ and DotNetProjects/). " +
            "Set BAK_GENERATED_DIR to override.");
    }

    private static string[] GetFiles(string filePath, string searchPattern) {
        return Directory.GetFileSystemEntries(filePath, searchPattern, new EnumerationOptions {
            MatchCasing = MatchCasing.CaseInsensitive
        });
    }

    private static void WriteToJsonFile(string fileName, ResourceType resourceType, string json) {
        // string resourceDirectory = resourceType.ToString();
        string resourceDirectory = Path.GetExtension(fileName)[1..].ToUpper();
        if (!Directory.Exists(resourceDirectory)) {
            Directory.CreateDirectory(resourceDirectory);
        }
        File.WriteAllText(Path.Combine(resourceDirectory, Path.GetFileNameWithoutExtension(fileName) + ".json"), json);
    }

    private static void WriteToCsvFile(string fileName, ResourceType resourceType, string csv) {
        var resourceDirectory = resourceType.ToString();
        if (!Directory.Exists(resourceDirectory)) {
            Directory.CreateDirectory(resourceDirectory);
        }
        File.WriteAllText(Path.Combine(resourceDirectory, Path.GetFileNameWithoutExtension(fileName) + ".csv"), csv);
    }

    private static void RunSaveGameFlagDiff(string beforeSavePath, string afterSavePath) {
        var saveGameExtractor = new SaveGameExtractor();
        SaveGame before = ExtractSaveGame(beforeSavePath, saveGameExtractor);
        SaveGame after = ExtractSaveGame(afterSavePath, saveGameExtractor);

        if (before.Data == null || after.Data == null) {
            Console.WriteLine("Unable to diff flags: one of the saves did not contain parsed state data.");
            return;
        }

        var globalFlagChanges = GetFlagBitChanges(before.Data.StateData.GlobalFlags, after.Data.StateData.GlobalFlags, 0);
        var globalFlag2Changes = GetFlagBitChanges(before.Data.StateData.GlobalFlags2, after.Data.StateData.GlobalFlags2, null);

        var diffReport = new SaveGameFlagDiffReport(
            Path.GetFileName(beforeSavePath),
            Path.GetFileName(afterSavePath),
            globalFlagChanges,
            globalFlag2Changes
        );

        string diffJson = JsonSerializer.Serialize(diffReport, ResourceExtensions.JsonOptions);

        string diffReportPath = $"{Path.GetFileNameWithoutExtension(beforeSavePath)}_to_{Path.GetFileNameWithoutExtension(afterSavePath)}.flagdiff.json";
        File.WriteAllText(diffReportPath, diffJson);

        Console.WriteLine($"Flag diff written: {Path.GetFullPath(diffReportPath)}");
        Console.WriteLine($"GlobalFlags changed bits: {globalFlagChanges.Count}");
        Console.WriteLine($"GlobalFlags2 changed bits: {globalFlag2Changes.Count}");
    }

    private static void ExtractGamFile(string gamPath) {
        var extractor = new SaveGameExtractor();
        using FileStream stream = File.OpenRead(gamPath);
        SaveGame saveGame = extractor.Extract(Path.GetFileName(gamPath), stream);
        Console.WriteLine($"Extracted: {saveGame.Id}");
        Console.WriteLine($"Name: '{saveGame.SaveGameName}'");
        Console.WriteLine($"Header Chapter: {saveGame.ChapterNumber}");
        Console.WriteLine($"Version: {saveGame.Version} (supported: {saveGame.IsSupportedVersion})");
        Console.WriteLine($"Temp.GAM bytes: {saveGame.TempGameData.Length}");
        string jsonPath = Path.GetFileNameWithoutExtension(gamPath) + ".savegame.json";
        File.WriteAllText(jsonPath, saveGame.ToJson());
        Console.WriteLine($"Dumped JSON: {Path.GetFullPath(jsonPath)}");
        if (saveGame.Data != null) {
            Console.WriteLine($"State Chapter: {saveGame.Data.StateData.ChapterNumber}");
            Console.WriteLine($"Party Gold: {saveGame.Data.StateData.PartyGold}");
            Console.WriteLine($"Zone: {saveGame.Data.StateData.CurrentZoneNumber} @ ({saveGame.Data.StateData.WorldXCoordinate},{saveGame.Data.StateData.WorldYCoordinate})");
            Console.WriteLine($"Active Party Members: {saveGame.Data.StateData.PartyConfigurationData.NumberOfActivePartyCharacters}");
            Console.WriteLine($"Game Time (2s ticks): {saveGame.Data.StateData.GameTimeIn2Seconds}");
            Console.WriteLine($"Timers: {saveGame.Data.StateData.Timers.Count(t => t.Type != 0)}");
            Console.WriteLine($"Zones with containers: {saveGame.Data.ZoneContainerStateData.Zones.Length}");
            int totalContainers = saveGame.Data.ZoneContainerStateData.Zones.Sum(z => z.Containers.Length);
            Console.WriteLine($"Total containers: {totalContainers}");
            int nonEmptyActors = saveGame.Data.ActorStateData.Count(a => a.NamePointer != 0);
            Console.WriteLine($"Non-empty actor slots: {nonEmptyActors}");
            Console.WriteLine($"Actor names: {string.Join(", ", saveGame.Data.StateData.ActorNames.Where(n => !string.IsNullOrEmpty(n)))}");
        } else {
            Console.WriteLine("WARNING: Data section could not be parsed!");
        }
    }

    private static SaveGame ExtractSaveGame(string savePath, SaveGameExtractor extractor) {
        using FileStream saveStream = File.OpenRead(savePath);
        return extractor.Extract(Path.GetFileName(savePath), saveStream);
    }

    private static void ExtractSaveGame(string gamPath, string outJson) {
        var extractor = new SaveGameExtractor();
        using FileStream stream = File.OpenRead(gamPath);
        SaveGame saveGame = extractor.Extract(Path.GetFileName(gamPath), stream);
        Directory.CreateDirectory(Path.GetDirectoryName(outJson)!);
        File.WriteAllText(outJson, saveGame.ToJson());
        Console.WriteLine($"Decoded {saveGame.Id} -> {outJson} " +
            $"(chapter {saveGame.ChapterNumber}, {saveGame.TempGameDataLength} body bytes)");
    }

    private static List<FlagBitChange> GetFlagBitChanges(byte[] before, byte[] after, int? keyBase) {
        int bytesToCompare = Math.Min(before.Length, after.Length);
        var changes = new List<FlagBitChange>();

        for (var byteIndex = 0; byteIndex < bytesToCompare; byteIndex++) {
            int xor = before[byteIndex] ^ after[byteIndex];
            if (xor == 0) {
                continue;
            }

            for (var bitIndex = 0; bitIndex < 8; bitIndex++) {
                if ((xor & (1 << bitIndex)) == 0) {
                    continue;
                }

                bool beforeSet = (before[byteIndex] & (1 << bitIndex)) != 0;
                bool afterSet = (after[byteIndex] & (1 << bitIndex)) != 0;
                int? key = keyBase.HasValue ? keyBase.Value + (byteIndex * 8) + bitIndex : null;

                changes.Add(new FlagBitChange(byteIndex, bitIndex, key, beforeSet, afterSet));
            }
        }

        return changes;
    }

    private sealed record FlagBitChange(int ByteIndex, int BitIndex, int? Key, bool BeforeSet, bool AfterSet);

    private sealed record SaveGameFlagDiffReport(
        string BeforeSave,
        string AfterSave,
        List<FlagBitChange> GlobalFlagsChanges,
        List<FlagBitChange> GlobalFlags2Changes
    );

    private static void ExtractZoneData(string filePath) {
        var defExtractor = new ZoneDefExtractor();
        var mapExtractor = new ZoneMapExtractor();
        var refExtractor = new ZoneRefExtractor();
        var shapeExtractor = new ZoneShapeExtractor();
        var boundsExtractor = new ZoneBoundsExtractor();

        string zoneDatPath = Path.Combine(filePath, "zone.dat");
        if (File.Exists(zoneDatPath)) {
            using var stream = File.OpenRead(zoneDatPath);
            var bounds = boundsExtractor.Extract("zone.dat", stream);
            WriteToJsonFile("zone.dat", ResourceType.DAT, bounds.ToJson());
        }

        // Z##.DAT — the zone's sky/ground pens and the overhead map's pen remap. Matched with an
        // explicit length test as well as the pattern: Windows-style wildcards would otherwise let
        // Z01DEF.DAT through and hand it to the wrong reader.
        var appearanceExtractor = new ZoneAppearanceExtractor();
        foreach (string zoneFile in GetFiles(filePath, "Z??.DAT")) {
            string fileName = Path.GetFileName(zoneFile);
            if (fileName.Length != "Z01.DAT".Length) {
                continue;
            }

            using var stream = File.OpenRead(zoneFile);
            var appearance = appearanceExtractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, appearance.ToJson());
        }

        foreach (string defFile in GetFiles(filePath, "Z??DEF.DAT")) {
            string fileName = Path.GetFileName(defFile);
            using var stream = File.OpenRead(defFile);
            var def = defExtractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, def.ToJson());
        }

        foreach (string mapFile in GetFiles(filePath, "Z??MAP.DAT")) {
            string fileName = Path.GetFileName(mapFile);
            using var stream = File.OpenRead(mapFile);
            var map = mapExtractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, map.ToJson());
        }

        foreach (string refFile in GetFiles(filePath, "Z??REF.DAT")) {
            string fileName = Path.GetFileName(refFile);
            using var stream = File.OpenRead(refFile);
            var zoneRef = refExtractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, zoneRef.ToJson());
        }

        foreach (string shpFile in GetFiles(filePath, "Z??SHP.DAT")) {
            string fileName = Path.GetFileName(shpFile);
            using var stream = File.OpenRead(shpFile);
            var shape = shapeExtractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, shape.ToJson());
        }
    }

    private static void ExtractWorldTiles(string filePath) {
        var extractor = new WorldItemExtractor();
        foreach (string wldFile in GetFiles(filePath, "T??????.WLD")) {
            string fileName = Path.GetFileName(wldFile);
            using var stream = File.OpenRead(wldFile);
            var tile = extractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.WLD, tile.ToJson());
        }
    }

    private static void ExtractTileEvents(string filePath) {
        var extractor = new TileEventExtractor();
        foreach (string datFile in GetFiles(filePath, "T??????.DAT")) {
            string fileName = Path.GetFileName(datFile);
            using var stream = File.OpenRead(datFile);
            var tile = extractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, tile.ToJson());
        }
    }

    private static void ExtractMonsterStats(string filePath) {
        var extractor = new MonsterStatsExtractor();
        foreach (string monstFile in GetFiles(filePath, "MONST*.DAT")) {
            string fileName = Path.GetFileName(monstFile);
            using var stream = File.OpenRead(monstFile);
            var stats = extractor.Extract(fileName, stream);
            WriteToJsonFile(fileName, ResourceType.DAT, stats.ToJson());
        }
    }

    private static void ExtractZoneTables(string filePath) {
        var extractor = new ZoneTableExtractor();
        var bitmapExtractor = new BitmapExtractor();
        foreach (string tblFile in GetFiles(filePath, "*.TBL")) {
            string fileName = Path.GetFileName(tblFile);
            Console.Error.WriteLine($"[TBL] begin {fileName}"); Console.Error.Flush();
            using var stream = File.OpenRead(tblFile);
            var table = extractor.Extract(fileName, stream);

            // Bake self-describing texture keys: resolve each face's raw slot-bitmap index against
            // this zone's actual Z##SLOT#.BMX image counts. Non-Z tables (COMBAT.TBL) and zones with
            // no slot files get no keys (all faces stay flat/null).
            int? zone = ZoneTableExtractor.ParseZoneNumber(fileName);
            if (zone is int zoneNumber) {
                var counts = GatherSlotImageCounts(filePath, zoneNumber, bitmapExtractor);
                ZoneTableExtractor.StampTextureKeys(table, zoneNumber, counts);
            }

            WriteToJsonFile(fileName, ResourceType.TBL, table.ToJson());
            Console.Error.WriteLine($"[TBL] done  {fileName} ({table.Entries.Count} entries)"); Console.Error.Flush();
        }
    }

    /// <summary>Read each Z##SLOT#.BMX image count in ascending file order until one is missing
    /// (matches the runtime's slots-0..6 load loop). Case-insensitive (fopen names are lowercase).</summary>
    private static List<int> GatherSlotImageCounts(string filePath, int zoneNumber, BitmapExtractor bitmapExtractor) {
        var counts = new List<int>();
        for (int slot = 0; slot < 7; slot++) {
            string pattern = $"Z{zoneNumber:D2}SLOT{slot}.BMX";
            string[] matches = Directory.GetFiles(filePath, pattern,
                new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
            if (matches.Length == 0) break;
            using var s = File.OpenRead(matches[0]);
            counts.Add(bitmapExtractor.Extract(Path.GetFileName(matches[0]), s).Images.Count);
        }
        return counts;
    }

    // Extracts CHAPSONG.DAT — see docs/FileFormats/CHAPSONG.DAT.md. 36 bytes:
    // 9 chapters × 2 × i16 song-id, loaded by open_book? at seg020:0x20d21.
    private static void ExtractCredits(string gamePath) {
        string fullPath = Path.Combine(gamePath, "CRED.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[CRED] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        CreditsData credits = new CredExtractor().Extract("CRED.DAT", stream);
        Directory.CreateDirectory("DAT");
        string json = credits.ToJson();
        File.WriteAllText(Path.Combine("DAT", "CRED.json"), json);
        Console.WriteLine($"[CRED] title \"{credits.Title}\", {credits.Lines.Count} lines written to DAT/CRED.json");
    }

    private static void ExtractChapterSongMap(string gamePath) {
        string fullPath = Path.Combine(gamePath, "CHAPSONG.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[CHAPSONG] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        ChapterSongMap map = new ChapterSongMapExtractor().Extract("CHAPSONG.DAT", stream);
        string json = JsonSerializer.Serialize(map, ResourceExtensions.JsonOptions);
        File.WriteAllText("CHAPSONG.json", json);
        Console.WriteLine($"[CHAPSONG] {map.Entries.Count} chapter entries written to CHAPSONG.json");
    }

    private static void ExtractStartData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "START.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[START] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        StartData data = new StartDataExtractor().Extract("START.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("START.json", json);
        Console.WriteLine($"[START] combatEye={data.CombatCameraHeightAboveGround}/{data.CombatCameraHeightUnderground} " +
                          $"combatPitch={data.CombatCameraPitchAboveGround}/{data.CombatCameraPitchUnderground} " +
                          $"gridCell={data.CombatGridCellSize} " +
                          $"viewport={data.ViewportX},{data.ViewportY},{data.ViewportWidth},{data.ViewportHeight} " +
                          $"written to START.json");
    }

    /// <summary>
    /// P1.DAT — one 22-byte combat record per playable character.
    /// </summary>
    /// <remarks>
    /// <b>Its <c>CreatureType</c> is the only translation from a character index to a drawable
    /// creature.</b> The 1730-slot actor table's low slots are monsters, so indexing THAT with a
    /// character index draws a goblin for Locklear; the six values here are 17, 15, 16, 45, 51, 47
    /// (Locklear, Gorath, Owyn, Pug, James, Patrus) in BNAMES numbering, and they are neither
    /// sequential nor in name order. Emitted so the mapping is visible in <c>generated/</c> rather
    /// than only reachable by loading the file at runtime.
    /// </remarks>
    private static void ExtractPartyCombatEntries(string gamePath) {
        string fullPath = Path.Combine(gamePath, "P1.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[P1] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        PartyCombatEntries data = new PartyCombatEntryExtractor().Extract("P1.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("P1.json", json);
        Console.WriteLine($"[P1] {data.Slots.Count} records; creatures "
            + string.Join(",", data.Slots.Select(r => r.CreatureType))
            + " written to P1.json");
    }

    private static void ExtractMovementData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "MOVEMENT.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[MOVEMENT] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        MovementData data = new MovementExtractor().Extract("MOVEMENT.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("MOVEMENT.json", json);
        Console.WriteLine($"[MOVEMENT] step={string.Join("/", data.StepDistances)} " +
                          $"turn={string.Join("/", data.TurnAngles)} " +
                          $"sec/step={string.Join("/", data.SecondsPerStep)} written to MOVEMENT.json");
    }

    private static void ExtractPartyData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "PARTY.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[PARTY] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        PartyData data = new PartyExtractor().Extract("PARTY.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("PARTY.json", json);
        Console.WriteLine($"[PARTY] {data.Members.Count} members: " +
                          $"{string.Join(", ", data.Members.Select(m => m.Name))} written to PARTY.json");
    }

    private static void ExtractObjectNames(string gamePath) {
        string fullPath = Path.Combine(gamePath, "ONAMES.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[ONAMES] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        ObjectNames data = new OnamesExtractor().Extract("ONAMES.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("ONAMES.json", json);
        Console.WriteLine($"[ONAMES] {data.Names.Count} names written to ONAMES.json (e.g. {string.Join(", ", data.Names.Take(3))})");
    }

    /// <summary>Extracts MNAMES.DAT (the creature-name table / <c>mnames</c> id space) to
    /// <c>DAT/mnames.json</c>. The de-indexed target catalog for encounter <c>EnemySlot.CreatureNumber</c>
    /// (reference-inventory #15). File name resolved case-insensitively.</summary>
    private static void ExtractCreatureNames(string gamePath) {
        string[] files = GetFiles(gamePath, "mnames.dat");
        if (files.Length == 0) {
            Console.Error.WriteLine($"[MNAMES] missing: {Path.Combine(gamePath, "MNAMES.DAT")}");
            return;
        }
        using FileStream stream = File.OpenRead(files[0]);
        GameData.Resources.Creature.CreatureNames data = new MnamesExtractor().Extract("mnames.dat", stream);
        WriteToJsonFile("mnames.dat", data.Type,
            JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions));
        Console.WriteLine($"[MNAMES] {data.Creatures.Count} creatures written to DAT/mnames.json");
    }

    // KRONDOR.EXE's combat affinity tables: the class-group modifier that scales melee accuracy and
    // armour, and the per-creature damage-type weakness/resistance masks. These live in the
    // executable's resident data rather than any .DAT, so this is the only way to ship them.
    private static void ExtractCombatAffinity(string gamePath) {
        string exePath = Path.Combine(gamePath, "KRONDOR.EXE");
        if (!File.Exists(exePath)) {
            Console.Error.WriteLine($"KRONDOR.EXE not found at {exePath}");
            return;
        }
        byte[] exe = File.ReadAllBytes(exePath);
        GameData.Resources.Combat.CombatAffinityTables tables =
            ResourceExtraction.Extractors.Exe.CombatAffinityReader.Read(exe);

        string json = JsonSerializer.Serialize(tables, ResourceExtensions.JsonOptions);
        Directory.CreateDirectory("EXE");
        File.WriteAllText(Path.Combine("EXE", "combat-affinity.json"), json);

        var weak = 0;
        var resist = 0;
        foreach (GameData.Resources.Combat.CreatureAffinity c in tables.Creatures) {
            if (c.WeaknessFlags != 0) {
                weak++;
            }
            if (c.ResistanceFlags != 0) {
                resist++;
            }
        }
        Console.WriteLine($"Extracted combat affinity: {weak} creature classes with a weakness, "
                          + $"{resist} with a resistance.");
    }

    /// <summary>
    /// Writes the translator's template (TASK-783): every keyed string of the player's data and
    /// KRONDOR.EXE, as a gettext POT.
    /// </summary>
    private static void WriteTranslationTemplate(string gamePath, string outPath) {
        IResourceProvider provider = ResourceProviderFactory.CreateResourceProvider(gamePath);
        var entries = ResourceExtraction.Text.TextInventory.Enumerate(provider).ToList();
        string exePath = Path.Combine(gamePath, "KRONDOR.EXE");
        if (File.Exists(exePath)) {
            IDictionary<string, string> exe = ExeStringManifest.Extract(File.ReadAllBytes(exePath));
            entries.AddRange(exe
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ResourceExtraction.Text.TextEntry(kv.Key, kv.Value, "KRONDOR.EXE")));
            // The port's sentences, composed from those pieces (TASK-776).
            entries.AddRange(GameData.Resources.Text.UiTemplates.EnglishFor(GameData.Resources.Text.UiStringCatalog.From(exe))
                .Select(kv => new ResourceExtraction.Text.TextEntry(kv.Key, kv.Value, "BaK-Again")));
        } else {
            Console.Error.WriteLine($"KRONDOR.EXE not found at {exePath}; its strings are not in the template.");
        }
        // The letter keys a pack can move (TASK-784): the travel HUD's and every REQ letter action.
        var pages = ResourceExtraction.Text.TextInventory.TextResources(provider)
            .Select(r => r.Resource).OfType<GameData.Resources.Menu.UserInterface>();
        entries.AddRange(GameData.Resources.Text.LetterHotkeys.Used(pages)
            .Select(c => new ResourceExtraction.Text.TextEntry(GameData.Resources.Text.LetterHotkeys.Key(c), c.ToString(), "BaK-Again")));
        // Each creature's gender and case forms, for a language that inflects (TASK-826).
        const string creature = "base:mnames:";
        entries.AddRange(entries.Where(e => e.Key.StartsWith(creature, StringComparison.Ordinal)).ToList()
            .SelectMany(e => GameData.Resources.Dialog.GermanCaseCodes.Fields.Select(f => new ResourceExtraction.Text.TextEntry(
                GameData.Resources.Dialog.GermanCaseCodes.Key("mnames:" + e.Key.Substring(creature.Length), f), e.Text, "BaK-Again"))));
        using (var writer = new StreamWriter(outPath, append: false, new UTF8Encoding(false))) {
            // A fixed caption's room is told in characters, measured in the font that draws it.
            var font = provider.GetResource<GameData.Resources.Font.FontResource>("GAME.FNT");
            ResourceExtraction.Text.PotTemplate.Write(entries, writer,
                text => text.Sum(c => font.GlyphFor(c)?.Width ?? 0));
        }
        Console.WriteLine($"Wrote {entries.Count} strings to {Path.GetFullPath(outPath)}.");
    }

    // KRONDOR.EXE's player-visible strings. Authoring-time only — the runtime reads the JSON this
    // writes, never the executable. Two outputs, one source of truth: the copy under GameData is the
    // embedded resource the game actually uses; the generated/ copy exists for verify-generated and
    // for human diffing.
    private static void ExtractUiStrings(string gamePath) {
        string exePath = Path.Combine(gamePath, "KRONDOR.EXE");
        if (!File.Exists(exePath)) {
            Console.Error.WriteLine($"KRONDOR.EXE not found at {exePath}");
            return;
        }
        byte[] exe = File.ReadAllBytes(exePath);
        IDictionary<string, string> entries = ExeStringManifest.Extract(exe);

        var ordered = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> kv in entries) {
            ordered[kv.Key] = kv.Value;
        }
        string json = JsonSerializer.Serialize(ordered, ResourceExtensions.JsonOptions);

        // Main already SetCurrentDirectory'd to <repo>/generated, same as every other WriteToJsonFile
        // caller — so this is "EXE", not "generated/EXE" (which would nest generated/generated/EXE).
        Directory.CreateDirectory("EXE");
        File.WriteAllText(Path.Combine("EXE", "uistrings.json"), json);
        Console.WriteLine($"Extracted {ordered.Count} UI strings.");
    }

    /// <summary>Extracts TELEPORT.DAT (the temple/teleport destination table) to
    /// <c>DAT/teleport.json</c>. This is the target catalog for the dialog <c>TeleportAction.DestinationId</c>
    /// reference (reference-inventory §B): destinations are id'd 0..N by position. Each destination is a
    /// world <c>Location</c> (zone + tile + offset + rotation) plus the GDS scene it opens onto
    /// (<c>GdsNumber</c>/<c>GdsLetter</c>). File name resolved case-insensitively (TELEPORT.DAT on disk).</summary>
    private static void ExtractTeleportDestinations(string gamePath) {
        string[] files = GetFiles(gamePath, "teleport.dat");
        if (files.Length == 0) {
            Console.Error.WriteLine($"[TELEPORT] missing: {Path.Combine(gamePath, "TELEPORT.DAT")}");
            return;
        }
        using FileStream stream = File.OpenRead(files[0]);
        List<TeleportDestination> destinations =
            new ResourceExtraction.Extractors.TeleportExtractor()
                .Extract("teleport.dat", stream).Destinations;
        WriteToJsonFile("teleport.dat", ResourceType.DAT, destinations.ToJson());
        Console.WriteLine($"[TELEPORT] {destinations.Count} destinations written to DAT/teleport.json");
    }

    /// <summary>Extracts KEYWORD.DAT (the global menu-label / dialog-keyword table) to
    /// <c>DAT/keywords.json</c>. This is the target catalog for the <c>KeywordChoiceBranch.Keyword</c>
    /// reference (dialog menu labels index the keyword list at <c>Keyword - 1</c>, 1-based). Read via
    /// the resource provider because keyword.dat lives inside the resource archive, not as a loose file.</summary>
    private static void ExtractKeywords(string gamePath) {
        GeneralResourceProvider provider = new(gamePath);
        using Stream stream = provider.GetResourceStream("keyword.dat");
        KeywordList keywordList = new KeywordExtractor().Extract("globalKeywords", stream);
        WriteToJsonFile("keywords.dat", keywordList.Type, keywordList.ToJson());
        Console.WriteLine($"[KEYWORD] {keywordList.Keywords.Count} keywords written to DAT/keywords.json");
    }

    private static void ExtractFilterData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "FILTER.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[FILTER] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        FilterData data = new FilterExtractor().Extract("FILTER.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("FILTER.json", json);
        Console.WriteLine($"[FILTER] {data.DetailLevels.Count} detail-level blocks × " +
                          $"{FilterData.EntityTypeCount} entity-type draw distances written to FILTER.json");
    }

    private static void ExtractDetectData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "DETECT.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[DETECT] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        DetectData data = new DetectExtractor().Extract("DETECT.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("DETECT.json", json);
        Console.WriteLine($"[DETECT] {data.Locations.Count} location blocks × " +
                          $"{DetectData.EntityTypeCount} entity-type detection ranges written to DETECT.json");
    }

    private static void ExtractSpellDescriptions(string gamePath) {
        string fullPath = Path.Combine(gamePath, "SPELLDOC.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[SPELLDOC] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        SpellDescriptions data = new SpellDocExtractor().Extract("SPELLDOC.DAT", stream);
        File.WriteAllText("SPELLDOC.json", JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions));
        Console.WriteLine($"[SPELLDOC] {data.Spells.Count} spells written to SPELLDOC.json");
    }

    private static void ExtractSpellAffinities(string gamePath) {
        var extractor = new SpellAffinityExtractor();
        foreach (string fileName in new[] { "SPELLWEA.DAT", "SPELLRES.DAT" }) {
            string fullPath = Path.Combine(gamePath, fileName);
            if (!File.Exists(fullPath)) {
                Console.Error.WriteLine($"[SPELLAFFINITY] missing: {fullPath}");
                continue;
            }
            using FileStream stream = File.OpenRead(fullPath);
            SpellAffinityTable data = extractor.Extract(fileName, stream);
            string outName = Path.GetFileNameWithoutExtension(fileName) + ".json";
            File.WriteAllText(outName, JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions));
            int withAffinity = data.Spells.Count(s => s.CreatureTypes.Count > 0);
            Console.WriteLine($"[SPELLAFFINITY] {fileName} -> {outName} ({data.Spells.Count} spells, {withAffinity} with entries)");
        }
    }

    private static void ExtractInputForms(string gamePath, string outputDir = "IN") {
        Directory.CreateDirectory(outputDir);
        var extractor = new InExtractor();
        foreach (string file in GetFiles(gamePath, "IN_*.DAT").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
            string name = Path.GetFileName(file)!;
            using FileStream stream = File.OpenRead(file);
            InputForm form = extractor.Extract(name, stream);
            string outPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(name) + ".json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(form, ResourceExtensions.JsonOptions));
            Console.WriteLine($"[IN] {name} -> {outPath} ({form.Fields.Count} fields)");
        }
    }

    private static void ExtractGridData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "GRID.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[GRID] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        GridData data = new GridExtractor().Extract("GRID.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("GRID.json", json);
        Console.WriteLine($"[GRID] {data.ZoneBorderPens.Count} per-zone border pens written to GRID.json: {string.Join(",", data.ZoneBorderPens)}");
    }

    private static void ExtractEncampData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "ENCAMP.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[ENCAMP] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        EncampData data = new EncampExtractor().Extract("ENCAMP.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("ENCAMP.json", json);
        Console.WriteLine($"[ENCAMP] {data.ClockEntries.Count} clock + {data.NeedleEntries.Count} needle points written to ENCAMP.json");
    }

    private static void ExtractTrapData(string gamePath) {
        string fullPath = Path.Combine(gamePath, "TRAPS.DAT");
        if (!File.Exists(fullPath)) {
            Console.Error.WriteLine($"[TRAPS] missing: {fullPath}");
            return;
        }
        using var stream = File.OpenRead(fullPath);
        TrapData data = new TrapExtractor().Extract("TRAPS.DAT", stream);
        string json = JsonSerializer.Serialize(data, ResourceExtensions.JsonOptions);
        File.WriteAllText("TRAPS.json", json);
        int active = data.Encounters.Count(e => e.Elements.Count > 0);
        Console.WriteLine($"[TRAPS] {data.Encounters.Count} encounter records ({active} non-empty) written to TRAPS.json");
    }

    private static void ExtractFullMap(string gamePath) {
        const string outputDir = "FMAP";
        Directory.CreateDirectory(outputDir);
        JsonSerializerOptions options = ResourceExtensions.JsonOptions;

        string twnPath = Path.Combine(gamePath, "FMAP_TWN.DAT");
        if (File.Exists(twnPath)) {
            using var stream = File.OpenRead(twnPath);
            FullMapTowns towns = new FullMapTownExtractor().Extract("FMAP_TWN.DAT", stream);
            File.WriteAllText(Path.Combine(outputDir, "FMAP_TWN.json"), JsonSerializer.Serialize(towns, options));
            Console.WriteLine($"[FMAP] {towns.Towns.Count} towns written to FMAP/FMAP_TWN.json");
        } else {
            Console.Error.WriteLine($"[FMAP] missing: {twnPath}");
        }

        string xyPath = Path.Combine(gamePath, "FMAP_XY.DAT");
        if (File.Exists(xyPath)) {
            using var stream = File.OpenRead(xyPath);
            FullMapPositions positions = new FullMapPositionExtractor().Extract("FMAP_XY.DAT", stream);
            File.WriteAllText(Path.Combine(outputDir, "FMAP_XY.json"), JsonSerializer.Serialize(positions, options));
            int markers = positions.Zones.Sum(z => z.Markers.Count);
            Console.WriteLine($"[FMAP] {positions.Zones.Count} zones, {markers} tile markers written to FMAP/FMAP_XY.json");
        } else {
            Console.Error.WriteLine($"[FMAP] missing: {xyPath}");
        }
    }

    // Extracts the DEF_*.DAT family — see docs/FileFormats/DEF_DAT family.md.
    // Three enum entries (def_comm/def_heal/def_soun) have no shipping data and
    // are skipped. Per-format extractors are added one by one as they're written.
    private static void ExtractDefDat(string gamePath) {
        const string outputDir = "DEF";
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        void Run<TEntry>(string fileName, DefFamilyExtractorBase<TEntry> extractor) {
            string fullPath = Path.Combine(gamePath, fileName);
            if (!File.Exists(fullPath)) {
                Console.Error.WriteLine($"[DEF] missing: {fileName}");
                return;
            }
            using var stream = File.OpenRead(fullPath);
            DefFamilyFile<TEntry> file = extractor.Extract(fileName, stream);
            string json = JsonSerializer.Serialize(file, ResourceExtensions.JsonOptions);
            File.WriteAllText(Path.Combine(outputDir, Path.GetFileNameWithoutExtension(fileName) + ".json"), json);
            int active = file.Records.Count(r => r.Status == 1);
            Console.WriteLine($"[DEF] {fileName}: {file.Records.Count} records ({active} active)");
        }

        Run("DEF_BKGR.DAT", new DefBkgrExtractor());
        Run("DEF_ENAB.DAT", new DefEnabExtractor());
        Run("DEF_DISA.DAT", new DefDisaExtractor());
        Run("DEF_TOWN.DAT", new DefTownExtractor());
        Run("DEF_BLOC.DAT", new DefBlocExtractor());
        Run("DEF_ZONE.DAT", new DefZoneExtractor());
        Run("DEF_DIAL.DAT", new DefDialExtractor());
        Run("DEF_TRAP.DAT", new DefTrapExtractor());
        Run("DEF_COMB.DAT", new DefCombExtractor());
    }

    private static void ExportObjectInfo(string[] args) {
        string gamePath = args.Length >= 2 ? args[1] : Directory.GetCurrentDirectory();
        string outputDir = args.Length >= 3 ? args[2] : "ObjectInfo";
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
        string objInfoPath = Directory.GetFileSystemEntries(gamePath, "objinfo.dat",
            new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).First();
        using FileStream objInfoStream = File.OpenRead(objInfoPath);
        List<ObjectInfo> objects = new ResourceExtraction.Extractors.Object.ObjectInfoSetExtractor()
            .Extract(Path.GetFileName(objInfoPath), objInfoStream).Items.ToList();
        string json = objects.ToJson();
        File.WriteAllText(Path.Combine(outputDir, "objinfo.json"), json);
        Console.WriteLine($"Exported: {objects.Count} objects to objinfo.json");
    }

    private static void ExportCutsceneData(string gamePath, string outputDir) {
        var generalResourceProvider = new GeneralResourceProvider(gamePath);
        var archiveResources = generalResourceProvider.GetDictionary();

        // DDX dialogs
        string ddxDir = Path.Combine(outputDir, "DDX");
        if (!Directory.Exists(ddxDir)) Directory.CreateDirectory(ddxDir);
        var ddxExtractor = new DdxExtractor();
        foreach (string ddxFile in GetFiles(gamePath, "*.ddx")) {
            using FileStream resourceFile = File.OpenRead(ddxFile);
            string fileName = Path.GetFileName(ddxFile);
            Dialog ddx = ddxExtractor.Extract(fileName, resourceFile);
            File.WriteAllText(Path.Combine(ddxDir, Path.GetFileNameWithoutExtension(fileName) + ".json"), ddx.ToJson());
            Console.WriteLine($"Exported DDX: {fileName}");
        }

        // ADS animator scripts — from both loose files and KRONDOR.001 archive
        string adsDir = Path.Combine(outputDir, "ADS");
        if (!Directory.Exists(adsDir)) Directory.CreateDirectory(adsDir);
        var adsExtractor = new AdsExtractor();
        var adsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string adsFile in GetFiles(gamePath, "*.ads"))
            adsNames.Add(Path.GetFileName(adsFile).ToUpper());
        foreach (string key in archiveResources.Keys)
            if (key.EndsWith(".ADS", StringComparison.OrdinalIgnoreCase))
                adsNames.Add(key);
        foreach (string adsName in adsNames.OrderBy(n => n)) {
            try {
                using Stream resourceStream = generalResourceProvider.GetResourceStream(adsName);
                AnimatorResource anim = adsExtractor.Extract(adsName, resourceStream);
                File.WriteAllText(Path.Combine(adsDir, Path.GetFileNameWithoutExtension(adsName) + ".json"), anim.ToJson());
                Console.WriteLine($"Exported ADS: {adsName}");
            } catch (Exception ex) {
                Console.WriteLine($"FAILED ADS: {adsName} - {ex.Message}");
            }
        }

        // TTM animation resources — from both loose files and KRONDOR.001 archive
        string ttmDir = Path.Combine(outputDir, "TTM");
        if (!Directory.Exists(ttmDir)) Directory.CreateDirectory(ttmDir);
        var ttmExtractor = new TtmExtractor();
        var ttmNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string ttmFile in GetFiles(gamePath, "*.ttm"))
            ttmNames.Add(Path.GetFileName(ttmFile).ToUpper());
        foreach (string key in archiveResources.Keys)
            if (key.EndsWith(".TTM", StringComparison.OrdinalIgnoreCase))
                ttmNames.Add(key);
        foreach (string ttmName in ttmNames.OrderBy(n => n)) {
            try {
                using Stream resourceStream = generalResourceProvider.GetResourceStream(ttmName);
                AnimationResource ttm = ttmExtractor.Extract(ttmName, resourceStream);
                File.WriteAllText(Path.Combine(ttmDir, Path.GetFileNameWithoutExtension(ttmName) + ".json"), ttm.ToJson());
                Console.WriteLine($"Exported TTM: {ttmName}");
            } catch (Exception ex) {
                Console.WriteLine($"FAILED TTM: {ttmName} - {ex.Message}");
            }
        }
    }

    private static void ExportDdxDialogs(string[] args) {
        string gamePath = args.Length >= 2 ? args[1] : Directory.GetCurrentDirectory();
        string outputDir = args.Length >= 3 ? args[2] : "DDX";
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
        var extractor = new DdxExtractor();
        foreach (string ddxFile in Directory.GetFileSystemEntries(gamePath, "*.ddx",
            new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })) {
            using FileStream resourceFile = File.OpenRead(ddxFile);
            Dialog ddx = extractor.Extract(Path.GetFileName(ddxFile), resourceFile);
            string json = ddx.ToJson();
            File.WriteAllText(Path.Combine(outputDir, Path.GetFileNameWithoutExtension(ddxFile) + ".json"), json);
            Console.WriteLine($"Exported: {Path.GetFileName(ddxFile)}");
        }
    }
}
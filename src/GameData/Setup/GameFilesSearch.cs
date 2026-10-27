namespace GameData.Setup;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public enum PickKind { GameFiles, Zip, GogInstaller, CdImage, Nothing }

/// <summary>Finds the game folder inside what the player picked, and says what a pick looks like.</summary>
public static class GameFilesSearch {
    /// <summary>The shallowest folder at or below <paramref name="root"/> (depth 0) holding all required files; ties go to ordinal path order.</summary>
    public static string? FindGameFolder(string root, int maxDepth = 2) {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) {
            return null;
        }
        var level = new List<string> { root };
        for (int depth = 0; depth <= maxDepth && level.Count > 0; depth++) {
            level.Sort(StringComparer.Ordinal);
            foreach (string folder in level) {
                if (CountFound(folder) == GameFolder.Required.Length) {
                    return folder;
                }
            }
            var next = new List<string>();
            if (depth < maxDepth) {
                foreach (string folder in level) {
                    try {
                        foreach (string sub in Directory.EnumerateDirectories(folder)) {
                            if (!new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                                next.Add(sub);
                            }
                        }
                    } catch (IOException) {
                    } catch (UnauthorizedAccessException) {
                    }
                }
            }
            level = next;
        }
        return null;
    }

    /// <summary>What a pick looks like, from bare file names. All three files beat everything else (a GOG install folder also holds bak.gog).</summary>
    public static PickKind Classify(IEnumerable<string> fileNames) {
        var names = fileNames.Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList();
        bool Has(Func<string, bool> test) => names.Any(test);
        if (GameFolder.Required.All(r => Has(n => n.Equals(r, StringComparison.OrdinalIgnoreCase)))) {
            return PickKind.GameFiles;
        }
        if (Has(n => n.StartsWith("setup_betrayal_at_krondor", StringComparison.OrdinalIgnoreCase)
                     && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) {
            return PickKind.GogInstaller;
        }
        if (Has(n => n.Equals("bak.gog", StringComparison.OrdinalIgnoreCase) || n.Equals("bak.inst", StringComparison.OrdinalIgnoreCase))) {
            return PickKind.CdImage;
        }
        return Has(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ? PickKind.Zip : PickKind.Nothing;
    }

    /// <summary>How many of the required files sit in <paramref name="folder"/> itself (0..3).</summary>
    public static int CountFound(string folder) => GameFolder.Required.Count(r => GameFolder.Find(folder, r) != null);
}

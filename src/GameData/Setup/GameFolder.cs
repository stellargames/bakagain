namespace GameData.Setup;

using System;
using System.IO;

/// <summary>Finds the original game's files by name, ignoring case (DOS names them in capitals; GOG and Android copies may not).</summary>
public static class GameFolder {
    public static readonly string[] Required = { "KRONDOR.001", "FRP.SX", "STARTUP.GAM" };

    /// <summary>The real path of <paramref name="fileName"/> in <paramref name="directory"/>, or null when absent.</summary>
    public static string? Find(string directory, string fileName) {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) {
            return null;
        }
        try {
            foreach (string file in Directory.EnumerateFiles(directory)) {
                if (string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase)) {
                    return file;
                }
            }
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
        return null;
    }

    /// <summary>RESOURCE.CFG in the game folder, else in the GOG install's <c>__support/save</c>.</summary>
    public static string? FindResourceConfig(string directory) =>
        Find(directory, "RESOURCE.CFG") ?? Find(Path.Combine(directory, "__support", "save"), "RESOURCE.CFG");
}

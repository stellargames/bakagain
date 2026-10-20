using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GameData.Mods;

/// <summary>One line of <c>mods/mods.json</c>: a mod's id, version and on/off flag (mod system spec 4.6).</summary>
public sealed record ModStateEntry(string Id, string Version, bool Enabled);

/// <summary>Reads and writes <c>mods/mods.json</c>, listed highest priority first.</summary>
public static class ModStateFile {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>A missing file gives an empty list.</summary>
    public static IReadOnlyList<ModStateEntry> Read(string path) {
        if (!File.Exists(path)) return Array.Empty<ModStateEntry>();
        return JsonSerializer.Deserialize<List<ModStateEntry>>(File.ReadAllText(path), Options) ?? new List<ModStateEntry>();
    }

    public static void Write(string path, IEnumerable<ModStateEntry> entries) {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, Options));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GameData.Mods;

/// <summary>A mod's optional <c>aliases.json</c>: one image serving several keys (mod system spec 4.3).</summary>
public static class ModAliases {
    /// <summary>Alias key to target, both relative paths without extension, <c>/</c>-separated. Empty when absent or unreadable.</summary>
    public static IReadOnlyDictionary<string, string> Read(string modFolder) {
        string path = Path.Combine(modFolder, "aliases.json");
        if (!File.Exists(path)) return new Dictionary<string, string>();
        return Parse(File.ReadAllText(path)) ?? new Dictionary<string, string>();
    }

    /// <summary>Null when <paramref name="json"/> is not an object of strings.</summary>
    internal static Dictionary<string, string>? Parse(string json) {
        try {
            var map = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (map == null || map.Values.Any(v => v == null)) return null;
            return map.ToDictionary(p => p.Key, p => p.Value!);
        } catch (JsonException) {
            return null;
        }
    }
}

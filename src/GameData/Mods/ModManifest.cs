using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameData.Mods;

/// <summary>A mod's <c>mod.json</c>, parsed and validated (mod system spec 4.2).</summary>
public sealed class ModManifest {
    public const int CurrentSchema = 1;

    public static readonly IReadOnlyList<string> KnownKinds = new[] {
        "sprites", "textures", "portraits", "sounds", "music", "rules", "items", "loot", "text", "language",
    };

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{1,47}\\z");

    public int Schema { get; private set; }
    public string Id { get; private set; } = "";
    public string Name { get; private set; } = "";
    public SemVersion Version { get; private set; }
    public string Author { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string License { get; private set; } = "";
    public VersionRange? Game { get; private set; }
    public IReadOnlyDictionary<string, VersionRange> Dependencies { get; private set; } = new Dictionary<string, VersionRange>();
    public IReadOnlyList<string> Conflicts { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Kinds { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<ModMode> Modes { get; private set; } = new[] { ModMode.Enhanced };

    public bool IsLanguage => Kinds.Contains("language");

    /// <summary>Parses and validates; returns null when <paramref name="errors"/> is not empty.</summary>
    public static ModManifest? Parse(string json, out IReadOnlyList<string> errors) {
        var errs = new List<string>();
        errors = errs;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { errs.Add("not valid JSON: " + e.Message); return null; }
        using (doc) {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { errs.Add("mod.json must be a JSON object"); return null; }
            var m = new ModManifest();

            if (root.TryGetProperty("schema", out JsonElement sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out int schema)) {
                m.Schema = schema;
                if (schema > CurrentSchema) errs.Add($"schema {schema} is newer than this game understands ({CurrentSchema})");
                else if (schema < 1) errs.Add($"schema {schema} is invalid");
            } else errs.Add("schema is missing or not a number");

            string? id = Str(root, "id", errs, required: true);
            if (id != null) {
                if (IdPattern.IsMatch(id)) m.Id = id;
                else errs.Add($"id '{id}' must match {IdPattern}");
            }
            m.Name = Str(root, "name", errs, required: true) ?? "";
            string? version = Str(root, "version", errs, required: true);
            if (version != null) {
                if (SemVersion.TryParse(version, out SemVersion v)) m.Version = v;
                else errs.Add($"version '{version}' is not a valid version");
            }
            m.Author = Str(root, "author", errs) ?? "";
            m.Description = Str(root, "description", errs) ?? "";
            m.License = Str(root, "license", errs) ?? "";

            string? game = Str(root, "game", errs);
            if (game != null) {
                if (VersionRange.TryParse(game, out VersionRange gr)) m.Game = gr;
                else errs.Add($"game '{game}' is not a valid version range");
            }

            if (root.TryGetProperty("dependencies", out JsonElement deps)) {
                var map = new Dictionary<string, VersionRange>();
                if (deps.ValueKind != JsonValueKind.Object) errs.Add("dependencies must be an object");
                else foreach (JsonProperty p in deps.EnumerateObject()) {
                    if (p.Value.ValueKind == JsonValueKind.String && VersionRange.TryParse(p.Value.GetString()!, out VersionRange r)) map[p.Name] = r;
                    else errs.Add($"dependency '{p.Name}' has an invalid version range");
                }
                m.Dependencies = map;
            }

            m.Conflicts = StrList(root, "conflicts", errs);
            m.Kinds = StrList(root, "kinds", errs);
            foreach (string k in m.Kinds.Where(k => !KnownKinds.Contains(k))) errs.Add($"unknown kind '{k}'");

            if (root.TryGetProperty("modes", out _)) {
                var modes = new List<ModMode>();
                foreach (string s in StrList(root, "modes", errs)) {
                    if (s == "enhanced") modes.Add(ModMode.Enhanced);
                    else if (s == "classic") modes.Add(ModMode.Classic);
                    else errs.Add($"unknown mode '{s}'");
                }
                if (modes.Count == 0 && errs.Count == 0) errs.Add("modes must not be empty");
                m.Modes = modes;
            }

            return errs.Count == 0 ? m : null;
        }
    }

    private static string? Str(JsonElement root, string name, List<string> errs, bool required = false) {
        if (!root.TryGetProperty(name, out JsonElement e)) {
            if (required) errs.Add($"{name} is missing");
            return null;
        }
        if (e.ValueKind == JsonValueKind.String) return e.GetString();
        errs.Add($"{name} must be a string");
        return null;
    }

    private static List<string> StrList(JsonElement root, string name, List<string> errs) {
        var list = new List<string>();
        if (!root.TryGetProperty(name, out JsonElement e)) return list;
        if (e.ValueKind != JsonValueKind.Array) { errs.Add($"{name} must be an array of strings"); return list; }
        foreach (JsonElement x in e.EnumerateArray()) {
            if (x.ValueKind == JsonValueKind.String) list.Add(x.GetString()!);
            else errs.Add($"{name} must be an array of strings");
        }
        return list;
    }
}

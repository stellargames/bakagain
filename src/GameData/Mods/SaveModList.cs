using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameData.Mods;

/// <summary>One mod recorded in a save's sidecar (mod system spec 4.7).</summary>
public sealed record SavedMod(string Id, string Version, IReadOnlyList<ModMode> Modes);

public enum SaveModIssueKind { Missing, MajorVersion }

/// <summary>A saved mod that is missing or disabled now, or installed at a different major version.</summary>
public sealed record SaveModIssue(string Id, string SavedVersion, string? InstalledVersion, SaveModIssueKind Kind);

/// <summary>The <c>SAVE##.MODS.json</c> sidecar next to each <c>SAVE##.GAM</c>.</summary>
public static class SaveModList {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Nullable so a hand-edited sidecar deserialises; Read drops what is unusable.
    private sealed class Entry {
        public string? Id { get; set; }
        public string? Version { get; set; }
        public List<ModMode>? Modes { get; set; }
    }

    private sealed class Document {
        public List<Entry?>? Mods { get; set; }
    }

    public static string SidecarPath(string savePath) => Path.ChangeExtension(savePath, ".MODS.json");

    /// <summary>Always writes, even an empty list: a save then means "no mods" explicitly.</summary>
    public static void Write(string savePath, IReadOnlyList<InstalledMod> active) {
        var doc = new Document {
            Mods = active.Select(m => new Entry { Id = m.Manifest.Id, Version = m.Manifest.Version.ToString(), Modes = m.Manifest.Modes.ToList() }).ToList(),
        };
        File.WriteAllText(SidecarPath(savePath), JsonSerializer.Serialize(doc, Options));
    }

    /// <summary>Empty when there is no sidecar or it is unreadable; entries without an id or version are dropped.</summary>
    public static IReadOnlyList<SavedMod> Read(string savePath) {
        string path = SidecarPath(savePath);
        if (!File.Exists(path)) return Array.Empty<SavedMod>();
        try {
            Document? doc = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            if (doc?.Mods == null) return Array.Empty<SavedMod>();
            return doc.Mods
                .Where(e => e != null && !string.IsNullOrEmpty(e.Id) && !string.IsNullOrEmpty(e.Version))
                .Select(e => new SavedMod(e!.Id!, e.Version!, e.Modes ?? new List<ModMode>()))
                .ToList();
        } catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) {
            return Array.Empty<SavedMod>();
        }
    }

    /// <summary>Only saved mods whose recorded modes include <paramref name="mode"/> count; extra active mods never do.</summary>
    public static IReadOnlyList<SaveModIssue> Compare(IReadOnlyList<SavedMod> saved, IReadOnlyList<InstalledMod> active, ModMode mode) {
        var issues = new List<SaveModIssue>();
        foreach (SavedMod s in saved.Where(s => s.Modes.Contains(mode))) {
            InstalledMod? now = active.FirstOrDefault(m => m.Manifest.Id == s.Id);
            if (now == null) {
                issues.Add(new SaveModIssue(s.Id, s.Version, null, SaveModIssueKind.Missing));
            } else if (SemVersion.TryParse(s.Version, out SemVersion was) && was.Major != now.Manifest.Version.Major) {
                issues.Add(new SaveModIssue(s.Id, s.Version, now.Manifest.Version.ToString(), SaveModIssueKind.MajorVersion));
            }
        }
        return issues;
    }
}
